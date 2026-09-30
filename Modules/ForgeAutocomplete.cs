using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// Lista desplegable de /forge make: las recetas de tu zona actual que te corresponden (las mismas que
// muestra /forge recipes), con las que YA podés forjar arriba (✅ tenés el oro y todos los materiales) y, debajo, las
// que todavía no, diciendo qué falta (❌ falta: 2 Hierro, 30 oro) — sirve de guía de qué conseguir. El
// valor es el nombre exacto del ítem resultado, así que ForgeModule.ExecuteMakeAsync no cambia.
public static class ForgeChoices
{
    // owned: cantidad que el jugador tiene de cada ítem, por nombre (los nombres de ítem son únicos).
    public static IReadOnlyList<AutocompleteResult> For(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, User player, IReadOnlyDictionary<string, int> owned, string typed)
    {
        // Exactamente las recetas que muestra /forge recipes: las de la zona actual del jugador (ver
        // GameData/RecipeCatalog.cs) que le corresponden a su clase.
        return RecipeCatalog.ViewFor(recipes, zones, player.Class, player.CurrentZoneId).Recipes
            .Where(recipe => FitsAsValue(recipe.ResultItem.Name) && Matches(recipe.ResultItem.Name, typed))
            .Select(recipe => new Candidate(recipe, player.Gold, owned))
            .OrderBy(c => c.Craftable ? 0 : 1)
            .ThenBy(c => Relevance(c.Recipe.ResultItem.Name, typed))
            .ThenBy(c => c.MissingUnits)
            .ThenByDescending(c => c.Recipe.ResultItem.StatValue)
            .ThenBy(c => c.Recipe.ResultItem.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(c => new AutocompleteResult(Truncate(c.Label()), c.Recipe.ResultItem.Name))
            .ToList();
    }

    private sealed class Candidate
    {
        public RecipeDetails Recipe { get; }
        public int GoldShort { get; }
        public IReadOnlyList<(string Name, int Short)> MissingIngredients { get; }

        public Candidate(RecipeDetails recipe, int playerGold, IReadOnlyDictionary<string, int> owned)
        {
            Recipe = recipe;
            GoldShort = Math.Max(0, recipe.GoldCost - playerGold);
            MissingIngredients = recipe.Ingredients
                .Select(i => (i.ItemName, Short: i.Quantity - (owned.TryGetValue(i.ItemName, out int have) ? have : 0)))
                .Where(x => x.Short > 0)
                .ToList();
        }

        public bool Craftable => GoldShort == 0 && MissingIngredients.Count == 0;

        // Unidades de material que faltan (el oro no cuenta acá: es otra unidad y el jugador lo consigue distinto).
        public int MissingUnits => MissingIngredients.Sum(x => x.Short);

        public string Label()
        {
            var item = Recipe.ResultItem;
            if (Craftable)
            {
                return $"✅ {item.Name} ({ItemStatLabel.Format(item) ?? item.Type}) — {Recipe.GoldCost} oro";
            }

            var parts = MissingIngredients.Select(x => $"{x.Short} {x.Name}").ToList();
            if (GoldShort > 0)
            {
                parts.Add($"{GoldShort} oro");
            }

            return $"❌ {item.Name} ({ItemStatLabel.Format(item) ?? item.Type}) — falta: {string.Join(", ", parts)}";
        }
    }
}

public sealed class ForgeAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        string typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        // GetByDiscordIdAsync (no GetOrCreate): abrir una lista no tiene que crearle cuenta a nadie.
        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(context.User.Id);
        if (player is null)
        {
            return AutocompletionResult.FromSuccess([]);
        }

        var recipes = await services.GetRequiredService<IRecipeRepository>().GetAllAsync();
        var zones = await services.GetRequiredService<IZoneRepository>().GetAllAsync();
        var inventory = await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(context.User.Id);
        var owned = inventory.ToDictionary(entry => entry.ItemName, entry => entry.Quantity);

        return AutocompletionResult.FromSuccess(ForgeChoices.For(recipes, zones, player, owned, typed));
    }
}
