namespace BotDsRpg.GameData;

// Navidad en el Fogón (v0.57.0, diseño en docs/navidad.md). Las reglas, puras: fechas, cuántos Regalos da cada cosa y el calendario de adviento.
// Los números de Regalos los decidió el dueño (2026-11-13); los del calendario que no son Regalos (oro, cajas, Polvo) son propuesta, «a medir».

// Las tres etapas: Off (antes y después), Farm (del 1 al 24 de diciembre: caen Regalos, calendario, jefes) y Shop (del 25 al 6 de enero: solo tienda).
public enum ChristmasPhase
{
    Off,
    Farm,
    Shop,
}

// Las fechas del evento, en UTC. Season es la clave con la que se guardan los Regalos: al cambiar de año es otra temporada, así que los que sobraron
// "desaparecen" solos (quedan en la base de la temporada vieja, que ya no se lee). Se cargan de la configuración (Christmas__Start, __FarmEnd, __End,
// fechas yyyy-MM-dd de Uruguay) para poder probarlo antes de diciembre.
public sealed record ChristmasSettings(DateTime StartUtc, DateTime FarmEndUtc, DateTime EndUtc, string Season)
{
    // Del 1 de diciembre de 2026 al 6 de enero de 2027 inclusive (cierra el 7 a la medianoche de Uruguay); el farmeo, hasta el 24 inclusive.
    public static ChristmasSettings Default { get; } = FromLocalDates(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 7));

    // Las fechas son días de Uruguay: cada etapa arranca a la medianoche de ese día (03:00 UTC). farmEnd y end son el PRIMER día que ya no es de esa etapa.
    public static ChristmasSettings FromLocalDates(DateOnly start, DateOnly farmEnd, DateOnly end) =>
        new(ToUtc(start), ToUtc(farmEnd), ToUtc(end), $"navidad{start.Year}");

    private static DateTime ToUtc(DateOnly day) => DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - UruguayCalendar.Offset;
}

// Lo que da un día del calendario de adviento: Regalos siempre, y algunos días además oro (unidades del /daily, escaladas a la zona), una caja de la zona y Polvo.
// Bells: Campanas de la tienda (días 11 y 18); Sock: un Calcetín abierto en el momento (el 24).
public sealed record AdventDay(int Day, int Gifts, RewardSpec? Reward = null, int Dust = 0, int Bells = 0, bool Sock = false);

public static class ChristmasRules
{
    // --- Regalos por victoria (el dueño, 2026-11-13) ---
    public const int HuntChancePercent = 50;   // /hunt y /autohunt: 1 Regalo con 50 % (el dueño: 40 → 50, 2026-11-13)
    public const int TravelGifts = 5;
    public const int BossGifts = 50;           // jefe de zona, solo o en equipo (cooldown de 10 h)
    public const int KrampusMinGifts = 30;
    public const int KrampusMaxGifts = 40;
    public const int DailyGifts = 10;
    public const int DailyStreakBonusGifts = 5; // con racha de 7 días o más
    public const int DailyStreakBonusFrom = 7;

    // --- Calendario de adviento ---
    public const int AdventDays = 24;
    public const int AdventBaseGifts = 20;
    public const int AdventDoubleGifts = 40;
    public const int AdventEveGifts = 150;
    public const int AdventDust = 100;          // «a medir»: ~3 h de farmeo de Polvo
    public const int AdventBells = 5;

    public static ChristmasPhase PhaseAt(ChristmasSettings settings, DateTime utcNow) =>
        utcNow < settings.StartUtc ? ChristmasPhase.Off
        : utcNow < settings.FarmEndUtc ? ChristmasPhase.Farm
        : utcNow < settings.EndUtc ? ChristmasPhase.Shop
        : ChristmasPhase.Off;

    // El día del calendario (1 a 24) que corresponde a ese instante, o 0 si no hay calendario (fuera del farmeo o pasado el día 24).
    public static int AdventDayAt(ChristmasSettings settings, DateTime utcNow)
    {
        if (PhaseAt(settings, utcNow) != ChristmasPhase.Farm)
        {
            return 0;
        }

        int day = (int)Math.Floor((utcNow - settings.StartUtc).TotalDays) + 1;
        return day is >= 1 and <= AdventDays ? day : 0;
    }

    // Cuántos Regalos da una victoria de ese tipo (GameEventKinds). roll: un número 0–99 (el azar lo pone quien llama, así se prueba).
    public static int VictoryGifts(string kind, int roll) => kind switch
    {
        GameEventKinds.HuntWin => roll < HuntChancePercent ? 1 : 0,
        GameEventKinds.TravelWin => TravelGifts,
        GameEventKinds.BossWin or GameEventKinds.RaidWin => BossGifts,
        _ => 0,
    };

    public static int DailyClaimGifts(int streakDay) => DailyGifts + (streakDay >= DailyStreakBonusFrom ? DailyStreakBonusGifts : 0);

    // El calendario día por día (docs/navidad.md): 20 Regalos todos los días; 3/10/17 oro; 5/12/19 doble; 7/14 caja de la zona; 9/16/23 Polvo;
    // 11/18 cinco Campanas; 21 caja grande; 24 Nochebuena (150 Regalos, caja grande y un Calcetín).
    public static AdventDay Advent(int day)
    {
        int d = Math.Clamp(day, 1, AdventDays);
        return d switch
        {
            24 => new AdventDay(d, AdventEveGifts, new RewardSpec(0, 0, BoxGrant.Weekly), Sock: true),
            11 or 18 => new AdventDay(d, AdventBaseGifts, Bells: AdventBells),
            21 => new AdventDay(d, AdventBaseGifts, new RewardSpec(0, 0, BoxGrant.Weekly)),
            7 or 14 => new AdventDay(d, AdventBaseGifts, new RewardSpec(0, 0, BoxGrant.Daily)),
            5 or 12 or 19 => new AdventDay(d, AdventDoubleGifts),
            3 or 10 or 17 => new AdventDay(d, AdventBaseGifts, new RewardSpec(DailyRewardCalculator.BaseGoldUnitsFor(7), 0)),
            9 or 16 or 23 => new AdventDay(d, AdventBaseGifts, Dust: AdventDust),
            _ => new AdventDay(d, AdventBaseGifts),
        };
    }

    // Lo que suman los 24 días en Regalos (lo usan la ayuda y las pruebas).
    public static int AdventTotalGifts => Enumerable.Range(1, AdventDays).Sum(d => Advent(d).Gifts);
}
