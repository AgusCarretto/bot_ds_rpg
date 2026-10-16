using BotDsRpg.GameData;

namespace BotDsRpg.Models;

// Resultado de /daily. Result y Receipt: null cuando Calculation.Status == TooSoon (no se aplicó ningún cambio).
// Receipt (v0.18.2): lo que se pagó de verdad (oro, EXP y caja ya resueltos con la zona y el nivel del jugador).
public sealed record DailyClaimOutcome(DailyClaimCalculation Calculation, LevelUpOutcome? Result, RewardReceipt? Receipt = null);
