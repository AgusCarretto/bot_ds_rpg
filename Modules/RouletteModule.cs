using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// La ruleta (v0.55.0; /play game:Ruleta apuesta:5k, aa ruleta 5k, aa ruleta rojo 5k, aa ruleta 17 500): una MESA con botones (🟥 Rojo,
// ⬛ Negro, Par, Impar) y dos menús para un número; al tocar, se cobra y se paga en UNA llamada (ICasinoRepository.PlaceBetAsync, como la
// moneda y los slots) y DESPUÉS se anima el giro editando el mensaje (el resultado ya está pagado: si la animación falla, nadie pierde
// nada). Al final la mesa vuelve a estar, para apostar otra vez lo mismo. La apuesta viaja en el id del botón: la mesa no guarda nada.
public class RouletteModule(
    IUserRepository userRepository,
    ICasinoRepository casinoRepository,
    IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    private const string BetPrefix = "rlt";
    private const string NumberPrefix = "rlt_n";

    // Pausa entre cuadros del giro (5 cuadros ≈ 2,8 s): Discord deja editar un mensaje unas 5 veces cada 5 segundos.
    public static TimeSpan FrameDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    // Quién está girando ahora: un segundo click mientras gira no apuesta otra vez encima de la animación.
    private static readonly ConcurrentDictionary<ulong, byte> Spinning = new();

    [ComponentInteraction($"{BetPrefix}:*:*:*")]
    public async Task HandleBetAsync(string ownerRaw, string amountRaw, string code) => await SpinFromTableAsync(ownerRaw, amountRaw, code);

    [ComponentInteraction($"{NumberPrefix}:*:*:*")]
    public async Task HandleNumberAsync(string ownerRaw, string amountRaw, string half, string[] selected) =>
        await SpinFromTableAsync(ownerRaw, amountRaw, selected.FirstOrDefault() ?? string.Empty);

    private async Task SpinFromTableAsync(string ownerRaw, string amountRaw, string code)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa mesa es de otra persona: abrí la tuya con **/play game:Ruleta** o **aa ruleta 5k**."), ephemeral: true);
                return;
            }

            var bet = RouletteRules.Parse(code);
            if (bet is null || !int.TryParse(amountRaw, out int amount))
            {
                await FollowupAsync(Loc.T("No entendí esa apuesta."), ephemeral: true);
                return;
            }

            await PlayAndAnimateAsync(userRepository, casinoRepository, gameEvents, Context.User.Id, amount, bet, Random.Shared,
                async (embed, components) => await ModifyOriginalResponseAsync(p => { p.Embed = embed; p.Components = components; }),
                async error => await FollowupAsync(error, ephemeral: true));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! Algo falló en la ruleta, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    public sealed record SpinOutcome(string? Error, int Number = 0, int Payout = 0, int Gold = 0);

    // Valida, cobra y paga en una sola llamada guardada. No anima: eso lo hace PlayAndAnimateAsync con el resultado ya pagado.
    public static async Task<SpinOutcome> ExecuteSpinAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, ulong discordId, int amount, RouletteBet bet, Random rng)
    {
        if (amount < RouletteRules.MinBet)
        {
            return new SpinOutcome(Loc.F($"La apuesta mínima es **{GameHistory.Number(RouletteRules.MinBet)}** de oro."));
        }

        if (bet.Kind == RouletteBetKind.Number && amount > RouletteRules.MaxNumberBet)
        {
            return new SpinOutcome(Loc.F($"A un número se le apuesta hasta **{GameHistory.Number(RouletteRules.MaxNumberBet)}** (si no, el premio no entra)."));
        }

        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new SpinOutcome(Loc.T("Todavía no tenés cuenta: empezá con **/start**."));
        }

        if (player.Gold < amount)
        {
            return new SpinOutcome(Loc.F($"No te alcanza: la apuesta es **{GameHistory.Number(amount)}** y tenés **{GameHistory.Number(player.Gold)}**."));
        }

        int number = RouletteRules.Spin(rng);
        int payout = RouletteRules.Payout(bet, amount, number);
        var updated = await casinoRepository.PlaceBetAsync(discordId, amount, payout);
        if (updated is null)
        {
            return new SpinOutcome(Loc.F($"No te alcanza: la apuesta es **{GameHistory.Number(amount)}** y ya no tenés suficiente."));
        }

        // Como la moneda y los slots: casino_win con lo ganado NETO, casino_loss con lo apostado (así /history y el logro Afortunado la cuentan).
        if (payout > amount)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.CasinoWin, amount: payout - amount, detail: "roulette");
        }
        else
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.CasinoLoss, amount: amount, detail: "roulette");
        }

        return new SpinOutcome(null, number, payout, updated.Gold);
    }

    // Juega y anima: el giro se muestra con los cuadros de RouletteRules.Frames y termina en el resultado con la mesa de nuevo.
    // edit = cómo editar el mensaje (botón o texto); error = cómo avisar un rechazo.
    public static async Task PlayAndAnimateAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, ulong discordId, int amount, RouletteBet bet, Random rng,
        Func<Embed, MessageComponent?, Task> edit, Func<string, Task> error)
    {
        if (!Spinning.TryAdd(discordId, 0))
        {
            await error(Loc.T("La rueda todavía está girando: esperá a que frene."));
            return;
        }

        try
        {
            var outcome = await ExecuteSpinAsync(userRepository, casinoRepository, gameEvents, discordId, amount, bet, rng);
            if (outcome.Error is not null)
            {
                await error(outcome.Error);
                return;
            }

            var frames = RouletteRules.Frames(outcome.Number, rng);
            for (int i = 0; i < frames.Count - 1; i++)
            {
                await edit(BuildSpinningEmbed(frames[i], amount, bet), null);
                await Task.Delay(FrameDelay);
            }

            await edit(BuildResultEmbed(frames[^1], amount, bet, outcome), BuildTableComponents(discordId, amount));
        }
        finally
        {
            Spinning.TryRemove(discordId, out _);
        }
    }

    public sealed record TableView(string? Error, Embed? Embed = null, MessageComponent? Components = null);

    // La mesa para un monto: lo lee como cualquier apuesta (500, 10k, all) contra el oro del jugador.
    public static async Task<TableView> OpenTableAsync(IUserRepository userRepository, ulong discordId, string betText)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var parsed = AmountParser.Parse(betText, player.Gold);
        if (!parsed.Ok)
        {
            return new TableView(parsed.Error);
        }

        if (parsed.Amount < RouletteRules.MinBet)
        {
            return new TableView(Loc.F($"La apuesta mínima es **{GameHistory.Number(RouletteRules.MinBet)}** de oro."));
        }

        if (player.Gold < parsed.Amount)
        {
            return new TableView(Loc.F($"No te alcanza: la apuesta es **{GameHistory.Number(parsed.Amount)}** y tenés **{GameHistory.Number(player.Gold)}**."));
        }

        return new TableView(null, BuildTableEmbed(parsed.Amount), BuildTableComponents(discordId, parsed.Amount));
    }

    private static string Rules() =>
        Loc.T("🟥 Rojo · ⬛ Negro · Par · Impar → **×2**") + "\n" +
        Loc.T("Un número del 0 al 36 → **×36**") + "\n" +
        Loc.T("Si sale el 🟩 0, pierden el color y el par o impar.");

    public static Embed BuildTableEmbed(int amount) => new EmbedBuilder()
        .WithTitle(Loc.T("🎡 Ruleta"))
        .WithColor(new Color(0x2E7D32))
        .WithDescription(Loc.F($"Apostás **{GameHistory.Number(amount)}** de oro. ¿A qué?") + "\n\n" + Rules())
        .WithFooter(Loc.T("Solo quien abrió la mesa puede apostar en ella"))
        .Build();

    public static Embed BuildSpinningEmbed(string frame, int amount, RouletteBet bet) => new EmbedBuilder()
        .WithTitle(Loc.T("🎡 Ruleta · girando…"))
        .WithColor(new Color(0x2E7D32))
        .WithDescription(frame + "\n\n" + Loc.F($"Apostaste **{GameHistory.Number(amount)}** a {RouletteRules.Describe(bet)}."))
        .Build();

    public static Embed BuildResultEmbed(string frame, int amount, RouletteBet bet, SpinOutcome outcome)
    {
        bool won = outcome.Payout > 0;
        string result = won
            ? Loc.F($"🏆 ¡Ganaste! Cobrás **{GameHistory.Number(outcome.Payout)}** (×{RouletteRules.Multiplier(bet)}).")
            : Loc.F($"💸 Perdiste **{GameHistory.Number(amount)}**.");

        return new EmbedBuilder()
            .WithTitle(Loc.F($"🎡 Ruleta · ¡salió el {RouletteRules.Cell(outcome.Number)}!"))
            .WithColor(won ? Color.Gold : Color.DarkGrey)
            .WithDescription(
                frame + "\n\n" +
                Loc.F($"Apostaste **{GameHistory.Number(amount)}** a {RouletteRules.Describe(bet)}.") + "\n" +
                result + "\n" +
                Loc.F($"Ahora tenés **{GameHistory.Number(outcome.Gold)}** de oro."))
            .WithFooter(Loc.F($"Tocá otra vez para apostar {GameHistory.Number(amount)} de nuevo"))
            .Build();
    }

    // Fila 1: rojo, negro, par, impar. Filas 2 y 3: un número (0 a 18 y 19 a 36; un menú admite 25 opciones). La apuesta va en el id.
    public static MessageComponent BuildTableComponents(ulong ownerId, int amount)
    {
        var builder = new ComponentBuilder()
            .WithButton(Loc.T("Rojo"), $"{BetPrefix}:{ownerId}:{amount}:red", ButtonStyle.Danger, new Emoji("🟥"), row: 0)
            .WithButton(Loc.T("Negro"), $"{BetPrefix}:{ownerId}:{amount}:black", ButtonStyle.Secondary, new Emoji("⬛"), row: 0)
            .WithButton(Loc.T("Par"), $"{BetPrefix}:{ownerId}:{amount}:even", ButtonStyle.Primary, row: 0)
            .WithButton(Loc.T("Impar"), $"{BetPrefix}:{ownerId}:{amount}:odd", ButtonStyle.Primary, row: 0);

        builder.WithSelectMenu(NumberMenu(ownerId, amount, "a", 0, 18, Loc.T("Un número del 0 al 18 (×36)")), row: 1);
        builder.WithSelectMenu(NumberMenu(ownerId, amount, "b", 19, 36, Loc.T("Un número del 19 al 36 (×36)")), row: 2);
        return builder.Build();
    }

    private static SelectMenuBuilder NumberMenu(ulong ownerId, int amount, string half, int from, int to, string placeholder)
    {
        var menu = new SelectMenuBuilder().WithCustomId($"{NumberPrefix}:{ownerId}:{amount}:{half}").WithPlaceholder(placeholder).WithMinValues(1).WithMaxValues(1);
        for (int n = from; n <= to; n++)
        {
            menu.AddOption(n.ToString(), n.ToString(), emote: new Emoji(RouletteRules.ColorEmoji(n)));
        }

        return menu;
    }
}
