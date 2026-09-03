namespace BotDsRpg.Repositories;

// Lista de columnas compartida por toda query que devuelva una fila completa de "items".
internal static class ItemSql
{
    public const string SelectColumns = """
        item_id     AS "ItemId",
        name        AS "Name",
        type        AS "Type",
        rarity      AS "Rarity",
        stat_value  AS "StatValue",
        sell_price  AS "SellPrice",
        buy_price   AS "BuyPrice",
        weapon_family AS "WeaponFamily",
        class_requirement AS "ClassRequirement"
        """;
}
