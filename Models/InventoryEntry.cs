namespace BotDsRpg.Models;

public sealed class InventoryEntry
{
    public string ItemName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Rarity { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string? Emoji { get; init; }
}
