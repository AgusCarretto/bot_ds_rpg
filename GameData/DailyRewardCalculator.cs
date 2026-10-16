namespace BotDsRpg.GameData;

public enum DailyClaimStatus
{
    TooSoon,        // < 24h desde el último claim: rechazado, no se aplica nada
    FirstClaim,     // nunca reclamó antes
    StreakContinued, // entre 24h y 48h: la racha sigue (+1)
    StreakReset,    // > 48h: se perdió la racha, vuelve a Día 1
}

// Qué caja da el día de racha: Iron = la caja "diaria" de la zona del jugador (rol daily de zone_boxes: Cajón de Pino, Baúl de Roble o Arcón de Hierro según la zona) y Gold = la caja "semanal"
// (rol weekly: Baúl de Roble, Arcón de Hierro o Cofre de Oro). Cuál es cuál para cada zona vive en la tabla zone_boxes (GameData/ZoneBoxes.cs), no acá.
public enum DailyBox
{
    None,
    Iron,
    Gold,
}

// Qué decide la cuenta pura: si se puede reclamar, qué día de racha toca y cuánto falta si no. LA RECOMPENSA se arma aparte (DailyRewardCalculator.SpecFor): depende de la zona y el nivel del jugador.
public sealed record DailyClaimCalculation(
    DailyClaimStatus Status,
    int NewStreak,
    TimeSpan? RemainingCooldown); // solo si Status == TooSoon

