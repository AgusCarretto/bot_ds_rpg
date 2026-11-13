using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// La ruleta (v0.55.0; el PAÑO de fichas desde la v0.55.1). /play game:Ruleta apuesta:1k o aa ruleta 1k abre el paño: la ficha vale lo
// que pusiste y arriba elegís ×1 ×5 ×10; cada toque pone una ficha en 🟥 Rojo, ⬛ Negro, Par o Impar, y los dos menús ponen una ficha en
// cada número que marques (varios de una). Girar cobra el total y paga cada apuesta en UNA llamada guardada
// (ICasinoRepository.PlaceBetAsync, como la moneda y los slots) y DESPUÉS anima el giro (el resultado ya está pagado: si la animación
// falla, nadie pierde nada). Las fichas quedan puestas para repetir. El dueño (2026-11-13): «¿no podemos tener un panel para poner
// fichas, tocando botoncitos?». Por texto sigue el atajo de un toque: «aa ruleta rojo 5k» gira de una.
public class RouletteModule(
    IUserRepository userRepository,
    ICasinoRepository casinoRepository,
    IGameEvents gameEvents,
    IRouletteBoardService boards) : InteractionModuleBase<SocketInteractionContext>
{
    private const string ChipPrefix = "rb_chip";
    private const string BetPrefix = "rb_bet";
    private const string NumberPrefix = "rb_num";
    private const string SpinPrefix = "rb_spin";
    private const string UndoPrefix = "rb_undo";
    private const string ClearPrefix = "rb_clear";

    // Pausa entre cuadros del giro (5 cuadros ≈ 2,8 s): Discord deja editar un mensaje unas 5 veces cada 5 segundos.
    public static TimeSpan FrameDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    // Quién está girando ahora: un segundo click mientras gira no apuesta otra vez encima de la animación.
    private static readonly ConcurrentDictionary<ulong, byte> Spinning = new();

    // Los botones de la mesa de un toque de la v0.55.0 (rlt / rlt_n): esa mesa ya no existe, se avisa cómo abrir el paño.
    [ComponentInteraction("rlt:*:*:*")]
    public async Task HandleOldTableAsync(string a, string b, string c) => await RespondAsync(Loc.T("Esa mesa es de antes: abrí el paño nuevo con **aa ruleta 1k** o **/play game:Ruleta**."), ephemeral: true);

    [ComponentInteraction("rlt_n:*:*:*")]
    public async Task HandleOldNumberAsync(string a, string b, string c, string[] selected) => await HandleOldTableAsync(a, b, c);

    [ComponentInteraction($"{ChipPrefix}:*:*")]
    public async Task HandleChipAsync(string ownerRaw, string multiplierRaw) =>
        await OnBoardAsync(ownerRaw, board =>
        {
            if (int.TryParse(multiplierRaw, out int m)) boards.SetMultiplier(board, m);
            return Task.FromResult<string?>(null);
        });

    [ComponentInteraction($"{BetPrefix}:*:*")]
    public async Task HandleBetAsync(string ownerRaw, string code) =>
        await OnBoardAsync(ownerRaw, board => PlaceAsync(board, RouletteRules.Parse(code) is { } bet ? [bet] : []));

    [ComponentInteraction($"{NumberPrefix}:*:*")]
    public async Task HandleNumbersAsync(string ownerRaw, string half, string[] selected) =>
        await OnBoardAsync(ownerRaw, board => PlaceAsync(board, selected.Select(RouletteRules.Parse).OfType<RouletteBet>().ToList()));

    [ComponentInteraction($"{UndoPrefix}:*")]
    public async Task HandleUndoAsync(string ownerRaw) =>
        await OnBoardAsync(ownerRaw, board => Task.FromResult(boards.Undo(board) ? null : Loc.T("No hay nada que deshacer.")));

    [ComponentInteraction($"{ClearPrefix}:*")]
    public async Task HandleClearAsync(string ownerRaw) =>
        await OnBoardAsync(ownerRaw, board => { boards.Clear(board); return Task.FromResult<string?>(null); });

    [ComponentInteraction($"{SpinPrefix}:*")]
    public async Task HandleSpinAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            var board = await BoardOfClickerAsync(ownerRaw);
            if (board is null) return;

            await SpinBoardAsync(userRepository, casinoRepository, gameEvents, board, Random.Shared,
                async (embed, components) => await ModifyOriginalResponseAsync(p => { p.Embed = embed; p.Components = components; }),
                async error => await FollowupAsync(error, ephemeral: true));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! Algo falló en la ruleta, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Un toque en el paño (que no es girar): lo valida, aplica el cambio y redibuja el paño. change devuelve un aviso privado o null.
    private async Task OnBoardAsync(string ownerRaw, Func<RouletteBoard, Task<string?>> change)
    {
        await DeferAsync();

        try
        {
            var board = await BoardOfClickerAsync(ownerRaw);
            if (board is null) return;

            if (Spinning.ContainsKey(board.OwnerId))
            {
                await FollowupAsync(Loc.T("La rueda todavía está girando: esperá a que frene."), ephemeral: true);
                return;
            }

            string? note = await change(board);
            int gold = (await userRepository.GetByDiscordIdAsync(board.OwnerId))?.Gold ?? 0;
            await ModifyOriginalResponseAsync(p => { p.Embed = BuildBoardEmbed(board, gold); p.Components = BuildBoardComponents(board); });
            if (note is not null)
            {
                await FollowupAsync(note, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! Algo falló en la ruleta, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    private async Task<RouletteBoard?> BoardOfClickerAsync(string ownerRaw)
    {
        if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
        {
            await FollowupAsync(Loc.T("Ese paño es de otra persona: abrí el tuyo con **/play game:Ruleta** o **aa ruleta 1k**."), ephemeral: true);
            return null;
        }

        var board = boards.Get(ownerId);
        if (board is null)
        {
            await FollowupAsync(Loc.T("Ese paño se cerró (el bot se reinició o abriste otro): abrí uno nuevo con **aa ruleta 1k**."), ephemeral: true);
        }

        return board;
    }

    private async Task<string?> PlaceAsync(RouletteBoard board, IReadOnlyList<RouletteBet> bets)
    {
        if (bets.Count == 0) return Loc.T("No entendí esa apuesta.");
        int gold = (await userRepository.GetByDiscordIdAsync(board.OwnerId))?.Gold ?? 0;
        return boards.Place(board, bets, gold) switch
        {
            RoulettePlaceStatus.NotEnoughGold => Loc.F($"No te alcanza: con esas fichas el paño pasaría tu oro (**{GameHistory.Number(gold)}**)."),
            RoulettePlaceStatus.TooMuch => Loc.F($"El paño no puede pasar de **{GameHistory.Number(RouletteRules.MaxTotalStake)}** (si no, el premio no entra)."),
            _ => null,
        };
    }

    public sealed record SpinOutcome(string? Error, int Number = 0, int Payout = 0, int Gold = 0, int Stake = 0);

    // Valida, cobra el total y paga todas las apuestas en una sola llamada guardada. No anima: eso lo hace AnimateAsync con lo ya pagado.
    public static async Task<SpinOutcome> ExecuteSpinAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, ulong discordId,
        IReadOnlyList<(RouletteBet Bet, int Amount)> bets, Random rng)
    {
        if (bets.Count == 0)
        {
            return new SpinOutcome(Loc.T("Poné al menos una ficha antes de girar."));
        }

        long stakeLong = bets.Sum(b => (long)b.Amount);
        if (stakeLong < RouletteRules.MinBet)
        {
            return new SpinOutcome(Loc.F($"La apuesta mínima es **{GameHistory.Number(RouletteRules.MinBet)}** de oro."));
        }

        if (stakeLong > RouletteRules.MaxTotalStake)
        {
            return new SpinOutcome(Loc.F($"El paño no puede pasar de **{GameHistory.Number(RouletteRules.MaxTotalStake)}** (si no, el premio no entra)."));
        }

        int stake = (int)stakeLong;
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new SpinOutcome(Loc.T("Todavía no tenés cuenta: empezá con **/start**."));
        }

        if (player.Gold < stake)
        {
            return new SpinOutcome(Loc.F($"No te alcanza: la apuesta es **{GameHistory.Number(stake)}** y tenés **{GameHistory.Number(player.Gold)}**."));
        }

        int number = RouletteRules.Spin(rng);
        int payout = RouletteRules.TotalPayout(bets, number);
        var updated = await casinoRepository.PlaceBetAsync(discordId, stake, payout);
        if (updated is null)
        {
            return new SpinOutcome(Loc.F($"No te alcanza: la apuesta es **{GameHistory.Number(stake)}** y ya no tenés suficiente."));
        }

        // Como la moneda y los slots: casino_win con lo ganado NETO, casino_loss con lo perdido neto (así /history y el logro Afortunado
        // la cuentan). Si recupera justo lo que puso, no cuenta como ganada ni perdida.
        if (payout > stake)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.CasinoWin, amount: payout - stake, detail: "roulette");
        }
        else if (payout < stake)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.CasinoLoss, amount: stake - payout, detail: "roulette");
        }

        return new SpinOutcome(null, number, payout, updated.Gold, stake);
    }

    // Una sola apuesta (el atajo «aa ruleta rojo 5k»).
    public static Task<SpinOutcome> ExecuteSpinAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, ulong discordId, int amount, RouletteBet bet, Random rng)
    {
        if (bet.Kind == RouletteBetKind.Number && amount > RouletteRules.MaxNumberBet)
        {
            return Task.FromResult(new SpinOutcome(Loc.F($"A un número se le apuesta hasta **{GameHistory.Number(RouletteRules.MaxNumberBet)}** (si no, el premio no entra).")));
        }

        return ExecuteSpinAsync(userRepository, casinoRepository, gameEvents, discordId, [(bet, amount)], rng);
    }

    // Gira el paño: juega todas sus fichas, anima y deja el paño igual para repetir.
    public static async Task SpinBoardAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, RouletteBoard board, Random rng,
        Func<Embed, MessageComponent?, Task> edit, Func<string, Task> error)
    {
        List<(RouletteBet Bet, int Amount)> bets;
        lock (board.Lock)
        {
            bets = board.Ordered();
        }

        await PlayAndAnimateAsync(userRepository, casinoRepository, gameEvents, board.OwnerId, bets, rng, edit, error, () => BuildBoardComponents(board));
    }

    // Juega y anima. finalComponents = lo que queda abajo al terminar (el paño, o nada en el atajo de texto).
    public static async Task PlayAndAnimateAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, ulong discordId,
        IReadOnlyList<(RouletteBet Bet, int Amount)> bets, Random rng,
        Func<Embed, MessageComponent?, Task> edit, Func<string, Task> error, Func<MessageComponent?>? finalComponents = null)
    {
        if (!Spinning.TryAdd(discordId, 0))
        {
            await error(Loc.T("La rueda todavía está girando: esperá a que frene."));
            return;
        }

        try
        {
            var outcome = await ExecuteSpinAsync(userRepository, casinoRepository, gameEvents, discordId, bets, rng);
            if (outcome.Error is not null)
            {
                await error(outcome.Error);
                return;
            }

            var frames = RouletteRules.Frames(outcome.Number, rng);
            for (int i = 0; i < frames.Count - 1; i++)
            {
                await edit(BuildSpinningEmbed(frames[i], bets), null);
                await Task.Delay(FrameDelay);
            }

            await edit(BuildResultEmbed(frames[^1], bets, outcome), finalComponents?.Invoke());
        }
        finally
        {
            Spinning.TryRemove(discordId, out _);
        }
    }

    // El atajo de una sola apuesta (texto), con la misma animación y sin paño al final.
    public static Task PlayAndAnimateAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, ulong discordId, int amount, RouletteBet bet, Random rng,
        Func<Embed, MessageComponent?, Task> edit, Func<string, Task> error)
    {
        if (bet.Kind == RouletteBetKind.Number && amount > RouletteRules.MaxNumberBet)
        {
            return error(Loc.F($"A un número se le apuesta hasta **{GameHistory.Number(RouletteRules.MaxNumberBet)}** (si no, el premio no entra)."));
        }

        return PlayAndAnimateAsync(userRepository, casinoRepository, gameEvents, discordId, [(bet, amount)], rng, edit, error);
    }

    public sealed record TableView(string? Error, Embed? Embed = null, MessageComponent? Components = null);

    // Abre el paño con fichas del monto pedido (500, 10k, all), contra el oro del jugador. Reemplaza el paño que tuviera.
    public static async Task<TableView> OpenBoardAsync(IUserRepository userRepository, IRouletteBoardService boards, ulong discordId, string betText)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var parsed = AmountParser.Parse(betText, player.Gold);
        if (!parsed.Ok)
        {
            return new TableView(parsed.Error);
        }

        if (parsed.Amount < RouletteRules.MinBet)
        {
            return new TableView(Loc.F($"La ficha mínima es **{GameHistory.Number(RouletteRules.MinBet)}** de oro."));
        }

        if (player.Gold < parsed.Amount)
        {
            return new TableView(Loc.F($"No te alcanza: la ficha es **{GameHistory.Number(parsed.Amount)}** y tenés **{GameHistory.Number(player.Gold)}**."));
        }

        var board = boards.Open(discordId, parsed.Amount);
        return new TableView(null, BuildBoardEmbed(board, player.Gold), BuildBoardComponents(board));
    }

    // «🟥 Rojo», «Par», «⬛ 17»: el nombre corto de una apuesta para las listas del paño.
    public static string Short(RouletteBet bet) => bet.Kind == RouletteBetKind.Number ? RouletteRules.Cell(bet.Number) : RouletteRules.Describe(bet);

    private static string Rules() =>
        Loc.T("🟥 Rojo · ⬛ Negro · Par · Impar → **×2**  ·  un número → **×36**") + "\n" +
        Loc.T("Si sale el 🟩 0, pierden el color y el par o impar.");

    public static Embed BuildBoardEmbed(RouletteBoard board, int gold)
    {
        List<(RouletteBet Bet, int Amount)> bets;
        long total;
        long chip;
        lock (board.Lock)
        {
            bets = board.Ordered();
            total = board.Total;
            chip = board.Chip;
        }

        string list = bets.Count == 0
            ? Loc.T("_Todavía no pusiste fichas: tocá abajo dónde apostar._")
            : string.Join("\n", bets.Select(b => $"{Short(b.Bet)}: **{GameHistory.Number(b.Amount)}**"))
              + "\n" + Loc.F($"Total: **{GameHistory.Number(total)}**");

        return new EmbedBuilder()
            .WithTitle(Loc.T("🎡 Ruleta · tu paño"))
            .WithColor(new Color(0x2E7D32))
            .WithDescription(list + "\n\n" + Rules())
            .WithFooter(Loc.F($"Ficha: {GameHistory.Number(chip)} · tenés {GameHistory.Number(gold)} de oro · solo vos podés tocar este paño"))
            .Build();
    }

    private static string Spun(IReadOnlyList<(RouletteBet Bet, int Amount)> bets) =>
        bets.Count == 1
            ? Loc.F($"Apostaste **{GameHistory.Number(bets[0].Amount)}** a {RouletteRules.Describe(bets[0].Bet)}.")
            : Loc.F($"Apostaste **{GameHistory.Number(bets.Sum(b => (long)b.Amount))}** en {bets.Count} apuestas.");

    public static Embed BuildSpinningEmbed(string frame, IReadOnlyList<(RouletteBet Bet, int Amount)> bets) => new EmbedBuilder()
        .WithTitle(Loc.T("🎡 Ruleta · girando…"))
        .WithColor(new Color(0x2E7D32))
        .WithDescription(frame + "\n\n" + Spun(bets))
        .Build();

    public static Embed BuildResultEmbed(string frame, IReadOnlyList<(RouletteBet Bet, int Amount)> bets, SpinOutcome outcome)
    {
        int net = outcome.Payout - outcome.Stake;
        string result = net > 0
            ? Loc.F($"🏆 ¡Ganaste! Cobrás **{GameHistory.Number(outcome.Payout)}** (**+{GameHistory.Number(net)}**).")
            : net == 0
                ? Loc.F($"🤝 Recuperaste lo que pusiste (**{GameHistory.Number(outcome.Payout)}**).")
                : outcome.Payout > 0
                    ? Loc.F($"💸 Cobrás **{GameHistory.Number(outcome.Payout)}**: perdiste **{GameHistory.Number(-net)}** en total.")
                    : Loc.F($"💸 Perdiste **{GameHistory.Number(outcome.Stake)}**.");

        // Con varias apuestas, cada una con su ✅ o ❌ (hasta 12 renglones; las demás se resumen).
        string detail = string.Empty;
        if (bets.Count > 1)
        {
            var lines = bets.Take(12).Select(b =>
            {
                int paid = RouletteRules.Payout(b.Bet, b.Amount, outcome.Number);
                return paid > 0 ? $"✅ {Short(b.Bet)}: **+{GameHistory.Number(paid)}**" : $"❌ {Short(b.Bet)}: {GameHistory.Number(b.Amount)}";
            }).ToList();
            if (bets.Count > 12) lines.Add(Loc.F($"… y {bets.Count - 12} más"));
            detail = string.Join("\n", lines) + "\n\n";
        }

        return new EmbedBuilder()
            .WithTitle(Loc.F($"🎡 Ruleta · ¡salió el {RouletteRules.Cell(outcome.Number)}!"))
            .WithColor(net > 0 ? Color.Gold : Color.DarkGrey)
            .WithDescription(frame + "\n\n" + Spun(bets) + "\n" + detail + result + "\n" + Loc.F($"Ahora tenés **{GameHistory.Number(outcome.Gold)}** de oro."))
            .WithFooter(Loc.T("Las fichas quedan puestas: tocá Girar para repetir"))
            .Build();
    }

    // Fila 0: tamaño de ficha (×1 ×5 ×10). Fila 1: rojo, negro, par, impar. Filas 2 y 3: números (varios de una). Fila 4: girar, deshacer, borrar.
    public static MessageComponent BuildBoardComponents(RouletteBoard board)
    {
        ulong owner = board.OwnerId;
        int current;
        lock (board.Lock) current = board.Multiplier;

        var builder = new ComponentBuilder();
        foreach (int m in RouletteBoard.ChipMultipliers)
        {
            builder.WithButton(Loc.F($"Ficha ×{m} ({GameHistory.Number((long)board.BaseChip * m)})"), $"{ChipPrefix}:{owner}:{m}",
                m == current ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: m == current, row: 0);
        }

        builder
            .WithButton(Loc.T("Rojo"), $"{BetPrefix}:{owner}:red", ButtonStyle.Danger, new Emoji("🟥"), row: 1)
            .WithButton(Loc.T("Negro"), $"{BetPrefix}:{owner}:black", ButtonStyle.Secondary, new Emoji("⬛"), row: 1)
            .WithButton(Loc.T("Par"), $"{BetPrefix}:{owner}:even", ButtonStyle.Primary, row: 1)
            .WithButton(Loc.T("Impar"), $"{BetPrefix}:{owner}:odd", ButtonStyle.Primary, row: 1);

        builder.WithSelectMenu(NumberMenu(owner, "a", 0, 18, Loc.T("Números del 0 al 18 (podés marcar varios)")), row: 2);
        builder.WithSelectMenu(NumberMenu(owner, "b", 19, 36, Loc.T("Números del 19 al 36 (podés marcar varios)")), row: 3);

        builder
            .WithButton(Loc.T("Girar"), $"{SpinPrefix}:{owner}", ButtonStyle.Success, new Emoji("🎡"), row: 4)
            .WithButton(Loc.T("Deshacer"), $"{UndoPrefix}:{owner}", ButtonStyle.Secondary, new Emoji("↩️"), row: 4)
            .WithButton(Loc.T("Borrar"), $"{ClearPrefix}:{owner}", ButtonStyle.Danger, new Emoji("🗑️"), row: 4);
        return builder.Build();
    }

    private static SelectMenuBuilder NumberMenu(ulong ownerId, string half, int from, int to, string placeholder)
    {
        var menu = new SelectMenuBuilder().WithCustomId($"{NumberPrefix}:{ownerId}:{half}").WithPlaceholder(placeholder).WithMinValues(1).WithMaxValues(to - from + 1);
        for (int n = from; n <= to; n++)
        {
            menu.AddOption(n.ToString(), n.ToString(), emote: new Emoji(RouletteRules.ColorEmoji(n)));
        }

        return menu;
    }
}
