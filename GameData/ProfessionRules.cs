using System.Globalization;

namespace BotDsRpg.GameData;

// Un oficio (v0.13.0, docs/superpowers/specs/2026-10-06-oficios-design.md): se sube usando el comando de siempre. StatKind es el contador de player_stats del que sale su XP (el mismo vocabulario
// que game_events.kind y los logros): XP = contador × XpPerAction. Desde la v0.20.0 el Leñador y el Minero tienen un contador PROPIO de XP (chop_xp / mine_xp, XpPerAction = 1: el contador ya es XP, y cada tirada suma la de la rareza
// que salió, ProfessionRules.GatherXp); el Encantador sigue por intentos (25 de XP cada uno). Así el progreso NO se guarda en ningún lado (no hay tabla ni migración, no se puede desincronizar) y lo que el jugador ya
// hizo antes de que existieran los oficios cuenta. Command es el comando que lo sube; AdvancedName, el nombre de su versión avanzada (se desbloquea al nivel máximo).
public sealed record ProfessionDefinition(
    string Key, string Name, string Emoji, string StatKind, int XpPerAction, string Command, string AdvancedName, IReadOnlyList<string> Aliases)
{
    // Los nombres para mostrar, en el idioma de quien los lee. Name y AdvancedName siguen siendo el español (Loc.Mark): es lo que buscan Find y los contadores.
    public string DisplayName => Loc.Tr(Name);
    public string DisplayAdvancedName => Loc.Tr(AdvancedName);
}

public static class ProfessionCatalog
{
    public const string WoodcutterKey = "woodcutter";
    public const string MinerKey = "miner";
    public const string EnchanterKey = "enchanter";

    // Sumar un oficio es una fila acá (y su efecto en ProfessionRules / PlayerBonuses): el resto del juego lo recorre.
    public static readonly IReadOnlyList<ProfessionDefinition> All =
    [
        new(WoodcutterKey, Loc.Mark("Leñador"), "🪓", GameEventKinds.ChopXp, 1, "/chop", Loc.Mark("Tala avanzada"), ["lenador", "leñador", "woodcutter", "chop", "talar", "tala", "madera"]),
        new(MinerKey, Loc.Mark("Minero"), "⛏️", GameEventKinds.MineXp, 1, "/mine", Loc.Mark("Minería avanzada"), ["minero", "miner", "mine", "minar", "mineria", "mineral"]),
        new(EnchanterKey, Loc.Mark("Encantador"), "✨", GameEventKinds.Enchant, 25, "/enchant", Loc.Mark("Encantamiento avanzado"), ["encantador", "enchanter", "enchant", "encantar", "encantamiento"]),
    ];

    public static ProfessionDefinition? Find(string? textOrKey) =>
        string.IsNullOrWhiteSpace(textOrKey)
            ? null
            : All.FirstOrDefault(p => AutocompleteText.SameName(p.Key, textOrKey) || AutocompleteText.SameName(p.Name, textOrKey) || p.Aliases.Any(a => AutocompleteText.SameName(a, textOrKey)));

    public static ProfessionDefinition Get(string key) => All.First(p => p.Key == key);
}

// El nivel de un oficio: de 0 (sin XP) hacia arriba. IntoLevel = XP juntada dentro del nivel actual; ToNext = XP que pide el próximo nivel (0 solo en el tope duro, ProfessionRules.HardCap).
// Desde la v0.38.1 el 100 ya NO es el final: AtEffectCap dice que los efectos llegaron al máximo (y la versión avanzada está abierta) y el nivel sigue subiendo, cada vez más caro.
public readonly record struct ProfessionProgress(int Level, long TotalXp, long IntoLevel, long ToNext)
{
    // Pasó el nivel donde los efectos dejan de crecer (ProfessionRules.MaxLevel).
    public bool AtEffectCap => Level >= ProfessionRules.MaxLevel;

    // Llegó al tope duro de la tabla (ProfessionRules.HardCap): inalcanzable jugando, existe para que la cuenta no se desborde.
    public bool IsMax => Level >= ProfessionRules.HardCap;
}

