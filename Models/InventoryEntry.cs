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
    // Polvo por unidad al desmantelar (items.dust_value); null = el de su rareza. Los drops de monstruo lo traen (no tienen rareza).
    public int? DustValue { get; init; }
    // La zona de la que sale un drop de monstruo (la del monstruo que no es jefe que lo suelta; null para todo lo demás: madera, minerales, comida, cajas...). Es lo que ordena los drops
    // del inventario: de la Zona 1 a la última, así los de las zonas 6 en adelante quedan abajo, en orden (v0.51.1).
    public int? DropZone { get; init; }
}
