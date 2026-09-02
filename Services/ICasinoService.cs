namespace BotDsRpg.Services;

// Reveal: 1 elemento para Coinflip ("Heads"/"Tails"), 3 elementos para Slots.
public sealed record CasinoResult(bool Won, int Payout, string[] Reveal);

public interface ICasinoService
{
    // predictedSide: "heads" o "tails" (sin distinguir mayúsculas). Si es null, tira la moneda
    // sin predicción (50/50 ciego) — CasinoModule exige elegir lado antes de llegar acá.
    CasinoResult PlayCoinflip(int bet, string? predictedSide);
    CasinoResult PlaySlots(int bet);
}
