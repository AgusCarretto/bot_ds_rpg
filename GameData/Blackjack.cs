namespace BotDsRpg.GameData;

// Una carta de blackjack. Rank: 1 = As, 2..10, 11 = J, 12 = Q, 13 = K. Suit: 0..3 (♠ ♥ ♦ ♣), solo para dibujarla.
public readonly record struct Card(int Rank, int Suit)
{
    // El valor de la carta con el As valiendo 1 (el As blando lo resuelve Blackjack.Evaluate).
    public int Value => Rank >= 10 ? 10 : Rank;

    public string Label => (Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) }) + "♠♥♦♣"[Math.Clamp(Suit, 0, 3)];
}

public enum BlackjackOutcome { InProgress, PlayerBlackjack, PlayerWin, Push, DealerWin, PlayerBust }

// Una mano. Stake es lo apostado (desde la v0.64.1 no se dobla: la apuesta no cambia en toda la mano).
public sealed record BlackjackState(IReadOnlyList<Card> Player, IReadOnlyList<Card> Dealer, int Stake, BlackjackOutcome Outcome)
{
    public bool Finished => Outcome != BlackjackOutcome.InProgress;
}

// El blackjack del casino (v0.25.0, el dueño: «armate el bj, aa bj 10k»). Puro: las cartas salen de la función draw que se le pasa (en el juego, un mazo de 52 mezclado para cada mano, ver NewDeck; en las pruebas, un guion).
// Reglas, todas acá:
//   · Un mazo de 52 por mano (desde la v0.64.1; antes era infinito y una carta podía repetirse): sin cartas repetidas en la mano, el 10, J, Q y K valen 10 (16 de 52).
//   · Se reparten dos cartas al jugador y dos a la banca (de la banca se ve una). Un blackjack «natural» (As + 10 en las dos primeras) paga 3 a 2; si los dos lo tienen es empate. Si lo tiene SOLO la banca,
//     la mano SIGUE (v0.46.0, el dueño: «que no pierdas al instante cuando la máquina tiene 21, porque si llegás a 21 podrías empatar»): el jugador juega a ciegas como siempre y, al
//     comparar, su 21 empata con el de la banca y cualquier otra cosa pierde. Hasta la v0.45 la banca «miraba» y la mano se perdía antes de jugar.
//   · El jugador pide o se planta. Sin doblar (se sacó en la v0.64.1, el dueño: «vamos a sacar el doblar y chau, nadie lo usa»), sin dividir, sin seguro, sin rendirse. Llegar a 21 pidiendo planta solo.
//   · La banca pide hasta 17 y también con el 17 BLANDO (desde la v0.64.1); con 17 duro o más se planta (DealerHits).
//   · Pierde quien se pasa de 21 (el jugador se pasa primero: la banca ni juega). Ganar paga 1 a 1, empatar devuelve la apuesta.
// Retorno medido (1.000.000 de manos con la estrategia básica de estas reglas, v0250test): ~0,983 (la banca se queda con ~1,7 %); jugando peor, menos. Antes de la v0.64.1 (mazo infinito, banca plantada en 17 blando,
// con doblar) era ~0,991; solo con el mazo de 52 había quedado en ~0,9998 y por eso se sumaron el 17 blando y sacar el doblar. Igual que las tragamonedas, se mantiene por debajo de 1.
public static class Blackjack
{
    public const int Target = 21;
    public const int DealerStandsOn = 17;

    // 3 a 2 en el blackjack natural.
    public const double BlackjackPays = 1.5;

    // (total, es blando): con un As que vale 11 sin pasarse de 21 es blando.
    public static (int Total, bool Soft) Evaluate(IEnumerable<Card> cards)
    {
        var list = cards.ToList();
        int sum = list.Sum(c => c.Value);
        return list.Any(c => c.Rank == 1) && sum + 10 <= Target ? (sum + 10, true) : (sum, false);
    }

    public static bool IsBlackjack(IReadOnlyList<Card> cards) => cards.Count == 2 && Evaluate(cards).Total == Target;

