namespace BotDsRpg.GameData;

public sealed record CraftIngredient(string ItemName, int Quantity);

public sealed record CraftRecipe(string ResultItemName, int GoldCost, IReadOnlyList<CraftIngredient> Ingredients);

// Recetas del herrero. Los nombres deben coincidir exactamente (sin distinguir mayúsculas)
// con los de la tabla items; CraftModule los resuelve a item_id antes de forjar.
// Agregar más entradas acá cuando el catálogo de armas/amuletos crezca.
public static class CraftingCatalog
{
    public static readonly IReadOnlyDictionary<string, CraftRecipe> Recipes =
        new List<CraftRecipe>
        {
            new("Hacha de Hierro MK3", 100,
            [
                new CraftIngredient("Hierro", 5),
                new CraftIngredient("Cuero de Jabalí", 2),
            ]),
            new("Amuleto del Levantador", 200,
            [
                new CraftIngredient("Piedra", 10),
                new CraftIngredient("Colmillo de Lobo", 5),
            ]),
        }.ToDictionary(recipe => recipe.ResultItemName, StringComparer.OrdinalIgnoreCase);
}
