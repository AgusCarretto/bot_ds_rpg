namespace BotDsRpg.Models;

public sealed class Item
{
    public int ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Rarity { get; init; } = string.Empty;
    public int StatValue { get; init; }
    public int SellPrice { get; init; }
}
