using BotDsRpg.GameData;
using Discord;
using Discord.Interactions;

namespace BotDsRpg.Services;

// Cuenta los comandos que cada jugador usa (el evento "command_used", que alimenta el logro Comandante). Cuenta un comando que SE EJECUTÓ: que después
// el módulo conteste "estás en cooldown" o "no te alcanza el oro" también cuenta (el comando corrió y contestó); lo que no cuenta es lo que ni
// llegó a ejecutarse (un comando que no existe, un jugador sin cuenta rechazado en Program.cs) ni los botones y menús (no son comandos).
// "/start" y "aa start" quedan afuera a propósito: ahí el jugador todavía puede no tener cuenta (el contador cuelga de ella).
//
// Va en la misma entrega que los avisos (NoticeDelivery.Attach) y ANTES de entregarlos: si este comando es el que cruza un tramo del logro, el
// aviso "¡Logro desbloqueado!" tiene que estar en la cola cuando se entrega.
public static class CommandCounter
{
    private const string StartCommand = "start";

    // Después de un comando de barra. result es el de Discord.Net: IsSuccess = el método del comando terminó sin tirar excepción.
    public static Task RecordSlashAsync(IGameEvents events, IInteractionContext context, IResult result) =>
        context.Interaction is IApplicationCommandInteraction command && result.IsSuccess
            ? RecordAsync(events, command.User.Id, command.Data.Name)
            : Task.CompletedTask;

    // Después de un comando de texto ("aa ..."). commandText es lo que va después del prefijo.
    public static Task RecordTextAsync(IGameEvents events, ulong userId, string commandText, bool success)
    {
        if (!success)
        {
            return Task.CompletedTask;
        }

        string name = commandText.TrimStart().Split(' ', 2)[0];
        return RecordAsync(events, userId, name);
    }

    private static Task RecordAsync(IGameEvents events, ulong userId, string commandName) =>
        commandName.Equals(StartCommand, StringComparison.OrdinalIgnoreCase)
            ? Task.CompletedTask
            : events.RecordAsync(userId, GameEventKinds.CommandUsed);
}
