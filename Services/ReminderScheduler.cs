using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.WebSocket;

namespace BotDsRpg.Services;

// El reloj de los recordatorios (GameData/Reminders.cs): cada 5 segundos saca de la cola los avisos vencidos y los manda al canal donde el jugador usó el comando.
//
// Reglas de cuándo NO avisar (todas en este archivo, para que se vean juntas):
//  - la política de elegibilidad (ReminderPolicy: hoy todos; mañana, la membresía);
//  - un aviso que llegó muy tarde porque el bot estuvo apagado (ReminderCatalog.IsStale): no se manda una ráfaga vieja al volver;
//  - las esperas CORTAS (cacería, talar, minar) si el jugador está en plena pelea o hizo algo hace menos de ActiveWindow: ya está jugando y el aviso sobra.
// Los avisos de esperas cortas se borran solos a los pocos minutos (ReminderKind.AutoDelete); los largos se quedan.
// Un aviso que no se pueda mandar (el canal ya no existe, el bot perdió el permiso) se registra y se descarta: nunca tira el reloj.
public sealed class ReminderScheduler(
    IReminderRepository reminders,
    IReminderService service,
    ICombatSessionService combat,
    IRaidSessionService raids,
    DiscordSocketClient client)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    // Cuánto se considera que el jugador "está jugando" después de su último comando.
    public static TimeSpan ActiveWindow { get; set; } = TimeSpan.FromSeconds(10);

    // Cómo se manda el mensaje: (jugador, canal, texto, borrar a los…). Por defecto, el cliente de Discord; las pruebas lo reemplazan por uno falso.
    public Func<ulong, ulong, string, TimeSpan?, Task>? Sender { get; set; }

    private int _started;

    // Idempotente: Discord.Net dispara Ready en cada reconexión y no tiene que arrancar un segundo reloj.
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await TickAsync(DateTime.UtcNow);
        }
        while (await timer.WaitForNextTickAsync());
    }

    // Un ciclo. Público para poder probarlo; nunca tira (un error se registra y el reloj sigue).
    public async Task TickAsync(DateTime nowUtc)
    {
        try
        {
            var due = await reminders.ClaimDueAsync(nowUtc);
            foreach (var group in due.GroupBy(d => (d.DiscordId, d.ChannelId)))
            {
                await DeliverAsync(group.Key.DiscordId, group.Key.ChannelId, group.ToList(), nowUtc);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }
    }

    // Decide qué de lo vencido se manda y lo manda en UN solo mensaje por jugador y canal.
    private async Task DeliverAsync(ulong discordId, ulong channelId, IReadOnlyList<DueReminder> due, DateTime nowUtc)
    {
        try
        {
            if (!ReminderPolicy.IsEligible(discordId))
            {
                return;
            }

            bool playing = IsPlaying(discordId, nowUtc);
            var kinds = due
                .Where(d => !ReminderCatalog.IsStale(d.Kind, d.DueAtUtc, nowUtc))
                .Where(d => !(playing && ReminderCatalog.IsShort(d.Kind)))
                .Select(d => d.Kind)
                .ToList();
            if (kinds.Count == 0)
            {
                return;
            }

            string text = ReminderCatalog.Compose(discordId, kinds);
            if (text.Length == 0)
            {
                return;
            }

            var autoDelete = ReminderCatalog.AutoDeleteFor(kinds);
            if (Sender is not null)
            {
                await Sender(discordId, channelId, text, autoDelete);
                return;
            }

            await SendToDiscordAsync(channelId, text, autoDelete);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    // Peleando (solo o en una raid) o con un comando de hace segundos.
    private bool IsPlaying(ulong discordId, DateTime nowUtc)
    {
        if (combat.Peek(discordId) is not null || raids.IsInAnyRaid(discordId))
        {
            return true;
        }

        return service.LastActivityUtc(discordId) is { } last && nowUtc - last < ActiveWindow;
    }

    private async Task SendToDiscordAsync(ulong channelId, string text, TimeSpan? autoDelete)
    {
        var channel = client.GetChannel(channelId) as IMessageChannel ?? await client.Rest.GetChannelAsync(channelId) as IMessageChannel;
        if (channel is null)
        {
            BotLog.Warn(new InvalidOperationException($"No encontré el canal {channelId} para mandar un recordatorio."));
            return;
        }

        // Solo se menciona a personas (el texto lleva únicamente la mención del jugador).
        var message = await channel.SendMessageAsync(text, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
        if (autoDelete is { } after)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(after);
                    await message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    BotLog.Warn(ex); // ya lo borraron a mano o el bot perdió el permiso: no importa
                }
            });
        }
    }
}
