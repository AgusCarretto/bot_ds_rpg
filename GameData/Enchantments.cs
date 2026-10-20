using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Encantamientos (/enchant): una mejora con TIER para el arma (sube su ATQ) y otra para el amuleto (sube su DEF). Es poder extra, así que está acotado:
//   · el bonus es un % del stat de la PIEZA (no del total del personaje) y llega al +26 % solo con el tier más raro (3 % de los intentos);
//   · cada intento cuesta oro + Polvo (el Polvo sale de desmantelar materiales, GameData/Dismantling.cs) según la zona de la pieza: es un sumidero;
//   · el tier sale al azar y SOLO reemplaza al actual si es mejor (nunca se baja), pero el intento se paga igual;
//   · vender la pieza equipada borra su encantamiento (es de la pieza, no del jugador).
// Con el equipo de la zona, el tier más alto da ~+15-20 % de ataque total o ~+20 de defensa: si hace falta, el contrapeso es subir los monstruos de la
// segunda vuelta (después del reset), no los de la primera (el escalón entre zonas está medido, ver CLAUDE.md).
public static class Enchantments
{
    // Cuántos tiers hay: sale de la TABLA de abajo, no es un número aparte. Era una constante (5) y alguien que la subió a mano (100) rompió todo lo que recorre los tiers
    // ("/enchant info" tiraba IndexOutOfRange): ahora no hay un número que desincronizar. Para agregar un tier se agrega una fila (y la base solo acepta 0 a 5: users_*_enchant_check).
    public static int MaxTier => Tiers.Length;

    // Un tier válido (1 hasta el último). Todas las cuentas de abajo y el perfil lo usan, así nadie indexa la tabla con un tier que no existe.
    public static bool IsValidTier(int tier) => tier >= 1 && tier <= Tiers.Length;

    private static readonly (string Name, int BonusPercent, int Weight)[] Tiers =
    [
        ("Tibio", 4, 40),
        ("Al Rojo", 8, 30),
        ("Ardiente", 13, 18),
        ("Incandescente", 19, 9),
        ("Soberano", 26, 3),
    ];

    // Los EFECTOS (v0.22.0, el dueño: «efectos nuevos»): cada pieza tiene UN efecto y un tier. El jugador ELIGE el efecto y el tier se sortea (cambiar de efecto reemplaza el que tenía).
    //   Arma:     Filo (+% de ATQ de la pieza, el de siempre) · Crítico (+puntos de chance de crítico) · Sangre (cura un % del daño de cada golpe).
    //   Amuleto:  Guarda (+% de DEF de la pieza, el de siempre) · Vida (+% de vida máxima) · Esquive (+puntos de chance de esquivar).
    // Filo y Guarda valen en todos lados (duelos incluidos, como siempre); los otros cuatro valen SOLO contra monstruos (como las bendiciones de poder): sin PlayerBonuses no cuentan.
    // MAGNITUDES (medidas con tools/balance2 parte L, jefe de las zonas 2 a 5 y cacería común, 4 clases): cada efecto al tier V queda a la par del Filo V (jefe de la zona 5 a mín+3: pierde 54 % sin encantar,
    // 32 % con Filo V, 34 % con Crítico V, 33 % con Sangre V, 26 % con Vida V y 34 % con Esquive V). La Guarda V (8 %) queda MUY por encima de todos: la defensa se resta fija de cada golpe y +26 % de una
    // amuleto de 120 son ~31 puntos menos por golpe (ver CLAUDE.md, «Amulets and defense»); es el estado de siempre y no se tocó.
    public const string Filo = "filo", Guarda = "guarda", Critico = "critico", Sangre = "sangre", Vida = "vida", Esquive = "esquive";

    // ValuesByTier: el valor de cada tier (1..5), en la unidad de Unit. Summary: lo que hace, en una frase (para /enchant info y la ayuda).
    public sealed record EnchantKind(string Key, string Slot, string Name, string Unit, IReadOnlyList<int> ValuesByTier, bool PveOnly, string Summary);

    private static readonly EnchantKind[] KindTable =
    [
        new(Filo, "weapon", "Filo", "% de ATQ de la pieza", Tiers.Select(t => t.BonusPercent).ToArray(), false, "sube el ATQ del arma"),
        new(Critico, "weapon", "Crítico", "puntos de chance de crítico", new[] { 3, 6, 10, 15, 20 }, true, "más chance de golpe crítico (el de base es 10 %)"),
        new(Sangre, "weapon", "Sangre", "% del daño en vida", new[] { 1, 2, 3, 4, 5 }, true, "curás un % del daño de cada golpe"),
        new(Guarda, "amulet", "Guarda", "% de DEF de la pieza", Tiers.Select(t => t.BonusPercent).ToArray(), false, "sube la DEF del amuleto"),
        new(Vida, "amulet", "Vida", "% de vida máxima", new[] { 4, 8, 13, 18, 24 }, true, "más vida máxima en combate"),
        new(Esquive, "amulet", "Esquive", "puntos de chance de esquivar", new[] { 3, 6, 9, 12, 15 }, true, "más chance de esquivar (la de base es 5 %)"),
    ];

