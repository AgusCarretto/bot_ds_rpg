using System.Globalization;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

public sealed record WorldBossJoinResult(WorldBossJoinStatus Status, DateOnly Day, WorldBossDef Boss, int Count, TimeSpan UntilPlay, TimeSpan CooldownLeft);

public sealed record WorldBossListing(DateOnly Day, WorldBossDef Boss, IReadOnlyList<WorldBossEntry> Entries, TimeSpan UntilPlay);

// Lo que pasó al cerrar un día: o se peleó (hay golpes y premio) o se canceló porque no quedó nadie a quien pagar. ChannelId = dónde anunciarlo. Paid = lo que cobró cada anotado.
public sealed record WorldBossResolution(
    DateOnly Day, WorldBossDef Boss, bool Cancelled, ulong? ChannelId, int Participants, bool Defeated, double TotalPercent, int Hunts,
    IReadOnlyList<WorldBossHit> Hits, IReadOnlyList<WorldBossPaid> Paid);

public interface IWorldBossService
{
    Task<WorldBossJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default);

    // Los anotados del jefe de HOY y cuánto falta para que se pelee.
    Task<WorldBossListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    // Pelea todos los días anteriores a hoy que siguen abiertos (en orden). Si el bot estuvo apagado a la medianoche, se pelea apenas vuelve. Idempotente y seguro de llamar desde varios lados a la vez.
    Task<IReadOnlyList<WorldBossResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    // Pelea UN día puntual (null si ya estaba cerrado).
    Task<WorldBossResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default);
}

// La lógica del jefe mundial: anotarse, listar y, a la medianoche, juntar a los anotados con el ataque y la zona que tengan EN ESE MOMENTO, pelear (GameData/WorldBoss.cs, pura) y pagar (IWorldBossRepository).
public sealed class WorldBossService(
    IWorldBossRepository repository, IUserRepository userRepository, IItemRepository itemRepository, IZoneRepository zoneRepository, IGameEvents gameEvents) : IWorldBossService
{
    // Un solo ResolveDue a la vez: el reloj y otro llamador que se cruzan no hacen el trabajo dos veces (la guarda de la base igual evitaría el doble pago).
    private readonly SemaphoreSlim _resolveGate = new(1, 1);

    public async Task<WorldBossJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var day = ArenaRules.DayOf(utcNow);
        var outcome = await repository.JoinAsync(day, discordId, displayName, channelId, cancellationToken);

        int count = outcome.Status is WorldBossJoinStatus.NoAccount ? 0 : (await repository.GetEntriesAsync(day, cancellationToken)).Count;
        if (outcome.Status == WorldBossJoinStatus.Joined)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.WorldBossJoin, detail: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return new WorldBossJoinResult(outcome.Status, day, WorldBossRules.BossOf(day), count, ArenaRules.PlaysAtUtc(day) - utcNow, outcome.CooldownLeft);
    }

    public async Task<WorldBossListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var day = ArenaRules.DayOf(utcNow);
        return new WorldBossListing(day, WorldBossRules.BossOf(day), await repository.GetEntriesAsync(day, cancellationToken), ArenaRules.PlaysAtUtc(day) - utcNow);
    }

    public async Task<IReadOnlyList<WorldBossResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await _resolveGate.WaitAsync(cancellationToken);
        try
        {
            var resolutions = new List<WorldBossResolution>();

            foreach (var day in await repository.GetOpenDaysBeforeAsync(ArenaRules.DayOf(utcNow), cancellationToken))
            {
                try
                {
                    if (await ResolveDayAsync(day, cancellationToken) is { } resolution)
                    {
                        resolutions.Add(resolution);
                    }
                }
                catch (Exception ex)
                {
                    // Un día que falla no frena a los demás ni tira el reloj: queda abierto y se reintenta en el próximo ciclo.
                    BotLog.Error(ex);
                }
            }

            return resolutions;
        }
        finally
        {
            _resolveGate.Release();
        }
    }

    public async Task<WorldBossResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var info = await repository.GetDayAsync(day, cancellationToken);
        if (info is null || info.Status != "open")
        {
            return null;
        }

        var boss = WorldBossRules.BossOf(day);
        ulong? channelId = info.ChannelId;
        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync(cancellationToken));

        // Los peleadores salen del ataque y la zona que tienen AHORA (a la medianoche): hasta ese momento se pueden preparar.
        var fighters = new List<WorldBossFighter>();
        var ranks = new Dictionary<ulong, int>();
        foreach (var entry in await repository.GetEntriesAsync(day, cancellationToken))
        {
            var player = await userRepository.GetByDiscordIdAsync(entry.DiscordId, cancellationToken);
            if (player is null)
            {
                continue;
            }

            var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId, cancellationToken) : null;
            var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId, cancellationToken) : null;
            var profile = PlayerCombatProfileCalculator.Resolve(player, weapon, amulet, 0);

            int rank = Math.Max(1, ZoneRanking.RankOf(zones, player.CurrentZoneId));
            int minLevel = zones.Count >= rank ? zones[rank - 1].MinLevel : 1;
            fighters.Add(new WorldBossFighter(entry.DiscordId, entry.DisplayName, player.Level, rank, minLevel, profile.Damage));
            ranks[entry.DiscordId] = rank;
        }

        if (fighters.Count == 0)
        {
            return await repository.CancelDayAsync(day, cancellationToken)
                ? new WorldBossResolution(day, boss, true, channelId, 0, false, 0, 0, [], [])
                : null;
        }

        var outcome = WorldBossRules.Resolve(fighters, Random.Shared);
        var resolved = await repository.ResolveDayAsync(day, boss.Key, outcome, ranks, cancellationToken);
        if (!resolved.Resolved)
        {
            return null; // Otro proceso lo cerró primero: no se pagó nada acá.
        }

        foreach (var paid in resolved.Paid)
        {
            await gameEvents.RecordAsync(paid.DiscordId, GameEventKinds.WorldBossPaid, amount: paid.Gold, detail: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return new WorldBossResolution(day, boss, false, channelId, outcome.Hits.Count, outcome.Defeated, outcome.TotalPercent, outcome.Hunts, outcome.Hits, resolved.Paid);
    }
}
