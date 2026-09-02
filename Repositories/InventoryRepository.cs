using System.Data;
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
                   inv.quantity AS "Quantity"
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
}