// Lógica pura de /daily (sin acceso a datos): dado el último claim y la racha actual, decide si corresponde rechazar, continuar la racha o reiniciarla, y qué premio toca ese día.
//
// v0.18.2: el premio escala con la ZONA y el NIVEL del jugador (como las misiones y los logros, GameData/MissionRewards.cs). Antes eran 100 de oro y 50 de EXP por día de racha (tope en el día 10: 1.000 y 500),
// sin mirar la zona: en la zona 5 una cacería sola paga 375, así que el tope eran ~2 cacerías de oro (1-2 minutos de juego) y 500 de EXP era ~1 % de un nivel a nivel 60 (el dueño: «el daily es muy poco,
// hay que mejorar mucho»). Ahora:
//   · ORO en "cacerías de oro" de la zona (MissionRewards.GoldUnit) y EXP como % de lo que pide el nivel actual: del día 1 al 7 sube, y del día 8 en adelante queda en el valor del día 7.
//   · HITOS de la racha: el día 3, monedas extra; el día 6, la caja diaria de la zona (Iron); el día 7 y cada 7 días en racha (14, 21, 28...), la caja semanal de la zona (Gold) más monedas extra;
//     y cada día de racha después del 7 que no cae en múltiplo de 7, la caja diaria de la zona (Iron). Lo pidió el dueño así: «el de oro cada 7 días si continúan en racha y el de hierro si siguen en racha después del día 7».
//   · Del día 1 al 5 no hay caja. Perder la racha (más de 48 h sin reclamar) vuelve al día 1 y a empezar de nuevo.
// Medido con Database/report_economy.sql: en régimen el daily suma una caja semanal más seis diarias por semana, o sea ~2× lo gratis que ya hay por semana en cada zona (misiones, logros, jefe): es lo que pidió el
// dueño, y las palancas si resulta mucho son IronBoxFromDay (o quitar la caja diaria de BoxFor) y las unidades de abajo.
public static class DailyRewardCalculator
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan StreakGraceWindow = TimeSpan.FromHours(48);

    // Oro (en cacerías de oro de la zona) y EXP (% del nivel actual) de cada día de racha del 1 al 7; del 8 en adelante rige el del día 7.
    private static readonly int[] GoldUnitsByDay = [6, 8, 10, 12, 14, 16, 20];
    private static readonly int[] XpPercentByDay = [2, 2, 3, 3, 4, 4, 6];

    public const int ThreeDayStreak = 3;
    public const int ThreeDayBonusUnits = 15;   // monedas extra del día 3
    public const int WeekLength = 7;
    public const int WeekBonusUnits = 30;       // monedas extra del día 7 y de cada 7 días en racha
    public const int IronBoxFromDay = 6;        // desde el día 6 (y todos los días después del 7) hay caja diaria; el día 7 y cada 7 días, la semanal

    // El día 7 y cada 7 (14, 21...): la caja semanal. Del día 6 en adelante (menos esos): la diaria. Antes del día 6: ninguna.
    public static DailyBox BoxFor(int streakDay) =>
        streakDay >= WeekLength && streakDay % WeekLength == 0 ? DailyBox.Gold : streakDay >= IronBoxFromDay ? DailyBox.Iron : DailyBox.None;

    public static bool IsThreeDayBonus(int streakDay) => streakDay == ThreeDayStreak;

    public static bool IsWeekMilestone(int streakDay) => streakDay >= WeekLength && streakDay % WeekLength == 0;

    // El oro del día SIN los extras de los hitos (lo que sube del día 1 al 7).
    public static int BaseGoldUnitsFor(int streakDay) => GoldUnitsByDay[Math.Min(Math.Max(1, streakDay), GoldUnitsByDay.Length) - 1];

    public static int GoldUnitsFor(int streakDay)
    {
        int day = Math.Max(1, streakDay);
        return BaseGoldUnitsFor(day) + (IsThreeDayBonus(day) ? ThreeDayBonusUnits : 0) + (IsWeekMilestone(day) ? WeekBonusUnits : 0);
    }

    public static int XpPercentFor(int streakDay) => XpPercentByDay[Math.Min(Math.Max(1, streakDay), XpPercentByDay.Length) - 1];

    // El premio de ese día de racha, en unidades que se resuelven con la zona y el nivel (MissionRewards.Resolve / RewardPayer).
    public static RewardSpec SpecFor(int streakDay) => new(
        GoldUnitsFor(streakDay),
        XpPercentFor(streakDay),
        BoxFor(streakDay) switch { DailyBox.Gold => BoxGrant.Weekly, DailyBox.Iron => BoxGrant.Daily, _ => BoxGrant.None });

    // El logro "Racha" cuenta la racha MÁS LARGA: el contador solo sube. Dada la racha de hoy y lo que ya lleva el contador, cuánto hay que sumarle (0 si no superó su récord).
    public static long StreakBestDelta(long counterNow, int newStreak) => Math.Max(0, newStreak - Math.Max(0, counterNow));

    // Una línea para el mensaje: qué trae el día siguiente de la racha, si trae algo especial (null si es un día común).
    public static string? NextDayHint(int currentStreak)
    {
        int next = currentStreak + 1;
        if (IsWeekMilestone(next))
        {
            return $"Mañana es el día {next}: ¡caja grande y monedas extra!";
        }

        if (next == IronBoxFromDay)
        {
            return $"Mañana es el día {next}: ¡desde ahí hay caja todos los días!";
        }

        if (IsThreeDayBonus(next))
        {
            return $"Mañana es el día {next}: ¡monedas extra!";
        }

        return null;
    }

    public static DailyClaimCalculation Evaluate(DateTime? lastClaimUtc, int currentStreak, DateTime nowUtc)
    {
        if (lastClaimUtc is null)
        {
            return new DailyClaimCalculation(DailyClaimStatus.FirstClaim, 1, null);
        }

        var elapsed = nowUtc - lastClaimUtc.Value;

        if (elapsed < MinInterval)
        {
            return new DailyClaimCalculation(DailyClaimStatus.TooSoon, currentStreak, MinInterval - elapsed);
        }

        if (elapsed <= StreakGraceWindow)
        {
            return new DailyClaimCalculation(DailyClaimStatus.StreakContinued, currentStreak + 1, null);
        }

        return new DailyClaimCalculation(DailyClaimStatus.StreakReset, 1, null);
    }
}
