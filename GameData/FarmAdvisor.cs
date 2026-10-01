using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// De dónde sale un material: de la recolección (/chop, /mine) o de qué monstruo lo suelta.
public enum FarmSource { Unknown, Chop, Mine, Hunt, Travel, Boss }

// Lo que le falta al jugador de un ingrediente, y de dónde lo consigue.
public sealed record MissingPiece(string ItemName, string? Emoji, int Missing, FarmSource Source);

// El consejo: qué receta le conviene tener en la mira (o que ya puede forjar una), qué le falta y de dónde sacarlo.
public sealed record FarmAdvice(string RecipeName, string? RecipeEmoji, bool CraftableNow, int GoldShort, IReadOnlyList<MissingPiece> Missing);

// "¿Qué hago ahora?": mira las recetas que ve el jugador en su zona (las mismas que el herrero) y le dice qué le falta para la más
// cercana y por qué comando se consigue. PURO (sin base ni Discord): el servicio (Services/FarmAdviceService.cs) le pasa los datos.
//
// QUÉ RECETA ELIGE: si ya puede forjar alguna (tiene todos los materiales y el oro) le avisa, y si hay varias, la de mayor stat. Si no,
// la MÁS AVANZADA (la que tiene mayor parte de sus materiales juntada). Las que ya tiene forjadas se saltean. QUÉ MUESTRA: como mucho
// los 2 ingredientes que más le faltan (más que eso es una lista de compras, no un consejo) y el oro si también falta.
public static class FarmAdvisor
{
    public const int MaxPieces = 2;

    // owned: cuánto tiene de cada ítem, por nombre (solo lo que tiene; lo que no tiene no aparece). sourceOf: de dónde sale un ítem.
    public static FarmAdvice? Choose(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyDictionary<string, int> owned, int gold, Func<string, FarmSource> sourceOf)
    {
        var candidates = recipes
            .Where(r => !owned.ContainsKey(r.ResultItem.Name))
            .Select(r => (Recipe: r, Gaps: GapsOf(r, owned), GoldShort: Math.Max(0, r.GoldCost - gold)))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var ready = candidates
            .Where(c => c.Gaps.Count == 0 && c.GoldShort == 0)
            .OrderByDescending(c => c.Recipe.ResultItem.StatValue)
            .ThenBy(c => c.Recipe.ResultItem.Name, StringComparer.Ordinal)
            .Cast<(RecipeDetails Recipe, List<RecipeIngredientDetails> Gaps, int GoldShort)?>()
            .FirstOrDefault();

        if (ready is { } now)
        {
            return new FarmAdvice(now.Recipe.ResultItem.Name, now.Recipe.ResultItem.Emoji, true, 0, []);
        }

        var best = candidates
            .OrderByDescending(c => Progress(c.Recipe, owned))
            .ThenBy(c => c.Gaps.Sum(g => g.Quantity - owned.GetValueOrDefault(g.ItemName)))
            .ThenByDescending(c => c.Recipe.ResultItem.StatValue)
            .ThenBy(c => c.Recipe.ResultItem.Name, StringComparer.Ordinal)
            .First();

        var pieces = best.Gaps
            .Select(g => new MissingPiece(g.ItemName, g.Emoji, g.Quantity - owned.GetValueOrDefault(g.ItemName), sourceOf(g.ItemName)))
            .OrderByDescending(p => p.Missing)
            .ThenBy(p => p.ItemName, StringComparer.Ordinal)
            .Take(MaxPieces)
            .ToList();

        return new FarmAdvice(best.Recipe.ResultItem.Name, best.Recipe.ResultItem.Emoji, false, best.GoldShort, pieces);
    }

    // El título y el texto del consejo que muestra /tips (Modules/TipsModule.cs), cortos y con aire: la receta, y una línea por cosa que falta con el comando para conseguirla.
    public static (string Title, string Value) ToField(FarmAdvice advice)
    {
        string recipe = ItemDisplay.Format(advice.RecipeEmoji, advice.RecipeName);

        if (advice.CraftableNow)
        {
            return ("💡 ¡Ya podés forjar algo!", $"**{recipe}** está lista: pasá por `/forge`.");
        }

        var lines = new List<string> { $"**{recipe}**" };
        lines.AddRange(advice.Missing.Select(p =>
            $"• {p.Missing}× {ItemDisplay.Format(p.Emoji, p.ItemName)}{SourceText(p.Source)}"));

        if (advice.GoldShort > 0)
        {
            lines.Add($"• 💰 {advice.GoldShort} de oro{SourceText(FarmSource.Travel)}");
        }

        return ("💡 Para tu próxima forja", string.Join('\n', lines));
    }

    private static string SourceText(FarmSource source) => source switch
    {
        FarmSource.Chop => " → 🪓 `/chop`",
        FarmSource.Mine => " → ⛏️ `/mine`",
        FarmSource.Hunt => " → 🏹 `/hunt`",
        FarmSource.Travel => " → 🗺️ `/travel`",
        FarmSource.Boss => " → 👑 `/boss`",
        _ => string.Empty,
    };

    private static List<RecipeIngredientDetails> GapsOf(RecipeDetails recipe, IReadOnlyDictionary<string, int> owned) =>
        recipe.Ingredients.Where(i => owned.GetValueOrDefault(i.ItemName) < i.Quantity).ToList();

    // Qué parte de los materiales de la receta ya tiene (0 a 1): lo que le sobra de un ingrediente no compensa lo que le falta de otro.
    private static double Progress(RecipeDetails recipe, IReadOnlyDictionary<string, int> owned)
    {
        int total = recipe.Ingredients.Sum(i => i.Quantity);
        int have = recipe.Ingredients.Sum(i => Math.Min(i.Quantity, owned.GetValueOrDefault(i.ItemName)));
        return total == 0 ? 1.0 : (double)have / total;
    }
}
