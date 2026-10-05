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
    public const int MaxTier = 5;

    private static readonly (string Name, int BonusPercent, int Weight)[] Tiers =
    [
        ("Tibio", 4, 40),
        ("Al Rojo", 8, 30),
        ("Ardiente", 13, 18),
        ("Incandescente", 19, 9),
        ("Soberano", 26, 3),
    ];

    // Dust por intento según la zona de la pieza (1..5), y el oro es "ocho cacerías de oro" de esa zona (MissionRewards.GoldUnit): el mismo
    // reloj de oro que los premios y las cajas. En Zona 1 un intento son ~20 minutos de juntar Polvo (10 a ~0,5 por minuto); en Zona 5, ~2 horas.
    private static readonly int[] DustPerAttempt = [10, 15, 25, 40, 60];
    private const int GoldUnitsPerAttempt = 8;

    public static string TierName(int tier) => tier is >= 1 and <= MaxTier ? Tiers[tier - 1].Name : "Sin encantar";

    public static int BonusPercent(int tier) => tier is >= 1 and <= MaxTier ? Tiers[tier - 1].BonusPercent : 0;

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
        tier is >= 1 and <= MaxTier ? $"{(slot == "weapon" ? "Filo" : "Guarda")} {Tiers[tier - 1].Name}" : "Sin encantar";

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
