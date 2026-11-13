namespace BotDsRpg.GameData;

// La ruleta (v0.55.0): europea, 37 casillas (0 a 36), solo oro, dentro de /play. El dueño (2026-11-13): giro animado con emojis, una
// mesa con botones y, para arrancar, solo las apuestas simples: color (rojo o negro) y par o impar pagan ×2 (18 de 37), un número
// paga ×36 (1 de 37). El 0 es verde y hace perder color y par/impar. Devolución 36/37 = 0,973 para cualquier apuesta (la casa se
// queda el 2,7 %), por debajo de 1 como todos los juegos salvo la moneda de canto. Puro: sin Discord ni base; el azar viene de afuera.
public enum RouletteBetKind { Red, Black, Even, Odd, Number }

public sealed record RouletteBet(RouletteBetKind Kind, int Number = -1)
{
    // El código que va en el id del botón y que se escribe: rojo/negro/par/impar o el número. Nunca se traduce.
    public string Code => Kind switch
    {
        RouletteBetKind.Red => "red",
        RouletteBetKind.Black => "black",
        RouletteBetKind.Even => "even",
        RouletteBetKind.Odd => "odd",
        _ => Number.ToString(),
    };
}

public static class RouletteRules
{
    // El valor de la opción «game» de /play y la palabra de «aa play ruleta»: nunca se traduce.
    public const string GameKey = "ruleta";

    public const int MinBet = 10;
    public const int ColorMultiplier = 2;
    public const int NumberMultiplier = 36;

    // El pago tiene que entrar en un int (ICasinoRepository.PlaceBetAsync): al número no se le apuesta más que esto.
    public const int MaxNumberBet = int.MaxValue / NumberMultiplier;

    public const double ReturnToPlayer = 36.0 / 37.0;

    // El orden de las casillas en una rueda europea (para la animación: los vecinos del número son los de verdad).
    public static readonly IReadOnlyList<int> WheelOrder =
        [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];

    private static readonly HashSet<int> Reds = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    public static bool IsRed(int n) => Reds.Contains(n);

    public static string ColorEmoji(int n) => n == 0 ? "🟩" : IsRed(n) ? "🟥" : "⬛";

    public static string Cell(int n) => $"{ColorEmoji(n)} {n}";

    // Lo que se escribe («rojo», «red», «par», «17»…) → la apuesta; null si no es ninguna.
    public static RouletteBet? Parse(string? text)
    {
        string t = (text ?? string.Empty).Trim().ToLowerInvariant();
        return t switch
        {
            "rojo" or "roja" or "red" or "r" => new RouletteBet(RouletteBetKind.Red),
            "negro" or "negra" or "black" or "n" or "b" => new RouletteBet(RouletteBetKind.Black),
            "par" or "even" => new RouletteBet(RouletteBetKind.Even),
            "impar" or "odd" => new RouletteBet(RouletteBetKind.Odd),
            _ => int.TryParse(t, out int n) && n is >= 0 and <= 36 ? new RouletteBet(RouletteBetKind.Number, n) : null,
        };
    }

    public static bool Wins(RouletteBet bet, int n) => bet.Kind switch
    {
        RouletteBetKind.Red => n != 0 && IsRed(n),
        RouletteBetKind.Black => n != 0 && !IsRed(n),
        RouletteBetKind.Even => n != 0 && n % 2 == 0,
        RouletteBetKind.Odd => n % 2 == 1,
        _ => n == bet.Number,
    };

    public static int Multiplier(RouletteBet bet) => bet.Kind == RouletteBetKind.Number ? NumberMultiplier : ColorMultiplier;

    // Lo que se cobra si sale n (0 si pierde). La apuesta ya está acotada para que entre en un int.
    public static int Payout(RouletteBet bet, int amount, int n) => Wins(bet, n) ? checked(amount * Multiplier(bet)) : 0;

    // El paño (v0.55.1): varias apuestas en un giro. El total apostado tiene que dejar que el premio más grande posible (todo a un
    // número, ×36) entre en un int, que es lo que cobra PlaceBetAsync.
    public const int MaxTotalStake = int.MaxValue / NumberMultiplier;

    public static int TotalPayout(IEnumerable<(RouletteBet Bet, int Amount)> bets, int n) =>
        checked(bets.Sum(b => Payout(b.Bet, b.Amount, n)));

    // Orden para mostrar el paño: rojo, negro, par, impar y después los números de menor a mayor.
    public static int SortKey(RouletteBet bet) => bet.Kind == RouletteBetKind.Number ? 10 + bet.Number : (int)bet.Kind;

    public static string Describe(RouletteBet bet) => bet.Kind switch
    {
        RouletteBetKind.Red => Loc.T("🟥 Rojo"),
        RouletteBetKind.Black => Loc.T("⬛ Negro"),
        RouletteBetKind.Even => Loc.T("Par"),
        RouletteBetKind.Odd => Loc.T("Impar"),
        _ => Loc.F($"el número {Cell(bet.Number)}"),
    };

    public static int Spin(Random rng) => rng.Next(37);

    // Los cuadros del giro: una ventana de 5 casillas de la rueda que avanza cada vez menos hasta dejar el número ganador en el medio.
    // La casilla del medio va resaltada. El último cuadro es el resultado.
    public static IReadOnlyList<string> Frames(int result, Random rng)
    {
        int target = WheelOrder.ToList().IndexOf(result);
        int extra = rng.Next(0, 5);
        int[] backs = [14 + extra, 7 + extra / 2, 3, 1, 0];
        return backs.Select(back => Window(target - back)).ToList();
    }

    private static string Window(int center)
    {
        int count = WheelOrder.Count;
        var cells = Enumerable.Range(-2, 5).Select(offset =>
        {
            int n = WheelOrder[((center + offset) % count + count) % count];
            return offset == 0 ? $"**【{Cell(n)}】**" : Cell(n);
        });
        return string.Join("  ", cells);
    }
}
