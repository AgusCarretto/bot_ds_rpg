using System.Globalization;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

public sealed record ArenaJoinResult(ArenaJoinStatus Status, DateOnly Day, int Count, TimeSpan UntilPlay);

public sealed record ArenaListing(DateOnly Day, IReadOnlyList<ArenaEntry> Entries, TimeSpan UntilPlay);

public sealed record ArenaResults(ArenaDayInfo Info, IReadOnlyList<ArenaMatchRow> Matches);

// Lo que pasó al cerrar un día: o se jugó (hay campeón y llave) o se canceló por falta de gente. ChannelId = dónde anunciarlo.
public sealed record ArenaResolution(
    DateOnly Day, bool Cancelled, ulong? ChannelId, ulong? WinnerId, string? WinnerName, int Participants, int Rounds,
    string? RewardText, int LevelsGained, int NewLevel, IReadOnlyList<ArenaMatchRow> Matches);

public interface IArenaService
{
    Task<ArenaJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default);

    // Los anotados del torneo de HOY y cuánto falta para que se juegue.
    Task<ArenaListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    // El último torneo jugado (null si todavía no hubo ninguno).
    Task<ArenaResults?> GetLatestResultsAsync(CancellationToken cancellationToken = default);

    // Juega todos los días anteriores a hoy que siguen abiertos (en orden). Si el bot estuvo apagado a la medianoche, el torneo se juega
    // apenas vuelve. Idempotente y seguro de llamar desde varios lados a la vez (scheduler, /arena results).
    Task<IReadOnlyList<ArenaResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    // Juega UN día puntual (null si ya estaba cerrado). ResolveDueAsync lo llama por cada día pendiente.
    Task<ArenaResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default);
}

// La lógica de la Arena: anotarse, listar, y a la medianoche armar la llave, pelear sola y pagar al campeón. La llave es pura
// (GameData/ArenaBracket.cs), las peleas son el motor de PvP (GameData/DuelEngine.cs, con la política de usar la habilidad apenas está lista)
// y lo único que toca la base es IArenaRepository (el cierre del día y el premio, en una transacción).
public sealed class ArenaService(
    IArenaRepository arenaRepository, IDuelFighterFactory fighterFactory, IUserRepository userRepository,
    IZoneRepository zoneRepository, IGameEvents gameEvents) : IArenaService
{
    // Un solo ResolveDue a la vez: el scheduler y un /arena results que se cruzan no hacen el trabajo dos veces (la guarda de la base
    // igual evitaría el doble pago, esto evita simular dos veces).
    private readonly SemaphoreSlim _resolveGate = new(1, 1);

    public async Task<ArenaJoinResult> JoinAsync(
        ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var day = ArenaRules.DayOf(utcNow);
        var status = await arenaRepository.JoinAsync(day, discordId, displayName, channelId, ArenaRules.MaxPlayers, cancellationToken);

        int count = status is ArenaJoinStatus.NoAccount ? 0 : (await arenaRepository.GetEntriesAsync(day, cancellationToken)).Count;
        if (status == ArenaJoinStatus.Joined)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.ArenaJoin, detail: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return new ArenaJoinResult(status, day, count, ArenaRules.PlaysAtUtc(day) - utcNow);
    }

    public async Task<ArenaListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var day = ArenaRules.DayOf(utcNow);
        var entries = await arenaRepository.GetEntriesAsync(day, cancellationToken);
        return new ArenaListing(day, entries, ArenaRules.PlaysAtUtc(day) - utcNow);
    }

    public async Task<ArenaResults?> GetLatestResultsAsync(CancellationToken cancellationToken = default)
    {
        var info = await arenaRepository.GetLatestResolvedAsync(cancellationToken);
        if (info is null)
        {
            return null;
        }

        return new ArenaResults(info, await arenaRepository.GetMatchesAsync(info.Day, cancellationToken));
    }

    public async Task<IReadOnlyList<ArenaResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await _resolveGate.WaitAsync(cancellationToken);
        try
        {
            var resolutions = new List<ArenaResolution>();

            foreach (var day in await arenaRepository.GetOpenDaysBeforeAsync(ArenaRules.DayOf(utcNow), cancellationToken))
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
                    // Un día que falla no frena a los demás ni tira el scheduler: queda abierto y se reintenta en el próximo ciclo.
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

    public async Task<ArenaResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var info = await arenaRepository.GetDayAsync(day, cancellationToken);
        if (info is null || info.Status != "open")
        {
            return null;
        }

        // Los luchadores salen del equipo y el nivel que tienen AHORA (a la medianoche): hasta ese momento se pueden preparar.
        var entries = await arenaRepository.GetEntriesAsync(day, cancellationToken);
        var fighters = new Dictionary<ulong, DuelFighter>();
        foreach (var entry in entries)
        {
            if (await fighterFactory.BuildAsync(entry.DiscordId, entry.DisplayName, cancellationToken) is { } fighter)
            {
                fighters[entry.DiscordId] = fighter;
            }
        }

        if (fighters.Count < ArenaRules.MinPlayers)
        {
            bool cancelled = await arenaRepository.CancelDayAsync(day, fighters.Count, cancellationToken);
            return cancelled
                ? new ArenaResolution(day, true, Channel(info), null, null, fighters.Count, 0, null, 0, 0, [])
                : null;
        }

        var entrants = fighters.Values.Select(f => new ArenaEntrant(f.DiscordId, f.Name)).ToList();
        var bracket = ArenaBracket.Run(entrants, Random.Shared, (p1, p2) =>
        {
            var result = DuelEngine.Simulate(fighters[p1.DiscordId], fighters[p2.DiscordId]);
            return new ArenaFightOutcome(result.Winner.Fighter.DiscordId, result.Actions);
        });

        var rows = bracket.Matches
            .Select(m => new ArenaMatchRow(m.Round, m.Slot, m.P1.DiscordId, m.P1.Name, m.P2?.DiscordId, m.P2?.Name, m.WinnerId, m.Actions))
            .ToList();

        // El premio escala con la zona del campeón (igual que las misiones y los logros).
        var champion = await userRepository.GetByDiscordIdAsync(bracket.Champion.DiscordId, cancellationToken);
        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync(cancellationToken));
        int zoneRank = Math.Max(1, ZoneRanking.RankOf(zones, champion?.CurrentZoneId ?? 0));

        var outcome = await arenaRepository.ResolveDayAsync(
            day, rows, fighters.Count, bracket.Rounds, bracket.Champion.DiscordId, bracket.Champion.Name,
            ArenaRules.ChampionReward, zoneRank, cancellationToken);

        if (!outcome.Resolved)
        {
            return null; // Otro proceso lo cerró primero: no se pagó nada acá.
        }

        string dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await gameEvents.RecordAsync(bracket.Champion.DiscordId, GameEventKinds.ArenaWin, champion?.CurrentZoneId, detail: dayKey);
        if (outcome.Receipt is { LevelsGained: > 0 } leveled)
        {
            await gameEvents.RecordAsync(
                bracket.Champion.DiscordId, GameEventKinds.LevelUp, champion?.CurrentZoneId, leveled.LevelsGained, leveled.NewLevel.ToString());
        }

        return new ArenaResolution(
            day, false, Channel(info), bracket.Champion.DiscordId, bracket.Champion.Name, fighters.Count, bracket.Rounds,
            outcome.Receipt is { } receipt ? MissionRewards.Describe(receipt.Reward) : null,
            outcome.Receipt?.LevelsGained ?? 0, outcome.Receipt?.NewLevel ?? 0, rows);
    }

    private static ulong? Channel(ArenaDayInfo info) => info.ChannelId;
}
