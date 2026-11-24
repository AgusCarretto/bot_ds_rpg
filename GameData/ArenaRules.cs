namespace BotDsRpg.GameData;

// Las reglas de la Arena, en un solo lugar. El torneo de cada DÍA DE URUGUAY junta anotados durante todo el día y se juega a la
// medianoche (00:00 hora de Uruguay, ver UruguayCalendar); en ese momento arranca el del día siguiente.
public static class ArenaRules
{
    // Menos de 2 anotados no es un torneo: se cancela (sin premio, para que nadie cobre el premio diario anotándose solo).
    public const int MinPlayers = 2;

    // Tope de anotados por torneo: con 32 la llave tiene 5 rondas y 31 peleas, y el resultado entra en un mensaje.
    public const int MaxPlayers = 32;

    // El premio del campeón, en las mismas unidades que las misiones y los logros (ver GameData/MissionRewards.cs): 15 cacerías de oro de SU
    // zona, un 6% de lo que le falta para el próximo nivel y una caja de su zona. Es un premio DIARIO y de uno solo, a la altura de un tramo
    // dos de logro: una gracia, no una forma de hacer plata (por eso va por unidades de zona y no un monto fijo).
    public static readonly RewardSpec ChampionReward = new(GoldUnits: 15, XpPercent: 6, Box: BoxGrant.ZoneTier);

    // El podio completo (v0.65.0, pedido de los jugadores: «premios para el puesto 2 y 3»): el 2.º es quien perdió la final y los 3.º, los dos que perdieron las semifinales (la llave no juega un partido por el 3.er puesto,
    // así que empatan). Cobran aproximadamente la mitad (2.º) y la cuarta parte (3.º) del oro y de la EXP del campeón, SIN caja (la caja es del campeón). PROPUESTA sin medir: los números de balance
    // los decide el dueño y son estos dos renglones.
    public static readonly RewardSpec RunnerUpReward = new(GoldUnits: 8, XpPercent: 3);
    public static readonly RewardSpec ThirdPlaceReward = new(GoldUnits: 4, XpPercent: 2);

    // El premio de cada puesto del podio (1 = campeón); null para quien no está en el podio.
    public static RewardSpec? RewardFor(int place) => place switch
    {
        1 => ChampionReward,
        2 => RunnerUpReward,
        3 => ThirdPlaceReward,
        _ => null,
    };

    // El día (de Uruguay) al que pertenece un instante: es la fecha local, que es lo que identifica al torneo.
    public static DateOnly DayOf(DateTime utcNow) => DateOnly.FromDateTime(UruguayCalendar.ToLocal(utcNow));

    // Cuándo se juega el torneo de ese día: la medianoche que lo cierra.
    public static DateTime PlaysAtUtc(DateOnly day)
    {
        var localMidnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return DateTime.SpecifyKind(localMidnight - UruguayCalendar.Offset, DateTimeKind.Utc);
    }

    public static string Format(DateOnly day) => day.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);
}
