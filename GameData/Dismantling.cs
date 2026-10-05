namespace BotDsRpg.GameData;

// Desmantelar (/dismantle): se rompe un MATERIAL (madera, mineral, drop de monstruo o trofeo) y da Polvo, que solo sirve para encantar (GameData/Enchantments.cs).
// El camino es de UNA sola mano: el Polvo nunca vuelve a ser material ni drop (las recetas están calibradas con 3 drops por zona y no se puede fabricar
// uno), y nada convierte Carbón en Hierro. Craftear quedó afuera (el dueño: "lo de craftear lo olvidamos").
public static class Dismantling
{
    // De a cuántas unidades se desmantela por comando (1 a 5).
    public const int MaxPerCommand = 5;

    // Qué tipos de ítem se pueden desmantelar: lo que se recolecta y lo que sueltan los monstruos. Las cajas se abren, la comida se come y el equipo se forja o se vende.
    public static bool CanDismantle(string itemType) => itemType is "Madera" or "Mineral" or "Material";

    // Polvo por unidad según la rareza. Está puesto para que rinda ~0,5 Polvo por minuto de farmeo en todas las rarezas (Común: 2,45 min → 1; Raro: 11,9 → 6;
    // Épico: 47,6 → 24; Legendario: 143 → 70; Mítico: 1.000 → 500, con los ritmos de Database/report_recipe_pacing.sql): así ninguna rareza es "la" forma de
    // juntar Polvo y el costo de los encantamientos (Enchantments.Cost) se puede razonar en minutos de juego.
    public static int DustPerUnit(string rarity) => rarity switch
    {
        "Común" => 1,
        "Raro" => 6,
        "Épico" => 24,
        "Legendario" => 70,
        "Mítico" => 500,
        _ => 0,
    };

    public static int DustFor(string rarity, int quantity) => DustPerUnit(rarity) * quantity;
}
