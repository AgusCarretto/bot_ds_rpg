namespace BotDsRpg.GameData;

// Sorteo ponderado de rareza, reutilizable por cualquier fuente de drops.
// Los pesos están en milésimos (deben sumar 1000) para poder representar el 0.5% de
// Mítico en /chop y /mine sin usar decimales en el roll.
public static class RarityCatalog
{
    private static readonly (string Rarity, int Weight)[] TravelWeights =
    [
        ("Común", 550),
        ("Raro", 250),
        ("Épico", 120),
        ("Legendario", 60),
        ("Mítico", 20),
    ];

    private static readonly (string Rarity, int Weight)[] GatheringWeights =
    [
        ("Común", 600),
        ("Raro", 250),
        ("Épico", 100),
        ("Legendario", 45),
        ("Mítico", 5),
    ];

    private static readonly string[] Order = ["Común", "Raro", "Épico", "Legendario", "Mítico"];

    // Orden de menor a mayor rareza, para listados (ej. /inventory). -1 si la rareza no se reconoce.
    public static int RankOf(string rarity) => Array.IndexOf(Order, rarity);

    // Usado por /travel para el drop ocasional de materiales al ganar un combate.
    public static string RollTravelRarity() => Roll(TravelWeights);

    // Usado por /chop y /mine: Común 60% / Raro 25% / Épico 10% / Legendario 4.5% / Mítico 0.5%.
    public static string RollGatheringRarity() => Roll(GatheringWeights);

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
