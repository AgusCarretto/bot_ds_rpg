namespace BotDsRpg.GameData;

// Una carta de blackjack. Rank: 1 = As, 2..10, 11 = J, 12 = Q, 13 = K. Suit: 0..3 (♠ ♥ ♦ ♣), solo para dibujarla.
public readonly record struct Card(int Rank, int Suit)
{
    // El valor de la carta con el As valiendo 1 (el As blando lo resuelve Blackjack.Evaluate).
    public int Value => Rank >= 10 ? 10 : Rank;

    public string Label => (Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) }) + "♠♥♦♣"[Math.Clamp(Suit, 0, 3)];
}

public enum BlackjackOutcome { InProgress, PlayerBlackjack, PlayerWin, Push, DealerWin, PlayerBust }

// Una mano. Stake es lo apostado EN TOTAL (se duplica al doblar).
public sealed record BlackjackState(IReadOnlyList<Card> Player, IReadOnlyList<Card> Dealer, int Stake, bool Doubled, BlackjackOutcome Outcome)
{
    public bool Finished => Outcome != BlackjackOutcome.InProgress;

    // Doblar: solo con las dos primeras cartas, una vez, y con la mano en juego.
    public bool CanDouble => !Finished && Player.Count == 2 && !Doubled;
}

// El blackjack del casino (v0.25.0, el dueño: «armate el bj, aa bj 10k»). Puro: las cartas salen de la función draw que se le pasa (en el juego, un mazo infinito al azar; en las pruebas, un guion).
// Reglas, todas acá:
//   · Mazo infinito: cada carta es uniforme entre 13 valores (el 10, J, Q y K valen 10, o sea 4 de cada 13).
//   · Se reparten dos cartas al jugador y dos a la banca (de la banca se ve una). Un blackjack «natural» (As + 10 en las dos primeras) paga 3 a 2; si los dos lo tienen es empate; si lo tiene SOLO la banca, se pierde
//     la mano al instante (la banca «mira» antes de que el jugador juegue).
//   · El jugador pide, se planta o dobla (solo con las dos primeras cartas: una sola carta más y la apuesta se duplica). Sin dividir, sin seguro, sin rendirse. Llegar a 21 pidiendo planta solo.
//   · La banca pide hasta 17 y SE PLANTA en todos los 17 (también en el 17 blando).
//   · Pierde quien se pasa de 21 (el jugador se pasa primero: la banca ni juega). Ganar paga 1 a 1, empatar devuelve la apuesta.
// Retorno medido (1.000.000 de manos con la estrategia básica de estas reglas): ~0,99 (la banca se queda con ~1 %); jugando peor, menos. Igual que las tragamonedas, se mantiene por debajo de 1.
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

    public static BlackjackState Start(int stake, Func<Card> draw)
    {
        var player = new List<Card> { draw(), draw() };
        var dealer = new List<Card> { draw(), draw() };

        bool playerNatural = IsBlackjack(player), dealerNatural = IsBlackjack(dealer);
        var outcome = playerNatural && dealerNatural ? BlackjackOutcome.Push
            : playerNatural ? BlackjackOutcome.PlayerBlackjack
            : dealerNatural ? BlackjackOutcome.DealerWin
            : BlackjackOutcome.InProgress;

        return new BlackjackState(player, dealer, stake, false, outcome);
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
        while (Evaluate(dealer).Total < DealerStandsOn)
        {
            dealer.Add(draw());
        }

        int player = Evaluate(state.Player).Total, bank = Evaluate(dealer).Total;
        var outcome = bank > Target || player > bank ? BlackjackOutcome.PlayerWin
            : player == bank ? BlackjackOutcome.Push
            : BlackjackOutcome.DealerWin;
        return state with { Dealer = dealer, Outcome = outcome };
    }

    // Dobla: una sola carta más, la apuesta se duplica y se planta sola. Si no se puede doblar devuelve la mano igual.
    public static BlackjackState Double(BlackjackState state, Func<Card> draw)
    {
        if (!state.CanDouble)
        {
            return state;
        }

        var hand = state.Player.Append(draw()).ToList();
        var doubled = state with { Player = hand, Stake = state.Stake * 2, Doubled = true };
        return Evaluate(hand).Total > Target ? doubled with { Outcome = BlackjackOutcome.PlayerBust } : Stand(doubled, draw);
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

    // Una carta al azar de un mazo infinito.
    public static Card RandomCard(Random rng) => new(rng.Next(1, 14), rng.Next(4));
}
