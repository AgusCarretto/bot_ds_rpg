namespace BotDsRpg.Repositories;

// Lista de columnas compartida por toda query que devuelva una fila completa de "items".
// Calificada con el alias "i" a propósito (no "items.", así queda corto): toda query que use esto
// tiene que tener a la tabla items aliaseada como "i" (ver ItemRepository.cs, RecipeRepository.cs,
// InventoryRepository.cs). item_id en particular DEBE ir calificado — inventory también tiene una
// columna item_id, y sin el prefijo Postgres rechaza la query entera por "referencia ambigua" en
// cualquier JOIN contra inventory (bug real que rompía /heal, nunca se había disparado porque
// hacía falta un jugador con HP de menos que además tuviera un Consumable en el inventario).
internal static class ItemSql
{
    public const string SelectColumns = """
        i.item_id     AS "ItemId",
        i.name        AS "Name",
        i.type        AS "Type",
        i.rarity      AS "Rarity",
        i.stat_value  AS "StatValue",
        i.sell_price  AS "SellPrice",
        i.buy_price   AS "BuyPrice",
        i.weapon_family AS "WeaponFamily",
        i.class_requirement AS "ClassRequirement",
        i.emoji AS "Emoji"
        """;
}
