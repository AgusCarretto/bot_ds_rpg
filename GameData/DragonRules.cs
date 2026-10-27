using System.Globalization;

namespace BotDsRpg.GameData;

// Lo que da el dragón de un jugador, ya en porcentajes (0 = sin efecto). Solo cuenta contra los monstruos: entra por PlayerBonuses, que los duelos y la Arena no reciben.
public sealed record DragonBonuses(double AttackPercent = 0, double DefensePercent = 0, double GoldPercent = 0, double XpPercent = 0)
{
    public static readonly DragonBonuses None = new();

    public bool IsNone => AttackPercent <= 0 && DefensePercent <= 0 && GoldPercent <= 0 && XpPercent <= 0;
}

// EL DRAGÓN (v0.30.0; /dragon, /dragon-breed, aa dragon; el «horse» de EPIC RPG hecho a nuestra manera; el dueño: «un dragón que se pueda comprar y mergear con otra gente… tier del 1 al 10… tipos: fuerza,
// defensa, especial, oro… por ahora solo horse breed, o sea criar»). Cada jugador tiene UNO, que compra en la taberna (tipo al azar) y sube de tier criándolo con el de otro jugador del MISMO tier:
// si sale bien suben los dos. Cada tipo da un bono que crece con el tier (curva convexa, como el de EPIC: lo bueno viene al final) y solo cuenta contra monstruos. Dura toda la vida de la cuenta: el Fuego Nuevo
// no lo toca (como las mascotas). Acá está TODO lo que se puede retocar (lo que cuesta, los bonos, las chances y el cooldown); es lógica pura, sin I/O. Los datos de cada jugador viven en player_dragons.
public static class DragonRules
{
    public const int MaxTier = 10;

    // El precio del dragón en la taberna (3.000 de oro: unas 215 cacerías de la Zona 1, ocho de la Zona 5). Es la entrada al juego de criar: el tier 1 casi no da nada (menos de +1 %).
    public const int ShopPrice = 3000;

    // Cuánto descansa un dragón después de criar (los dos del par). Con 12 horas son unas 2 crías por día.
    public static readonly TimeSpan BreedCooldown = TimeSpan.FromHours(12);

    public const string Strength = "fuerza";
    public const string Defense = "defensa";
    public const string Gold = "oro";
    public const string Special = "especial";

    public static readonly IReadOnlyList<string> Kinds = [Strength, Defense, Gold, Special];

    public static bool IsKind(string? kind) => kind is not null && Kinds.Contains(kind);

    public static string KindName(string kind) => kind switch { Strength => "Fuerza", Defense => "Defensa", Gold => "Oro", _ => "Especial" };

    public static string KindEmoji(string kind) => kind switch { Strength => "🗡️", Defense => "🛡️", Gold => "💰", _ => "✨" };

    // Qué mejora cada tipo, para frases como «+12 % ATQ».
    public static string KindEffect(string kind) => kind switch
    {
        Strength => "de ATQ contra monstruos",
        Defense => "de DEF contra monstruos",
        Gold => "de oro en las peleas",
        _ => "de EXP en las peleas",
    };

    // Los nombres de cada tier, del dragoncito al eterno.
    private static readonly string[] TierNames = ["Cría", "Dragoncito", "Joven", "Adolescente", "Adulto", "Curtido", "Anciano", "Ancestral", "Legendario", "Eterno"];

    public static string TierName(int tier) => TierNames[Math.Clamp(tier, 1, MaxTier) - 1];

    private static readonly string[] Roman = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

    public static string TierNumeral(int tier) => Roman[Math.Clamp(tier, 1, MaxTier) - 1];

    // Qué parte del bono máximo da cada tier (curva convexa: 4 % de lo máximo en el tier 1, un tercio en el 5 y todo en el 10). Se parece a la del caballo de EPIC (0 / 5 / 10 / 20 / 30 / 45 / 60 / 80 / 100 / 200 %).
    private static readonly double[] Curve = [0.04, 0.08, 0.14, 0.21, 0.30, 0.42, 0.56, 0.72, 0.86, 1.00];

