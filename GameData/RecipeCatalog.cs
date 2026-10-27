using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Cómo se agrupa una receta al mostrarla (ver RecipeCatalog.GroupOf).
public enum RecipeGroup { ClassWeapon, GeneralWeapon, Amulet, Other }

// Lo que un jugador ve del herrero. El molde de cada zona (v0.27.0) son 5 recetas — 4 armas de CLASE (una por clase, con su ATQ fijo: ya no hay sinergia ×1,5) + 1 amuleto — de las que cada jugador ve 2: el arma de su
// clase y el amuleto. Aparte hay UNA sola receta sin zona, la de la Hoja de Acero Puro (la única arma general, hecha solo con materiales de recolección), que ve todo el mundo. Así el mensaje de /forge recipes
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
    // Las recetas SIN ZONA (la de la Hoja de Acero Puro) van siempre: son la base que cualquiera puede comprarse.
    public static RecipeView ViewFor(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, string playerClass, int currentZoneId, bool includeGate = false)
    {
        var zone = ZoneToShow(zones, recipes, currentZoneId);
        var gateRecipes = includeGate
            ? recipes.Where(r => r.ZoneId == FogonRules.GateZoneId && IsVisibleTo(r, playerClass)).ToList()
            : [];
        var zoneless = recipes.Where(r => r.ZoneId is null && IsVisibleTo(r, playerClass)).ToList();

        if (zone is null)
        {
            return new RecipeView(null, false, gateRecipes.Concat(zoneless).ToList(), gateRecipes);
        }

        var visible = recipes
            .Where(r => r.ZoneId == zone.ZoneId && IsVisibleTo(r, playerClass))
            .Concat(zoneless)
            .Concat(gateRecipes)
            .ToList();

        return new RecipeView(zone, zone.ZoneId != currentZoneId, visible, gateRecipes);
    }

    // La PÁGINA CLÁSICA de una zona (v0.28.1, el recetario por defecto): lo mismo que ViewFor menos un detalle. La receta sin zona (la Hoja de Acero Puro) se muestra SOLO en la página de la primera zona (la más fácil), que
    // es donde empieza todo el mundo, y no se repite en las demás; el dueño: «el general que no es de zona dejalo en la zona 1». La lista para forjar sigue usando ViewFor (ahí la Hoja se ofrece desde cualquier zona).
    public static RecipeView PageFor(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, string playerClass, int currentZoneId, bool includeGate = false)
    {
        var view = ViewFor(recipes, zones, playerClass, currentZoneId, includeGate);
        if (view.Zone is null)
        {
            return view;
        }

        bool isFirstZone = ZoneRanking.OrderByDifficulty(zones).FirstOrDefault()?.ZoneId == view.Zone.ZoneId;
        return view with { Recipes = view.Recipes.Where(r => r.ZoneId is not null || isFirstZone).ToList() };
    }

    // Una parte del recetario: las recetas de UNA zona (Zone), las de «Sin zona específica» (Zone null y no IsGate) o las del Fogón Eterno (IsGate).
    public sealed record RecipeSection(Zone? Zone, bool IsGate, IReadOnlyList<RecipeDetails> Recipes);

    // El recetario COMPLETO para la clase del jugador (v0.27.0, estilo `rpg recipes` de EPIC RPG): una parte por zona, de la más fácil a la más difícil, con lo que ESE jugador ve (su arma de clase y el amuleto),
    // después «Sin zona específica» y, si includeGate, el equipo del Fogón. Las zonas sin nada para mostrar no aparecen. onlyZoneId: solo esa zona (aa forge recipes 3; 0 es el Fogón).
    public static IReadOnlyList<RecipeSection> BookFor(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, string playerClass, bool includeGate, int? onlyZoneId = null)
    {
        var sections = new List<RecipeSection>();

        foreach (var zone in ZoneRanking.OrderByDifficulty(zones))
        {
            if (onlyZoneId is int only && only != zone.ZoneId)
            {
                continue;
            }

            var inZone = Sorted(recipes.Where(r => r.ZoneId == zone.ZoneId && IsVisibleTo(r, playerClass)));
            if (inZone.Count > 0)
            {
                sections.Add(new RecipeSection(zone, false, inZone));
            }
        }

        if (onlyZoneId is null)
        {
            var zoneless = Sorted(recipes.Where(r => r.ZoneId is null && IsVisibleTo(r, playerClass)));
            if (zoneless.Count > 0)
            {
                sections.Add(new RecipeSection(null, false, zoneless));
            }
        }

        if (includeGate && (onlyZoneId is null || onlyZoneId == FogonRules.GateZoneId))
        {
            var gate = Sorted(recipes.Where(r => r.ZoneId == FogonRules.GateZoneId && IsVisibleTo(r, playerClass)));
            if (gate.Count > 0)
            {
                sections.Add(new RecipeSection(null, true, gate));
            }
        }

        return sections;
    }

    // Dentro de cada parte: primero armas, después amuletos, y de menor a mayor poder.
    private static List<RecipeDetails> Sorted(IEnumerable<RecipeDetails> recipes) => recipes
        .OrderBy(r => GroupOf(r))
        .ThenBy(r => r.ResultItem.StatValue)
        .ThenBy(r => r.ResultItem.Name, StringComparer.Ordinal)
        .ToList();

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

    // Una receta la ve el jugador si no es exclusiva de otra clase, y — si es un arma de afinidad (de clase) — si es la
    // de SU clase (la familia del arma coincide con el tipo de arma de la clase).
    public static bool IsVisibleTo(RecipeDetails recipe, string playerClass) =>
        (recipe.ResultItem.ClassRequirement is null
            || string.Equals(recipe.ResultItem.ClassRequirement, playerClass, StringComparison.OrdinalIgnoreCase))
        && (!recipe.Affinity || IsOwnFamily(playerClass, recipe.ResultItem.WeaponFamily));

    private static bool IsOwnFamily(string playerClass, string? weaponFamily) =>
        weaponFamily is not null && ClassCatalog.All.Any(c => c.Name == playerClass && c.WeaponType == weaponFamily);

    public static RecipeGroup GroupOf(RecipeDetails recipe) => recipe.ResultItem.Type switch
    {
        "Amulet" => RecipeGroup.Amulet,
        "Weapon" => recipe.Affinity ? RecipeGroup.ClassWeapon : RecipeGroup.GeneralWeapon,
        _ => RecipeGroup.Other,
    };
}
