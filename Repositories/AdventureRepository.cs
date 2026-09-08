using System.Data;
using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class AdventureRepository(IDbConnectionFactory connectionFactory) : IAdventureRepository
{
    public async Task<bool> TryClaimCooldownAsync(ulong discordId, string commandName, TimeSpan cooldownDuration, CancellationToken cancellationToken = default)
    {
        // Mismo upsert "guardado" que CooldownGuard (Repositories/TransactionalHelpers.cs), pero
        // standalone: acá no hace falta una transacción explícita, la sentencia ya es atómica.
        const string sql = """
            INSERT INTO cooldowns (discord_id, command_name, last_executed_at)
            VALUES (@DiscordId, @CommandName, now())
            ON CONFLICT (discord_id, command_name) DO UPDATE
                SET last_executed_at = EXCLUDED.last_executed_at
                WHERE cooldowns.last_executed_at <= now() - @CooldownInterval
            RETURNING last_executed_at;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, CommandName = commandName, CooldownInterval = cooldownDuration },
            cancellationToken: cancellationToken);

        DateTime? applied = await connection.QuerySingleOrDefaultAsync<DateTime?>(command);
        return applied is not null;
    }

    public Task<LevelUpOutcome> ApplyVictoryAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        CancellationToken cancellationToken = default) =>
        ApplyVictoryInternalAsync(discordId, goldReward, xpReward, hpDelta, droppedItemId, droppedItemQuantity, clearedZoneId: null, cancellationToken);

    public Task<LevelUpOutcome> ApplyBossVictoryAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        int clearedZoneId,
        CancellationToken cancellationToken = default) =>
        ApplyVictoryInternalAsync(discordId, goldReward, xpReward, hpDelta, droppedItemId, droppedItemQuantity, clearedZoneId, cancellationToken);

    private async Task<LevelUpOutcome> ApplyVictoryInternalAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        int? clearedZoneId,
        CancellationToken cancellationToken)
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

            var leveled = await LevelingApplier.ApplyAsync(connection, transaction, current, xpReward, hpDelta, cancellationToken);

            // Segunda UPDATE solo para el oro (no es parte del nivelado): mantiene LevelingApplier
            // genérico y reusable por ProgressionRepository, que no reparte oro. Si es un jefe
            // (clearedZoneId no nulo), la MISMA sentencia sube highest_zone_cleared con GREATEST
            // (nunca lo baja) — atómico con el resto, sin una tercera ida a la base.
            string updateGoldSql = $"""
                UPDATE users
                SET gold = gold + @Gold,
                    highest_zone_cleared = CASE
                        WHEN @ClearedZoneId IS NOT NULL THEN GREATEST(highest_zone_cleared, @ClearedZoneId)
                        ELSE highest_zone_cleared
                    END
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var finalUser = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateGoldSql,
                new { DiscordId = (long)discordId, Gold = goldReward, ClearedZoneId = clearedZoneId },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (droppedItemId is not null)
            {
                await InventoryUpsert.AddItemAsync(connection, transaction, discordId, droppedItemId.Value, droppedItemQuantity, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new LevelUpOutcome(finalUser, leveled.LevelsGained);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
