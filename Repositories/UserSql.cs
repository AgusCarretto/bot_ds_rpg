namespace BotDsRpg.Repositories;

// Lista de columnas compartida por toda query que devuelva una fila completa de "users"
// (UserRepository y AdventureRepository), para no repetir/desincronizar el mismo bloque
// de alias cada vez que se agrega una columna nueva a la tabla.
internal static class UserSql
{
    public const string SelectColumns = """
        discord_id        AS "DiscordId",
        class             AS "Class",
        level             AS "Level",
        xp                AS "Xp",
        gold              AS "Gold",
        max_hp            AS "MaxHp",
        current_hp        AS "CurrentHp",
        current_weapon_id AS "CurrentWeaponId"
        """;
}
