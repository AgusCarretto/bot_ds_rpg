using Discord;

namespace BotDsRpg.Services;

// Por dónde le habla un turno de combate solitario al jugador. El turno se resuelve en UN solo lugar (AdventureModule.ResolveTurnCoreAsync) y puede llegar de dos maneras:
//   · un botón (interacción): la pantalla se EDITA en el mismo mensaje y los avisos son efímeros (solo los ve quien tocó);
//   · un comando de texto ("aa attack", "aa ability", "aa flee"): el botón se traba a veces (Discord da 3 segundos para contestar un clic y si el bot demora dice "no respondió"), así
//     que por texto la pantalla sale como un MENSAJE NUEVO, con botones nuevos, y al mensaje viejo se le sacan los botones.
public interface ICombatTurnOutput
{
    // Un aviso que no es parte de la pelea ("no tenés combate activo", "tu habilidad está en enfriamiento"...).
    Task NoteAsync(string text);

    // El destino con el que sigue la sesión cuando el combate continúa (lo pide TryAdvance ANTES de que la pantalla del turno exista). previous = el destino de antes.
    ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous);

    // Muestra la pantalla del turno. next = lo que devolvió NextReplyTarget (null si la pelea terminó); previous = el destino que tenía la sesión antes de este turno.
    Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous);
}

// El botón de siempre: edita el mensaje que se tocó.
public sealed class InteractionCombatTurnOutput(IDiscordInteraction interaction) : ICombatTurnOutput
{
    public Task NoteAsync(string text) => interaction.FollowupAsync(text, ephemeral: true);

    public ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous) => new InteractionCombatReplyTarget(interaction);

    public Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous) =>
        interaction.ModifyOriginalResponseAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
}

// El turno por texto: un mensaje nuevo en el canal donde escribió, con sus botones; el combate sigue editando ESE mensaje (timeout incluido) y el viejo queda sin botones.
public sealed class MessageCombatTurnOutput(IMessageChannel channel) : ICombatTurnOutput
{
    public Task NoteAsync(string text) => channel.SendMessageAsync(text);

    public ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous) => new SwitchableCombatReplyTarget(previous);

    public async Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous)
    {
        var sent = await channel.SendMessageAsync(embed: embed, components: components);

        // A partir de acá la sesión edita el mensaje nuevo (el destino se creó antes de que existiera: se reapunta).
        if (next is SwitchableCombatReplyTarget live)
        {
            live.Current = new MessageCombatReplyTarget(sent);
        }

        // El mensaje viejo (el trabado) sin botones. Si ya no se puede editar no pasa nada: el combate sigue en el nuevo.
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
