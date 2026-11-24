using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Un puesto del podio sin el premio: 2 = perdió la final, 3 = perdió una semifinal (el 1.º es el campeón y ya lo guarda el día).
public sealed record ArenaPlace(int Place, ulong DiscordId, string Name);

// El podio de una llave (v0.65.0), PURO: sale de las peleas guardadas (las mismas que lee /arena results), así que sirve igual para pagar a medianoche que para mostrar la llave después.
//   · 2.º: el que perdió la final (siempre hay uno: la final es la última ronda y nunca es un pase directo).
//   · 3.º: los que perdieron las semifinales (la ronda anterior a la final): uno o dos. No se juega un partido por el 3.er puesto, así que los dos semifinalistas eliminados empatan.
// Un pase directo (sin rival) no pierde nadie, así que no genera puesto. Con 2 anotados no hay semifinal: solo campeón y 2.º. Con 3, la única pelea de la primera ronda ya es una semifinal.
public static class ArenaPodium
{
    public static IReadOnlyList<ArenaPlace> Of(IEnumerable<ArenaMatchRow> matches, int rounds)
    {
        var places = new List<ArenaPlace>();
        if (rounds < 1)
        {
            return places;
        }

        foreach (var match in matches.Where(m => m.P2Id is not null).OrderBy(m => m.Round).ThenBy(m => m.Slot))
        {
            int place = match.Round == rounds ? 2 : match.Round == rounds - 1 ? 3 : 0;
            if (place == 0)
            {
                continue;
            }

            bool firstWon = match.WinnerId == match.P1Id;
            places.Add(firstWon
                ? new ArenaPlace(place, match.P2Id!.Value, match.P2Name ?? "?")
                : new ArenaPlace(place, match.P1Id, match.P1Name));
        }

        return places.OrderBy(p => p.Place).ToList();
    }
}
