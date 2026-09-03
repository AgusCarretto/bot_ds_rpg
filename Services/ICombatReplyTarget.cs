using Discord;

namespace BotDsRpg.Services;

// Abstrae "dónde editar el mensaje de un combate en curso" para que a CombatSessionService no
// le importe si la partida arrancó desde un slash command (una interacción) o desde un comando
// de texto tradicional ("aa hunt", un mensaje normal). Una vez que el jugador toca un botón, los
// turnos siguientes SIEMPRE llegan como interacción de componente (Discord así lo modela sin
// importar el origen del mensaje), así que en la práctica esto solo importa para el primer turno.
public interface ICombatReplyTarget
{
    Task UpdateAsync(Embed embed, MessageComponent components);
}

public sealed class InteractionCombatReplyTarget(IDiscordInteraction interaction) : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) =>
        interaction.ModifyOriginalResponseAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
}

public sealed class MessageCombatReplyTarget(IUserMessage message) : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) =>
        message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
}
