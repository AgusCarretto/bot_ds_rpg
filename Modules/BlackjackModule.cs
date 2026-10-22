using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Lo que se manda al jugador: un rechazo en texto, o la mano (el embed y, si sigue en juego, los botones).
public sealed record BlackjackReply(string? PlainMessage, Embed? Embed, MessageComponent? Components);

// El blackjack del casino (/play game:blackjack, aa bj <apuesta>; GameData/Blackjack.cs, Services/BlackjackService.cs). El dueño (2026-10-22): «armate el bj, aa bj 10k por ejemplo». Esta clase tiene los botones de la mano
// (Pedir / Plantarse / Doblar) y arma lo que se ve; el comando que EMPIEZA una mano vive en CasinoModule (/play) y en Modules/TextCommandModule.Blackjack.cs (aa bj), los dos con la misma lógica de acá.
// Los botones llevan el dueño y la mano en el id: otro jugador no puede jugar la tuya, y un botón de una mano vieja muestra cómo terminó en vez de hacer nada.
public class BlackjackModule(IBlackjackService blackjack) : InteractionModuleBase<SocketInteractionContext>
{
    private const string HitPrefix = "bj_hit";
    private const string StandPrefix = "bj_stand";
    private const string DoublePrefix = "bj_double";

    [ComponentInteraction($"{HitPrefix}:*:*")]
    public Task HandleHitAsync(string ownerRaw, string gameRaw) => ActAsync(ownerRaw, gameRaw, BlackjackAction.Hit);

    [ComponentInteraction($"{StandPrefix}:*:*")]
    public Task HandleStandAsync(string ownerRaw, string gameRaw) => ActAsync(ownerRaw, gameRaw, BlackjackAction.Stand);

    [ComponentInteraction($"{DoublePrefix}:*:*")]
    public Task HandleDoubleAsync(string ownerRaw, string gameRaw) => ActAsync(ownerRaw, gameRaw, BlackjackAction.Double);

    private async Task ActAsync(string ownerRaw, string gameRaw, BlackjackAction action)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa mano es de otra persona: empezá la tuya con **/play game:blackjack** o **aa bj <apuesta>**.", ephemeral: true);
                return;
            }

            var reply = await ExecuteActAsync(blackjack, Context.User.Id, Guid.TryParseExact(gameRaw, "N", out var gameId) ? gameId : null, action);
            if (reply.Embed is null)
            {
                await FollowupAsync(reply.PlainMessage, ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = reply.Embed;
                p.Components = reply.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! Algo falló en la mesa, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa bj" y /play (sin Context) ----

    // Empieza una mano apostando betText (10k, 1m, all...). Si no se puede (sin oro, ya hay una mano), PlainMessage dice por qué.
    public static async Task<BlackjackReply> ExecuteStartAsync(IBlackjackService service, ulong discordId, string betText) =>
        ToReply(await service.StartAsync(discordId, betText), discordId);

    // Pide, planta o dobla. gameId null = la mano actual del jugador (los comandos de texto).
    public static async Task<BlackjackReply> ExecuteActAsync(IBlackjackService service, ulong discordId, Guid? gameId, BlackjackAction action) =>
        ToReply(await service.ActAsync(discordId, gameId, action), discordId);

    private static BlackjackReply ToReply(BlackjackResult result, ulong owner) =>
        result.View is { } view
            ? new BlackjackReply(null, BuildEmbed(view), BuildComponents(view, owner))
            : new BlackjackReply(result.Message, null, null);

    // La mesa: tu mano y la de la banca (con una carta tapada mientras se juega), cómo viene y, al terminar, el resultado. Pública y pura para probarla.
    public static Embed BuildEmbed(BlackjackView view)
    {
        var state = view.State;
        var (playerTotal, playerSoft) = Blackjack.Evaluate(state.Player);
        string playerText = $"**Tu mano** ({playerTotal}{(playerSoft ? " blando" : string.Empty)}): `{string.Join(" ", state.Player.Select(c => c.Label))}`";

        string dealerText;
        if (state.Finished && !(state.Outcome == BlackjackOutcome.PlayerBust))
        {
            dealerText = $"**La banca** ({Blackjack.Evaluate(state.Dealer).Total}): `{string.Join(" ", state.Dealer.Select(c => c.Label))}`";
        }
        else if (state.Finished)
        {
            // Te pasaste: la banca ni juega, se le ve lo que tenía.
            dealerText = $"**La banca**: `{string.Join(" ", state.Dealer.Select(c => c.Label))}`";
        }
        else
        {
            dealerText = $"**La banca**: `{state.Dealer[0].Label} ❓`";
        }

        string stakeText = state.Doubled ? $"{state.Stake} (doblada)" : state.Stake.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string> { playerText, dealerText, string.Empty };

        if (!state.Finished)
        {
            lines.Add("Elegí: **Pedir** otra carta, **Plantarte**" + (view.CanDouble ? " o **Doblar** (una sola carta más y la apuesta se duplica)." : "."));
            lines.Add("_En texto: `aa bj hit`, `aa bj stand`" + (view.CanDouble ? ", `aa bj double`" : string.Empty) + "._");
        }
        else
        {
            int payout = Blackjack.Payout(state), net = Blackjack.Net(state);
            lines.Add(state.Outcome switch
            {
                BlackjackOutcome.PlayerBlackjack => $"🎉 **¡Blackjack!** Paga 3 a 2: cobrás **{payout}** (+{net}).",
                BlackjackOutcome.PlayerWin => $"✅ **¡Ganaste!** Cobrás **{payout}** (+{net}).",
                BlackjackOutcome.Push => $"🤝 **Empate.** Te devuelvo los **{state.Stake}**.",
                BlackjackOutcome.PlayerBust => $"💥 **Te pasaste.** Perdés **{state.Stake}**.",
                _ => Blackjack.IsBlackjack(state.Dealer) ? $"😢 **La banca tenía blackjack.** Perdés **{state.Stake}**." : $"😢 **La banca gana.** Perdés **{state.Stake}**.",
            });
        }

        if (!string.IsNullOrWhiteSpace(view.Note))
        {
            lines.Add(string.Empty);
            lines.Add(view.Note);
        }

        return new EmbedBuilder()
            .WithTitle($"🃏 Blackjack · apuesta {stakeText}")
            .WithColor(!state.Finished ? Color.Blue : state.Outcome switch
            {
                BlackjackOutcome.PlayerBlackjack or BlackjackOutcome.PlayerWin => Color.Green,
                BlackjackOutcome.Push => Color.LightGrey,
                _ => Color.Red,
            })
            .WithDescription(string.Join("\n", lines))
            .WithFooter($"Oro: {view.Gold} · la banca se planta en 17, el blackjack paga 3 a 2")
            .Build();
    }

    // Los botones de una mano en juego (ninguno si ya terminó). Doblar solo se ofrece con las dos primeras cartas y con oro para igualar la apuesta.
    public static MessageComponent? BuildComponents(BlackjackView view, ulong owner)
    {
        if (view.State.Finished)
        {
            return null;
        }

        string game = view.GameId.ToString("N");
        return new ComponentBuilder()
            .WithButton("Pedir", $"{HitPrefix}:{owner}:{game}", ButtonStyle.Primary, new Emoji("➕"))
            .WithButton("Plantarse", $"{StandPrefix}:{owner}:{game}", ButtonStyle.Secondary, new Emoji("✋"))
            .WithButton("Doblar", $"{DoublePrefix}:{owner}:{game}", ButtonStyle.Success, new Emoji("⏫"), disabled: !view.CanDouble)
            .Build();
    }
}
