using System.Data;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class RecipeRepository(IDbConnectionFactory connectionFactory) : IRecipeRepository
{
    public async Task<IReadOnlyList<RecipeDetails>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = connectionFactory.CreateConnection();

        // Dos consultas en vez de un solo JOIN de 3 tablas: evita duplicar la fila de la receta
        // una vez por ingrediente (y el remapeo manual que eso implicaría) — el catálogo es chico,
        // no vale la pena la complejidad de una sola query con GROUP BY/agregación en JSON.
        string headersSql = $"""
            SELECT r.recipe_id AS "RecipeId", r.gold_cost AS "GoldCost", {ItemSql.SelectColumns}
            FROM recipes r
            JOIN items i ON i.item_id = r.result_item_id
            ORDER BY i.name;
            """;

        var headers = (await connection.QueryAsync<RecipeHeaderRow, Item, RecipeHeaderRow>(
            new CommandDefinition(headersSql, cancellationToken: cancellationToken),
            (header, resultItem) => header with { ResultItem = resultItem },
            splitOn: "ItemId")).AsList();

        const string ingredientsSql = """
            SELECT ri.recipe_id AS "RecipeId", i.item_id AS "ItemId", i.name AS "ItemName", ri.quantity AS "Quantity", i.emoji AS "Emoji"
            FROM recipe_ingredients ri
            JOIN items i ON i.item_id = ri.item_id
            ORDER BY ri.recipe_id;
            """;

        var ingredientRows = (await connection.QueryAsync<IngredientRow>(
            new CommandDefinition(ingredientsSql, cancellationToken: cancellationToken))).AsList();
        var ingredientsByRecipe = ingredientRows.ToLookup(row => row.RecipeId);

        return headers
            .Select(header => new RecipeDetails(
                header.RecipeId,
                header.ResultItem!,
                header.GoldCost,
                ingredientsByRecipe[header.RecipeId]
                    .Select(i => new RecipeIngredientDetails(i.ItemId, i.ItemName, i.Quantity, i.Emoji))
                    .ToList()))
            .ToList();
    }

    public async Task<RecipeDetails?> GetByResultItemNameAsync(string resultItemName, CancellationToken cancellationToken = default)
    {
        // Sin distinguir mayúsculas ni tildes (mismo criterio que ItemRepository.GetByNameAsync),
        // reusando TextNormalization en vez de un segundo query con unaccent() — el catálogo es
        // chico, alcanza con traer todo y filtrar acá.
        var allRecipes = await GetAllAsync(cancellationToken);
        string normalized = TextNormalization.RemoveDiacritics(resultItemName).Trim();

        return allRecipes.FirstOrDefault(recipe =>
            string.Equals(TextNormalization.RemoveDiacritics(recipe.ResultItem.Name), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record RecipeHeaderRow(int RecipeId, int GoldCost)
    {
        public Item? ResultItem { get; init; }
    }

    private sealed record IngredientRow(int RecipeId, int ItemId, string ItemName, int Quantity, string? Emoji);
}
