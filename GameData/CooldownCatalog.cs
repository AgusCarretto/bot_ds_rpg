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
    // El jefe de zona y el raid son EL MISMO combate (el raid es el jefe entre varios): un solo cooldown, y /cd lo muestra en una sola línea.
    // Es un hito de progresión, no contenido para repetir: 5 HORAS (hasta la v0.7.0 eran 30 minutos). Es el único lugar para cambiarlo.
    // OJO: el x6 de recompensa del jefe (CombatRewardCalculator.BossRewardMultiplier) se calibró con 30 minutos.
    public static readonly CooldownDefinition Boss = new("boss", "Jefe / Raid", "👑", TimeSpan.FromHours(5));

    // El raid lee el cooldown con la misma definición (mismo CommandName: reclamar uno bloquea al otro). Se mantiene el nombre porque el raid lo chequea
    // al arrancar y al unirse (antes recién se enteraba al empezar y dejaba afuera a quien lo tenía ocupado).
    public static readonly CooldownDefinition Raid = Boss;

    // Comprar una caja en /shop: una compra por hora, y cada compra es de UNA caja (ShopCatalog.BoxesPerPurchase). Es un freno al
    // ritmo con que entran cajas al juego; el cooldown se cobra SOLO si la compra sale bien (si falta oro no se gasta). Para cambiar
    // cada cuánto se puede comprar, la duración de acá abajo es el único lugar.
    public static readonly CooldownDefinition BoxBuy = new("buybox", "Comprar caja", "📦", TimeSpan.FromHours(1));

    public static readonly IReadOnlyList<CooldownDefinition> All = [Hunt, Travel, Chop, Mine, Boss, BoxBuy];
}
