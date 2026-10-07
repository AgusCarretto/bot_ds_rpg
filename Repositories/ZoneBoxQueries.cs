using System.Data.Common;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

// Carga la tabla zone_boxes (con la posición de cada zona por dificultad) para armar un ZoneBoxTable. Se usa DENTRO de una transacción (RewardPayer: el premio se paga con la
// misma tabla que se lee) y desde Services/ZoneBoxService, que la guarda unos minutos.
public static class ZoneBoxQueries
{
    public static async Task<ZoneBoxTable> LoadAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken = default)
    {
        // La posición de cada zona normal por min_level (la misma regla de GameData/ZoneRanking: nunca por id); la puerta es 0.
        var rows = await connection.QueryAsync<ZoneBoxRow>(new CommandDefinition(
            """
            SELECT zb.zone_id AS "ZoneId", COALESCE(rk.rank, 0) AS "Rank", zb.role AS "Role", i.name AS "BoxName", zb.chance_percent AS "ChancePercent"
            FROM zone_boxes zb
            JOIN items i ON i.item_id = zb.box_item_id
            LEFT JOIN (SELECT zone_id, (row_number() OVER (ORDER BY min_level, zone_id))::int AS rank FROM zones WHERE kind = 'normal') rk ON rk.zone_id = zb.zone_id
            ORDER BY COALESCE(rk.rank, 0), zb.role;
            """,
            transaction: transaction, cancellationToken: cancellationToken));

        return new ZoneBoxTable(rows);
    }
}
