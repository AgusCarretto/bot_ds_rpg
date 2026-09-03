using System.Data;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ItemRepository(IDbConnectionFactory connectionFactory) : IItemRepository
{
    public async Task<Item?> GetRandomByTypeAndRarityAsync(string type, string rarity, CancellationToken cancellationToken = default)
    {
        // ORDER BY random() es aceptable acá porque el catálogo de ítems es chico;
        // no usar este patrón sobre tablas grandes.
        string sql = $"""
            SELECT {ItemSql.SelectColumns}
            FROM items
            WHERE type = @Type AND rarity = @Rarity
            ORDER BY random()
            LIMIT 1;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { Type = type, Rarity = rarity }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Item>(command);
    }

    public async Task<Item?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // Igualdad exacta sin distinguir mayúsculas NI tildes (requiere la extensión unaccent,
        // ver Database/add_unaccent_extension.sql): "jabali" tiene que encontrar "Jabalí" sin
        // que el jugador tenga que escribir el acento desde el celular. NO usar ILIKE acá,
        // porque el nombre lo escribe el usuario y podría contener '%' o '_' (comodines de LIKE)
        // sin querer decir eso.
        string sql = $"""
            SELECT {ItemSql.SelectColumns}
            FROM items
            WHERE unaccent(LOWER(name)) = unaccent(LOWER(@Name))
            LIMIT 1;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { Name = name }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Item>(command);
    }

    public async Task<Item?> GetByIdAsync(int itemId, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {ItemSql.SelectColumns}
            FROM items
            WHERE item_id = @ItemId;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { ItemId = itemId }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Item>(command);
    }

    public async Task<IReadOnlyList<Item>> GetAllByTypeAsync(string type, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {ItemSql.SelectColumns}
            FROM items
            WHERE type = @Type
            ORDER BY name;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { Type = type }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<Item>(command);
        return rows.AsList();
    }
}
