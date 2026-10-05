using System.Data;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class MonsterRepository(IDbConnectionFactory connectionFactory) : IMonsterRepository
{
    public async Task<IReadOnlyList<MonsterTemplate>> GetMonstersByZoneAsync(int zoneId, CancellationToken cancellationToken = default) =>
        // Ni el jefe ni el monstruo de /travel entran al pool aleatorio de /hunt: cada uno se
        // enfrenta a propósito con su comando (ver GetBossByZoneAsync / GetTravelMonsterByZoneAsync).
        (await QueryMonstersAsync(zoneId, cancellationToken)).Where(m => m.Kind == MonsterKind.Hunt).Select(m => m.Monster).ToList();

    public async Task<MonsterTemplate?> GetBossByZoneAsync(int zoneId, CancellationToken cancellationToken = default) =>
        // A lo sumo un jefe por zona (no hay constraint que lo garantice a nivel de base, pero el
        // seed de jefes solo carga uno por zona) — el primero alcanza.
        (await QueryMonstersAsync(zoneId, cancellationToken)).FirstOrDefault(m => m.Kind == MonsterKind.Boss)?.Monster;

    public async Task<MonsterTemplate?> GetTravelMonsterByZoneAsync(int zoneId, CancellationToken cancellationToken = default) =>
        // Igual que con el jefe: el seed carga uno solo por zona.
        (await QueryMonstersAsync(zoneId, cancellationToken)).FirstOrDefault(m => m.Kind == MonsterKind.Travel)?.Monster;

    public Task<IReadOnlyList<ZoneMonster>> GetAllAsync(CancellationToken cancellationToken = default) =>
        QueryMonstersAsync(zoneId: null, cancellationToken);

    // Dos consultas en vez de un JOIN de 3 tablas (mismo criterio que RecipeRepository.GetAllAsync):
    // evita duplicar la fila del monstruo una vez por drop. Trae todos los tipos (hunt, jefe y travel) de una
    // zona —o de todas si zoneId es null— y cada uno sale etiquetado con su MonsterKind, así un mismo
    // método alimenta a /hunt, /boss, /travel y /drops.
    private async Task<IReadOnlyList<ZoneMonster>> QueryMonstersAsync(int? zoneId, CancellationToken cancellationToken)
    {
        using IDbConnection connection = connectionFactory.CreateConnection();

        const string monstersSql = """
            SELECT monster_id AS "MonsterId", zone_id AS "ZoneId", name AS "Name", emoji AS "Emoji", portrait_emoji AS "Portrait", min_hp AS "MinHp",
                   max_hp AS "MaxHp", min_damage AS "MinDamage", max_damage AS "MaxDamage",
                   gold_reward AS "GoldBonus", xp_reward AS "XpBonus", is_boss AS "IsBoss", is_travel AS "IsTravel"
            FROM monsters
            WHERE CAST(@ZoneId AS integer) IS NULL OR zone_id = CAST(@ZoneId AS integer)
            ORDER BY monster_id;
            """;

        var monsterRows = (await connection.QueryAsync<MonsterRow>(new CommandDefinition(
            monstersSql, new { ZoneId = zoneId }, cancellationToken: cancellationToken))).AsList();

        if (monsterRows.Count == 0)
        {
            return [];
        }

        const string dropsSql = """
            SELECT md.monster_id AS "MonsterId", i.name AS "ItemName"
            FROM monster_drops md
            JOIN monsters m ON m.monster_id = md.monster_id
            JOIN items i ON i.item_id = md.item_id
            WHERE CAST(@ZoneId AS integer) IS NULL OR m.zone_id = CAST(@ZoneId AS integer)
            ORDER BY md.monster_id;
            """;

        var dropRows = (await connection.QueryAsync<DropRow>(new CommandDefinition(
            dropsSql, new { ZoneId = zoneId }, cancellationToken: cancellationToken))).AsList();
        var dropsByMonster = dropRows.ToLookup(row => row.MonsterId, row => row.ItemName);

        return monsterRows
            .Select(row => new ZoneMonster(
                row.ZoneId,
                row.IsBoss ? MonsterKind.Boss : row.IsTravel ? MonsterKind.Travel : MonsterKind.Hunt,
                new MonsterTemplate(
                    row.Name,
                    row.Emoji ?? string.Empty,
                    row.MinHp,
                    row.MaxHp,
                    row.MinDamage,
                    row.MaxDamage,
                    dropsByMonster[row.MonsterId].ToList(),
                    row.GoldBonus,
                    row.XpBonus,
                    row.Portrait)))
            .ToList();
    }

    private sealed record MonsterRow(
        int MonsterId, int ZoneId, string Name, string? Emoji, string? Portrait, int MinHp, int MaxHp, int MinDamage, int MaxDamage,
        int GoldBonus, int XpBonus, bool IsBoss, bool IsTravel);

    private sealed record DropRow(int MonsterId, string ItemName);
}
