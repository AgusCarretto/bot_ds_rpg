using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// «Elementos» (/play game:Elementos rival:@amigo apuesta:5k, aa play elementos @amigo 5k, aa elementos @amigo 5k): fuego, hielo y agua
// apostando oro entre dos jugadores (v0.54.0; nació como /ppt, piedra, papel o tijera, y el dueño lo pasó a /play con elementos). El
// desafío lo arma CasinoModule (y el comando de texto) con PrepareChallengeAsync; acá viven los botones. Una sola mano: cada uno elige con
// los botones del mensaje público (la respuesta de quién eligió qué es privada), se revelan juntas y el que gana se lleva la
// apuesta del otro. Empate = otra mano (hasta 3); el que no elige en 60 s pierde. Sin tope de apuesta (el dueño). El oro se mueve
// UNA vez, al final, con la transferencia guardada de /give (ITransferRepository): no hay depósito, así un reinicio no se come nada.
public class RpsModule(
    IUserRepository userRepository,
    ITransferRepository transferRepository,
    IGameEvents gameEvents,
    IRpsService rpsService) : InteractionModuleBase<SocketInteractionContext>
{
    private const string AcceptPrefix = "rps_accept";
    private const string DeclinePrefix = "rps_decline";
    private const string PickPrefix = "rps_pick";

    [ComponentInteraction($"{AcceptPrefix}:*")]
    public async Task HandleAcceptAsync(string idRaw)
    {
        await DeferAsync();

        try
        {
            var game = Guid.TryParse(idRaw, out var id) ? rpsService.Get(id) : null;
            if (game is null)
            {
                await FollowupAsync(Loc.T("Esa partida ya no existe: venció o ya se jugó."), ephemeral: true);
                return;
            }

            if (Context.User.Id != game.OpponentId)
            {
                await FollowupAsync(Loc.T("Ese desafío no es para vos."), ephemeral: true);
                return;
            }

            // Los dos tienen que tener el oro AL ACEPTAR (se vuelve a mirar al cobrar).
            string? noGold = await CheckGoldAsync(userRepository, game);
            if (noGold is not null)
            {
                await FollowupAsync(noGold, ephemeral: true);
                return;
            }

            switch (rpsService.Accept(id, Context.User.Id))
            {
                case RpsAcceptStatus.Ok:
                    game.Message = ((SocketMessageComponent)Context.Interaction).Message;
                    await ShowAsync(game);
                    StartPickTimer(rpsService, transferRepository, gameEvents, game, game.Round);
                    return;
                case RpsAcceptStatus.Expired:
                    await ModifyOriginalResponseAsync(p =>
                    {
                        p.Embed = BuildFinalEmbed(game, Loc.T("⌛ El desafío venció sin que lo aceptaran."));
                        p.Components = new ComponentBuilder().Build();
                    });
                    return;
                case RpsAcceptStatus.Busy:
                    await FollowupAsync(Loc.T("Alguno de los dos ya está jugando otra partida: terminen esa primero."), ephemeral: true);
                    return;
                default:
                    await FollowupAsync(Loc.T("Esa partida ya empezó."), ephemeral: true);
                    return;
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude aceptar la partida, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{DeclinePrefix}:*")]
    public async Task HandleDeclineAsync(string idRaw)
    {
        await DeferAsync();

        try
        {
            var game = Guid.TryParse(idRaw, out var id) ? rpsService.Get(id) : null;
            if (game is null || !game.IsPlayer(Context.User.Id))
            {
                await FollowupAsync(game is null ? Loc.T("Esa partida ya no existe: venció o ya se jugó.") : Loc.T("Ese desafío no es para vos."), ephemeral: true);
                return;
            }

            if (!rpsService.Decline(id, Context.User.Id))
            {
                await FollowupAsync(Loc.T("Esa partida ya empezó."), ephemeral: true);
                return;
            }

            string text = Context.User.Id == game.OpponentId
                ? Loc.F($"🙅 <@{game.OpponentId}> no aceptó el desafío.")
                : Loc.F($"🙅 <@{game.ChallengerId}> retiró el desafío.");
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = string.Empty;
                p.Embed = BuildFinalEmbed(game, text);
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cancelar la partida, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{PickPrefix}:*:*")]
    public async Task HandlePickAsync(string idRaw, string code)
    {
        await DeferAsync();

        try
        {
            var choice = RpsRules.FromCode(code);
            var game = Guid.TryParse(idRaw, out var id) ? rpsService.Get(id) : null;
            if (game is null || choice is null)
            {
                await FollowupAsync(Loc.T("Esa partida ya terminó."), ephemeral: true);
                return;
            }

            var result = rpsService.Pick(id, Context.User.Id, choice.Value);
            switch (result.Status)
            {
                case RpsPickStatus.NotPlayer:
                    await FollowupAsync(Loc.T("No estás en esta partida: mirá nomás 👀"), ephemeral: true);
                    return;
                case RpsPickStatus.AlreadyPicked:
                    await FollowupAsync(Loc.T("Ya elegiste: esperá al otro."), ephemeral: true);
                    return;
                case RpsPickStatus.NotFound:
                case RpsPickStatus.NotPicking:
                    await FollowupAsync(Loc.T("Esa partida ya terminó."), ephemeral: true);
                    return;
            }

            // Lo que eligió se le dice SOLO a él (privado); el mensaje público solo muestra que ya eligió.
            await FollowupAsync(Loc.F($"Elegiste {RpsRules.Label(choice.Value)}."), ephemeral: true);

            if (result.Status == RpsPickStatus.Waiting)
            {
                await ShowAsync(game);
                return;
            }

            if (result.Status == RpsPickStatus.Tie)
            {
                await ShowAsync(game, Loc.F($"🤝 Los dos eligieron {RpsRules.Label(result.ChallengerChoice!.Value)}: empate {game.Ties} de {RpsRules.MaxTies}. ¡Otra mano!"));
                StartPickTimer(rpsService, transferRepository, gameEvents, game, game.Round);
                return;
            }

            string final = await SettleAsync(transferRepository, gameEvents, game, result);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = string.Empty;
                p.Embed = BuildFinalEmbed(game, final);
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude anotar tu elección, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    private async Task ShowAsync(RpsGame game, string? note = null) =>
        await ModifyOriginalResponseAsync(p =>
        {
            p.Content = string.Empty;
            p.Embed = BuildEmbed(game, note);
            p.Components = BuildComponents(game);
        });

    public sealed record RpsChallengeResult(string? Error, RpsGame? Game);

    // Estático (sin Context): lo usan /play y «aa play elementos» / «aa elementos». Valida todo ANTES de crear la propuesta y nunca crea cuentas.
    public static async Task<RpsChallengeResult> PrepareChallengeAsync(
        IUserRepository userRepository, IRpsService rpsService, ulong challengerId, string challengerName, IUser opponent, string amountText)
    {
        if (opponent.IsBot)
        {
            return new RpsChallengeResult(Loc.T("Los bots no juegan por plata 🤖"), null);
        }

        if (opponent.Id == challengerId)
        {
            return new RpsChallengeResult(Loc.T("No podés jugarte contra vos mismo."), null);
        }

        var challenger = await userRepository.GetByDiscordIdAsync(challengerId);
        if (challenger is null)
        {
            return new RpsChallengeResult(Loc.T("Todavía no tenés cuenta: empezá con **/start**."), null);
        }

        var parsed = AmountParser.Parse(amountText, challenger.Gold);
        if (!parsed.Ok)
        {
            return new RpsChallengeResult(parsed.Error, null);
        }

        int bet = parsed.Amount;
        if (bet < RpsRules.MinBet)
        {
            return new RpsChallengeResult(Loc.F($"La apuesta mínima es **{GameHistory.Number(RpsRules.MinBet)}** de oro."), null);
        }

        if (challenger.Gold < bet)
        {
            return new RpsChallengeResult(Loc.F($"No te alcanza: tenés **{GameHistory.Number(challenger.Gold)}** de oro y querés apostar **{GameHistory.Number(bet)}**."), null);
        }

        var rival = await userRepository.GetByDiscordIdAsync(opponent.Id);
        if (rival is null)
        {
            return new RpsChallengeResult(Loc.F($"{opponent.Mention} todavía no empezó a jugar: que use **/start** primero."), null);
        }

        if (rival.Gold < bet)
        {
            return new RpsChallengeResult(Loc.F($"A {opponent.Mention} no le alcanza: tiene **{GameHistory.Number(rival.Gold)}** de oro y la apuesta es **{GameHistory.Number(bet)}**."), null);
        }

        var game = rpsService.Create(challengerId, challengerName, opponent.Id, opponent.GlobalName ?? opponent.Username, bet);
        return game is null
            ? new RpsChallengeResult(Loc.T("Alguno de los dos ya está jugando otra partida: terminen esa primero."), null)
            : new RpsChallengeResult(null, game);
    }

    // Null si los dos tienen el oro de la apuesta; si no, el aviso.
    public static async Task<string?> CheckGoldAsync(IUserRepository userRepository, RpsGame game)
    {
        foreach (var id in new[] { game.ChallengerId, game.OpponentId })
        {
            var user = await userRepository.GetByDiscordIdAsync(id);
            if (user is null || user.Gold < game.Bet)
            {
                return Loc.F($"A <@{id}> ya no le alcanza el oro para la apuesta de **{GameHistory.Number(game.Bet)}**.");
            }
        }

        return null;
    }

    // Mueve el oro (una sola vez) y anota los eventos. Devuelve el texto final de la partida. Nunca tira: si algo falla, lo dice.
    public static async Task<string> SettleAsync(ITransferRepository transferRepository, IGameEvents gameEvents, RpsGame game, RpsPickResult result)
    {
        if (result.Status == RpsPickStatus.Cancelled)
        {
            return result.ByTimeout || result.ChallengerChoice is not { } tied
                ? Loc.T("⌛ Ninguno eligió a tiempo: la partida se cancela y nadie pierde oro.")
                : Loc.F($"🤝 Empataron {RpsRules.MaxTies} veces seguidas ({RpsRules.Label(tied)}): la partida se cancela y nadie pierde oro.");
        }

        ulong winner = result.WinnerId, loser = result.LoserId;
        string winnerName = winner == game.ChallengerId ? game.ChallengerName : game.OpponentName;
        string loserName = loser == game.ChallengerId ? game.ChallengerName : game.OpponentName;
        string reveal = result.ByTimeout
            ? Loc.F($"⌛ <@{loser}> no eligió a tiempo.")
            : Loc.F($"<@{game.ChallengerId}> {RpsRules.Label(result.ChallengerChoice!.Value)}  vs  {RpsRules.Label(result.OpponentChoice!.Value)} <@{game.OpponentId}>")
              + "\n" + RpsRules.WhyWins(winner == game.ChallengerId ? result.ChallengerChoice!.Value : result.OpponentChoice!.Value);

        TransferOutcome outcome;
        try
        {
            outcome = await transferRepository.TransferGoldAsync(loser, winner, game.Bet);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return reveal + "\n\n" + Loc.T("❌ No pude mover el oro: la partida se anula y nadie pierde nada.");
        }

        if (outcome.Status != TransferStatus.Ok)
        {
            return reveal + "\n\n" + Loc.F($"❌ <@{loser}> ya no tiene el oro apostado: la partida se anula y no se mueve nada.");
        }

        await gameEvents.RecordAsync(winner, GameEventKinds.RpsWin, detail: loserName);
        await gameEvents.RecordAsync(loser, GameEventKinds.RpsLoss, detail: winnerName);
        await gameEvents.RecordAsync(winner, GameEventKinds.RpsGoldWon, amount: game.Bet, detail: loserName);
        await gameEvents.RecordAsync(loser, GameEventKinds.RpsGoldLost, amount: game.Bet, detail: winnerName);

        return reveal + "\n\n" + Loc.F($"🏆 ¡Gana <@{winner}> y se lleva **{GameHistory.Number(game.Bet)}** de oro de <@{loser}>!");
    }

    // El reloj de UNA mano. Corre afuera de la interacción; si alguien no eligió a tiempo, cierra la partida y edita el mensaje.
    // Un reloj de una mano que ya se empató o se decidió no hace nada (Round distinto).
    public static void StartPickTimer(IRpsService rpsService, ITransferRepository transferRepository, IGameEvents gameEvents, RpsGame game, int round)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RpsRules.PickTimeout);
                var result = rpsService.Timeout(game.Id, round);
                if (result.Status is not (RpsPickStatus.Decided or RpsPickStatus.Cancelled))
                {
                    return;
                }

                string final = await SettleAsync(transferRepository, gameEvents, game, result);
                if (game.Message is not null)
                {
                    await game.Message.ModifyAsync(p =>
                    {
                        p.Content = string.Empty;
                        p.Embed = BuildFinalEmbed(game, final);
                        p.Components = new ComponentBuilder().Build();
                    });
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        });
    }

    private static string Title() => Loc.T("🔥❄️💧 Elementos");

    // La pantalla mientras se juega: la propuesta (Aceptar / Rechazar) o la mano en curso (quién ya eligió).
    public static Embed BuildEmbed(RpsGame game, string? note = null)
    {
        string bet = GameHistory.Number(game.Bet);
        var embed = new EmbedBuilder().WithTitle(Title()).WithColor(Color.Gold);

        if (game.Phase == RpsPhase.Offered)
        {
            return embed
                .WithDescription(
                    Loc.F($"<@{game.ChallengerId}> desafía a <@{game.OpponentId}>: **{bet}** de oro cada uno.") + "\n" +
                    Loc.T("El que gana se lleva la apuesta del otro.") + "\n\n" +
                    Loc.F($"<@{game.OpponentId}>, ¿aceptás?"))
                .WithFooter(Loc.T("El desafío vence en 2 minutos"))
                .Build();
        }

        string Line(ulong id, RpsChoice? pick) => pick is null
            ? Loc.F($"⏳ <@{id}> está pensando…")
            : Loc.F($"✅ <@{id}> ya eligió");

        var lines = new List<string>();
        if (note is not null) lines.Add(note + "\n");
        lines.Add(Loc.F($"Apuesta: **{bet}** de oro cada uno."));
        lines.Add(Line(game.ChallengerId, game.ChallengerPick));
        lines.Add(Line(game.OpponentId, game.OpponentPick));

        return embed
            .WithDescription(string.Join("\n", lines))
            .WithFooter(Loc.T("Elegí con los botones (nadie más ve qué elegiste) · 60 segundos: el que no elige, pierde"))
            .Build();
    }

    public static Embed BuildFinalEmbed(RpsGame game, string text) =>
        new EmbedBuilder().WithTitle(Title()).WithColor(Color.DarkGrey).WithDescription(text).Build();

    public static MessageComponent BuildComponents(RpsGame game)
    {
        var builder = new ComponentBuilder();
        if (game.Phase == RpsPhase.Offered)
        {
            builder.WithButton(Loc.T("Aceptar"), $"{AcceptPrefix}:{game.Id}", ButtonStyle.Success);
            builder.WithButton(Loc.T("Rechazar"), $"{DeclinePrefix}:{game.Id}", ButtonStyle.Danger);
        }
        else if (game.Phase == RpsPhase.Picking)
        {
            foreach (var choice in new[] { RpsChoice.Fire, RpsChoice.Ice, RpsChoice.Water })
            {
                builder.WithButton(RpsRules.Name(choice), $"{PickPrefix}:{game.Id}:{RpsRules.Code(choice)}", ButtonStyle.Primary, new Emoji(RpsRules.Emoji(choice)));
            }
        }

        return builder.Build();
    }
}
