namespace BotDsRpg.GameData;

public sealed record CooldownDefinition(string CommandName, string DisplayName, string Emoji, TimeSpan Duration);

// Fuente única de verdad para nombre/emoji/duración de cada comando con cooldown.
// La usan /hunt, /travel, /chop, /mine (cada uno su propia entrada) y /cd (recorre "All").
public static class CooldownCatalog
{
    public static readonly CooldownDefinition Hunt = new("hunt", "Cazar", "🏹", TimeSpan.FromMinutes(1));
    public static readonly CooldownDefinition Travel = new("travel", "Viajar", "🗺️", TimeSpan.FromMinutes(10));
    public static readonly CooldownDefinition Chop = new("chop", "Talar", "🪓", TimeSpan.FromMinutes(5));
    public static readonly CooldownDefinition Mine = new("mine", "Minar", "⛏️", TimeSpan.FromMinutes(5));
    // Más largo que /travel a propósito: el jefe no es contenido para farmear cada 10 minutos,
    // es un hito de progresión — igual queda repetible (sin bloqueo si ya lo venciste) por si
    // alguien quiere reintentar el loot, pero no gratis.
    public static readonly CooldownDefinition Boss = new("boss", "Jefe", "👑", TimeSpan.FromMinutes(30));

    public static readonly IReadOnlyList<CooldownDefinition> All = [Hunt, Travel, Chop, Mine, Boss];
}
