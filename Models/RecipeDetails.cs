namespace BotDsRpg.Models;

// Un ingrediente de una receta ya resuelto contra la tabla items (nombre incluido para no tener
// que volver a buscarlo al armar el embed de /forge recipes o el mensaje de error de /forge make).
public sealed record RecipeIngredientDetails(int ItemId, string ItemName, int Quantity);

// Una receta completa (recipes + recipe_ingredients + su ítem resultado, ya resueltos en una sola
// consulta) — ver Repositories/IRecipeRepository.cs.
public sealed record RecipeDetails(int RecipeId, Item ResultItem, int GoldCost, IReadOnlyList<RecipeIngredientDetails> Ingredients);
