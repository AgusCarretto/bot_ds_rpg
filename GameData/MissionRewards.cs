namespace BotDsRpg.GameData;

// Qué caja se regala. Desde la v0.14.0 CUÁL caja es de cada zona sale de la tabla zone_boxes (GameData/ZoneBoxes.cs), no de una escalera escrita en el código.
public enum BoxGrant
{
    None,
    ZoneTier,        // el premio de su zona (rol "prize"): los tramos II de los logros y el campeón de la Arena
    ZoneTierPlusOne, // el premio mayor de su zona (rol "prize_top"): los tramos III de los logros
    Mythic,          // el Arca del Soberano: solo premio, no se compra (ver Database/seed_boxes.sql)
    Daily,           // la caja por completar las misiones del día de su zona (rol "daily")
    Weekly,          // la caja por completar las misiones de la semana de su zona (rol "weekly")
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

    public const string MythicBox = "Arca del Soberano";

    // Una zona desconocida (rank 0) cuenta como la primera: el premio nunca queda en cero ni rompe.
    public static int GoldUnit(int zoneRank) => GoldPerHunt[Math.Clamp(zoneRank, 1, GoldPerHunt.Length) - 1];

    // La caja de un premio para la zona en esa posición, de la tabla zone_boxes (ZoneBoxTable). Una zona sin esa fila es un error de carga de datos: se tira, no se paga sin caja.
    public static string? BoxName(BoxGrant grant, int zoneRank, ZoneBoxTable boxes)
    {
        string? role = grant switch
        {
            BoxGrant.ZoneTier => ZoneBoxRole.Prize,
            BoxGrant.ZoneTierPlusOne => ZoneBoxRole.PrizeTop,
            BoxGrant.Daily => ZoneBoxRole.Daily,
            BoxGrant.Weekly => ZoneBoxRole.Weekly,
            _ => null,
        };

        if (grant == BoxGrant.None)
        {
            return null;
        }

        if (grant == BoxGrant.Mythic)
        {
            return MythicBox;
        }

        return boxes.ForRank(zoneRank, role!)?.BoxName
            ?? throw new InvalidOperationException($"zone_boxes no tiene la caja \"{role}\" para la zona en la posición {zoneRank}: cargala (Database/seed_zone_boxes.sql).");
    }

    // boxes: la tabla de cajas por zona; solo se consulta si el premio incluye una caja de zona (con ZoneBoxTable.Empty alcanza para los premios sin caja o con el Arca).
    public static ResolvedReward Resolve(RewardSpec spec, int zoneRank, int playerLevel, ZoneBoxTable boxes)
    {
        int gold = spec.GoldUnits * GoldUnit(zoneRank);
        int xp = spec.XpPercent <= 0
            ? 0
            : Math.Max(1, (int)(LevelingCalculator.RequiredXpForLevel(Math.Max(1, playerLevel)) * spec.XpPercent / 100.0));
        string? box = BoxName(spec.Box, zoneRank, boxes);

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
