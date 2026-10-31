using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// El botón "¡Juntar!" del minievento (ver Services/MiniEventService.cs). No es un comando: el aviso lo manda el bot solo, a veces, en el
// canal donde se está jugando. Cada jugador se suma una vez; la respuesta es privada para no llenar el canal.
public class MiniEventModule(IMiniEventService miniEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("miniev_join:*")]
    public async Task HandleJoinAsync(string eventIdRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!Guid.TryParse(eventIdRaw, out var eventId))
            {
                await FollowupAsync(Loc.T("Ese aviso ya terminó."), ephemeral: true);
                return;
            }

            var outcome = await miniEvents.JoinAsync(eventId, Context.User.Id);
            string message = outcome.Status switch
            {
                JoinStatus.Joined => Loc.F($"✅ ¡Te sumaste! Ya son **{outcome.Participants}**. Esperá el resultado: cuanta más gente, más se lleva cada uno."),
                JoinStatus.AlreadyJoined => Loc.F($"Ya estás sumado (son **{outcome.Participants}**). Esperá el resultado."),
                JoinStatus.NoAccount => Loc.T("Primero tenés que empezar tu aventura con **/start**."),
                JoinStatus.Finished => Loc.T("Llegaste tarde: ya se cerró."),
                _ => Loc.T("Ese aviso ya terminó."),
            };

            await FollowupAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude sumarte, intentá de nuevo."), ephemeral: true);
        }
    }
}

// Adaptador al mensaje real de Discord (el servicio solo conoce IEventAnnouncement).
public sealed class DiscordEventAnnouncement(IUserMessage message) : IEventAnnouncement
{
    public Task UpdateAsync(Embed embed, MessageComponent? components) =>
        message.ModifyAsync(p =>
        {
            p.Embed = embed;
            p.Components = components ?? new ComponentBuilder().Build();
        });
}
