using System.Collections.Concurrent;

namespace BotDsRpg.Services;

// Un jugador anotado en el tablón de crías (v0.33.0): busca pareja de dragón de su mismo tier. GuildId y ChannelId son DÓNDE se anotó (v0.62.0: ahí le llega el aviso cuando se anota
// alguien de su tier); la anotación en sí vale para todos los servidores.
public sealed record DragonBoardEntry(ulong GuildId, ulong DiscordId, DateTime ListedUtc, DateTime ExpiresUtc, ulong ChannelId = 0);

public interface IDragonBoardService
{
    // Te anota (si ya estabas, renueva el tiempo y pasa a avisarte en este canal). Una sola anotación por jugador.
    DragonBoardEntry List(ulong guildId, ulong discordId, ulong channelId = 0);

    // Te saca. True si estabas anotado.
    bool Remove(ulong discordId);

    // Tu anotación vigente, o null si no estás o ya venció.
    DragonBoardEntry? Get(ulong discordId);

    // Lo que falta para que tu anotación venza, o null si no estás anotado.
    TimeSpan? Remaining(ulong discordId);

    // Los anotados vigentes que SE ANOTARON en ese servidor, los más nuevos primero (lo de antes de la v0.62.0; queda para las pruebas y el diagnóstico).
    IReadOnlyList<DragonBoardEntry> Entries(ulong guildId);

    // TODOS los anotados vigentes, los más nuevos primero. Cada tablón filtra a los que son miembros de su servidor (DragonModule.BuildBoardAsync).
    IReadOnlyList<DragonBoardEntry> All();

    // ¿Puede este jugador disparar el aviso de «se anotó alguien de tu tier»? Sí una vez cada `every` (así entrar y salir del tablón no llena de menciones a nadie). Lo deja marcado si devuelve true.
    bool TryClaimNotify(ulong discordId, TimeSpan every);
}

// El tablón de crías (v0.33.0), EN MEMORIA como las invitaciones de /trade y de /dragon-breed: anotarse dura unas horas y, si el bot se reinicia, la gente simplemente se vuelve a anotar. Por eso no hay tabla
// ni migración. Lo que NO guarda es el dragón de cada uno: el tier y si puede criar se leen de la base cada vez que se mira el tablón (Modules/DragonModule.BuildBoardAsync), así nunca muestra a alguien que ya
// subió de tier o que está descansando. El reloj se puede inyectar para probar el vencimiento.
// v0.62.0 (el dueño, 2026-11-17: «que un jugador aparezca en todos los servers en que se encuentre… así si otra persona juega en otro server, puedo hacer breed en el oficial»): la anotación es UNA y global;
// cada servidor muestra a los anotados que son miembros de ese servidor (los dos se pueden cruzar ahí y criar con la invitación de siempre).
public sealed class DragonBoardService : IDragonBoardService
{
    // Cuánto dura anotarse (un descanso de cría son 12 h: así quien se anota está, en general, cerca del teclado).
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<ulong, DragonBoardEntry> _entries = new();
    private readonly ConcurrentDictionary<ulong, DateTime> _lastNotify = new();
    private readonly Func<DateTime> _now;

    public DragonBoardService() : this(() => DateTime.UtcNow)
    {
    }

    public DragonBoardService(Func<DateTime> now)
    {
        _now = now;
    }

    public DragonBoardEntry List(ulong guildId, ulong discordId, ulong channelId = 0)
    {
        Purge();

        var now = _now();
        var entry = new DragonBoardEntry(guildId, discordId, now, now + Lifetime, channelId);
        _entries[discordId] = entry;
        return entry;
    }

    public bool Remove(ulong discordId) => _entries.TryRemove(discordId, out _);

    public DragonBoardEntry? Get(ulong discordId) =>
        _entries.TryGetValue(discordId, out var entry) && entry.ExpiresUtc > _now() ? entry : null;

    public TimeSpan? Remaining(ulong discordId) => Get(discordId) is { } entry ? entry.ExpiresUtc - _now() : null;

    public IReadOnlyList<DragonBoardEntry> Entries(ulong guildId) => All().Where(e => e.GuildId == guildId).ToList();

    public IReadOnlyList<DragonBoardEntry> All()
    {
        Purge();
        return _entries.Values.OrderByDescending(e => e.ListedUtc).ToList();
    }

    public bool TryClaimNotify(ulong discordId, TimeSpan every)
    {
        var now = _now();
        while (true)
        {
            if (!_lastNotify.TryGetValue(discordId, out var last))
            {
                if (_lastNotify.TryAdd(discordId, now))
                {
                    return true;
                }

                continue;
            }

            if (now - last < every)
            {
                return false;
            }

            if (_lastNotify.TryUpdate(discordId, now, last))
            {
                return true;
            }
        }
    }

    private void Purge()
    {
        var now = _now();
        foreach (var expired in _entries.Values.Where(e => e.ExpiresUtc <= now))
        {
            _entries.TryRemove(expired.DiscordId, out _);
        }
    }
}
