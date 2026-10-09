using Discord;
using Discord.Interactions;

namespace BotDsRpg.Services;

// Entrega los avisos que dejó un comando (misión completada, logro, ¡SUBISTE DE NIVEL!; ver Services/IGameEvents.cs).
//
// POR QUÉ ES UN EVENTO Y NO UNA LLAMADA DESPUÉS DE ExecuteCommandAsync: el InteractionService de Discord.Net corre los comandos en modo
// asíncrono por defecto (RunMode.Async), o sea que ExecuteCommandAsync DEVUELVE ÉXITO APENAS LANZA el comando, con el comando todavía
// corriendo (peleando, escribiendo en la base...). Entregar los avisos ahí encontraba la cola vacía y el cartelito de "subiste de nivel"
// quedaba esperando hasta que el jugador tiraba OTRO comando. InteractionExecuted, en cambio, salta cuando el comando de verdad terminó.
// (Poner el servicio en modo síncrono lo arreglaba también, pero cada comando bloquearía el hilo del gateway y los de todos los demás
// jugadores esperarían detrás: no.) Los comandos de texto ("aa ...") corren en modo síncrono, así que ahí alcanza con llamar a DeliverAsync.
public static class NoticeDelivery
{
    // Engancha la entrega al final de cada comando de barra y de cada botón/menú. Se llama una vez al armar el bot.
    // Antes de entregar cuenta el comando (Services/CommandCounter.cs): va en el MISMO manejador y no en otra suscripción para que el orden sea seguro,
    // porque ese conteo puede cruzar un tramo de logro y su aviso tiene que estar en la cola cuando se entrega.
    // reminders (opcional): después de entregar, deja armados los recordatorios de cooldown del jugador (Services/ReminderService.cs).
    public static void Attach(InteractionService commands, IGameEvents events, IReminderService? reminders = null) =>
        commands.InteractionExecuted += async (_, context, result) =>
        {
            try
            {
                await CommandCounter.RecordSlashAsync(events, context, result);
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex); // contar un comando nunca tira abajo la entrega de los avisos
            }

            await DeliverAfterInteractionAsync(events, context);
            await SyncRemindersAsync(reminders, context);
        };

    // Los recordatorios salen del estado de los cooldowns DESPUÉS del comando (un botón de pelea que gana un jefe también cuenta). Nunca tira: SyncAsync atrapa todo.
    private static async Task SyncRemindersAsync(IReminderService? reminders, IInteractionContext context)
    {
        if (reminders is null)
        {
            return;
        }

        var interaction = context.Interaction;
        string? hint = interaction switch
        {
            IApplicationCommandInteraction command => command.Data.Name,
            IComponentInteraction component => component.Data.CustomId,
            _ => null, // los autocompletados no ejecutan nada
        };
        if (hint is null || interaction.ChannelId is not { } channelId)
        {
            return;
        }

        await reminders.SyncAsync(interaction.User.Id, channelId, hint);
    }

    private static async Task DeliverAfterInteractionAsync(IGameEvents events, IInteractionContext context)
    {
        try
        {
            // Los autocompletados no pueden recibir mensajes de seguimiento, así que ahí no se entrega nada.
            var interaction = context.Interaction;
            if (interaction.Type is not (InteractionType.ApplicationCommand or InteractionType.MessageComponent))
            {
                return;
            }

            await DeliverAsync(
                events,
                interaction.User.Id,
                notice => interaction.FollowupAsync(
                    string.IsNullOrEmpty(notice.Text) ? null : notice.Text, embed: notice.Embed, ephemeral: !notice.Public));
        }
        catch (Exception ex)
        {
            // Un evento que tira excepción no tiene a quién avisarle: se registra y listo (el comando ya salió bien).
            BotLog.Warn(ex);
        }
    }

    // Saca los avisos pendientes de un jugador y los manda con "send". Un aviso que no se pueda mandar (el canal ya no existe, el token venció)
    // se registra y se descarta: nunca tira abajo el comando que ya salió bien.
    public static async Task DeliverAsync(IGameEvents events, ulong userId, Func<GameNotice, Task> send)
    {
        foreach (var notice in events.TakeNotices(userId))
        {
            try
            {
                await send(notice);
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        }
    }
}
