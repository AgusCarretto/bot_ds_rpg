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
//
// Como un arma o un amuleto se forja directo a equipamiento, hay dos casos más: si ese casillero ya tiene OTRA pieza puesta, la receta
// aparece con 🔁 ("vendé tu arma primero", aunque tengas todo) después de las que se pueden forjar ya, y la que ya llevás puesta, con 🟢 al final.
public static class ForgeChoices
{
    // owned: cantidad que el jugador tiene de cada ítem, por nombre (los nombres de ítem son únicos).
    public static IReadOnlyList<AutocompleteResult> For(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, User player, IReadOnlyDictionary<string, int> owned, string typed)
    {
        // Exactamente las recetas que muestra /forge recipes: las de la zona actual del jugador (ver
        // GameData/RecipeCatalog.cs) que le corresponden a su clase.
        // + las recetas del Fogón Eterno cuando ya venció al jefe de la última zona (GameData/FogonRules.cs).
        bool gateOpen = FogonRules.IsGateOpen(ZoneRanking.OrderByDifficulty(zones), player.HighestZoneCleared);
        return RecipeCatalog.ViewFor(recipes, zones, player.Class, player.CurrentZoneId, gateOpen).Recipes
            .Where(recipe => FitsAsValue(recipe.ResultItem.Name) && Matches(recipe.ResultItem.Name, typed))
            .Select(recipe => new Candidate(recipe, player, owned))
            .OrderBy(c => c.Group)
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

        // 0 = se puede forjar ya, 1 = tendría todo pero ese casillero está ocupado, 2 = falta algo, 3 = ya la llevás puesta.
        public int Group => AlreadyEquipped ? 3 : SlotTaken ? 1 : Craftable ? 0 : 2;

        // El casillero (arma / amuleto) de esta receta y qué tiene puesto el jugador ahí.
        private readonly int? _equippedHere;
        public bool AlreadyEquipped => _equippedHere == Recipe.ResultItem.ItemId;
        public bool SlotTaken => _equippedHere is not null && !AlreadyEquipped;

        public Candidate(RecipeDetails recipe, User player, IReadOnlyDictionary<string, int> owned)
        {
            Recipe = recipe;
            _equippedHere = recipe.ResultItem.Type switch { "Weapon" => player.WeaponId, "Amulet" => player.AmuletId, _ => null };
            GoldShort = Math.Max(0, recipe.GoldCost - player.Gold);
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
            bool weapon = item.Type == "Weapon";

            if (AlreadyEquipped)
            {
                return $"🟢 {item.Name} ({ItemStatLabel.Format(item) ?? item.Type}) — {(weapon ? "ya la llevás equipada" : "ya lo llevás equipado")}";
            }

            if (SlotTaken && Craftable)
            {
                return $"🔁 {item.Name} ({ItemStatLabel.Format(item) ?? item.Type}) — primero vendé tu {(weapon ? "arma" : "amuleto")} equipad{(weapon ? "a" : "o")}";
            }

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
