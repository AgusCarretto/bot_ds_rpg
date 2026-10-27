using System.Globalization;
using System.Text.RegularExpressions;

namespace BotDsRpg.GameData;

// Lo que da el dragón de un jugador, ya en porcentajes (0 = sin efecto). Solo cuenta contra los monstruos: entra por PlayerBonuses, que los duelos y la Arena no reciben.
// Attack / Defense / Gold / Xp: lo de su TIPO (más los extras de tier). Gather = cantidad de /chop y /mine, Drop = chance de drop de /hunt y /travel, Hp = vida máxima de combate.
// LastStand: el perk de «Aliento final» (una vez por pelea, si el golpe te iba a matar, quedás con 1 de vida).
public sealed record DragonBonuses(
    double AttackPercent = 0, double DefensePercent = 0, double GoldPercent = 0, double XpPercent = 0,
    double GatherPercent = 0, double DropPercent = 0, double HpPercent = 0, bool LastStand = false)
{
    public static readonly DragonBonuses None = new();

    public bool IsNone =>
        AttackPercent <= 0 && DefensePercent <= 0 && GoldPercent <= 0 && XpPercent <= 0 && GatherPercent <= 0 && DropPercent <= 0 && HpPercent <= 0 && !LastStand;
}

// Una mejora que desbloquea un tier (cada tier da una NUEVA, y se acumulan): su nombre y lo que hace, para mostrarlo.
public sealed record DragonPerk(int Tier, string Name, string Text);

// EL DRAGÓN (v0.30.0; /dragon, /dragon-breed, /dragon-train, aa dragon; el «horse» de EPIC RPG hecho a nuestra manera; el dueño: «un dragón que se pueda comprar y mergear con otra gente… tier del 1 al 10… tipos:
// fuerza, defensa, especial, oro… cada tier da una mejora nueva, como EPIC… que se pueda entrenar, que salga plata y que cada nivel salga más caro»). Cada jugador tiene UNO, que compra en la taberna (tipo al azar) y que
// mejora de DOS formas: criándolo con el de otro jugador del MISMO tier (sube el TIER: si sale bien suben los dos y se desbloquea una mejora nueva) y ENTRENÁNDOLO con oro (sube el NIVEL, hasta tier × 10, y cada nivel suma
// al bono de su tipo). Todo cuenta solo contra monstruos y dura toda la vida de la cuenta (el Fuego Nuevo no lo toca, como las mascotas). Acá está TODO lo que se puede retocar; es lógica pura, sin I/O.
public static class DragonRules
{
    public const int MaxTier = 10;

    // El precio del dragón en la taberna (3.000 de oro: unas 215 cacerías de la Zona 1, ocho de la Zona 5). Es la entrada: el tier 1 sin entrenar casi no da nada (menos de +1,3 %).
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

    // El emoji de cada tipo: el dragón propio que se haya subido al portal (dragon_fuerza…, ver docs/dragon-emojis/README.md) o, mientras no esté, el de siempre.
    public static string KindEmoji(string kind) => UiEmojis.DragonKind(kind, kind switch { Strength => "🗡️", Defense => "🛡️", Gold => "💰", _ => "✨" });

    // Qué mejora cada tipo, para frases como «+12 % de ATQ contra monstruos».
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

    // ---- El bono de su tipo: el del TIER (base) + el del NIVEL (entrenamiento) ----
    // Qué parte del bono máximo da cada tier (curva convexa: 4 % de lo máximo en el tier 1, un tercio en el 5 y todo en el 10). Se parece a la del caballo de EPIC (0 / 5 / 10 / 20 / 30 / 45 / 60 / 80 / 100 / 200 %).
    private static readonly double[] Curve = [0.04, 0.08, 0.14, 0.21, 0.30, 0.42, 0.56, 0.72, 0.86, 1.00];

    // El bono máximo de cada tipo (en el tier 10, sin entrenar), en %: el oro rinde mucho más que el ataque porque la defensa y el ataque pesan más en las peleas (la defensa se resta pareja a cada golpe).
    public static double MaxPercent(string kind) => kind switch { Strength => 12, Defense => 8, Gold => 30, _ => 25 };

    // El bono de un tipo en un tier, en %, con un decimal (sin entrenar).
    public static double BonusPercent(string kind, int tier) => Math.Round(MaxPercent(kind) * Curve[Math.Clamp(tier, 1, MaxTier) - 1], 1);

    // Cuánto suma cada nivel de entrenamiento, como parte del bono máximo del tipo: 0,5 % de lo máximo por nivel (en el nivel 100 suma la mitad de lo máximo).
    public const double LevelShare = 0.005;

