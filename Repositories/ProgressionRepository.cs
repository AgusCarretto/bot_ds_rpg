using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ProgressionRepository(IDbConnectionFactory connectionFactory) : IProgressionRepository
{
    public async Task<LevelUpOutcome> AddXpAsync(ulong discordId, int xpGained, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila hasta el commit para que otra operación concurrente
            // sobre el mismo usuario no pise este cálculo.
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            var outcome = await LevelingApplier.ApplyAsync(connection, transaction, current, xpGained, hpDelta: 0, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DailyClaimOutcome> ClaimDailyAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila hasta el commit para que otra operación concurrente
            // sobre el mismo usuario no pise este cálculo (ej. doble click en /daily).
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            var now = DateTime.UtcNow;
            var calculation = DailyRewardCalculator.Evaluate(current.LastDailyClaim, current.DailyStreak, now);

            if (calculation.Status == DailyClaimStatus.TooSoon)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DailyClaimOutcome(calculation, null);
            }

            var leveled = await LevelingApplier.ApplyAsync(connection, transaction, current, calculation.XpReward, hpDelta: 0, cancellationToken);

            // Segunda UPDATE para las columnas que no son de nivelado (oro, racha, timestamp del
            // claim): separada de LevelingApplier a propósito, para que ese helper quede genérico
            // y lo puedan reusar operaciones que no tocan racha diaria (ej. AddXpAsync arriba).
            string updateExtrasSql = $"""
                UPDATE users
                SET gold = gold + @Gold, daily_streak = @Streak, last_daily_claim = @Now
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var finalUser = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateExtrasSql,
                new { DiscordId = (long)discordId, Gold = calculation.GoldReward, Streak = calculation.NewStreak, Now = now },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new DailyClaimOutcome(calculation, new LevelUpOutcome(finalUser, leveled.LevelsGained));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