// Las reglas de los oficios, puras y con nombre (todo se retoca acá). Lo que da cada nivel es LINEAL y siempre igual (como Fuego Nuevo): el nivel 100 se nota muchísimo contra el 10.
//   · Leñador y Minero: +0,2 % de cantidad y +0,1 % de chance de mejorar la rareza por nivel (+20 % y +10 % al 100).
//   · Encantador: −0,25 % de Polvo por intento y por nivel (−25 % al 100).
//   · Al 100: la versión avanzada de cada comando (ver Advanced*).
// Medido con report_recipe_pacing.sql: con los tres oficios al 100 el camino de recetas de las 5 zonas baja solo ~12 % (lo traban los drops de monstruos, no la recolección).
public static class ProfessionRules
{
    // El nivel donde los EFECTOS dejan de crecer y se abre la versión avanzada. Desde la v0.38.1 el nivel del oficio sigue subiendo después (ver PostCapGrowth), pero lo que da el oficio se queda en el del 100.
    public const int MaxLevel = 100;

    // Más allá del 100 (v0.38.1, el dueño: «que siga subiendo… así luego ponemos una pasiva»): pasar de un nivel al siguiente cuesta el del nivel anterior × PostCapGrowth, o sea EXPONENCIAL (+5 % por nivel:
    // del 100 al 150 son ~19.000 de XP, al 200 ~240.000). Por ahora ese avance no da ningún efecto nuevo. HardCap es el último nivel de la tabla (con 5 % cada nivel pide 1,6 millones de XP ahí): nadie lo toca
    // jugando, existe para que la cuenta acumulada no se desborde.
    public const double PostCapGrowth = 1.05;
    public const int HardCap = 300;

    // La curva: pasar del nivel L-1 al L pide XpUnit × (1 + CurveGrowth × L^1,5) de XP. XpUnit era 10 hasta la v0.19.x, cuando cada /chop o /mine daba 10 de XP fijos; desde la v0.20.0 la XP
    // depende de la rareza del material (GatherXp, promedio ~5,4 por tirada), así que la unidad bajó a 5 para que el RITMO siga igual: llegar al 100 son ~750 tiradas (eran ~810) y, con 25 de XP
    // por intento de encantamiento (eran 50), ~160 intentos. Los jugadores que ya tenían nivel conservan el suyo (Database/add_profession_xp.sql siembra su XP vieja × 5 en la escala nueva).
    public const int XpUnit = 5;
    public const double CurveGrowth = 0.0176;

    // Lo que da cada nivel.
    public const double QuantityPerLevel = 0.002;
    public const double RarityUpgradePerLevel = 0.001;
    public const double DustDiscountPerLevel = 0.0025;
    // v0.22.1: cada nivel del Encantador suma esta chance de que el tier SORTEADO suba uno (+10 % al 100): es como mejorar la rareza de un Leñador o un Minero, y lo que hace posible el tier V.
    public const double TierUpgradePerLevel = 0.001;

    // La versión avanzada (nivel MaxLevel): recolección de 4 sorteos juntos con su propio cooldown, y encantamiento de 2 tiradas (se queda con la mejor) a 1,5 veces el costo.
    public const int AdvancedGatherRolls = 4;
    public static readonly TimeSpan AdvancedGatherCooldown = TimeSpan.FromHours(1);
    public const int AdvancedEnchantRolls = 2;
    public const double AdvancedEnchantCostMultiplier = 1.5;

    // XP acumulada que hace falta para estar en cada nivel (índice 0 = nivel 0 = 0 de XP).
    private static readonly long[] CumulativeXp = BuildCumulative();

    private static long[] BuildCumulative()
    {
        var table = new long[HardCap + 1];
        for (int level = 1; level <= HardCap; level++)
        {
            table[level] = table[level - 1] + XpForLevel(level);
        }

        return table;
    }

    // XP que pide pasar del nivel anterior a ESTE nivel (1..HardCap). Hasta el 100 es la curva de siempre; después, el costo del 100 × PostCapGrowth^(niveles pasados del 100).
    public static long XpForLevel(int level)
    {
        int clamped = Math.Clamp(level, 1, HardCap);
        if (clamped <= MaxLevel)
        {
            return (long)Math.Round(XpUnit * (1 + (CurveGrowth * Math.Pow(clamped, 1.5))), MidpointRounding.AwayFromZero);
        }

        return (long)Math.Round(XpForLevel(MaxLevel) * Math.Pow(PostCapGrowth, clamped - MaxLevel), MidpointRounding.AwayFromZero);
    }

