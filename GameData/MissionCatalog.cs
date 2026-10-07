namespace BotDsRpg.GameData;

public enum MissionPeriod
{
    Daily,
    Weekly,
}

// Una misión: "hacé Target veces Kind". Kind es un GameEventKinds (game_events.kind): el progreso es la suma de lo registrado
// de ese tipo desde que empezó el período, así que no hay nada que "avanzar" aparte — el registro de eventos ES el progreso.
public sealed record MissionTemplate(string Key, MissionPeriod Period, string Kind, long Target, string Title, RewardSpec Reward);

// El catálogo de misiones y la regla de cuáles tocan cada día/semana. Todo puro: ni base ni reloj propio.
//
// QUÉ TOCA HOY: es una función del inicio del período (no del jugador), así que TODOS tienen las mismas misiones el mismo día —
// da de qué hablar ("hoy toca talar") y no hay que guardar qué misiones se le asignaron a cada uno. Cada misión del pool tiene
// que poder cumplirla cualquiera, de cualquier nivel y zona: por eso no hay misiones de jefe (se bloquean por nivel) ni de gastar
// una cantidad fija de oro (no vale lo mismo en Zona 1 que en Zona 5).
//
// PREMIOS: en unidades que escalan con la zona del jugador (ver MissionRewards). Una misión diaria rinde ~4-6 cacerías de oro;
// completar las 3 suma una caja de su zona. Las semanales son ~10 veces más largas y pagan en proporción, con una caja mayor.
public static class MissionCatalog
{
    public const int DailyCount = 3;
    public const int WeeklyCount = 2;

    // La clave con la que se reclama el premio por completar todas las misiones del período.
    public const string BonusKey = "_bonus";

    public static readonly RewardSpec DailyBonus = new(0, 0, BoxGrant.Daily);
    public static readonly RewardSpec WeeklyBonus = new(0, 0, BoxGrant.Weekly);

    public static readonly IReadOnlyList<MissionTemplate> DailyPool =
    [
        new("d_hunt10",   MissionPeriod.Daily, GameEventKinds.HuntWin,       10, "Ganá 10 cacerías (/hunt)",               new(6, 4)),
        new("d_hunt20",   MissionPeriod.Daily, GameEventKinds.HuntWin,       20, "Ganá 20 cacerías (/hunt)",               new(11, 6)),
        new("d_travel1",  MissionPeriod.Daily, GameEventKinds.TravelWin,     1,  "Ganá un viaje contra un élite (/travel)", new(5, 4)),
        new("d_travel2",  MissionPeriod.Daily, GameEventKinds.TravelWin,     2,  "Ganá 2 viajes contra élites (/travel)",   new(9, 6)),
        new("d_chop3",    MissionPeriod.Daily, GameEventKinds.Chop,          3,  "Talá árboles 3 veces (/chop)",            new(4, 3)),
        new("d_mine3",    MissionPeriod.Daily, GameEventKinds.Mine,          3,  "Minerá 3 veces (/mine)",                  new(4, 3)),
        new("d_gather15", MissionPeriod.Daily, GameEventKinds.GatheredUnits, 15, "Juntá 15 materiales talando y minando",   new(5, 3)),
        new("d_daily",    MissionPeriod.Daily, GameEventKinds.DailyClaim,    1,  "Reclamá tu recompensa diaria (/daily)",   new(2, 2)),
    ];

    public static readonly IReadOnlyList<MissionTemplate> WeeklyPool =
    [
        new("w_hunt100",   MissionPeriod.Weekly, GameEventKinds.HuntWin,       100, "Ganá 100 cacerías (/hunt)",                 new(45, 14)),
        new("w_hunt150",   MissionPeriod.Weekly, GameEventKinds.HuntWin,       150, "Ganá 150 cacerías (/hunt)",                 new(65, 18)),
        new("w_travel8",   MissionPeriod.Weekly, GameEventKinds.TravelWin,     8,   "Ganá 8 viajes contra élites (/travel)",     new(45, 14)),
        new("w_travel15",  MissionPeriod.Weekly, GameEventKinds.TravelWin,     15,  "Ganá 15 viajes contra élites (/travel)",    new(75, 22)),
        new("w_gather300", MissionPeriod.Weekly, GameEventKinds.GatheredUnits, 300, "Juntá 300 materiales talando y minando",    new(60, 16)),
        new("w_gather500", MissionPeriod.Weekly, GameEventKinds.GatheredUnits, 500, "Juntá 500 materiales talando y minando",    new(95, 22)),
        new("w_craft1",    MissionPeriod.Weekly, GameEventKinds.Craft,         1,   "Forjá un ítem en la herrería (/forge make)", new(25, 10)),
        new("w_craft3",    MissionPeriod.Weekly, GameEventKinds.Craft,         3,   "Forjá 3 ítems en la herrería (/forge make)", new(60, 18)),
        new("w_boxes3",    MissionPeriod.Weekly, GameEventKinds.BoxOpened,     3,   "Abrí 3 cajas (/open)",                      new(30, 10)),
        new("w_daily5",    MissionPeriod.Weekly, GameEventKinds.DailyClaim,    5,   "Reclamá tu /daily 5 días",                   new(20, 10)),
    ];

    public static IReadOnlyList<MissionTemplate> Pool(MissionPeriod period) =>
        period == MissionPeriod.Daily ? DailyPool : WeeklyPool;

    public static int CountFor(MissionPeriod period) =>
        period == MissionPeriod.Daily ? DailyCount : WeeklyCount;

    public static RewardSpec BonusFor(MissionPeriod period) =>
        period == MissionPeriod.Daily ? DailyBonus : WeeklyBonus;

    // Nombre del período en la base (mission_claims.period).
    public static string PeriodKey(MissionPeriod period) =>
        period == MissionPeriod.Daily ? "daily" : "weekly";

    // Las misiones del período que empieza en "periodStartUtc": un sorteo con semilla (el inicio del período), así que es el
    // mismo resultado para todos y para siempre. Nunca elige dos del mismo tipo de evento (no tiene sentido "ganá 10 cacerías" y
    // "ganá 20 cacerías" el mismo día): recorre el pool mezclado y se queda con la primera de cada tipo.
    public static IReadOnlyList<MissionTemplate> ForPeriod(MissionPeriod period, DateTime periodStartUtc)
    {
        var pool = Pool(period);
        var rng = new StableRandom(Seed(period, periodStartUtc));

        // Fisher-Yates con el generador propio: el orden de los pools no cambia el resultado de un día ya pasado.
        var order = Enumerable.Range(0, pool.Count).ToArray();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.Next() % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }

        var picked = new List<MissionTemplate>();
        foreach (int index in order)
        {
            var candidate = pool[index];
            if (picked.All(p => p.Kind != candidate.Kind))
            {
                picked.Add(candidate);
            }

            if (picked.Count == CountFor(period))
            {
                break;
            }
        }

        return picked;
    }

    private static ulong Seed(MissionPeriod period, DateTime periodStartUtc) =>
        (ulong)periodStartUtc.Ticks ^ (period == MissionPeriod.Daily ? 0x9E3779B97F4A7C15UL : 0xD1B54A32D192ED03UL);

    // SplitMix64: un generador de 3 líneas con el mismo resultado en cualquier máquina y versión de .NET. System.Random no sirve
    // para esto (su secuencia con semilla puede cambiar entre versiones, y las misiones de "hoy" no pueden cambiar de un día al otro).
    private sealed class StableRandom(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
