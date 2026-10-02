using BotDsRpg.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;

// El reloj de la Arena: cada 30 segundos le pide al servicio que juegue los torneos de días anteriores que sigan abiertos, y anuncia el
// resultado. Eso hace que el torneo del día se juegue a las 00:00 de Uruguay (en el primer ciclo después de la medianoche) y que, si el bot
// estuvo apagado a esa hora, se juegue apenas vuelve — nada depende de que el proceso estuviera despierto justo a medianoche.
//
// El torneo se NARRA en el canal ronda por ronda (ArenaModule.BuildBroadcast), con unos segundos entre mensaje y mensaje.
// El anuncio va al canal donde se anotó el primero de ese torneo, salvo que Arena__ChannelId (el .env) diga otro. Un anuncio que no se pueda
// mandar (el canal ya no existe, el bot perdió el permiso) se registra y se descarta: el torneo ya se jugó y se ve con /arena results.
// Vive en Modules/ (y no en Services/) porque arma el mensaje con el mismo código que /arena results (ArenaModule.BuildAnnouncement).
public sealed class ArenaScheduler(IArenaService arena, DiscordSocketClient client, IConfiguration configuration)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    // Cuánto espera el bot entre un mensaje de la transmisión y el siguiente (para que se lea ronda por ronda y no sea un bloque de golpe).
    public static TimeSpan RoundPause { get; set; } = TimeSpan.FromSeconds(4);

    // Cómo se busca el canal. Por defecto, el cliente de Discord; las pruebas lo reemplazan por uno falso.
    public Func<ulong, Task<IMessageChannel?>>? ChannelLookup { get; set; }

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
            await TickAsync();
        }
        while (await timer.WaitForNextTickAsync());
    }

    // Un ciclo. Público para poder probarlo; nunca tira (un error se registra y el reloj sigue).
    public async Task TickAsync()
    {
        try
        {
            foreach (var resolution in await arena.ResolveDueAsync(DateTime.UtcNow))
            {
                await AnnounceAsync(resolution);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }
    }

    private async Task AnnounceAsync(ArenaResolution resolution)
    {
        try
        {
            ulong? channelId = ulong.TryParse(configuration["Arena:ChannelId"], out ulong configured) && configured != 0
                ? configured
                : resolution.ChannelId;
            if (channelId is not { } id || id == 0)
            {
                return;
            }

            var channel = ChannelLookup is not null
                ? await ChannelLookup(id)
                : client.GetChannel(id) as IMessageChannel ?? await client.Rest.GetChannelAsync(id) as IMessageChannel;
            if (channel is null)
            {
                BotLog.Warn(new InvalidOperationException($"No encontré el canal {id} para anunciar la Arena del {resolution.Day}."));
                return;
            }

            // El torneo se cuenta ronda por ronda (ArenaModule.BuildBroadcast). Si un mensaje falla a la mitad se corta ahí: el resultado
            // completo igual queda en /arena results.
            var messages = ArenaModule.BuildBroadcast(resolution);
            for (int i = 0; i < messages.Count; i++)
            {
                await channel.SendMessageAsync(messages[i].Content, embed: messages[i].Embed);
                if (i < messages.Count - 1 && RoundPause > TimeSpan.Zero)
                {
                    await Task.Delay(RoundPause);
                }
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }
}
