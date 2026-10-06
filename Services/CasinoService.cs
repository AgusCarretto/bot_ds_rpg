namespace BotDsRpg.Services;

public sealed class CasinoService : ICasinoService
{
    // 5 símbolos (v0.10.1: antes eran 4 y el par pagaba ×2, lo que regalaba oro: ver SlotsReturnToPlayer). El 🔥 son las brasas del asado.
    private static readonly string[] SlotSymbols = ["🥩", "🧉", "🪵", "🪙", "🔥"];

    // Lo que paga cada jugada, multiplicando la apuesta (lo que vuelve a tu billetera, apuesta incluida). Están acá, con nombre, para que la ayuda ("/info play") lea
    // los mismos números que el juego y no se desincronicen si se retocan.
    public const int CoinflipMultiplier = 2;
    public const int SlotsThreeMatchMultiplier = 5;
    // El par paga ×1,5 (la apuesta y la mitad más, redondeado hacia abajo): ganar siempre da algo, pero ya no regala oro (ver SlotsReturnToPlayer).
    public const double SlotsPairMultiplier = 1.5;

    public static IReadOnlyList<string> SlotsSymbolList => SlotSymbols;

    // Las chances exactas de cada resultado con tres tiradas independientes sobre N símbolos: tres iguales 1/N², tres distintos (N-1)(N-2)/N², y el par es lo que queda.
    public static double SlotsThreeMatchChance => 1.0 / (SlotSymbols.Length * SlotSymbols.Length);

    public static double SlotsAllDifferentChance => (SlotSymbols.Length - 1.0) * (SlotSymbols.Length - 2.0) / (SlotSymbols.Length * SlotSymbols.Length);

    public static double SlotsPairChance => 1.0 - SlotsThreeMatchChance - SlotsAllDifferentChance;

    // Lo que vuelve de cada 1 apostado, en promedio (sin redondeos): con 5 símbolos es 0,92 = la casa se queda con el 8 %. Tiene que ser MENOR que 1: con 4 símbolos y el par en
    // ×2 daba 1,4375 y /play slots all repetido duplicaba el oro. Una prueba lo sortea y comprueba que no vuelva a pasar.
    public static double SlotsReturnToPlayer => (SlotsThreeMatchChance * SlotsThreeMatchMultiplier) + (SlotsPairChance * SlotsPairMultiplier);

    public CasinoResult PlayCoinflip(int bet, string? predictedSide)
    {
        bool landedHeads = Random.Shared.Next(2) == 0; // 50% de probabilidad
        string landedSide = landedHeads ? "Heads" : "Tails";

        bool won = predictedSide is null
            ? landedHeads
            : string.Equals(predictedSide, landedSide, StringComparison.OrdinalIgnoreCase);

        int payout = won ? bet * CoinflipMultiplier : 0;
        return new CasinoResult(won, payout, [landedSide]);
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
    // hacia abajo (con la apuesta mínima de 10 el premio ya es de 15: siempre mayor a lo apostado, ver CasinoModule).
    public static int SlotsPayout(int bet, int maxMatches)
    {
        long payout = maxMatches switch
        {
            3 => (long)bet * SlotsThreeMatchMultiplier,
            2 => (long)Math.Floor(bet * SlotsPairMultiplier),
            _ => 0,
        };

        return (int)Math.Min(payout, int.MaxValue);
    }
}