    // El bono máximo de cada tipo (en el tier 10), en %: el oro rinde mucho más que el ataque porque la defensa y el ataque pesan más en las peleas (la defensa se resta pareja a cada golpe).
    public static double MaxPercent(string kind) => kind switch { Strength => 12, Defense => 8, Gold => 30, _ => 25 };

    // El bono de un tipo en un tier, en %, con un decimal.
    public static double BonusPercent(string kind, int tier) => Math.Round(MaxPercent(kind) * Curve[Math.Clamp(tier, 1, MaxTier) - 1], 1);

    public static DragonBonuses BonusesFor(string kind, int tier)
    {
        double percent = BonusPercent(kind, tier);
        return kind switch
        {
            Strength => new DragonBonuses(AttackPercent: percent),
            Defense => new DragonBonuses(DefensePercent: percent),
            Gold => new DragonBonuses(GoldPercent: percent),
            _ => new DragonBonuses(XpPercent: percent),
        };
    }

    // ---- Criar ----
    // La chance de que un intento de criar suba el tier (tier 1 → 2 es seguro; después baja fuerte, como en EPIC: 100 / 60 / 30 / 15 / 5 / 2 / 1 / 0,33 / 0,11 %).
    private static readonly double[] BreedChances = [1.0, 0.60, 0.30, 0.15, 0.05, 0.02, 0.01, 0.0033, 0.0011];

    // Cuántos intentos fallidos SEGUIDOS aguanta ese tier: con ese tope de fallos, el siguiente intento sale seguro (así el camino tiene fin aunque la chance sea de 0,11 %). Es una «piedad» como la de EPIC.
    private static readonly int[] PityCaps = [0, 2, 4, 7, 12, 20, 30, 40, 50];

    public static double BreedChance(int tier) => tier is >= 1 and < MaxTier ? BreedChances[tier - 1] : 0;

    public static int PityCap(int tier) => tier is >= 1 and < MaxTier ? PityCaps[tier - 1] : 0;

    // Un intento de criar: sale seguro si los fallos seguidos ya llegaron al tope; si no, se sortea con la chance del tier. En el tier máximo no hay nada que subir.
    public static bool RollBreed(int tier, int failStreak, Random? rng = null)
    {
        if (tier < 1 || tier >= MaxTier)
        {
            return false;
        }

        return failStreak >= PityCap(tier) || (rng ?? Random.Shared).NextDouble() < BreedChance(tier);
    }

    // Cuántos intentos hacen falta en promedio para pasar de ese tier al siguiente (con la piedad): la suma de (1 − p)^k para k = 0..tope.
    public static double ExpectedAttempts(int tier)
    {
        if (tier is < 1 or >= MaxTier)
        {
            return 0;
        }

        double p = BreedChance(tier), total = 0;
        for (int k = 0; k <= PityCap(tier); k++)
        {
            total += Math.Pow(1 - p, k);
        }

        return total;
    }

    // ---- Comprar ----
    // Qué tipo sale al comprarlo (al azar, como el caballo de EPIC): Especial es el raro.
    private static readonly (string Kind, int Weight)[] ShopOdds = [(Strength, 30), (Defense, 30), (Gold, 30), (Special, 10)];

    public static int ShopWeight(string kind) => ShopOdds.First(o => o.Kind == kind).Weight;

    public static string RollKind(Random? rng = null)
    {
        int roll = (rng ?? Random.Shared).Next(ShopOdds.Sum(o => o.Weight));
        foreach (var (kind, weight) in ShopOdds)
        {
            if (roll < weight)
            {
                return kind;
            }

            roll -= weight;
        }

        return Strength;
    }

    // «12,5» (coma decimal, sin ceros de más): el formato de los porcentajes en español.
    public static string Percent(double value) =>
        value.ToString(value % 1 == 0 ? "0" : "0.0##", CultureInfo.InvariantCulture).Replace('.', ',');
}
