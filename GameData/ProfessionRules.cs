using System.Globalization;

namespace BotDsRpg.GameData;

// Un oficio (v0.13.0, docs/superpowers/specs/2026-10-06-oficios-design.md): se sube usando el comando de siempre. StatKind es el contador de player_stats del que sale su XP (el mismo vocabulario
// que game_events.kind y los logros): XP = contador × XpPerAction. Así el progreso NO se guarda en ningún lado (no hay tabla ni migración, no se puede desincronizar) y lo que el jugador ya
// hizo antes de que existieran los oficios cuenta. Command es el comando que lo sube; AdvancedName, el nombre de su versión avanzada (se desbloquea al nivel máximo).
public sealed record ProfessionDefinition(
    string Key, string Name, string Emoji, string StatKind, int XpPerAction, string Command, string AdvancedName, IReadOnlyList<string> Aliases);

public static class ProfessionCatalog
{
    public const string WoodcutterKey = "woodcutter";
    public const string MinerKey = "miner";
    public const string EnchanterKey = "enchanter";

    // Sumar un oficio es una fila acá (y su efecto en ProfessionRules / PlayerBonuses): el resto del juego lo recorre.
    public static readonly IReadOnlyList<ProfessionDefinition> All =
    [
        new(WoodcutterKey, "Leñador", "🪓", GameEventKinds.Chop, 10, "/chop", "Tala avanzada", ["lenador", "leñador", "woodcutter", "chop", "talar", "tala", "madera"]),
        new(MinerKey, "Minero", "⛏️", GameEventKinds.Mine, 10, "/mine", "Minería avanzada", ["minero", "miner", "mine", "minar", "mineria", "mineral"]),
        new(EnchanterKey, "Encantador", "✨", GameEventKinds.Enchant, 50, "/enchant", "Encantamiento avanzado", ["encantador", "enchanter", "enchant", "encantar", "encantamiento"]),
    ];

    public static ProfessionDefinition? Find(string? textOrKey) =>
        string.IsNullOrWhiteSpace(textOrKey)
            ? null
            : All.FirstOrDefault(p => AutocompleteText.SameName(p.Key, textOrKey) || AutocompleteText.SameName(p.Name, textOrKey) || p.Aliases.Any(a => AutocompleteText.SameName(a, textOrKey)));

    public static ProfessionDefinition Get(string key) => All.First(p => p.Key == key);
}

// El nivel de un oficio: de 0 (sin XP) a ProfessionRules.MaxLevel. IntoLevel = XP juntada dentro del nivel actual; ToNext = XP que pide el próximo nivel (0 si ya está al máximo).
public readonly record struct ProfessionProgress(int Level, long TotalXp, long IntoLevel, long ToNext)
{
    public bool IsMax => Level >= ProfessionRules.MaxLevel;
}

// Las reglas de los oficios, puras y con nombre (todo se retoca acá). Lo que da cada nivel es LINEAL y siempre igual (como Fuego Nuevo): el nivel 100 se nota muchísimo contra el 10.
//   · Leñador y Minero: +0,2 % de cantidad y +0,1 % de chance de mejorar la rareza por nivel (+20 % y +10 % al 100).
//   · Encantador: −0,25 % de Polvo por intento y por nivel (−25 % al 100).
//   · Al 100: la versión avanzada de cada comando (ver Advanced*).
// Medido con report_recipe_pacing.sql: con los tres oficios al 100 el camino de recetas de las 5 zonas baja solo ~12 % (lo traban los drops de monstruos, no la recolección).
public static class ProfessionRules
{
    public const int MaxLevel = 100;

    // La curva: pasar del nivel L-1 al L pide XpUnit × (1 + CurveGrowth × L^1,5) de XP. Con 10 de XP por /chop o /mine: el nivel 1 es un solo uso, el 100 son 19, y llegar al 100 son
    // ~810 usos (≈ 5 semanas jugando ~2 horas por día al ritmo del cooldown de 5 minutos); con 50 de XP por intento de encantamiento son ~160 intentos.
    public const int XpUnit = 10;
    public const double CurveGrowth = 0.0176;

    // Lo que da cada nivel.
    public const double QuantityPerLevel = 0.002;
    public const double RarityUpgradePerLevel = 0.001;
    public const double DustDiscountPerLevel = 0.0025;

    // La versión avanzada (nivel MaxLevel): recolección de 4 sorteos juntos con su propio cooldown, y encantamiento de 2 tiradas (se queda con la mejor) a 1,5 veces el costo.
    public const int AdvancedGatherRolls = 4;
    public static readonly TimeSpan AdvancedGatherCooldown = TimeSpan.FromHours(1);
    public const int AdvancedEnchantRolls = 2;
    public const double AdvancedEnchantCostMultiplier = 1.5;

    // XP acumulada que hace falta para estar en cada nivel (índice 0 = nivel 0 = 0 de XP).
    private static readonly long[] CumulativeXp = BuildCumulative();

    private static long[] BuildCumulative()
    {
        var table = new long[MaxLevel + 1];
        for (int level = 1; level <= MaxLevel; level++)
        {
            table[level] = table[level - 1] + XpForLevel(level);
        }

        return table;
    }

