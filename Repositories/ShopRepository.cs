using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ShopRepository(IDbConnectionFactory connectionFactory) : IShopRepository
{
    public async Task<User?> BuyItemAsync(ulong discordId, int itemId, int quantity, int totalCost, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Update guardado: si no le alcanza el oro, el WHERE bloquea la actualización
            // y no devuelve fila.
            string deductGoldSql = $"""
                UPDATE users
                SET gold = gold - @TotalCost
                WHERE discord_id = @DiscordId AND gold >= @TotalCost
                RETURNING {UserSql.SelectColumns};
                """;

            var buyer = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                deductGoldSql,
                new { DiscordId = (long)discordId, TotalCost = totalCost },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (buyer is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, itemId, quantity, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return buyer;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<User?> SellItemAsync(ulong discordId, int itemId, int quantity, int totalRefund, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Update guardado: si no tiene esa cantidad, el WHERE bloquea la actualización
            // y no devuelve fila.
            const string deductInventorySql = """
                UPDATE inventory
                SET quantity = quantity - @Quantity
                WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= @Quantity
                RETURNING quantity;
                """;

            int? remaining = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                deductInventorySql,
                new { DiscordId = (long)discordId, ItemId = itemId, Quantity = quantity },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (remaining is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            if (remaining == 0)
            {
                // Limpiamos la fila para no acumular basura en el inventario.
                const string cleanupSql = "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity <= 0;";
                await connection.ExecuteAsync(new CommandDefinition(
                    cleanupSql, new { DiscordId = (long)discordId, ItemId = itemId }, transaction: transaction, cancellationToken: cancellationToken));
            }

            string creditGoldSql = $"""
                UPDATE users
                SET gold = gold + @Refund
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var seller = await connection.QuerySingleAsync<User>(new CommandDefinition(
                creditGoldSql, new { DiscordId = (long)discordId, Refund = totalRefund }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return seller;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SellAllOutcome?> SellAllAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE OF inv: solo bloqueamos las filas de inventory (que vamos a borrar),
            // no las de items (tabla de referencia compartida por todos los jugadores).
            const string selectInventorySql = """
                SELECT inv.quantity AS "Quantity", i.sell_price AS "SellPrice"
                FROM inventory inv
                JOIN items i ON i.item_id = inv.item_id
                WHERE inv.discord_id = @DiscordId
                FOR UPDATE OF inv;
                """;

            var rows = (await connection.QueryAsync<InventoryValueRow>(new CommandDefinition(
                selectInventorySql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken))).AsList();

            if (rows.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            int totalRefund = rows.Sum(row => row.Quantity * row.SellPrice);
            int itemsSoldCount = rows.Sum(row => row.Quantity);

            const string clearInventorySql = "DELETE FROM inventory WHERE discord_id = @DiscordId;";
            await connection.ExecuteAsync(new CommandDefinition(
                clearInventorySql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            string creditGoldSql = $"""
                UPDATE users
                SET gold = gold + @Refund
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var seller = await connection.QuerySingleAsync<User>(new CommandDefinition(
                creditGoldSql, new { DiscordId = (long)discordId, Refund = totalRefund }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new SellAllOutcome(seller, itemsSoldCount, totalRefund);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private sealed record InventoryValueRow(int Quantity, int SellPrice);
}
