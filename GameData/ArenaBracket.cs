namespace BotDsRpg.GameData;

public sealed record ArenaEntrant(ulong DiscordId, string Name);

// P2 null = pasó directo (no tuvo rival en la primera ronda: pasa sin pelear). Actions: cuántas acciones duró la pelea (0 si pasó directo).
public sealed record ArenaMatch(int Round, int Slot, ArenaEntrant P1, ArenaEntrant? P2, ulong WinnerId, int Actions, int WinnerHpPercent = 100);

// WinnerHpPercent: con qué porcentaje de su vida terminó el ganador (100 = ni lo tocaron). Sirve para narrar si fue paliza o por un pelo.
public sealed record ArenaFightOutcome(ulong WinnerId, int Actions, int WinnerHpPercent = 100);

public sealed record ArenaBracketResult(IReadOnlyList<ArenaMatch> Matches, int Rounds, ArenaEntrant Champion);

// La llave del torneo: eliminación directa. PURA — quien arma la llave le pasa la función que resuelve cada pelea (la arena real usa
// GameData/DuelEngine.Simulate; las pruebas, una función falsa), así el cruce se prueba sin combatir.
//
//  - Se sortea el orden (nadie tiene un cruce "armado" a favor).
//  - Con un número de anotados que no es potencia de 2, los que sobran para completar la llave pasan directo la primera ronda (los
//    "byes"): son los primeros del sorteo. Con 5 anotados: 3 byes, 1 pelea en la primera ronda, 4 en la segunda; nunca hay un pasa-directo
//    contra otro pasa-directo (los byes nunca superan la mitad de la llave).
//  - Desde la segunda ronda se cruzan los ganadores de a dos, en el orden en que salieron.
public static class ArenaBracket
{
    public static int RoundsFor(int players)
    {
        int rounds = 0;
        for (int size = 1; size < players; size <<= 1)
        {
            rounds++;
        }

        return rounds;
    }

    public static ArenaBracketResult Run(
        IReadOnlyList<ArenaEntrant> entrants, Random rng, Func<ArenaEntrant, ArenaEntrant, ArenaFightOutcome> fight)
    {
        if (entrants.Count < 2)
        {
            throw new ArgumentException("Un torneo necesita al menos 2 jugadores.", nameof(entrants));
        }

        var order = entrants.ToList();
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        int rounds = RoundsFor(order.Count);
        int size = 1 << rounds;
        int byes = size - order.Count;

        var matches = new List<ArenaMatch>();
        var advancing = new List<ArenaEntrant>();

        // Primera ronda: los primeros "byes" lugares pasan directo, el resto se cruza de a dos.
        int next = 0;
        for (int slot = 0; slot < size / 2; slot++)
        {
            var first = order[next++];
            if (slot < byes)
            {
                matches.Add(new ArenaMatch(1, slot, first, null, first.DiscordId, 0));
                advancing.Add(first);
                continue;
            }

            var second = order[next++];
            advancing.Add(Play(matches, 1, slot, first, second, fight));
        }

        for (int round = 2; round <= rounds; round++)
        {
            var winners = new List<ArenaEntrant>();
            for (int slot = 0; slot < advancing.Count / 2; slot++)
            {
                winners.Add(Play(matches, round, slot, advancing[slot * 2], advancing[slot * 2 + 1], fight));
            }

            advancing = winners;
        }

        return new ArenaBracketResult(matches, rounds, advancing[0]);
    }

    private static ArenaEntrant Play(
        List<ArenaMatch> matches, int round, int slot, ArenaEntrant p1, ArenaEntrant p2,
        Func<ArenaEntrant, ArenaEntrant, ArenaFightOutcome> fight)
    {
        var outcome = fight(p1, p2);
        var winner = outcome.WinnerId == p1.DiscordId ? p1 : outcome.WinnerId == p2.DiscordId
            ? p2
            : throw new InvalidOperationException("La pelea devolvió un ganador que no peleó.");

        matches.Add(new ArenaMatch(round, slot, p1, p2, winner.DiscordId, outcome.Actions, Math.Clamp(outcome.WinnerHpPercent, 0, 100)));
        return winner;
    }

    // "Final", "Semifinal", "Cuartos de final", "Octavos de final"; más atrás, el número de ronda.
    public static string RoundName(int round, int totalRounds) => (totalRounds - round) switch
    {
        0 => "🏆 Final",
        1 => "Semifinal",
        2 => "Cuartos de final",
        3 => "Octavos de final",
        _ => $"Ronda {round}",
    };
}