    public static double LevelBonusPercent(string kind, int level) => Math.Round(MaxPercent(kind) * LevelShare * Math.Max(0, level), 1);

    // En el tier 10 la mejora «Llama eterna» hace que el bono de su tipo (base + entrenamiento) rinda 1,25 veces.
    public const double EternalMultiplier = 1.25;

    // El bono TOTAL de su tipo, en %: el del tier + el del nivel, y con la Llama eterna en el tier 10.
    public static double TypePercent(string kind, int tier, int level) =>
        Math.Round((BonusPercent(kind, tier) + LevelBonusPercent(kind, level)) * (tier >= MaxTier ? EternalMultiplier : 1.0), 1);

    // ---- Una mejora nueva por tier (acumulativas) ----
    public const double DropPerk = 4;       // tier 2: +4 % de chance de drop en /hunt y /travel
    public const double GatherPerk = 5;     // tier 3: +5 % de cantidad en /chop y /mine
    public const double HpPerk = 4;         // tier 5: +4 % de vida máxima en las peleas
    public const double XpPerk = 5;         // tier 6: +5 % de EXP
    public const double GoldPerk = 5;       // tier 7: +5 % de oro
    public const double PowerPerk = 3;      // tier 8: +3 % de ATQ y de DEF
    public const double FortunePerk = 3;    // tier 9: +3 % más de drop, recolección, oro y EXP
    public const int LastStandTier = 4;     // tier 4: Aliento final (como el tier IV del caballo de EPIC, que te deja con 1 HP)

    public static readonly IReadOnlyList<DragonPerk> Perks =
    [
        new(1, "Cría", "Da el bono de su tipo."),
        new(2, "Ojo de tesoro", $"+{Percent(DropPerk)} % de chance de drop en /hunt y /travel."),
        new(3, "Garras de recolector", $"+{Percent(GatherPerk)} % de cantidad en /chop y /mine."),
        new(4, "Aliento final", "Una vez por pelea, si el golpe te iba a matar, quedás con 1 de vida."),
        new(5, "Piel de escamas", $"+{Percent(HpPerk)} % de vida máxima en las peleas."),
        new(6, "Instinto de caza", $"+{Percent(XpPerk)} % de EXP en las peleas."),
        new(7, "Hoguera de oro", $"+{Percent(GoldPerk)} % de oro en las peleas."),
        new(8, "Sangre de dragón", $"+{Percent(PowerPerk)} % de ATQ y de DEF contra monstruos."),
        new(9, "Fortuna", $"+{Percent(FortunePerk)} % más de drop, recolección, oro y EXP."),
        new(10, "Llama eterna", $"El bono de su tipo rinde ×{Percent(EternalMultiplier)}."),
    ];

    // Todo lo que da un dragón de ese tipo, tier y nivel: el bono de su tipo y los extras de cada tier que ya desbloqueó.
    public static DragonBonuses BonusesFor(string kind, int tier, int level = 0)
    {
        tier = Math.Clamp(tier, 1, MaxTier);
        level = Math.Clamp(level, 0, LevelCap(tier));
        double type = TypePercent(kind, tier, level);
        double attack = kind == Strength ? type : 0, defense = kind == Defense ? type : 0, gold = kind == Gold ? type : 0, xp = kind == Special ? type : 0;
        double gather = 0, drop = 0, hp = 0;

        if (tier >= 2) { drop += DropPerk; }
        if (tier >= 3) { gather += GatherPerk; }
        if (tier >= 5) { hp += HpPerk; }
        if (tier >= 6) { xp += XpPerk; }
        if (tier >= 7) { gold += GoldPerk; }
        if (tier >= 8) { attack += PowerPerk; defense += PowerPerk; }
        if (tier >= 9) { drop += FortunePerk; gather += FortunePerk; gold += FortunePerk; xp += FortunePerk; }

        return new DragonBonuses(attack, defense, gold, xp, gather, drop, hp, LastStand: tier >= LastStandTier);
    }

    // ---- Entrenar ----
    // El nivel más alto que admite un tier: tier × 10 (como el caballo de EPIC). Para pasar de ahí hay que subir de tier criando.
    public static int LevelCap(int tier) => Math.Clamp(tier, 1, MaxTier) * 10;

    public const int MaxLevel = MaxTier * 10;

    // Cuánto cuesta pasar del nivel fromLevel al siguiente, en oro: 50 × (nivel nuevo)^1,9, redondeado a la decena. Cada nivel sale más caro que el anterior (nivel 1: 50, nivel 10: ~4.000, nivel 50: ~84.000,
    // nivel 100: ~315.000); entrenar hasta el 100 son unos 11 millones de oro: un sumidero de oro de largo plazo, como el entrenamiento del caballo de EPIC.
    public static int TrainCost(int fromLevel) => (int)(Math.Round(50 * Math.Pow(Math.Max(0, fromLevel) + 1, 1.9) / 10.0) * 10);

