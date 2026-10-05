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

    // La zona cuyo jefe todavía hay que derrotar para poder entrar a "targetZoneId", según hasta dónde
    // llegó el jugador (users.highest_zone_cleared, 0 = ninguna), o null si la progresión no lo frena
    // (es la primera zona, o ya despejó la anterior). No mira si esa zona TIENE un jefe cargado: si no
    // lo tiene no hay a quién derrotar y el llamador no bloquea (no sería justo trabar el avance por
    // contenido que todavía no existe). Es la ÚNICA definición de esta regla: /zona y la lista de zonas
    // que se ofrece al escribirlo tienen que coincidir, si no la lista promete algo que /zona rechaza.
    public static Zone? PendingGatekeeperZone(IReadOnlyList<Zone> orderedZones, int targetZoneId, int highestZoneCleared)
    {
        int targetRank = RankOf(orderedZones, targetZoneId);
        if (targetRank <= 1)
        {
            return null;
        }

        int clearedRank = highestZoneCleared == 0 ? 0 : RankOf(orderedZones, highestZoneCleared);
        return targetRank > clearedRank + 1 ? orderedZones[targetRank - 2] : null;
    }

    // La zona más alta que el jugador tiene DESBLOQUEADA (1-indexada por dificultad): la misma regla que /zona (nivel mínimo de la zona y jefe de la
    // anterior derrotado). La primera siempre está. Las desbloqueadas son un tramo corrido desde la primera, así que se corta en la primera cerrada.
    public static int MaxUnlockedRank(IReadOnlyList<Zone> orderedZones, int playerLevel, int highestZoneCleared)
    {
        int best = 1;
        for (int i = 0; i < orderedZones.Count; i++)
        {
            var zone = orderedZones[i];
            if (i > 0 && (playerLevel < zone.MinLevel || PendingGatekeeperZone(orderedZones, zone.ZoneId, highestZoneCleared) is not null))
            {
                break;
            }

            best = i + 1;
        }

        return best;
    }
}
