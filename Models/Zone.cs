namespace BotDsRpg.Models;

// Refleja 1:1 la tabla "zones".
public sealed class Zone
{
    public int ZoneId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int MinLevel { get; init; }
    public string? Emoji { get; init; }
    // "normal" = una zona de la escalera; "gate" = El Fogón Eterno (zona 0): las listas de zonas no la incluyen (ver IZoneRepository).
    public string Kind { get; init; } = "normal";
}
