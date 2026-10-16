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

    public async Task<DailyClaimOutcome> ClaimDailyAsync(ulong discordId, int zoneRank, CancellationToken cancellationToken = default)
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

            // v0.18.2: el premio del día (oro, EXP con subida de nivel y la caja que toque) escala con la zona y el nivel, y lo paga el mismo RewardPayer que usan las misiones y los logros,
            // DENTRO de esta transacción: si la caja del día no existe (zone_boxes o items mal cargados) tira y se revierte TODO, incluida la racha, en vez de cobrar sin la caja.
            var receipt = await RewardPayer.PayAsync(connection, transaction, current, DailyRewardCalculator.SpecFor(calculation.NewStreak), zoneRank, cancellationToken);

            // Segunda UPDATE para las columnas de la racha (daily_streak y el momento del claim): el oro y la EXP ya los puso el pago de arriba. Devuelve la fila final del jugador.
            string updateExtrasSql = $"""
                UPDATE users
                SET daily_streak = @Streak, last_daily_claim = @Now
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var finalUser = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateExtrasSql,
                new { DiscordId = (long)discordId, Streak = calculation.NewStreak, Now = now },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new DailyClaimOutcome(calculation, new LevelUpOutcome(finalUser, receipt.LevelsGained), receipt);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
