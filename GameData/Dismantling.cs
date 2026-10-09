namespace BotDsRpg.GameData;

// Desmantelar (/dismantle): se rompe un MATERIAL (madera, mineral o drop de monstruo) y da Polvo, que solo sirve para encantar (GameData/Enchantments.cs).
// El camino es de UNA sola mano: el Polvo nunca vuelve a ser material ni drop (las recetas están calibradas con 3 drops por zona y no se puede fabricar
// uno), y nada convierte Carbón en Hierro. Craftear quedó afuera (el dueño: "lo de craftear lo olvidamos").
public static class Dismantling
{
    // De a cuántas unidades se desmantela por comando (1 a 100; eran 5 hasta la v0.9.4 y el dueño lo subió a 100: con cientos de Piedra o Pino en la mochila 5 por vez era
    // eterno). Subirlo NO cambia el ritmo del Polvo (se paga por unidad, y las unidades se gastan de verdad), solo cuántos comandos hacen falta.
    public const int MaxPerCommand = 100;

    // Qué tipos de ítem se pueden desmantelar: lo que se recolecta y lo que sueltan los monstruos. Las cajas se abren, la comida se come y el equipo se forja o se vende.
    public static bool CanDismantle(string itemType) => itemType is "Madera" or "Mineral" or "Material";

    // El Polvo de un DROP DE MONSTRUO se paga por su ORIGEN, no por la rareza del catálogo (v0.15.1, el dueño: «el drop de monstruo no tiene rareza, el % de caída es el mismo en la Zona 1 y en la 5»).
    // Un drop de la Zona 5 figuraba como Mítico y daba 500 de Polvo (30 por cacería, 60 veces el ritmo calibrado de 0,5 por minuto) cuando cae igual que uno de la Zona 1. Se calibra con la misma
    // vara que la tabla de abajo: ~0,5 Polvo por minuto de farmeo. Un drop de cacería sale cada 2 / 6 % = 33,3 min (hay 2 monstruos de cacería por zona y 1 cacería por minuto) → 17; uno de viaje, cada
    // 30 min / 40 % = 75 min → 38. Es un dato de la base (items.dust_value, Database/rebalance_dust.sql): si cambian las chances de drop hay que volver a correrlo con los números nuevos.
    public const double DustPerFarmingMinute = 0.5;

    public static readonly int HuntDropDust = (int)Math.Round(DustPerFarmingMinute * 2 / (CombatRewardCalculator.HuntDropChancePercent / 100.0) * CooldownCatalog.Hunt.Duration.TotalMinutes, MidpointRounding.AwayFromZero);

    public static readonly int TravelDropDust = (int)Math.Round(DustPerFarmingMinute * CooldownCatalog.Travel.Duration.TotalMinutes / (CombatRewardCalculator.TravelDropChancePercent / 100.0), MidpointRounding.AwayFromZero);

    // Polvo por unidad según la rareza. Está puesto para que rinda ~0,5 Polvo por minuto de farmeo en todas las rarezas (Común: 2,45 min → 1; Raro: 11,9 → 6;
    // Épico: 47,6 → 24; Legendario: 143 → 70; Mítico: 1.000 → 500, con los ritmos de Database/report_recipe_pacing.sql): así ninguna rareza es "la" forma de
    // juntar Polvo y el costo de los encantamientos (Enchantments.Cost) se puede razonar en minutos de juego.
    // dustValue: el valor propio del ítem (items.dust_value, los drops de monstruo); si lo tiene, manda sobre la rareza.
    public static int DustPerUnit(string rarity, int? dustValue = null) => dustValue ?? rarity switch
    {
        "Común" => 1,
        "Raro" => 6,
        "Épico" => 24,
        "Legendario" => 70,
        "Mítico" => 500,
        _ => 0,
    };

    public static int DustFor(string rarity, int quantity, int? dustValue = null) => DustPerUnit(rarity, dustValue) * quantity;
}
