using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class CraftingRepository(IDbConnectionFactory connectionFactory) : ICraftingRepository
{
    public async Task<CraftOutcome> CraftAsync(
        ulong discordId,
        int goldCost,
        IReadOnlyList<(int ItemId, string ItemName, int Quantity)> ingredients,
        int resultItemId,
        CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila del usuario hasta el commit, para que otra operación
            // concurrente (ej. /shop buy o /hunt en simultáneo) no pise esta validación.
            string selectUserSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var user = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectUserSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (user.Gold < goldCost)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CraftOutcome(false, null, $"te falta oro (necesitás {goldCost}, tenés {user.Gold}).");
            }

            const string selectQuantitySql = """
                SELECT quantity FROM inventory
                WHERE discord_id = @DiscordId AND item_id = @ItemId
                FOR UPDATE;
                """;

            foreach (var ingredient in ingredients)
            {
                int owned = await connection.QuerySingleOrDefaultAsync<int>(new CommandDefinition(
                    selectQuantitySql,
                    new { DiscordId = (long)discordId, ItemId = ingredient.ItemId },
                    transaction: transaction,
                    cancellationToken: cancellationToken));

                if (owned < ingredient.Quantity)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new CraftOutcome(false, null, $"te falta **{ingredient.ItemName}** (necesitás {ingredient.Quantity}, tenés {owned}).");
                }
            }

            // Ya validamos que alcanza con todo: descontamos oro e ingredientes, sumamos el resultado.
            const string deductGoldSql = "UPDATE users SET gold = gold - @GoldCost WHERE discord_id = @DiscordId;";
            await connection.ExecuteAsync(new CommandDefinition(
                deductGoldSql, new { DiscordId = (long)discordId, GoldCost = goldCost }, transaction: transaction, cancellationToken: cancellationToken));

            const string deductIngredientSql = """
                UPDATE inventory SET quantity = quantity - @Quantity
                WHERE discord_id = @DiscordId AND item_id = @ItemId;
                """;

            foreach (var ingredient in ingredients)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    deductIngredientSql,
                    new { DiscordId = (long)discordId, ItemId = ingredient.ItemId, Quantity = ingredient.Quantity },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
            }

            // Limpiamos las filas que quedaron en 0 para no acumular basura en el inventario.
            const string cleanupSql = "DELETE FROM inventory WHERE discord_id = @DiscordId AND quantity <= 0;";
            await connection.ExecuteAsync(new CommandDefinition(
                cleanupSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, resultItemId, 1, cancellationToken);

            string selectUpdatedUserSql = $"SELECT {UserSql.SelectColumns} FROM users WHERE discord_id = @DiscordId;";
            var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectUpdatedUserSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new CraftOutcome(true, updated, null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
