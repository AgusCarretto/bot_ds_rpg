namespace BotDsRpg.Models;

public sealed class InventoryEntry
{
    public string ItemName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Rarity { get; init; } = string.Empty;
    public int Quantity { get; init; }
    // Cuánto paga la tienda por UNA unidad (items.sell_price; 0 = no se puede vender, es un premio).
    public int SellPrice { get; init; }
    public string? Emoji { get; init; }
}
