namespace BotDsRpg.Repositories;

// Lista de columnas compartida por toda query que devuelva una fila completa de "zones".
internal static class ZoneSql
{
    public const string SelectColumns = """
        zone_id     AS "ZoneId",
        name        AS "Name",
        description AS "Description",
        min_level   AS "MinLevel",
        emoji       AS "Emoji"
        """;
}
