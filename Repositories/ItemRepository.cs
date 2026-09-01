using System.Data;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ItemRepository(IDbConnectionFactory connectionFactory) : IItemRepository
{
    public async Task<Item?> GetRandomByRarityAsync(string rarity, CancellationToken cancellationToken = default)
    {
        // ORDER BY random() es aceptable acá porque el catálogo de ítems es chico;
        // no usar este patrón sobre tablas grandes.
        const string sql = """
            SELECT item_id     AS "ItemId",
                   name        AS "Name",
                   type        AS "Type",
                   rarity      AS "Rarity",
                   stat_value  AS "StatValue",
                   sell_price  AS "SellPrice"
            FROM items
            WHERE rarity = @Rarity
            ORDER BY random()
            LIMIT 1;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { Rarity = rarity }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Item>(command);
    }

    public async Task<Item?> GetRandomByTypeAndRarityAsync(string type, string rarity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT item_id     AS "ItemId",
                   name        AS "Name",
                   type        AS "Type",
                   rarity      AS "Rarity",
                   stat_value  AS "StatValue",
                   sell_price  AS "SellPrice"
            FROM items
            WHERE type = @Type AND rarity = @Rarity
            ORDER BY random()
            LIMIT 1;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { Type = type, Rarity = rarity }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Item>(command);
    }
}
