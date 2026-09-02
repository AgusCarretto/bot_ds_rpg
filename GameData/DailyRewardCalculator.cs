namespace BotDsRpg.GameData;

public enum DailyClaimStatus
{
    TooSoon,        // < 24h desde el último claim: rechazado, no se aplica nada
    FirstClaim,     // nunca reclamó antes
    StreakContinued, // entre 24h y 48h: la racha sigue (+1)
    StreakReset,    // > 48h: se perdió la racha, vuelve a Día 1
}

public sealed record DailyClaimCalculation(
    DailyClaimStatus Status,
    int NewStreak,
    int GoldReward,
    int XpReward,
    TimeSpan? RemainingCooldown); // solo si Status == TooSoon

// Lógica pura de /daily (sin acceso a datos): dado el último claim y la racha actual,
// decide si corresponde rechazar, continuar la racha o reiniciarla, y calcula la recompensa.
public static class DailyRewardCalculator
{
    private const int MaxStreakMultiplier = 10;
    private const int GoldPerMultiplier = 100;
    private const int XpPerMultiplier = 50;

    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StreakGraceWindow = TimeSpan.FromHours(48);

    public static DailyClaimCalculation Evaluate(DateTime? lastClaimUtc, int currentStreak, DateTime nowUtc)
    {
        if (lastClaimUtc is null)
        {
            return BuildReward(DailyClaimStatus.FirstClaim, newStreak: 1);
        }

        var elapsed = nowUtc - lastClaimUtc.Value;

        if (elapsed < MinInterval)
        {
            return new DailyClaimCalculation(DailyClaimStatus.TooSoon, currentStreak, 0, 0, MinInterval - elapsed);
        }

        if (elapsed <= StreakGraceWindow)
        {
            return BuildReward(DailyClaimStatus.StreakContinued, currentStreak + 1);
        }

        return BuildReward(DailyClaimStatus.StreakReset, newStreak: 1);
    }

    private static DailyClaimCalculation BuildReward(DailyClaimStatus status, int newStreak)
    {
        int multiplier = Math.Min(MaxStreakMultiplier, newStreak);
        int gold = GoldPerMultiplier * multiplier;
        int xp = XpPerMultiplier * multiplier;
        return new DailyClaimCalculation(status, newStreak, gold, xp, RemainingCooldown: null);
    }
}
