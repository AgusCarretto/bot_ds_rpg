namespace BotDsRpg.Services;

public sealed class CasinoService : ICasinoService
{
    private static readonly string[] SlotSymbols = ["🥩", "🧉", "🪵", "🪙"];

    // Lo que paga cada jugada, multiplicando la apuesta (lo que vuelve a tu billetera, apuesta incluida). Están acá, con nombre, para que la ayuda ("/info play") lea
    // los mismos números que el juego y no se desincronicen si se retocan.
    public const int CoinflipMultiplier = 2;
    public const int SlotsThreeMatchMultiplier = 5;
    public const int SlotsPairMultiplier = 2;

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

        int payout = maxMatches switch
        {
            3 => bet * SlotsThreeMatchMultiplier,
            2 => bet * SlotsPairMultiplier,
            _ => 0,
        };

        return new CasinoResult(payout > 0, payout, rolled);
    }
}
