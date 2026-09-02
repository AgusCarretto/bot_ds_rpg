using BotDsRpg.GameData;

namespace BotDsRpg.Models;

// Result: null cuando Calculation.Status == TooSoon (no se aplicó ningún cambio).
public sealed record DailyClaimOutcome(DailyClaimCalculation Calculation, LevelUpOutcome? Result);
