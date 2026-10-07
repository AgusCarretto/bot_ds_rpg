namespace BotDsRpg.GameData;

// Los roles de la tabla zone_boxes (v0.14.0, Database/seed_zone_boxes.sql): qué caja GRATIS da una zona y cuándo.
public static class ZoneBoxRole
{
    public const string Repeat = "repeat";       // lo que da cada victoria REPETIDA sobre el jefe (la primera vez por vuelta es el cofre de monster_drops)
    public const string Daily = "daily";         // el premio por completar todas las misiones del día
    public const string Weekly = "weekly";       // ídem, de la semana
    public const string Prize = "prize";         // logros (tramo II) y campeón de la Arena
    public const string PrizeTop = "prize_top";  // logros (tramo III)

    public static readonly IReadOnlyList<string> All = [Repeat, Daily, Weekly, Prize, PrizeTop];
}

// Una fila de zone_boxes ya resuelta: de qué zona (su id y su posición por dificultad, 1 = la primera; 0 = la puerta), el rol, el nombre de la caja y su chance en %.
public sealed record ZoneBoxRow(int ZoneId, int Rank, string Role, string BoxName, int ChancePercent);

// Las cajas gratis de cada zona (la tabla zone_boxes entera), para consultar sin base de datos: PURA, se arma con ZoneBoxQueries.LoadAsync (o a mano en las pruebas).
// Reemplaza a la escalera de cajas que estaba escrita en MissionRewards: una zona nueva ahora necesita sus filas en la base y no una línea de código.
public sealed class ZoneBoxTable(IEnumerable<ZoneBoxRow> rows)
{
    public static readonly ZoneBoxTable Empty = new([]);

    private readonly IReadOnlyList<ZoneBoxRow> _rows = rows.ToList();

    public IReadOnlyList<ZoneBoxRow> Rows => _rows;

    // La posición de la última zona normal que tiene filas.
    public int MaxRank => _rows.Select(r => r.Rank).DefaultIfEmpty(0).Max();

    // La caja de ese rol para la zona en esa posición (1 = la primera). Una posición fuera de rango se acota (0 o menos = la primera, más allá de la última = la última): un premio
    // nunca queda sin caja ni rompe. Null si esa zona no tiene el rol.
    public ZoneBoxRow? ForRank(int zoneRank, string role)
    {
        int max = MaxRank;
        if (max == 0)
        {
            return null;
        }

        int rank = Math.Clamp(zoneRank, 1, max);
        return _rows.FirstOrDefault(r => r.Rank == rank && r.Role == role);
    }

    // La caja de ese rol para una zona puntual (por su id, incluida la puerta). Null si no tiene.
    public ZoneBoxRow? ForZone(int zoneId, string role) => _rows.FirstOrDefault(r => r.ZoneId == zoneId && r.Role == role);

    // Qué falta para que TODAS las zonas normales tengan sus cinco roles (y la puerta, el de repetición): "Zona 6:daily", ... Vacío = está completo. El arranque del bot lo loguea.
    public IReadOnlyList<string> Missing(IEnumerable<(int ZoneId, string Name)> normalZones, IEnumerable<(int ZoneId, string Name)> gateZones)
    {
        var missing = new List<string>();
        foreach (var (zoneId, name) in normalZones)
        {
            missing.AddRange(ZoneBoxRole.All.Where(role => ForZone(zoneId, role) is null).Select(role => $"{name}:{role}"));
        }

        foreach (var (zoneId, name) in gateZones)
        {
            if (ForZone(zoneId, ZoneBoxRole.Repeat) is null)
            {
                missing.Add($"{name}:{ZoneBoxRole.Repeat}");
            }
        }

        return missing;
    }
}
