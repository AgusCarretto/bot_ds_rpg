namespace BotDsRpg.Models;

// Un ítem del catálogo que el jugador tiene en inventario, con el Item completo resuelto (no solo
// nombre/rareza como InventoryEntry) y la cantidad que posee. Usado donde hace falta StatValue/
// BuyPrice del ítem, como elegir automáticamente qué consumible gastar en /heal.
public sealed record OwnedItem(Item Item, int Quantity);
