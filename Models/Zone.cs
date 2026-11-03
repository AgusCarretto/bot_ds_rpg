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
    // v0.47.0: cuántos Fuegos Nuevos hacen falta para que esta zona exista para el jugador (0 = las zonas 1 a 5; 5/8/11/14/17 = las zonas 6 a 10). Ver GameData/ZoneRanking.ForPlayer.
    public int MinFuegoNuevo { get; init; }
}
