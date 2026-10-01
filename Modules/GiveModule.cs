using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /give: darle monedas a otro jugador. El oro que sale es el que entra (sin impuesto) y todo queda registrado en game_events,
// así que si algún día se abusa de esto (cuentas alternativas pasándose el /daily) se ve en los datos y se le pone un tope
// o un impuesto en ExecuteGiveAsync, que es el único lugar con las reglas.
public class GiveModule(IUserRepository userRepository, ITransferRepository transferRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("give", "Dale monedas a otro jugador.")]
    public async Task HandleGiveAsync(
        [Summary("jugador", "A quién le das las monedas.")] IUser player,
        [Summary("cantidad", "Cuántas monedas le das.")] [MinValue(1)] int amount)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteGiveAsync(userRepository, transferRepository, gameEvents, Context.User.Id, player, amount);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude dar las monedas, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que ShopModule.ShopActionResult).
    public sealed record GiveResult(string? PlainMessage, Embed? Embed);

    // Estático (sin Context) para que "aa give" comparta exactamente la misma lógica.
    public static async Task<GiveResult> ExecuteGiveAsync(
        IUserRepository userRepository, ITransferRepository transferRepository, IGameEvents gameEvents,
        ulong fromDiscordId, IUser recipient, int amount)
    {
        if (amount < 1)
        {
            return new GiveResult("La cantidad tiene que ser al menos 1.", null);
        }

        if (recipient.IsBot)
        {
            return new GiveResult("Los bots no usan monedas 🤖", null);
        }

        if (recipient.Id == fromDiscordId)
        {
            return new GiveResult("No podés darte monedas a vos mismo.", null);
        }

        if (await userRepository.GetByDiscordIdAsync(recipient.Id) is null)
        {
            return new GiveResult($"{recipient.Mention} todavía no empezó a jugar: que use **/start** y después le das.", null);
        }

        var outcome = await transferRepository.TransferGoldAsync(fromDiscordId, recipient.Id, amount);

        switch (outcome.Status)
        {
            case TransferStatus.Ok:
                await gameEvents.RecordAsync(fromDiscordId, GameEventKinds.GoldGiven, amount: amount, detail: recipient.Id.ToString());
                await gameEvents.RecordAsync(recipient.Id, GameEventKinds.GoldReceived, amount: amount, detail: fromDiscordId.ToString());

                return new GiveResult(null, new EmbedBuilder()
                    .WithTitle("💸 ¡Monedas enviadas!")
                    .WithColor(Color.Gold)
                    .WithDescription($"{MentionUtils.MentionUser(fromDiscordId)} le dio **{amount}** monedas a {recipient.Mention}.")
                    .AddField("💰 Tu oro ahora", outcome.SenderGold.ToString(), true)
                    .Build());

            case TransferStatus.InsufficientGold:
                return new GiveResult($"No te alcanza: tenés **{outcome.SenderGold}** monedas y querés dar **{amount}**.", null);

            case TransferStatus.RecipientMissing:
                return new GiveResult($"{recipient.Mention} todavía no empezó a jugar: que use **/start** y después le das.", null);

            case TransferStatus.SenderMissing:
                return new GiveResult("Todavía no tenés cuenta: empezá con **/start**.", null);

            default:
                return new GiveResult("No pude hacer la transferencia.", null);
        }
    }
}