    public static IReadOnlyList<EnchantKind> Kinds => KindTable;

    public static IReadOnlyList<EnchantKind> KindsFor(string slot) => KindTable.Where(k => k.Slot == slot).ToList();

    public static EnchantKind? FindKind(string? key) => KindTable.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.Ordinal));

    // El efecto de siempre de cada pieza: el que tienen todas las piezas ya encantadas antes de la v0.22.0.
    public static string DefaultKind(string slot) => slot == "weapon" ? Filo : Guarda;

    // Lo que escribe el jugador («crítico», "Critico", «SANGRE»...) → la clave del efecto DE ESA PIEZA (o null si no existe o es de la otra pieza). Sin tildes ni mayúsculas.
    public static string? ParseKind(string slot, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return KindTable.FirstOrDefault(k => k.Slot == slot && (AutocompleteText.SameName(k.Key, text) || AutocompleteText.SameName(k.Name, text)))?.Key;
    }

    // Un efecto que cambia el STAT de la pieza (Filo, Guarda) y no el combate: el único que usa Apply.
    public static bool IsStatKind(string? key) => key is Filo or Guarda;

    // El tier que cuenta para Apply: solo si el efecto de la pieza es el de stat (con un efecto de combate el stat no cambia).
    public static int StatTier(string? kindKey, int tier) => IsStatKind(kindKey) ? tier : 0;

    // El valor del efecto en ese tier (en su unidad), o 0 si no hay tier o el efecto no existe.
    public static int ValueOf(string? kindKey, int tier) =>
        FindKind(kindKey) is { } kind && IsValidTier(tier) ? kind.ValuesByTier[tier - 1] : 0;

    // «Crítico Ardiente», «Filo Soberano»...: el nombre del efecto con su tier.
    public static string KindLabel(string? kindKey, int tier) =>
        FindKind(kindKey) is { } kind && IsValidTier(tier) ? $"{kind.Name} {Tiers[tier - 1].Name}" : "Sin encantar";

    // «+13 %», «+6 puntos de crítico»...: lo que da ese efecto en ese tier, completo.
    public static string ValueText(string? kindKey, int tier)
    {
        var kind = FindKind(kindKey);
        if (kind is null || !IsValidTier(tier))
        {
            return string.Empty;
        }

        int value = kind.ValuesByTier[tier - 1];
        return kind.Key switch
        {
            Filo or Guarda => $"+{value} %",
            Critico => $"+{value} puntos de crítico",
            Sangre => $"{value} % del daño en vida",
            Vida => $"+{value} % de vida",
            Esquive => $"+{value} puntos de esquive",
            _ => string.Empty,
        };
    }

    // La versión corta para una línea del perfil: «+13 %», «Crít +6», «Sangre 6 %», «Vida +6 %», «Esq +3».
    public static string ShortText(string? kindKey, int tier)
    {
        int value = ValueOf(kindKey, tier);
        return kindKey switch
        {
            Filo or Guarda => $"+{value} %",
            Critico => $"Crít +{value}",
            Sangre => $"Sangre {value} %",
            Vida => $"Vida +{value} %",
            Esquive => $"Esq +{value}",
            _ => string.Empty,
        };
    }

    // «+4/8/13/19/26 % de ATQ de la pieza», «cura el 1/2/3/4/5 % del daño de cada golpe»...: los cinco valores de un efecto en palabras (para /enchant info).
    public static string DescribeValues(EnchantKind kind)
    {
        string values = string.Join("/", kind.ValuesByTier);
        return kind.Key switch
        {
            Filo => $"+{values} % de ATQ de la pieza",
            Guarda => $"+{values} % de DEF de la pieza",
            Critico => $"+{values} puntos de chance de crítico",
            Sangre => $"cura el {values} % del daño de cada golpe",
            Vida => $"+{values} % de vida máxima",
            Esquive => $"+{values} puntos de chance de esquivar",
            _ => values,
        };
    }

    // Suma los efectos de COMBATE de la pieza encantada al perfil de pasivas de la clase (solo se llama con PlayerBonuses, o sea contra monstruos): el crítico y el esquive son
    // puntos de chance, la vida un % más de vida máxima y la Sangre la cura por golpe. Filo y Guarda no pasan por acá (suman al stat de la pieza).
    public static ClassPassiveProfile ApplyEffects(ClassPassiveProfile passives, User player)
    {
        passives = ApplyOne(passives, player.WeaponEnchantKind, player.WeaponEnchant);
        return ApplyOne(passives, player.AmuletEnchantKind, player.AmuletEnchant);
    }

    private static ClassPassiveProfile ApplyOne(ClassPassiveProfile passives, string? kindKey, int tier)
    {
        if (!IsValidTier(tier) || IsStatKind(kindKey))
        {
            return passives;
        }

        double value = ValueOf(kindKey, tier);
        return kindKey switch
        {
            Critico => passives with { CritChanceBonus = passives.CritChanceBonus + (value / 100.0) },
            Esquive => passives with { DodgeChanceBonus = passives.DodgeChanceBonus + (value / 100.0) },
            Vida => passives with { MaxHpMultiplier = passives.MaxHpMultiplier * (1 + (value / 100.0)) },
            Sangre => passives with { LeechRatio = passives.LeechRatio + (value / 100.0) },
            _ => passives,
        };
    }


    // Dust por intento según la zona de la pieza (1..5), y el oro es "ocho cacerías de oro" de esa zona (MissionRewards.GoldUnit): el mismo
    // reloj de oro que los premios y las cajas. En Zona 1 un intento son ~20 minutos de juntar Polvo (10 a ~0,5 por minuto); en Zona 5, ~2 horas.
    private static readonly int[] DustPerAttempt = [10, 15, 25, 40, 60];
    private const int GoldUnitsPerAttempt = 8;

    public static string TierName(int tier) => IsValidTier(tier) ? Tiers[tier - 1].Name : "Sin encantar";

    public static int BonusPercent(int tier) => IsValidTier(tier) ? Tiers[tier - 1].BonusPercent : 0;

    // La chance (en %) de que UN intento saque justo ese tier: el peso de la tabla sobre el total (40 / 30 / 18 / 9 / 3 %). Es lo que muestra "/enchant info".
    public static double ChancePercent(int tier) =>
        IsValidTier(tier) ? 100.0 * Tiers[tier - 1].Weight / Tiers.Sum(t => t.Weight) : 0;

    // Cuántos intentos hacen falta, en promedio, para llegar a ese tier o a uno mejor: 1 / (chance de sacarlo o algo mejor en un intento). Como el tier
    // nunca baja, el progreso se acumula: Tibio es 1, Al Rojo ~1,7, Ardiente ~3,3, Incandescente ~8,3 y Soberano ~33.
    public static double AttemptsToReach(int tier)
    {
        if (!IsValidTier(tier))
        {
            return 0;
        }

        double atLeast = Tiers.Skip(tier - 1).Sum(t => t.Weight) / (double)Tiers.Sum(t => t.Weight);
        return 1.0 / atLeast;
    }

    // El stat con el encantamiento aplicado: siempre suma al menos 1 si hay encantamiento (si no, un +4 % de un arma de +9 no se notaría).
    public static int Apply(int stat, int tier)
    {
        int percent = BonusPercent(tier);
        if (percent == 0 || stat <= 0)
        {
            return stat;
        }

        return stat + Math.Max(1, (int)Math.Round(stat * percent / 100.0));
    }

    // "Filo Ardiente" / "Guarda Ardiente": cómo se llama el encantamiento de cada pieza. slot: "weapon" o "amulet".
    public static string Label(string slot, int tier) =>
        IsValidTier(tier) ? $"{(slot == "weapon" ? "Filo" : "Guarda")} {Tiers[tier - 1].Name}" : "Sin encantar";

    // El tier de un intento (1..5) con los pesos 40 / 30 / 18 / 9 / 3.
    public static int Roll(Random? rng = null)
    {
        int point = (rng ?? Random.Shared).Next(Tiers.Sum(t => t.Weight));
        int cumulative = 0;
        for (int i = 0; i < Tiers.Length; i++)
        {
            cumulative += Tiers[i].Weight;
            if (point < cumulative)
            {
                return i + 1;
            }
        }

        return Tiers.Length;
    }

    // Lo que cuesta UN intento sobre una pieza de esa zona (1 = Praderas ... 5 = Cráter).
    public static (int Gold, int Dust) Cost(int gearRank)
    {
        int rank = Math.Clamp(gearRank, 1, DustPerAttempt.Length);
        return (MissionRewards.GoldUnit(rank) * GoldUnitsPerAttempt, DustPerAttempt[rank - 1]);
    }

    // La zona de una pieza de equipo sale de su rareza (Común = Zona 1 ... Mítico = Zona 5), igual que en las cajas (BoxCatalog.RequiredZoneRank).
    public static int GearRank(string rarity) => Math.Max(1, RarityCatalog.RankOf(rarity) + 1);
}
