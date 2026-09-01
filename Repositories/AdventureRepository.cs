using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class AdventureRepository(IDbConnectionFactory connectionFactory) : IAdventureRepository
{
    public async Task<LevelUpOutcome?> ApplyRewardAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        int goldReward,
        int xpReward,
        int hpLost,
        int? droppedItemId,
        int droppedItemQuantity,
        CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            bool claimed = await CooldownGuard.TryClaimAsync(connection, transaction, discordId, commandName, cooldownDuration, cancellationToken);
            if (!claimed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            // FOR UPDATE: bloquea la fila hasta el commit para que otra operación concurrente
            // sobre el mismo usuario (ej. /heal en simultáneo) no pise este cálculo.
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            // El daño del combate se aplica antes del cálculo de nivel: si subir de nivel cura
            // al máximo, esa curación debe ganarle al golpe que acaba de recibir, no al revés.
            int hpAfterDamage = Math.Max(0, current.CurrentHp - hpLost);
            var leveling = LevelingCalculator.ApplyXpGain(current.Level, current.Xp, current.MaxHp, hpAfterDamage, xpReward);

            string updateSql = $"""
                UPDATE users
                SET gold = gold + @Gold, level = @Level, xp = @Xp, max_hp = @MaxHp, current_hp = @CurrentHp
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateSql,
                new
                {
                    DiscordId = (long)discordId,
                    Gold = goldReward,
                    Level = leveling.Level,
                    Xp = leveling.Xp,
                    MaxHp = leveling.MaxHp,
                    CurrentHp = leveling.CurrentHp,
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (droppedItemId is not null)
            {
                await InventoryUpsert.AddItemAsync(connection, transaction, discordId, droppedItemId.Value, droppedItemQuantity, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new LevelUpOutcome(updated, leveling.LevelsGained);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
