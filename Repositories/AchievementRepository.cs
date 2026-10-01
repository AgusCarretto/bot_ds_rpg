using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class AchievementRepository(IDbConnectionFactory connectionFactory) : IAchievementRepository
{
    public async Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetClaimedTiersAsync(
        ulong discordId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ClaimRow>(new CommandDefinition(
            "SELECT achievement_key AS Key, tier AS Tier FROM achievement_claims WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return rows
            .GroupBy(r => r.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(r => r.Tier).ToHashSet());
    }

    public async Task<ClaimOutcome> ClaimTierAsync(
        ulong discordId, AchievementDefinition achievement, int tier, int zoneRank, CancellationToken cancellationToken = default)
    {
        if (tier < 1 || tier > achievement.Tiers.Count)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        var target = achievement.Tiers[tier - 1];

        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Igual que en las misiones: bloquear al jugador primero (cobros de la misma persona en fila, y de ahí sale su nivel).
        var user = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
        if (user is null)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        long value = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE((SELECT value FROM player_stats WHERE discord_id = @DiscordId AND stat_key = @StatKey), 0);",
            new { DiscordId = (long)discordId, StatKey = achievement.StatKind }, transaction: transaction, cancellationToken: cancellationToken));

        if (value < target.Threshold)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        int? inserted = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            INSERT INTO achievement_claims (discord_id, achievement_key, tier)
            VALUES (@DiscordId, @Key, @Tier)
            ON CONFLICT DO NOTHING
            RETURNING 1;
            """,
            new { DiscordId = (long)discordId, achievement.Key, Tier = tier }, transaction: transaction, cancellationToken: cancellationToken));

        if (inserted is null)
        {
            return new ClaimOutcome(ClaimStatus.AlreadyClaimed);
        }

        var receipt = await RewardPayer.PayAsync(connection, transaction, user, target.Reward, zoneRank, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ClaimOutcome(ClaimStatus.Claimed, receipt);
    }

    private sealed record ClaimRow(string Key, int Tier);
}
