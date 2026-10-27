using System.Collections.Concurrent;

namespace BotDsRpg.Services;

// Un jugador anotado en el tablón de crías de un servidor (v0.33.0): busca pareja de dragón de su mismo tier.
public sealed record DragonBoardEntry(ulong GuildId, ulong DiscordId, DateTime ListedUtc, DateTime ExpiresUtc);

public interface IDragonBoardService
{
    // Te anota en el tablón de ese servidor (si ya estabas, renueva el tiempo; si estabas en otro servidor, te muda). Una sola anotación por jugador.
    DragonBoardEntry List(ulong guildId, ulong discordId);

    // Te saca. True si estabas anotado.
    bool Remove(ulong discordId);

    // Tu anotación vigente (de cualquier servidor), o null si no estás o ya venció.
    DragonBoardEntry? Get(ulong discordId);

    // Lo que falta para que tu anotación venza, o null si no estás anotado.
    TimeSpan? Remaining(ulong discordId);

    // Los anotados vigentes de un servidor, los más nuevos primero.
    IReadOnlyList<DragonBoardEntry> Entries(ulong guildId);
}

// El tablón de crías (v0.33.0): una lista de «busco pareja de mi tier» por servidor, EN MEMORIA como las invitaciones de /trade y de /dragon-breed: anotarse dura unas horas y, si el bot se reinicia, la gente
// simplemente se vuelve a anotar. Por eso no hay tabla ni migración. Lo que NO guarda es el dragón de cada uno: el tier y si puede criar se leen de la base cada vez que se mira el tablón (Modules/DragonModule.BuildBoardAsync),
// así nunca muestra a alguien que ya subió de tier o que está descansando. El reloj se puede inyectar para probar el vencimiento.
public sealed class DragonBoardService : IDragonBoardService
{
    // Cuánto dura anotarse (un descanso de cría son 12 h: así quien se anota está, en general, cerca del teclado).
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<ulong, DragonBoardEntry> _entries = new();
    private readonly Func<DateTime> _now;

    public DragonBoardService() : this(() => DateTime.UtcNow)
    {
    }

    public DragonBoardService(Func<DateTime> now)
    {
        _now = now;
    }

    public DragonBoardEntry List(ulong guildId, ulong discordId)
    {
        Purge();

        var now = _now();
        var entry = new DragonBoardEntry(guildId, discordId, now, now + Lifetime);
        _entries[discordId] = entry;
        return entry;
    }

    public bool Remove(ulong discordId) => _entries.TryRemove(discordId, out _);

    public DragonBoardEntry? Get(ulong discordId) =>
        _entries.TryGetValue(discordId, out var entry) && entry.ExpiresUtc > _now() ? entry : null;

    public TimeSpan? Remaining(ulong discordId) => Get(discordId) is { } entry ? entry.ExpiresUtc - _now() : null;

    public IReadOnlyList<DragonBoardEntry> Entries(ulong guildId)
    {
        Purge();
        return _entries.Values.Where(e => e.GuildId == guildId).OrderByDescending(e => e.ListedUtc).ToList();
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
