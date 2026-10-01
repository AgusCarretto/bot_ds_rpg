using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Qué se vende en la tienda: la comida (Consumable) y las cajas (Caja). Una única regla para todo lo que lista o valida
// compras (/shop view, /shop buy, el autocompletado): "se vende" = es de uno de esos tipos Y tiene precio de compra mayor que
// 0. Las cajas que son solo premio (la Mítica) tienen precio 0, así no aparecen ni se pueden comprar.
public static class ShopCatalog
{
    public static readonly string[] SoldTypes = ["Consumable", "Caja"];

    public static bool IsForSale(Item item) => item.BuyPrice > 0 && SoldTypes.Contains(item.Type);
}
