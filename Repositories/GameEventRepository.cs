using System.Data.Common;
using BotDsRpg.Data;
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

    private sealed record StatRow(string StatKey, long Value);
}
