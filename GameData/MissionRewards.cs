namespace BotDsRpg.GameData;

// Qué caja se regala. Las cajas de zona siguen la escalera de cajas (Cajón de Pino -> Cofre de Oro) según la zona del jugador.
public enum BoxGrant
{
    None,
    ZoneTier,        // la caja que corresponde a su zona
    ZoneTierPlusOne, // una más arriba que la de su zona (el premio mayor)
    Mythic,          // el Arca del Soberano: solo premio, no se compra (ver Database/seed_boxes.sql)
}

// El premio de una misión o de un tramo de logro, ANTES de saber a quién se le paga: en unidades que escalan con la zona y el
// nivel del jugador. Resolve() lo convierte en números concretos.
//   GoldUnits:  cuántas "cacerías de oro" de su zona vale (ver GoldPerHunt).
//   XpPercent:  porcentaje de lo que le falta para subir el nivel que tiene hoy (así vale igual de nivel 3 que de nivel 30).
public sealed record RewardSpec(int GoldUnits, int XpPercent, BoxGrant Box = BoxGrant.None, int BoxQuantity = 1);

public sealed record ResolvedReward(int Gold, int Xp, string? BoxName, int BoxQuantity);

// La cuenta pura de los premios de misiones y logros. Misma unidad que usan las cajas para su precio (Database/seed_boxes.sql):
// lo que da UNA cacería de la zona. Si cambia la economía de oro de las zonas, hay que actualizar GoldPerHunt (y volver a mirar
// Database/report_box_economy.sql).
public static class MissionRewards
{
    // Oro promedio de una cacería por posición de zona (1..5): Z1 ~14, Z2 ~58, Z3 ~118, Z4 ~215, Z5 ~375.
    private static readonly int[] GoldPerHunt = [14, 58, 118, 215, 375];

    // La escalera de cajas que se venden en la tienda; la quinta zona usa la misma que la cuarta (la Mítica no se regala por zona).
    private static readonly string[] BoxLadder = ["Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro"];

    public const string MythicBox = "Arca del Soberano";

    // Una zona desconocida (rank 0) cuenta como la primera: el premio nunca queda en cero ni rompe.
    public static int GoldUnit(int zoneRank) => GoldPerHunt[Math.Clamp(zoneRank, 1, GoldPerHunt.Length) - 1];

    public static string? BoxName(BoxGrant grant, int zoneRank) => grant switch
    {
        BoxGrant.None => null,
        BoxGrant.Mythic => MythicBox,
        BoxGrant.ZoneTier => BoxLadder[Math.Clamp(zoneRank, 1, BoxLadder.Length) - 1],
        BoxGrant.ZoneTierPlusOne => BoxLadder[Math.Clamp(zoneRank + 1, 1, BoxLadder.Length) - 1],
        _ => null,
    };

    public static IReadOnlyList<string> AllBoxNames => [.. BoxLadder, MythicBox];

    public static ResolvedReward Resolve(RewardSpec spec, int zoneRank, int playerLevel)
    {
        int gold = spec.GoldUnits * GoldUnit(zoneRank);
        int xp = spec.XpPercent <= 0
            ? 0
            : Math.Max(1, (int)(LevelingCalculator.RequiredXpForLevel(Math.Max(1, playerLevel)) * spec.XpPercent / 100.0));
        string? box = BoxName(spec.Box, zoneRank);

        return new ResolvedReward(gold, xp, box, box is null ? 0 : Math.Max(1, spec.BoxQuantity));
    }

    // "💰 84 oro · ⭐ 120 XP · 📦 Cajón de Pino" — lo que se muestra en las pantallas y en el recibo.
    public static string Describe(ResolvedReward reward)
    {
        var parts = new List<string>();

        if (reward.Gold > 0)
        {
            parts.Add($"💰 {reward.Gold} oro");
        }

        if (reward.Xp > 0)
        {
            parts.Add($"⭐ {reward.Xp} XP");
        }

        if (reward.BoxName is not null)
        {
            parts.Add(reward.BoxQuantity > 1 ? $"📦 {reward.BoxQuantity}× {reward.BoxName}" : $"📦 {reward.BoxName}");
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }
}
