namespace BotDsRpg.Services;

public sealed class CasinoService : ICasinoService
{
    // 6 símbolos (v0.10.1: antes eran 4 y el par pagaba ×2, lo que regalaba oro: ver SlotsReturnToPlayer; v0.14.1: el 🍷 es el sexto, que el dueño pidió para poder subir el par a ×1,3
    // sin volver a regalar oro). El 🔥 son las brasas del asado.
    private static readonly string[] SlotSymbols = ["🥩", "🧉", "🪵", "🪙", "🔥", "🍷"];

    // Lo que paga cada jugada, multiplicando la apuesta (lo que vuelve a tu billetera, apuesta incluida). Están acá, con nombre, para que la ayuda ("/info play") lea
    // los mismos números que el juego y no se desincronicen si se retocan.
    public const int CoinflipMultiplier = 2;

    // La moneda de canto (v0.46.0, el dueño: «puede llegar a salir canto, que ahí te paga ×5, es como una sorpresa, un regalo… no un tipo de apuesta»): 1 tirada de cada 100 la moneda
    // cae parada y paga ×5 elijas el lado que elijas. No se apuesta al canto. Con esto el coinflip devuelve 1,04 por cada 1 apostado (0,495 × 2 + 0,01 × 5): el dueño lo sabe y lo quiere así
    // («que sea suerte y verdad»). Si hubiera que volverlo a 1 o menos, la palanca es bajar CoinflipMultiplier a 1,9 o la chance del canto.
    public const int CoinflipEdgeMultiplier = 5;
    public const int CoinflipEdgeChancePerMille = 10;
    public const string CoinflipEdge = "Edge";

    // Lo que vuelve de cada 1 apostado en el coinflip, en promedio.
    public static double CoinflipReturnToPlayer =>
        ((1000.0 - CoinflipEdgeChancePerMille) / 2000.0 * CoinflipMultiplier) + (CoinflipEdgeChancePerMille / 1000.0 * CoinflipEdgeMultiplier);
    public const int SlotsThreeMatchMultiplier = 5;
    // El par paga ×1,3 (la apuesta y un 30 % más, redondeado hacia abajo). Era ×1,5 hasta la v0.12.0, el dueño lo bajó a ×1,1 (2026-10-06: «ganamos mucho»: con un amigo juntaron oro
    // de más jugando slots) y el 2026-10-07 pidió ×1,3 con un símbolo más: el sexto símbolo hace que el par y el trío salgan menos seguido y compensa el pago mayor. Con la apuesta
    // mínima de 10 el premio es 13, o sea que ganar el par sigue sumando al menos +1.
    public const double SlotsPairMultiplier = 1.3;

    public static IReadOnlyList<string> SlotsSymbolList => SlotSymbols;

    // Las chances exactas de cada resultado con tres tiradas independientes sobre N símbolos: tres iguales 1/N², tres distintos (N-1)(N-2)/N², y el par es lo que queda.
    public static double SlotsThreeMatchChance => 1.0 / (SlotSymbols.Length * SlotSymbols.Length);

    public static double SlotsAllDifferentChance => (SlotSymbols.Length - 1.0) * (SlotSymbols.Length - 2.0) / (SlotSymbols.Length * SlotSymbols.Length);

    public static double SlotsPairChance => 1.0 - SlotsThreeMatchChance - SlotsAllDifferentChance;

    // Lo que vuelve de cada 1 apostado, en promedio (sin redondeos): con 6 símbolos y el par en ×1,3 es 0,681 = la casa se queda con el 32 % (con 5 símbolos y el par en ×1,1 era 0,728; con 5 y ×1,5, 0,92). Tiene que ser MENOR que 1: con 4 símbolos y el par en
    // ×2 daba 1,4375 y /play slots all repetido duplicaba el oro. Una prueba lo sortea y comprueba que no vuelva a pasar.
    public static double SlotsReturnToPlayer => (SlotsThreeMatchChance * SlotsThreeMatchMultiplier) + (SlotsPairChance * SlotsPairMultiplier);

    public CasinoResult PlayCoinflip(int bet, string? predictedSide)
    {
        // Una tirada de 0 a 999: las primeras CoinflipEdgeChancePerMille son el canto; el resto se reparte mitad y mitad entre cara y cruz.
        int roll = Random.Shared.Next(1000);
        string landedSide = roll < CoinflipEdgeChancePerMille ? CoinflipEdge : roll % 2 == 0 ? "Heads" : "Tails";
        int payout = CoinflipPayout(bet, landedSide, predictedSide);
        return new CasinoResult(payout > 0, payout, [landedSide]);
    }

    // El premio del coinflip según cómo cayó la moneda: pura, para probarla sin azar. El canto paga ×5 a todo el mundo; si no, gana quien acertó el lado (sin lado elegido, Heads).
    public static int CoinflipPayout(int bet, string landedSide, string? predictedSide)
    {
        if (landedSide == CoinflipEdge)
        {
            return (int)Math.Min((long)bet * CoinflipEdgeMultiplier, int.MaxValue);
        }

        bool won = string.Equals(predictedSide ?? "Heads", landedSide, StringComparison.OrdinalIgnoreCase);
        return won ? (int)Math.Min((long)bet * CoinflipMultiplier, int.MaxValue) : 0;
    }

    public CasinoResult PlaySlots(int bet)
    {
        string[] rolled =
        [
            SlotSymbols[Random.Shared.Next(SlotSymbols.Length)],
            SlotSymbols[Random.Shared.Next(SlotSymbols.Length)],
            SlotSymbols[Random.Shared.Next(SlotSymbols.Length)],
        ];

        // Con 3 tiradas solo hay tres casos posibles: las 3 iguales, exactamente un par, o las 3 distintas.
        int maxMatches = rolled.GroupBy(symbol => symbol).Max(group => group.Count());

        int payout = SlotsPayout(bet, maxMatches);
        return new CasinoResult(payout > 0, payout, rolled);
    }

    // El premio de una jugada según cuántos símbolos iguales salieron (3, 2 o ninguno repetido): pura, para probarla sin azar. El par se calcula en long y se redondea
    // hacia abajo (con la apuesta mínima de 10 el premio ya es de 13: siempre mayor a lo apostado, ver CasinoModule).
    public static int SlotsPayout(int bet, int maxMatches)
    {
        long payout = maxMatches switch
        {
            3 => (long)bet * SlotsThreeMatchMultiplier,
            // En decimal (no en double): 1,1 no es exacto en binario y un redondeo hacia abajo podría quitarle 1 de oro a una apuesta que da un entero justo.
            2 => (long)Math.Floor((decimal)bet * (decimal)SlotsPairMultiplier),
            _ => 0,
        };

        return (int)Math.Min(payout, int.MaxValue);
    }
}