    // Lo que cuesta entrenar de un nivel a otro (suma de cada paso).
    public static long TrainCostBetween(int fromLevel, int toLevel)
    {
        long total = 0;
        for (int level = fromLevel; level < toLevel; level++)
        {
            total += TrainCost(level);
        }

        return total;
    }

    // ---- El nombre (se paga cada vez que se cambia) ----
    public const int RenamePrice = 5000;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 24;

    // Letras, números, espacios y . - ' ! (sin menciones, símbolos de formato de Discord ni códigos de emoji: el nombre se muestra en mensajes públicos y en el perfil).
    private static readonly Regex NameRule = new(@"^[\p{L}\p{N}][\p{L}\p{N} '.\-!]*$", RegexOptions.Compiled);

    // El nombre limpio (sin espacios de más) o null si no sirve.
    public static string? CleanName(string? raw)
    {
        string name = Regex.Replace((raw ?? string.Empty).Trim(), @"\s+", " ");
        return name.Length >= NameMinLength && name.Length <= NameMaxLength && NameRule.IsMatch(name) ? name : null;
    }

    // ---- Criar ----
    // La chance de que un intento de criar suba el tier (tier 1 → 2 es seguro; después baja fuerte, como en EPIC: 100 / 60 / 30 / 15 / 5 / 2 / 1 / 0,33 / 0,11 %).
    private static readonly double[] BreedChances = [1.0, 0.60, 0.30, 0.15, 0.05, 0.02, 0.01, 0.0033, 0.0011];

    // Cuántos intentos fallidos SEGUIDOS aguanta ese tier: con ese tope de fallos, el siguiente intento sale seguro (así el camino tiene fin aunque la chance sea de 0,11 %). Es una «piedad» como la de EPIC.
    private static readonly int[] PityCaps = [0, 2, 4, 7, 12, 20, 30, 40, 50];

    public static double BreedChance(int tier) => tier is >= 1 and < MaxTier ? BreedChances[tier - 1] : 0;

    public static int PityCap(int tier) => tier is >= 1 and < MaxTier ? PityCaps[tier - 1] : 0;

    // Criar SOLO, sin pareja (v0.31.0, el dueño: «que el dragón se pueda criar solo también y cuente como intento, pero sacale la mitad del % de subir de tier»; después el dueño la bajó a UN TERCIO: «me gusta que sea un tercio, así promovés el breed con más gente»): la chance es un TERCIO de la del tier. Cuenta como un intento
    // de verdad: pone el descanso de 12 h y suma (o reinicia) los fallos seguidos, así que la piedad sigue funcionando igual (con el tope de fallos el siguiente intento sale seguro, también solo).
    public const double SoloChanceFactor = 1.0 / 3.0;

    public static double SoloBreedChance(int tier) => BreedChance(tier) * SoloChanceFactor;

    // El tope de fallos seguidos criando SOLO: el de siempre pero de al menos 1. Con pareja el tier 1 es seguro (su tope es 0), pero solo tiene un tercio de la chance: puede fallar una vez y recién después sale seguro.
    public static int SoloPityCap(int tier) => tier is >= 1 and < MaxTier ? Math.Max(1, PityCaps[tier - 1]) : 0;

    // Un intento de criar: sale seguro si los fallos seguidos ya llegaron al tope; si no, se sortea con la chance del tier (por chanceFactor: 1 con pareja, SoloChanceFactor solo). En el tier máximo no hay nada que subir.
    public static bool RollBreed(int tier, int failStreak, Random? rng = null, double chanceFactor = 1.0)
    {
        if (tier < 1 || tier >= MaxTier)
        {
            return false;
        }

        return failStreak >= (chanceFactor < 1.0 ? SoloPityCap(tier) : PityCap(tier)) || (rng ?? Random.Shared).NextDouble() < BreedChance(tier) * chanceFactor;
    }

    // Cuántos intentos hacen falta en promedio para pasar de ese tier al siguiente (con la piedad): la suma de (1 − p)^k para k = 0..tope.
    public static double ExpectedAttempts(int tier, double chanceFactor = 1.0)
    {
        if (tier is < 1 or >= MaxTier)
        {
            return 0;
        }

        double p = BreedChance(tier) * chanceFactor, total = 0;
        int cap = chanceFactor < 1.0 ? SoloPityCap(tier) : PityCap(tier);
        for (int k = 0; k <= cap; k++)
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
