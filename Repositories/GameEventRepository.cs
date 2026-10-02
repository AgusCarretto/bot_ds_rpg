using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class GameEventRepository(IDbConnectionFactory connectionFactory) : IGameEventRepository
{
    public async Task<long> RecordAsync(
        ulong discordId, string kind, int? zoneId, long amount, string? detail, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_events (discord_id, kind, zone_id, amount, detail)
            VALUES (@DiscordId, @Kind, @ZoneId, @Amount, @Detail);
            """,
            new { DiscordId = (long)discordId, Kind = kind, ZoneId = zoneId, Amount = amount, Detail = detail },
            transaction: transaction, cancellationToken: cancellationToken));

        long newValue = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO player_stats (discord_id, stat_key, value)
            VALUES (@DiscordId, @Kind, @Amount)
            ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = player_stats.value + EXCLUDED.value
            RETURNING value;
            """,
            new { DiscordId = (long)discordId, Kind = kind, Amount = amount },
            transaction: transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return newValue;
    }

    public async Task<IReadOnlyDictionary<string, long>> GetStatsAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<StatRow>(new CommandDefinition(
            "SELECT stat_key AS StatKey, value AS Value FROM player_stats WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.StatKey, r => r.Value);
    }

    public async Task<IReadOnlyList<EventTotal>> GetBreakdownAsync(
        ulong discordId, IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default)
    {
        if (kinds.Count == 0)
        {
            return [];
        }

        await using DbConnection connection = connectionFactory.CreateConnection();

        // SUM(bigint) es numeric: el ::bigint es para que Dapper lo lea como long (ver MissionRepository.GetProgressAsync).
        var rows = await connection.QueryAsync<EventTotal>(new CommandDefinition(
            """
            SELECT kind AS Kind, detail AS Detail, COUNT(*)::bigint AS Count, COALESCE(SUM(amount), 0)::bigint AS Amount
            FROM game_events
            WHERE discord_id = @DiscordId AND kind = ANY(@Kinds)
            GROUP BY kind, detail;
            """,
            new { DiscordId = (long)discordId, Kinds = kinds.ToArray() }, cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<RecentEvent>> GetRecentAsync(
        ulong discordId, IReadOnlyCollection<string> kinds, int limit, CancellationToken cancellationToken = default)
    {
        if (kinds.Count == 0 || limit <= 0)
        {
            return [];
        }

        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<RecentRow>(new CommandDefinition(
            """
            SELECT occurred_at AS OccurredAt, kind AS Kind, detail AS Detail, amount AS Amount
            FROM game_events
            WHERE discord_id = @DiscordId AND kind = ANY(@Kinds)
            ORDER BY occurred_at DESC, event_id DESC
            LIMIT @Limit;
            """,
            new { DiscordId = (long)discordId, Kinds = kinds.ToArray(), Limit = limit }, cancellationToken: cancellationToken));

        return rows.Select(r => new RecentEvent(DateTime.SpecifyKind(r.OccurredAt, DateTimeKind.Utc), r.Kind, r.Detail, r.Amount)).ToList();
    }

    private sealed record StatRow(string StatKey, long Value);

    private sealed class RecentRow
    {
        public DateTime OccurredAt { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string? Detail { get; init; }
        public long Amount { get; init; }
    }
}
