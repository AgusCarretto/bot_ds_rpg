namespace BotDsRpg.Models;

// Refleja 1:1 la tabla "zones".
public sealed class Zone
{
    public int ZoneId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int MinLevel { get; init; }
    public string? Emoji { get; init; }
}
