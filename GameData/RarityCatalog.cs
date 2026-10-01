namespace BotDsRpg.GameData;

// Sorteo ponderado de rareza, reutilizable por cualquier fuente de drops.
// Los pesos están en milésimos (deben sumar 1000) para poder representar el 0.5% de
// Mítico en /chop y /mine sin usar decimales en el roll.
public static class RarityCatalog
{
    // LAS PROBABILIDADES DE /chop Y /mine (en milésimos, tienen que sumar 1000). Es la única tabla: para cambiarlas se toca solo acá.
    // Primero sale la rareza con estos pesos y DESPUÉS un ítem al azar entre los de esa rareza y ese tipo (Madera o Mineral), así que la
    // chance de cada ítem es su rareza dividida por cuántos ítems comparten esa rareza (ej. Raro de /mine = 21%, repartido entre Carbón
    // y Hierro = 10,5% cada uno). Las recetas están calibradas contra estos números: si se tocan, volver a correr
    // Database/report_recipe_pacing.sql (acepta -v wc= wr= we= wl= wm= para probar un escenario antes de aplicarlo) y mirar cuánto cambia.
    // Historial: hasta la v0.6.0 era 600 / 250 / 100 / 45 / 5 (Común 60%); subió el Común para que lo básico salga más seguido y lo valioso
    // cueste más (las recetas que dependen de Hierro, Nogal, Oro Puro, Ébano o Zafiro tardan ~15% más en promedio).
    private static readonly (string Rarity, int Weight)[] GatheringWeights =
    [
        ("Común", 680),
        ("Raro", 210),
        ("Épico", 70),
        ("Legendario", 35),
        ("Mítico", 5),
    ];

    private static readonly string[] Order = ["Común", "Raro", "Épico", "Legendario", "Mítico"];

    // Orden de menor a mayor rareza, para listados (ej. /inventory). -1 si la rareza no se reconoce.
    public static int RankOf(string rarity) => Array.IndexOf(Order, rarity);

    // Usado por /chop y /mine: Común 68% / Raro 21% / Épico 7% / Legendario 3.5% / Mítico 0.5%.
    public static string RollGatheringRarity() => Roll(GatheringWeights);

    // Las chances de rareza de /chop y /mine en porcentaje (para listarlas y para probar que suman 100).
    public static IReadOnlyList<(string Rarity, double Percent)> GatheringChances =>
        GatheringWeights.Select(w => (w.Rarity, w.Weight / 10.0)).ToList();

    private static string Roll((string Rarity, int Weight)[] weights)
    {
        int totalWeight = weights.Sum(w => w.Weight);
        int roll = Random.Shared.Next(totalWeight);

        int cumulative = 0;
        foreach (var (rarity, weight) in weights)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                return rarity;
            }
        }

        return weights[^1].Rarity; // defensivo: no debería alcanzarse si los pesos están bien sumados
    }
}
