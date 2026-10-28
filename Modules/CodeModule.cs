using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /code (v0.37.0): canjear un código por premios. La respuesta es PRIVADA (solo la ve quien lo canjea): un código escrito en un canal lo puede leer cualquiera, así que el de barra es la forma
// recomendada. Toda la lógica está en CodeHandler (la comparte «aa code»).
public class CodeModule(IRedeemCodeRepository codes, IRedeemAttemptLimiter limiter, IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("code", "Canjeá un código por premios (solo lo ves vos).")]
    public async Task HandleAsync([Summary("codigo", "El código que te dieron.")] string codigo)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var reply = await CodeHandler.RedeemAsync(codes, limiter, gameEvents, Context.User.Id, codigo);
            await FollowupAsync(reply.PlainMessage, embed: reply.Embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("No pude canjear el código ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }
}
