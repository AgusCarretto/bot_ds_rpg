using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Utilidades puras sobre el orden de dificultad de las zonas. zone_id NO está garantizado
// consecutivo en orden de dificultad para siempre (ver el comentario en Database/schema.sql sobre
// users.highest_zone_cleared) — todo lo que necesite "cuál es la zona siguiente/anterior" tiene que
// pasar por acá, ordenando por min_level, en vez de comparar zone_id crudo.
public static class ZoneRanking
{
    public static IReadOnlyList<Zone> OrderByDifficulty(IReadOnlyList<Zone> zones) =>
        zones.OrderBy(z => z.MinLevel).ToList();

    // Nivel mínimo para desafiar al jefe de "currentZoneId": el min_level de la zona siguiente en
    // dificultad (derrotar a ese jefe es justamente lo que te deja cruzar a esa zona). Null si no
    // hay zona siguiente conocida (última zona cargada, o currentZoneId no está en la lista).
    public static int? RequiredLevelForBoss(IReadOnlyList<Zone> orderedZones, int currentZoneId)
    {
        int currentRank = orderedZones.ToList().FindIndex(z => z.ZoneId == currentZoneId);
        if (currentRank < 0 || currentRank + 1 >= orderedZones.Count)
        {
            return null;
        }

        return orderedZones[currentRank + 1].MinLevel;
    }

    // Posición 1-indexada de una zona dentro del orden de dificultad (usado por el gate de
    // progresión de /zona: no podés saltar más de un escalón más allá de tu highest_zone_cleared).
    // 0 si la zona no está en la lista.
    public static int RankOf(IReadOnlyList<Zone> orderedZones, int zoneId) =>
        orderedZones.ToList().FindIndex(z => z.ZoneId == zoneId) + 1;
}
