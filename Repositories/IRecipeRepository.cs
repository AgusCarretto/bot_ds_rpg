using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Recetas del herrero, ahora en la base (tablas "recipes"/"recipe_ingredients", ver
// Database/add_recipes_tables.sql) en vez de en código — ver Modules/ForgeModule.cs.
public interface IRecipeRepository
{
    // Catálogo completo, cada receta con sus ingredientes ya resueltos. Para /forge recipes.
    Task<IReadOnlyList<RecipeDetails>> GetAllAsync(CancellationToken cancellationToken = default);

    // Busca por el nombre de su ítem resultado, sin distinguir mayúsculas ni tildes (mismo
    // criterio que IItemRepository.GetByNameAsync). Null si no existe. Para /forge make.
    Task<RecipeDetails?> GetByResultItemNameAsync(string resultItemName, CancellationToken cancellationToken = default);
}