    // La XP de UNA tirada de /chop o /mine según la rareza del material que salió (v0.20.0, el dueño: «normal 2, raro 7, épico 15, legendario 30, mítico 100»). Es por TIRADA y no por unidad:
    // si no, los bonus de cantidad (Fuego Nuevo, bendiciones, mascotas) harían subir más rápido el oficio. La rareza que cuenta es la que SALIÓ, ya con la mejora del propio oficio.
    private static readonly (string Rarity, int Xp)[] GatherXpTable = [("Común", 2), ("Raro", 7), ("Épico", 15), ("Legendario", 30), ("Mítico", 100)];

    public static int GatherXp(string? rarity) =>
        GatherXpTable.FirstOrDefault(e => e.Rarity == rarity) is { Rarity: not null } entry ? entry.Xp : GatherXpTable[0].Xp;

    // «Común 2 · Raro 7 · Épico 15 · Legendario 30 · Mítico 100» (para /professions y la ayuda).
    public static string DescribeGatherXp() => string.Join(" · ", GatherXpTable.Select(e => $"{e.Rarity} {e.Xp}"));

    // La XP promedio por tirada con las chances reales de /chop y /mine (RarityCatalog.GatheringChances), sin la mejora del oficio: con ella se calcula cuántas tiradas son llegar al máximo.
    public static double AverageGatherXp() => RarityCatalog.GatheringChances.Sum(c => c.Percent / 100.0 * GatherXp(c.Rarity));

    // De dónde sale la XP de cada oficio, en palabras.
    public static string DescribeXpSource(ProfessionDefinition profession) => profession.Key switch
    {
        ProfessionCatalog.EnchanterKey => $"{profession.XpPerAction} XP por intento",
        _ => $"según lo que te salga, por tirada: {DescribeGatherXp()} XP",
    };

    // XP total para llegar al nivel donde los efectos topan (MaxLevel, el 100).
    public static long TotalXpToMax => CumulativeXp[MaxLevel];

    // XP acumulada con la que se llega a ESTE nivel (0..HardCap). La usan los títulos de maestro de oficio (GameData/TitleCatalog.cs) para expresar la meta en el contador de XP.
    public static long XpToReach(int level) => CumulativeXp[Math.Clamp(level, 0, HardCap)];

    public static ProfessionProgress ProgressFor(long totalXp)
    {
        long xp = Math.Max(0, totalXp);
        int level = 0;
        while (level < HardCap && xp >= CumulativeXp[level + 1])
        {
            level++;
        }

        if (level >= HardCap)
        {
            return new ProfessionProgress(HardCap, xp, 0, 0);
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

    // La chance (0..0,1) de que un tiro de encantamiento suba un tier por el oficio Encantador.
    public static double TierUpgradeChance(int level) => TierUpgradePerLevel * Math.Clamp(level, 0, MaxLevel);

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

    // Lo que da el oficio en ese nivel, en palabras (para /professions y la ayuda). Pasado el 100 los efectos no crecen: se cuentan con el nivel del tope.
    public static string DescribeEffect(ProfessionDefinition profession, int level)
    {
        int effective = Math.Clamp(level, 0, MaxLevel);
        return profession.Key switch
        {
            ProfessionCatalog.EnchanterKey => $"−{Pct(DustDiscountPerLevel * effective)} de Polvo por intento y +{Pct(TierUpgradePerLevel * effective)} de chance de mejorar el tier",
            _ => $"+{Pct(QuantityPerLevel * effective)} de cantidad y +{Pct(RarityUpgradePerLevel * effective)} de chance de mejorar la rareza",
        };
    }

    // «5 %»: cuánto más cuesta cada nivel después del 100 que el anterior (para /professions y la ayuda).
    public static string DescribePostCapGrowth() => Pct(PostCapGrowth - 1);

    // Lo que da CADA nivel, en palabras.
    public static string DescribePerLevel(ProfessionDefinition profession) => profession.Key switch
    {
        ProfessionCatalog.EnchanterKey => $"−{Pct(DustDiscountPerLevel)} de Polvo por intento y +{Pct(TierUpgradePerLevel)} de chance de mejorar el tier",
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
