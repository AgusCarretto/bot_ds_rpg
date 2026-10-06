using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Qué se vende en la tienda: la comida (Consumable), las cajas (Caja) y la comida de las mascotas (PetFood, v0.10.0). Una única regla para todo lo que lista o valida
// compras (/shop view, /shop buy, el autocompletado): "se vende" = es de uno de esos tipos Y tiene precio de compra mayor que
// 0. Las cajas que son solo premio (la Mítica) tienen precio 0, así no aparecen ni se pueden comprar (los huevos tampoco: no están en SoldTypes).
public static class ShopCatalog
{
    // items.type de la "Comida para Mascotas": no cura (no se come con /use ni /heal), se le da a una mascota con /pet feed.
    public const string PetFoodType = "PetFood";

    public static readonly string[] SoldTypes = ["Consumable", "Caja", PetFoodType];

    // Cuántas cajas se pueden comprar en una sola compra (y hay una compra cada 2 horas: CooldownCatalog.BoxBuy). La comida no tiene tope.
    public const int BoxesPerPurchase = 1;

    public static bool IsForSale(Item item) => item.BuyPrice > 0 && SoldTypes.Contains(item.Type);
}
