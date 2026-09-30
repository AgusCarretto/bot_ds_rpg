namespace BotDsRpg.Models;

// Un ingrediente de una receta ya resuelto contra la tabla items (nombre incluido para no tener
// que volver a buscarlo al armar el embed de /forge recipes o el mensaje de error de /forge make).
public sealed record RecipeIngredientDetails(int ItemId, string ItemName, int Quantity, string? Emoji);

// Una receta completa (recipes + recipe_ingredients + su ítem resultado, ya resueltos en una sola
// consulta) — ver Repositories/IRecipeRepository.cs.
// ZoneId: a qué zona pertenece la receta (recipes.zone_id) — /forge recipes muestra solo las de la zona del
// jugador. Null = sin zona asignada. Affinity: es el arma de afinidad de una clase en esa zona (la de la
// familia de su clase; solo la ve un jugador de esa clase) — false para armas generales y amuletos. Ver
// GameData/RecipeCatalog.cs.
public sealed record RecipeDetails(
    int RecipeId, Item ResultItem, int GoldCost, IReadOnlyList<RecipeIngredientDetails> Ingredients,
    int? ZoneId = null, bool Affinity = false);
