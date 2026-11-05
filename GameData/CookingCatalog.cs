using System.Globalization;
using System.Text;

namespace BotDsRpg.GameData;

// La cocina (v0.49.0; diseño: docs/superpowers/specs/2026-11-05-cocina-design.md). Un plato se COCINA Y SE COME EN EL MOMENTO: no es un ítem, no pasa por la mochila. Los permanentes suman su stat cada vez que se
// cocinan, se apilan sin tope y el Fuego Nuevo los borra (tabla player_cooked); los de tiempo (tanda 2) dan un bufo por 60 minutos. Todo vale SOLO contra monstruos (entra por PlayerBonuses, que los duelos y la Arena no reciben).
// Los platos son datos en CÓDIGO a propósito: son pocos y sus efectos están atados al código, no es una tabla que se edite a mano. Los ingredientes son ítems que ya existen en el catálogo (se nombran por su nombre).
public enum DishKind { Permanent, Timed }

public sealed record DishIngredient(string ItemName, int Quantity);

// MaxHp, Defense y Attack: lo que suma CADA plato de un permanente (planos, no porcentajes: +1 DEF vale casi como +1 % de vida efectiva contra un jefe porque la defensa se resta plana de cada golpe).
public sealed record Dish(string Key, string Name, DishKind Kind, IReadOnlyList<DishIngredient> Ingredients, int Fuel, int MaxHp = 0, int Defense = 0, int Attack = 0)
{
    // El emoji del plato se sube al portal con este nombre y el bot lo lee por nombre (como los del dragón): sin subir, sale el 🍳 común.
    public string IconName => CookingCatalog.IconPrefix + Key;

    public string Icon => UiEmojis.Icon(IconName, "🍳");
}

// Lo que escribió el jugador en «aa cook …»: las recetas, un plato con su cantidad, o un error ya con su texto.
public sealed record CookRequest(bool ShowRecipes, string? Dish, int Times, string? Error);

public static class CookingCatalog
{
    public const string FuelItemName = "Carbón";
    public const string IconPrefix = "plato_";

    // Cuántos platos se pueden cocinar en un solo pedido.
    public const int MaxTimes = 10;

    public const string SurubiDisco = "surubi_disco";
    public const string DoradoParrilla = "dorado_parrilla";
    public const string MojarrasEscabeche = "mojarras_escabeche";

    // Tanda 1: los 3 permanentes. Las horas de farmeo de cada uno (FN 7, herramientas de Zona 3) están en el diseño: Surubí al Disco ~4,8, Dorado a la Parrilla ~6,2 (la más cara a propósito, la DEF es lo más fuerte por punto)
    // y Mojarras en Escabeche ~5,1.
    public static readonly IReadOnlyList<Dish> All =
    [
        new(SurubiDisco, "Surubí al Disco", DishKind.Permanent,
            [new("Surubí", 7), new("Gema de Zafiro", 2), new("Oro Puro", 6)], Fuel: 16, MaxHp: 10),
        new(DoradoParrilla, "Dorado a la Parrilla", DishKind.Permanent,
            [new("Dorado", 40), new("Surubí", 1), new("Madera de Ébano", 4), new("Madera de Nogal", 8)], Fuel: 20, Defense: 1),
        new(MojarrasEscabeche, "Mojarras en Escabeche", DishKind.Permanent,
            [new("Mojarra", 150), new("Dorado", 10), new("Madera de Ébano", 3), new("Gema de Zafiro", 3)], Fuel: 20, Attack: 2),
    ];

    // Un plato por su clave o su nombre, sin importar mayúsculas, tildes ni espacios ("surubi al disco", "SURUBÍ AL DISCO", "surubi_disco"). Null si no existe.
    public static Dish? Find(string? text)
    {
        string folded = Fold(text);
        return folded.Length == 0 ? null : All.FirstOrDefault(d => Fold(d.Key) == folded || Fold(d.Name) == folded);
    }

    // Lo que suma lo cocinado (clave del plato → cuántos se cocinaron) en cada stat. Una clave que ya no existe no suma nada.
    public static int Sum(IReadOnlyDictionary<string, int>? cooked, Func<Dish, int> perDish) =>
        cooked is null ? 0 : All.Sum(d => cooked.TryGetValue(d.Key, out int count) && count > 0 ? perDish(d) * count : 0);

    // Todo lo que gasta cocinar `times` veces un plato: los ingredientes y el combustible, juntos (si el combustible fuera también un ingrediente, se suman).
    public static IReadOnlyList<DishIngredient> Needs(Dish dish, int times) =>
        dish.Ingredients.Select(i => new DishIngredient(i.ItemName, i.Quantity * times))
            .Append(new DishIngredient(FuelItemName, dish.Fuel * times))
            .GroupBy(n => n.ItemName)
            .Select(g => new DishIngredient(g.Key, g.Sum(n => n.Quantity)))
            .ToList();

    // «+10 de vida máxima», «+1 de DEF» o «+2 de ATQ», para `times` platos (por defecto uno).
    public static string EffectLine(Dish dish, int times = 1)
    {
        var parts = new List<string>();
        if (dish.MaxHp > 0) parts.Add(Loc.F($"+{dish.MaxHp * times} de vida máxima"));
        if (dish.Defense > 0) parts.Add(Loc.F($"+{dish.Defense * times} de DEF"));
        if (dish.Attack > 0) parts.Add(Loc.F($"+{dish.Attack * times} de ATQ"));
        return string.Join(" · ", parts);
    }

    private static readonly string[] RecipeWords = ["recipes", "recipe", "recetas", "receta", "menu", "lista", "list"];

    // «aa cook» (sin nada) y «aa cook recipes» muestran las recetas; «aa cook <plato>» cocina uno; «aa cook <plato> 5» cocina cinco. De 1 a MaxTimes.
    public static CookRequest ParseRequest(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 || RecipeWords.Contains(Fold(trimmed)))
        {
            return new CookRequest(true, null, 1, null);
        }

        string[] tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && int.TryParse(tokens[^1], NumberStyles.None, CultureInfo.InvariantCulture, out int times))
        {
            if (times is < 1 or > MaxTimes)
            {
                return new CookRequest(false, null, 1, Loc.F($"Se cocinan de 1 a {MaxTimes} platos por vez."));
            }

            return new CookRequest(false, string.Join(' ', tokens[..^1]), times, null);
        }

        return new CookRequest(false, trimmed, 1, null);
    }

    // Minúsculas, sin tildes y con los espacios (y guiones bajos) iguales: para comparar lo que escribe el jugador con los nombres.
    private static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (char c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c == '_' ? ' ' : char.ToLowerInvariant(c));
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
