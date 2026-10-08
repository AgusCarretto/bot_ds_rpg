using Discord;

namespace BotDsRpg.Services;

// Lo mismo que Services/CombatTurnOutput.cs pero para un turno de RAID (ver RaidModule.ResolveAttackCoreAsync / ResolveFleeCoreAsync). El mensaje del raid es UNO solo y
// compartido por todos los participantes (session.ReplyTarget): con un botón se edita ese mensaje; con un comando de texto ("aa attack", "aa ability", "aa flee") se manda un
// MENSAJE NUEVO con botones nuevos, se reapunta la sesión a él y al viejo se le sacan los botones. Los botones nuevos andan para todos: sus ids llevan el id del raid, no el del mensaje.
public interface IRaidTurnOutput
{
    // Un aviso que no es parte de la pelea ("no sos parte de este raid", "tu habilidad está en enfriamiento"...).
    Task NoteAsync(string text);

    // Muestra la pantalla del raid cuando la pelea sigue.
    Task ShowAsync(RaidSession session, Embed embed, MessageComponent components);

    // Se llama justo ANTES de resolver el final (victoria, derrota o raid abandonado), que edita session.ReplyTarget: por texto, deja ahí un mensaje nuevo para que el final
    // salga en el mensaje que el jugador está mirando.
    Task PrepareFinalAsync(RaidSession session);
}

public sealed class InteractionRaidTurnOutput(IDiscordInteraction interaction) : IRaidTurnOutput
{
    public Task NoteAsync(string text) => interaction.FollowupAsync(text, ephemeral: true);

    public Task ShowAsync(RaidSession session, Embed embed, MessageComponent components) => session.ReplyTarget.UpdateAsync(embed, components);

    public Task PrepareFinalAsync(RaidSession session) => Task.CompletedTask;
}

public sealed class MessageRaidTurnOutput(IMessageChannel channel) : IRaidTurnOutput
{
    public Task NoteAsync(string text) => channel.SendMessageAsync(text);

    public async Task ShowAsync(RaidSession session, Embed embed, MessageComponent components)
    {
        var sent = await channel.SendMessageAsync(embed: embed, components: components);
        await RebindAsync(session, sent);
    }

    public async Task PrepareFinalAsync(RaidSession session)
    {
        var placeholder = new EmbedBuilder()
            .WithTitle("🏁 Resolviendo el final del raid…")
            .WithColor(Color.DarkGrey)
            .Build();

        var sent = await channel.SendMessageAsync(embed: placeholder);
        await RebindAsync(session, sent);
    }

    // A partir de acá el raid se edita en el mensaje nuevo, y el viejo (el trabado) queda sin botones; si ya no se puede editar no pasa nada.
    private static async Task RebindAsync(RaidSession session, IUserMessage sent)
    {
        var previous = session.ReplyTarget;
        session.ReplyTarget = new MessageCombatReplyTarget(sent);

        try
        {
            await previous.ClearComponentsAsync();
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }
}
