using System.Data;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class MonsterRepository(IDbConnectionFactory connectionFactory) : IMonsterRepository
{
    public async Task<IReadOnlyList<MonsterTemplate>> GetMonstersByZoneAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = connectionFactory.CreateConnection();

        // Dos consultas en vez de un JOIN de 3 tablas (mismo criterio que RecipeRepository.GetAllAsync):
        // evita duplicar la fila del monstruo una vez por drop.
        const string monstersSql = """
            SELECT monster_id AS "MonsterId", name AS "Name", emoji AS "Emoji", min_hp AS "MinHp", max_hp AS "MaxHp",
                   min_damage AS "MinDamage", max_damage AS "MaxDamage", gold_reward AS "GoldBonus", xp_reward AS "XpBonus"
            FROM monsters
            WHERE zone_id = @ZoneId
            ORDER BY monster_id;
            """;

        var monsterRows = (await connection.QueryAsync<MonsterRow>(
            new CommandDefinition(monstersSql, new { ZoneId = zoneId }, cancellationToken: cancellationToken))).AsList();

        if (monsterRows.Count == 0)
        {
            return [];
        }

        const string dropsSql = """
            SELECT md.monster_id AS "MonsterId", i.name AS "ItemName"
            FROM monster_drops md
            JOIN monsters m ON m.monster_id = md.monster_id
            JOIN items i ON i.item_id = md.item_id
            WHERE m.zone_id = @ZoneId
            ORDER BY md.monster_id;
            """;

        var dropRows = (await connection.QueryAsync<DropRow>(
            new CommandDefinition(dropsSql, new { ZoneId = zoneId }, cancellationToken: cancellationToken))).AsList();
        var dropsByMonster = dropRows.ToLookup(row => row.MonsterId, row => row.ItemName);

        return monsterRows
            .Select(row => new MonsterTemplate(
                row.Name,
                row.Emoji ?? string.Empty,
                row.MinHp,
                row.MaxHp,
                row.MinDamage,
                row.MaxDamage,
                dropsByMonster[row.MonsterId].ToList(),
                row.GoldBonus,
                row.XpBonus))
            .ToList();
    }

    private sealed record MonsterRow(int MonsterId, string Name, string? Emoji, int MinHp, int MaxHp, int MinDamage, int MaxDamage, int GoldBonus, int XpBonus);
    private sealed record DropRow(int MonsterId, string ItemName);
}
