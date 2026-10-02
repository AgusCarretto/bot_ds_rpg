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

    // El raid usa el MISMO cooldown que /boss (mismo CommandName, así reclamar uno bloquea al otro y se cuentan juntos): es el jefe de la zona, nada más
    // que entre varios. Tiene su propia entrada para que /cd lo muestre por separado y el raid lo chequee al arrancar y al unirse (antes recién
    // se enteraba al empezar y dejaba afuera a quien lo tenía ocupado).
    public static readonly CooldownDefinition Raid = new("boss", "Raid", "🛡️", Boss.Duration);

    // Comprar una caja en /shop: una compra por hora, y cada compra es de UNA caja (ShopCatalog.BoxesPerPurchase). Es un freno al
    // ritmo con que entran cajas al juego; el cooldown se cobra SOLO si la compra sale bien (si falta oro no se gasta). Para cambiar
    // cada cuánto se puede comprar, la duración de acá abajo es el único lugar.
    public static readonly CooldownDefinition BoxBuy = new("buybox", "Comprar caja", "📦", TimeSpan.FromHours(1));

    public static readonly IReadOnlyList<CooldownDefinition> All = [Hunt, Travel, Chop, Mine, Boss, Raid, BoxBuy];
}
