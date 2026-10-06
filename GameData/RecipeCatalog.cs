using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Cómo se agrupa una receta al mostrarla (ver RecipeCatalog.GroupOf).
public enum RecipeGroup { ClassWeapon, GeneralWeapon, Amulet, Other }

// Lo que un jugador ve del herrero: SOLO las recetas de su zona, y de esas solo las que le corresponden.
// El molde de cada zona son 8 recetas — 4 armas de AFINIDAD (una por clase, la de la familia de su
// clase) + 2 armas GENERALES + 2 amuletos (los amuletos son siempre generales) — de las que cada
// jugador ve 5: su arma de afinidad, las 2 generales y los 2 amuletos. Así el mensaje de /forge recipes
// y la lista de /forge make entran sobrados en los límites de Discord (6000 caracteres por embed).
//
// Es lógica pura (sin I/O): /forge recipes (ForgeModule) y la lista desplegable de /forge make
// (ForgeAutocomplete) tienen que mostrar EXACTAMENTE lo mismo, por eso comparten esto.
public static class RecipeCatalog
{
    // La zona que se muestra (Zone null = no hay nada para mostrar), y si es una zona anterior a la del
    // jugador (IsFallback).
    // GateRecipes (v0.11.0): las recetas de El Fogón Eterno (zona 0, GameData/FogonRules.cs) que el jugador puede ver; ya están DENTRO de Recipes (la lista que usan el menú y el
    // autocompletado de /forge) y vienen aparte para que la página de recetas las muestre en su propio bloque.
    public sealed record RecipeView(Zone? Zone, bool IsFallback, IReadOnlyList<RecipeDetails> Recipes, IReadOnlyList<RecipeDetails>? GateRecipes = null);

    // includeGate: el jugador ya venció al jefe de la última zona (FogonRules.IsGateOpen): se suman las recetas del Fogón (el equipo para entrar a la zona 0).
    public static RecipeView ViewFor(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, string playerClass, int currentZoneId, bool includeGate = false)
    {
        var zone = ZoneToShow(zones, recipes, currentZoneId);
        var gateRecipes = includeGate
            ? recipes.Where(r => r.ZoneId == FogonRules.GateZoneId && IsVisibleTo(r, playerClass)).ToList()
            : [];

        if (zone is null)
        {
            return new RecipeView(null, false, gateRecipes, gateRecipes);
        }

        var visible = recipes
            .Where(r => r.ZoneId == zone.ZoneId && IsVisibleTo(r, playerClass))
            .Concat(gateRecipes)
            .ToList();

        return new RecipeView(zone, zone.ZoneId != currentZoneId, visible, gateRecipes);
    }

    // La zona actual del jugador; si esa todavía no tiene ninguna receta cargada (hoy solo hay de Zona 1),
    // la zona anterior más cercana que sí, para no dejar sin herrería a quien ya avanzó. Null si ninguna
    // zona hasta la actual tiene recetas. El orden es por dificultad (min_level), nunca por zone_id crudo.
    public static Zone? ZoneToShow(IReadOnlyList<Zone> zones, IEnumerable<RecipeDetails> recipes, int currentZoneId)
    {
        var ordered = ZoneRanking.OrderByDifficulty(zones);
        if (ordered.Count == 0)
        {
            return null;
        }

        int rank = Math.Max(1, ZoneRanking.RankOf(ordered, currentZoneId)); // zona desconocida: se parte de la primera
        var zonesWithRecipes = recipes.Where(r => r.ZoneId is not null).Select(r => r.ZoneId!.Value).ToHashSet();

        for (int i = rank; i >= 1; i--)
        {
            if (zonesWithRecipes.Contains(ordered[i - 1].ZoneId))
            {
                return ordered[i - 1];
            }
        }

        return null;
    }

    // Una receta la ve el jugador si no es exclusiva de otra clase, y — si es un arma de afinidad — si es la
    // de SU clase (ClassWeaponSynergy.Applies: la familia del arma coincide con el tipo de arma de la clase).
    public static bool IsVisibleTo(RecipeDetails recipe, string playerClass) =>
        (recipe.ResultItem.ClassRequirement is null
            || string.Equals(recipe.ResultItem.ClassRequirement, playerClass, StringComparison.OrdinalIgnoreCase))
        && (!recipe.Affinity || ClassWeaponSynergy.Applies(playerClass, recipe.ResultItem.WeaponFamily));

    public static RecipeGroup GroupOf(RecipeDetails recipe) => recipe.ResultItem.Type switch
    {
        "Amulet" => RecipeGroup.Amulet,
        "Weapon" => recipe.Affinity ? RecipeGroup.ClassWeapon : RecipeGroup.GeneralWeapon,
        _ => RecipeGroup.Other,
    };
}
