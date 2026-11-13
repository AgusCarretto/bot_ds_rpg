using System.Data.Common;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

// El bestiario (v0.56.0): qué monstruos venció cada jugador y cuántas veces (tabla player_bestiary, Database/add_bestiary.sql).
public interface IBestiaryRepository
{
    // Suma una muerte de ese monstruo (por nombre: monsters.name es único). Devuelve true si fue la PRIMERA vez que ese jugador lo
    // vence; false si ya lo tenía o si no hay un monstruo con ese nombre (el jefe mundial, por ejemplo, no está en la tabla).
    Task<bool> RecordKillAsync(ulong discordId, string monsterName, CancellationToken cancellationToken = default);

    // Nombre del monstruo → cuántas veces lo venció. Solo los que venció alguna vez.
    Task<IReadOnlyDictionary<string, long>> GetKillsAsync(ulong discordId, CancellationToken cancellationToken = default);
}

public sealed class BestiaryRepository(IDbConnectionFactory connectionFactory) : IBestiaryRepository
{
    public async Task<bool> RecordKillAsync(ulong discordId, string monsterName, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // UNA sentencia: el upsert es atómico, así que dos victorias simultáneas suman las dos y solo una se lleva el «primera vez»
        // (xmax = 0 solo en la fila que se insertó).
        bool? inserted = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            """
            INSERT INTO player_bestiary (discord_id, monster_id, kills)
            SELECT @DiscordId, monster_id, 1 FROM monsters WHERE name = @Name
            ON CONFLICT (discord_id, monster_id) DO UPDATE SET kills = player_bestiary.kills + 1
            RETURNING (xmax = 0);
            """,
            new { DiscordId = (long)discordId, Name = monsterName }, cancellationToken: cancellationToken));

        if (inserted != true)
        {
            return false;
        }

        // Primera vez: el contador arranca con lo que ya había vencido ANTES del bestiario (el dueño, 2026-11-13: «que lo desbloquee de 0
        // pero si alguien ya jugaba que aparezca desde la cantidad actual»). Se suma aparte del upsert y con «kills + h», así una victoria
        // simultánea no se pisa.
        long history = await HistoryAsync(connection, discordId, monsterName, cancellationToken);
        if (history > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE player_bestiary SET kills = kills + @History
                WHERE discord_id = @DiscordId AND monster_id = (SELECT monster_id FROM monsters WHERE name = @Name);
                """,
                new { DiscordId = (long)discordId, Name = monsterName, History = history }, cancellationToken: cancellationToken));
        }

        return true;
    }

    // Cuántas veces lo venció antes de que el bestiario anotara monstruos, sacado de game_events (que guarda zona y tipo de pelea, NO el
    // monstruo). El corte es la primera victoria con nombre de ese jugador (enemy_defeated con detail): de ahí en adelante cuenta el
    // bestiario, así nada se cuenta dos veces.
    //  - viaje: travel_win de su zona (hay UN monstruo de viaje por zona: exacto);
    //  - jefe: boss_win + raid_win de su zona menos gate_win (al Asador se le gana con /boss parado en la última zona: exacto);
    //  - el Asador (zona 0): gate_win (exacto);
    //  - cacería: hunt_win de su zona repartido entre los monstruos de cacería de la zona (las zonas 6 a 10 tienen uno: exacto; las 1 a 5
    //    tienen dos y /hunt elige al azar, así que es la MITAD de cada uno, una estimación).
    private static async Task<long> HistoryAsync(DbConnection connection, ulong discordId, string monsterName, CancellationToken cancellationToken)
    {
        var monster = await connection.QuerySingleOrDefaultAsync<(int ZoneId, bool IsBoss, bool IsTravel)?>(new CommandDefinition(
            "SELECT zone_id, is_boss, is_travel FROM monsters WHERE name = @Name;", new { Name = monsterName }, cancellationToken: cancellationToken));
        if (monster is not { } m)
        {
            return 0;
        }

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            WITH cut AS (
                SELECT COALESCE(MIN(occurred_at), now()) AS t FROM game_events
                WHERE discord_id = @DiscordId AND kind = 'enemy_defeated' AND detail IS NOT NULL
            ),
            past AS (
                SELECT kind, zone_id FROM game_events, cut WHERE discord_id = @DiscordId AND occurred_at < cut.t
                  AND kind IN ('hunt_win', 'travel_win', 'boss_win', 'raid_win', 'gate_win')
            )
            SELECT CASE
                WHEN @ZoneId = 0 THEN (SELECT count(*) FROM past WHERE kind = 'gate_win')
                WHEN @IsBoss THEN GREATEST(0,
                    (SELECT count(*) FROM past WHERE kind IN ('boss_win', 'raid_win') AND zone_id = @ZoneId)
                  - (SELECT count(*) FROM past WHERE kind = 'gate_win' AND zone_id = @ZoneId))
                WHEN @IsTravel THEN (SELECT count(*) FROM past WHERE kind = 'travel_win' AND zone_id = @ZoneId)
                ELSE (SELECT count(*) FROM past WHERE kind = 'hunt_win' AND zone_id = @ZoneId)
                   / GREATEST(1, (SELECT count(*) FROM monsters WHERE zone_id = @ZoneId AND NOT is_boss AND NOT is_travel))
            END;
            """,
            new { DiscordId = (long)discordId, m.ZoneId, m.IsBoss, m.IsTravel }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyDictionary<string, long>> GetKillsAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<(string Name, long Kills)>(new CommandDefinition(
            "SELECT m.name, b.kills FROM player_bestiary b JOIN monsters m ON m.monster_id = b.monster_id WHERE b.discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.Name, r => r.Kills);
    }
}
