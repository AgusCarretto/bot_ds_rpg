namespace BotDsRpg.GameData;

// Cuántas unidades da un /chop o /mine al conseguir un material, según su rareza (qué ítem sale lo sortea
// RarityCatalog.RollGatheringRarity): lo común viene a montones y lo mejor de a uno.
//
//   Común (Madera de Pino, Piedra)                        1 a 5   (promedio 3)
//   Raro (Roble, Carbón, Hierro)                          1 a 3   (promedio 2)
//   Épico (Nogal, Oro Puro)                               1 a 2   (promedio 1,5)
//   Legendario y Mítico (Ébano, Zafiro, Meteorito...)     1 solo
//
// El máximo BAJA con cada escalón de rareza: lo básico viene a montones y nunca sale mucho de algo raro (pedido del
// dueño; antes Raro y Épico eran los dos 1 a 3). Las cantidades de las recetas (Database/rework_drops_and_recipes.sql, que
// pisa a seed_recipes.sql y seed_zoneN_gear_and_recipes.sql) están calculadas contra estos promedios con el modelo de
// Database/report_recipe_pacing.sql. Si se tocan estos rangos hay que recalcularlas y actualizar el promedio de ese script.
public static class GatheringYield
{
    // Multiplicador de RUN sobre la cantidad sorteada: 1 en la run 1. El reset que se desbloquea al terminar la
    // Zona 5 lo va a ir subiendo (más material por acción, junto con más % de drop); queda como parámetro de Roll
    // para que se enchufe sin reescribir nada.
    public const int RunMultiplier = 1;

    public static (int Min, int Max) RangeFor(string rarity) => rarity switch
    {
        "Común" => (1, 5),
        "Raro" => (1, 3),
        "Épico" => (1, 2),
        _ => (1, 1), // Legendario, Mítico (y cualquier rareza que no se reconozca: lo seguro es darle 1)
    };

    public static double Average(string rarity)
    {
        var (min, max) = RangeFor(rarity);
        return (min + max) / 2.0;
    }

    public static int Roll(string rarity, int runMultiplier = RunMultiplier)
    {
        var (min, max) = RangeFor(rarity);
        return Random.Shared.Next(min, max + 1) * runMultiplier;
    }
}
