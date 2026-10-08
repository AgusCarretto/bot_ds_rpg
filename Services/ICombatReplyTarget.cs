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

    // Le saca los botones al mensaje SIN tocar su contenido. Lo usa el turno por texto ("aa attack"): manda un mensaje nuevo con botones nuevos y deja el viejo (el que se había
    // trabado) sin botones, para que nadie lo siga tocando. Por defecto no hace nada (los destinos de prueba no tienen que implementarlo).
    Task ClearComponentsAsync() => Task.CompletedTask;
}

public sealed class InteractionCombatReplyTarget(IDiscordInteraction interaction) : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) =>
        interaction.ModifyOriginalResponseAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });

    public Task ClearComponentsAsync() =>
        interaction.ModifyOriginalResponseAsync(props => props.Components = new ComponentBuilder().Build());
}

public sealed class MessageCombatReplyTarget(IUserMessage message) : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) =>
        message.ModifyAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });

    public Task ClearComponentsAsync() =>
        message.ModifyAsync(props => props.Components = new ComponentBuilder().Build());
}

// Un destino que se puede REAPUNTAR a otro mensaje: lo usa el turno por texto, que manda el mensaje nuevo DESPUÉS de avanzar la sesión (CombatSessionService.TryAdvance pide el
// destino del próximo turno antes de que ese mensaje exista). Mientras no se reapunta, edita el destino de antes.
public sealed class SwitchableCombatReplyTarget(ICombatReplyTarget initial) : ICombatReplyTarget
{
    private volatile ICombatReplyTarget _current = initial;

    public ICombatReplyTarget Current
    {
        get => _current;
        set => _current = value;
    }

    public Task UpdateAsync(Embed embed, MessageComponent components) => _current.UpdateAsync(embed, components);

    public Task ClearComponentsAsync() => _current.ClearComponentsAsync();
}
