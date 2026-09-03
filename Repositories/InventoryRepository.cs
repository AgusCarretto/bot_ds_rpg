using System.Data;
using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class InventoryRepository(IDbConnectionFactory connectionFactory) : IInventoryRepository
{
    public async Task<IReadOnlyList<InventoryEntry>> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT i.name     AS "ItemName",
                   i.type     AS "Type",
                   i.rarity   AS "Rarity",
                   inv.quantity AS "Quantity",
                   i.emoji    AS "Emoji"
            FROM inventory inv
            JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = @DiscordId
            ORDER BY i.name;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { DiscordId = (long)discordId }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<InventoryEntry>(command);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<OwnedItem>> GetOwnedByTypeAsync(ulong discordId, string type, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {ItemSql.SelectColumns}, inv.quantity AS "Quantity"
            FROM inventory inv
            JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = @DiscordId AND i.type = @Type
            ORDER BY i.buy_price;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { DiscordId = (long)discordId, Type = type }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<Item, int, OwnedItem>(
            command, (item, quantity) => new OwnedItem(item, quantity), splitOn: "Quantity");
        return rows.AsList();
    }

    public async Task<int> GetQuantityAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT quantity
            FROM inventory
            WHERE discord_id = @DiscordId AND item_id = @ItemId;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { DiscordId = (long)discordId, ItemId = itemId }, cancellationToken: cancellationToken);
        // QuerySingleOrDefaultAsync<int> devuelve 0 (default) si no hay fila, que es exactamente "no lo tiene".
        return await connection.QuerySingleOrDefaultAsync<int>(command);
    }

    public async Task<bool> TryConsumeAsync(ulong discordId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Update guardado: si no tiene esa cantidad, el WHERE bloquea la actualización
            // y no devuelve fila (mismo patrón que ShopRepository.SellItemAsync).
            const string deductSql = """
                UPDATE inventory
                SET quantity = quantity - @Quantity
                WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= @Quantity
                RETURNING quantity;
                """;

            int? remaining = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                deductSql,
                new { DiscordId = (long)discordId, ItemId = itemId, Quantity = quantity },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (remaining is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (remaining == 0)
            {
                // Limpiamos la fila para no acumular basura (y que no aparezca como "x0" en /inventory).
                const string cleanupSql = "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity <= 0;";
                await connection.ExecuteAsync(new CommandDefinition(
                    cleanupSql, new { DiscordId = (long)discordId, ItemId = itemId }, transaction: transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