    // XP que pide pasar del nivel anterior a ESTE nivel (1..MaxLevel).
    public static long XpForLevel(int level) =>
        (long)Math.Round(XpUnit * (1 + (CurveGrowth * Math.Pow(Math.Clamp(level, 1, MaxLevel), 1.5))), MidpointRounding.AwayFromZero);

    // XP total para llegar al nivel máximo.
    public static long TotalXpToMax => CumulativeXp[MaxLevel];

    public static ProfessionProgress ProgressFor(long totalXp)
    {
        long xp = Math.Max(0, totalXp);
        int level = 0;
        while (level < MaxLevel && xp >= CumulativeXp[level + 1])
        {
            level++;
        }

        if (level >= MaxLevel)
        {
            return new ProfessionProgress(MaxLevel, xp, 0, 0);
        }

        return new ProfessionProgress(level, xp, xp - CumulativeXp[level], XpForLevel(level + 1));
    }

    public static int LevelFor(long totalXp) => ProgressFor(totalXp).Level;

    // El XP de un oficio a partir del contador de player_stats (0 si no hay).
    public static long XpFrom(ProfessionDefinition profession, IReadOnlyDictionary<string, long>? stats) =>
        stats is not null && stats.TryGetValue(profession.StatKind, out long count) ? Math.Max(0, count) * profession.XpPerAction : 0;

    // Lo que da el nivel (multiplicadores y chances; con nivel 0 no cambian nada).
    public static double QuantityMultiplier(int level) => 1 + (QuantityPerLevel * Math.Clamp(level, 0, MaxLevel));

    public static double RarityUpgradeChance(int level) => RarityUpgradePerLevel * Math.Clamp(level, 0, MaxLevel);

    public static double DustMultiplier(int level) => 1 - (DustDiscountPerLevel * Math.Clamp(level, 0, MaxLevel));

    public static bool AdvancedUnlocked(int level) => level >= MaxLevel;

    // Con esa chance la rareza sube UN escalón (Común → Raro → Épico → Legendario). El Mítico nunca se mejora (su 0,5 % es la lotería de largo plazo del juego) y el Legendario no pasa a Mítico.
    // rng se inyecta para probarlo.
    public static string UpgradeRarity(string rarity, double chance, Random? rng = null)
    {
        if (chance <= 0)
        {
            return rarity;
        }

        string? next = rarity switch { "Común" => "Raro", "Raro" => "Épico", "Épico" => "Legendario", _ => null };
        return next is not null && (rng ?? Random.Shared).NextDouble() < chance ? next : rarity;
    }

    // El Polvo de un intento de encantamiento con el descuento del oficio (siempre al menos 1) y, si es avanzado, por 1,5. El oro del intento solo se multiplica en el avanzado.
    public static (int Gold, int Dust) EnchantCost((int Gold, int Dust) baseCost, int enchanterLevel, bool advanced)
    {
        double dust = baseCost.Dust * DustMultiplier(enchanterLevel);
        double gold = baseCost.Gold;
        if (advanced)
        {
            dust *= AdvancedEnchantCostMultiplier;
            gold *= AdvancedEnchantCostMultiplier;
        }

        return ((int)Math.Round(gold, MidpointRounding.AwayFromZero), Math.Max(1, (int)Math.Round(dust, MidpointRounding.AwayFromZero)));
    }

    // Lo que da el oficio en ese nivel, en palabras (para /professions y la ayuda).
    public static string DescribeEffect(ProfessionDefinition profession, int level) => profession.Key switch
    {
        ProfessionCatalog.EnchanterKey => $"−{Pct(DustDiscountPerLevel * level)} de Polvo por intento",
        _ => $"+{Pct(QuantityPerLevel * level)} de cantidad y +{Pct(RarityUpgradePerLevel * level)} de chance de mejorar la rareza",
    };

    // Lo que da CADA nivel, en palabras.
    public static string DescribePerLevel(ProfessionDefinition profession) => profession.Key switch
    {
        ProfessionCatalog.EnchanterKey => $"−{Pct(DustDiscountPerLevel)} de Polvo por intento",
        _ => $"+{Pct(QuantityPerLevel)} de cantidad y +{Pct(RarityUpgradePerLevel)} de chance de mejorar la rareza",
    };

    // Lo que da la versión avanzada, en palabras.
    public static string DescribeAdvanced(ProfessionDefinition profession) => profession.Key switch
    {
        ProfessionCatalog.EnchanterKey => $"**{profession.AdvancedName}** (`/enchant modo:Avanzado`): tira {AdvancedEnchantRolls} veces y se queda con el mejor tier, a {Dec(AdvancedEnchantCostMultiplier)}× el costo",
        _ => $"**{profession.AdvancedName}** (`{profession.Command} modo:Avanzada`): {AdvancedGatherRolls} sorteos de una vez, con su propio enfriamiento de {(int)AdvancedGatherCooldown.TotalHours} h",
    };

    // 0,002 → "0,2 %" (con coma decimal y sin ceros de más).
    public static string Pct(double fraction) => $"{Dec(fraction * 100)} %";

    private static string Dec(double value) => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
}
