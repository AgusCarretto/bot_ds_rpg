using BotDsRpg.GameData;

namespace BotDsRpg.Models;

public enum ClaimStatus
{
    Claimed,        // se cobró (Receipt viene con valor)
    NotCompleted,   // todavía no llegó a la meta (o, en el premio por completar todas, falta cobrar alguna)
    AlreadyClaimed, // ya lo había cobrado (doble click, o dos botones a la vez): no se paga de nuevo
    Expired,        // el período terminó mientras tanto: esa misión ya no se puede cobrar
}

// Lo que se pagó: el premio ya con números concretos, el oro que quedó y si subió de nivel (XP de misiones y logros).
public sealed record RewardReceipt(ResolvedReward Reward, int GoldAfter, int LevelsGained, int NewLevel);

public sealed record ClaimOutcome(ClaimStatus Status, RewardReceipt? Receipt = null);
