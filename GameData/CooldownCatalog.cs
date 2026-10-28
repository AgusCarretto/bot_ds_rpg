namespace BotDsRpg.GameData;

// RetryAfterFailure: si no es null, el cooldown se reclama al EMPEZAR la pelea dejando solo ese tiempo, y recién una VICTORIA lo completa
// (ver IAdventureRepository.TryClaimCooldownAsync y ApplyBossVictoryAsync): perder, huir o abandonar cuesta menos que ganar.
public sealed record CooldownDefinition(string CommandName, string DisplayName, string Emoji, TimeSpan Duration, TimeSpan? RetryAfterFailure = null);

// Fuente única de verdad para nombre/emoji/duración de cada comando con cooldown.
// La usan /hunt, /travel, /chop, /mine (cada uno su propia entrada) y /cd (recorre "All").
public static class CooldownCatalog
{
    public static readonly CooldownDefinition Hunt = new("hunt", Loc.Mark("Cazar"), "🏹", TimeSpan.FromMinutes(1));
    // 30 minutos (hasta la v0.7.1 eran 10): una expedición contra un élite. La recompensa y la chance de drop se subieron para pagar LO MISMO por minuto
    // (CombatRewardCalculator.TravelRewardMultiplier; la caja del viaje sale de zone_boxes, rol "travel").
    public static readonly CooldownDefinition Travel = new("travel", Loc.Mark("Viajar"), "🗺️", TimeSpan.FromMinutes(30));
    public static readonly CooldownDefinition Chop = new("chop", Loc.Mark("Talar"), "🪓", TimeSpan.FromMinutes(5));
    public static readonly CooldownDefinition Mine = new("mine", Loc.Mark("Minar"), "⛏️", TimeSpan.FromMinutes(5));
    // El jefe de zona solo o en equipo ("/boss @amigo", antes /raid) es EL MISMO combate: un solo cooldown, y /cd lo muestra en una sola línea.
    // Es un hito de progresión, no contenido para repetir: 5 horas (30 minutos hasta la v0.7.0, 5 horas en la v0.7.1, 1 hora en la v0.8.0 y otra vez 5 horas
    // por pedido del dueño: con 1 hora los cofres de jefe eran demasiados). Es el único lugar para cambiarlo.
    // 5 horas si lo ganás; si perdés, huís o abandonás, solo 30 minutos (RetryAfterFailure): reintentar el jefe de la puerta de la zona no puede costar horas.
    public static readonly CooldownDefinition Boss = new("boss", Loc.Mark("Jefe"), "👑", TimeSpan.FromHours(5), RetryAfterFailure: TimeSpan.FromMinutes(30));

    // El raid lee el cooldown con la misma definición (mismo CommandName: reclamar uno bloquea al otro). Se mantiene el nombre porque el raid lo chequea
    // al arrancar y al unirse (antes recién se enteraba al empezar y dejaba afuera a quien lo tenía ocupado).
    public static readonly CooldownDefinition Raid = Boss;

    // Comprar una caja en /shop: una compra cada 2 horas (era 1 hora hasta la v0.8.0), y cada compra es de UNA caja (ShopCatalog.BoxesPerPurchase). Es un
    // freno al ritmo con que entran cajas al juego; el cooldown se cobra SOLO si la compra sale bien (si falta oro no se gasta). Para cambiar
    // cada cuánto se puede comprar, la duración de acá abajo es el único lugar.
    public static readonly CooldownDefinition BoxBuy = new("buybox", Loc.Mark("Comprar caja"), "📦", TimeSpan.FromHours(2));

    // La versión avanzada de /chop y /mine (v0.13.0): solo con el oficio al nivel máximo (GameData/ProfessionRules.cs), con su PROPIO cooldown para no pisar al normal. No están en All:
    // /cd las muestra solo a quien ya las desbloqueó.
    public static readonly CooldownDefinition ChopAdvanced = new("chop_adv", Loc.Mark("Tala avanzada"), "🪓", ProfessionRules.AdvancedGatherCooldown);
    public static readonly CooldownDefinition MineAdvanced = new("mine_adv", Loc.Mark("Minería avanzada"), "⛏️", ProfessionRules.AdvancedGatherCooldown);

    // Votar por el bot (v0.18.0, GameData/Vote.cs). La fila guarda el momento del último voto COBRADO (el created_at de top.gg), así que "última vez + 12 h" es cuándo se puede votar de nuevo.
    // No está en All a propósito: /cd la muestra solo cuando el voto está habilitado (hay token de top.gg), para no listar algo que todavía no se puede hacer.
    public static readonly CooldownDefinition Vote = new("vote", Loc.Mark("Votar"), "🗳️", VoteRules.Interval);

    public static readonly IReadOnlyList<CooldownDefinition> All = [Hunt, Travel, Chop, Mine, Boss, BoxBuy];

    // La versión avanzada de un cooldown de recolección (null si no tiene).
    public static CooldownDefinition? AdvancedOf(CooldownDefinition definition) =>
        definition.CommandName == Chop.CommandName ? ChopAdvanced : definition.CommandName == Mine.CommandName ? MineAdvanced : null;
}