    // La banca pide con menos de 17 y TAMBIÉN con el 17 blando (un 17 con un As que vale 11, como A+6) desde la v0.64.1 (el dueño, 2026-11-18: «mepa bien que pida con 17 blando»): con el mazo de 52 por mano la
    // casa casi no ganaba y esto le devuelve parte de su ventaja. Con 17 duro (10+7) o más, se planta.
    public static bool DealerHits(IEnumerable<Card> dealer)
    {
        var (total, soft) = Evaluate(dealer);
        return total < DealerStandsOn || (total == DealerStandsOn && soft);
    }

    public static BlackjackState Start(int stake, Func<Card> draw)
    {
        var player = new List<Card> { draw(), draw() };
        var dealer = new List<Card> { draw(), draw() };

        // El blackjack de la banca no corta la mano: se resuelve al plantarse (su 21 contra el del jugador).
        bool playerNatural = IsBlackjack(player), dealerNatural = IsBlackjack(dealer);
        var outcome = playerNatural && dealerNatural ? BlackjackOutcome.Push
            : playerNatural ? BlackjackOutcome.PlayerBlackjack
            : BlackjackOutcome.InProgress;

        return new BlackjackState(player, dealer, stake, outcome);
    }

    public static BlackjackState Hit(BlackjackState state, Func<Card> draw)
    {
        if (state.Finished)
        {
            return state;
        }

        var hand = state.Player.Append(draw()).ToList();
        int total = Evaluate(hand).Total;
        var next = state with { Player = hand };
        return total > Target ? next with { Outcome = BlackjackOutcome.PlayerBust }
            : total == Target ? Stand(next, draw)
            : next;
    }

    // Se planta: juega la banca hasta 17 y se compara.
    public static BlackjackState Stand(BlackjackState state, Func<Card> draw)
    {
        if (state.Finished)
        {
            return state;
        }

        var dealer = state.Dealer.ToList();
        while (DealerHits(dealer))
        {
            dealer.Add(draw());
        }

        int player = Evaluate(state.Player).Total, bank = Evaluate(dealer).Total;
        var outcome = bank > Target || player > bank ? BlackjackOutcome.PlayerWin
            : player == bank ? BlackjackOutcome.Push
            : BlackjackOutcome.DealerWin;
        return state with { Dealer = dealer, Outcome = outcome };
    }


    // Lo que se le DEVUELVE al jugador al terminar (ya incluye su apuesta): ganar 2 × apuesta, blackjack apuesta + 1,5 × apuesta (redondeado hacia abajo), empate la apuesta, perder 0.
    public static int Payout(BlackjackState state) => state.Outcome switch
    {
        BlackjackOutcome.PlayerBlackjack => state.Stake + (int)Math.Floor(state.Stake * BlackjackPays),
        BlackjackOutcome.PlayerWin => state.Stake * 2,
        BlackjackOutcome.Push => state.Stake,
        _ => 0,
    };

    // Lo que ganó (positivo) o perdió (negativo) de neto.
    public static int Net(BlackjackState state) => Payout(state) - state.Stake;

    // Una carta al azar de un mazo infinito (lo que usaba el juego hasta la v0.64.0; queda para las pruebas que miden con él).
    public static Card RandomCard(Random rng) => new(rng.Next(1, 14), rng.Next(4));

    // UN MAZO DE 52 POR MANO (2026-11-18, el dueño: «arreglar el bj para que no repita cartas»): las 52 cartas mezcladas (Fisher-Yates) y se reparten de arriba sin volver al mazo, así en una mano nunca sale
    // dos veces la misma carta (el 7♥ una sola vez). Cada mano empieza con un mazo nuevo y completo. Una mano usa como mucho ~20 cartas, así que nunca se acaba; si igual se acabara, se mezcla otro mazo entero.
    public static Func<Card> NewDeck(Random rng)
    {
        var deck = new Stack<Card>(Shuffled(rng));
        return () =>
        {
            if (deck.Count == 0)
            {
                deck = new Stack<Card>(Shuffled(rng));
            }

            return deck.Pop();
        };
    }

    private static Card[] Shuffled(Random rng)
    {
        var cards = new Card[52];
        for (int i = 0; i < 52; i++)
        {
            cards[i] = new Card(i % 13 + 1, i / 13);
        }

        rng.Shuffle(cards);
        return cards;
    }
}
