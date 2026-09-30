namespace BotDsRpg.GameData;

// Cuántas unidades da un /chop o /mine al conseguir un material, según su rareza (qué ítem sale lo sortea
// RarityCatalog.RollGatheringRarity): lo común viene a montones y lo mejor de a uno.
//
//   Común (Madera de Pino, Piedra)                        1 a 5   (promedio 3)
//   Raro y Épico (Roble, Carbón, Hierro, Nogal, Oro Puro) 1 a 3   (promedio 2)
//   Legendario y Mítico (Ébano, Zafiro, Meteorito...)     1 solo
//
// Las cantidades de las recetas (Database/seed_recipes.sql y seed_zoneN_gear_and_recipes.sql) están calibradas
// contra estos promedios: cada material de recolección pide ~3x (Común) o ~2x (Raro/Épico) lo que pedía cuando
// daba 1 por vez, así que el ritmo de la run 1 no cambió. Si se tocan estos rangos hay que recalcularlas.
public static class GatheringYield
{
    // Multiplicador de RUN sobre la cantidad sorteada: 1 en la run 1. El reset que se desbloquea al terminar la
    // Zona 5 lo va a ir subiendo (más material por acción, junto con más % de drop); queda como parámetro de Roll
    // para que se enchufe sin reescribir nada.
    public const int RunMultiplier = 1;

    public static (int Min, int Max) RangeFor(string rarity) => rarity switch
    {
        "Común" => (1, 5),
        "Raro" or "Épico" => (1, 3),
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
