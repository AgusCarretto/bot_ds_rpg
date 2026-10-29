namespace BotDsRpg.GameData;

// Lo que hace falta saber de un ingrediente para ordenarlo: su tipo (items.type), su rareza, la zona del monstruo que lo suelta (solo los drops; null si no es un drop) y su nombre.
public readonly record struct IngredientSortKey(string Type, string Rarity, int? DropZone, string Name);

// El orden en que se muestran los ingredientes de una receta (v0.38.0, el dueño: «poner los drops primero por zona, o sea zona 5 4 3 2 1, y después seguidito los mats de mítico a común»):
// primero los DROPS de monstruo de la zona más alta a la más baja y después los materiales de recolección del más raro al más común. Dentro de lo igual, por nombre.
// Lo usa RecipeRepository, así el herrero, el recetario, /forge-special, /tips y el Carpintero muestran la misma lista en el mismo orden.
public static class IngredientOrder
{
    // Un drop de monstruo es un ítem de tipo Material (lo único de ese tipo que existe, ver «Item catalog is data-driven»).
    public static bool IsDrop(IngredientSortKey key) => key.Type == "Material";

    public static IEnumerable<T> Sort<T>(IEnumerable<T> ingredients, Func<T, IngredientSortKey> keyOf) =>
        ingredients
            .OrderBy(i => IsDrop(keyOf(i)) ? 0 : 1)
            .ThenByDescending(i => IsDrop(keyOf(i)) ? keyOf(i).DropZone ?? 0 : 0)
            .ThenByDescending(i => IsDrop(keyOf(i)) ? 0 : RarityCatalog.RankOf(keyOf(i).Rarity))
            .ThenBy(i => keyOf(i).Name, StringComparer.Ordinal);
}
