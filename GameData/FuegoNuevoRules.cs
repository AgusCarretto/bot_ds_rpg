namespace BotDsRpg.GameData;

// Fuego Nuevo (v0.12.0): volver a empezar, más rápido. Los PORCENTAJES de cada vuelta, puros y con nombre (el diseño aprobado por el dueño el 2026-10-06, ver
// docs/superpowers/specs/2026-10-06-fuego-nuevo-design.md). "FN 3" = tres Fuegos Nuevos hechos (users.fuego_nuevo).
//
// Son LINEALES y siempre iguales: cada Fuego Nuevo suma lo mismo sobre la base (FN N = base × (1 + paso × N), no se compone), así el FN 10 se nota muchísimo contra el FN 1.
// La única excepción es /travel, cuyo paso BAJA con cada vuelta (arranca en +10 % y pierde 0,5 puntos por Fuego Nuevo, con un mínimo de +2 %): su drop es el cuello de botella
// de las armas de clase y los amuletos y, con una base de 40 %, un paso parejo lo toparía en 100 % antes de tiempo.
//   · /hunt   la CHANCE de drop del monstruo   ×1,30 por FN     (6 % de base → 7,8 % en el FN 1, 24 % en el FN 10)
//   · /travel la CHANCE de drop del monstruo   +10 % bajando    (40 % de base → 44 % en el FN 1, 71 % en el FN 10, 100 % en el FN 41)
//   · /chop y /mine  la CANTIDAD por acción    ×1,20 por FN     (×3 en el FN 10)
//   · EXP de las peleas                        +10 % por FN
//   · Oro: sin %, el oro ya se queda entre vueltas.
// El cofre del jefe no cambia. Medido con report_recipe_pacing.sql: las recetas bajan a ~51 % del tiempo de la vuelta 1 en el FN 10 y a ~34 % en el FN 30.
public static class FuegoNuevoRules
{
    public const double HuntDropStep = 0.30;   // /hunt: +30 % relativo a la chance por Fuego Nuevo
    public const double GatherStep = 0.20;     // /chop y /mine: +20 % de cantidad por Fuego Nuevo
    public const double XpStep = 0.10;         // EXP de las peleas: +10 % por Fuego Nuevo (el paso lo puse yo; el dueño no lo fijó)

    // Los tipos de ítem que SOBREVIVEN al reinicio: los huevos y la comida de las mascotas (items.type), porque las mascotas se quedan y perder el huevo de una que todavía no
    // nació o la comida que las hace crecer sería castigar justo lo que el dueño dijo que se conserva. Todo lo demás de la mochila se va, sin reembolso.
    public static readonly IReadOnlyList<string> KeptItemTypes = ["Huevo", "PetFood"];

    // /class (cambiar de clase) solo se puede empezando de cero (nivel 1 y 0 de EXP) o al hacer un Fuego Nuevo, que la vuelve a elegir: antes de la v0.12.0 se podía cambiar en cualquier
    // momento, pero con las vueltas la clase pasó a ser una decisión de cada vuelta (el dueño: «la clase se bloquea por run»).
    public static bool CanChangeClass(int level, int xp) => level <= 1 && xp <= 0;

    // /travel, en milésimas para que la suma sea exacta: el FN k suma max(20, 100 - 5 × (k - 1)) milésimas (+10 %, +9,5 %, ... mínimo +2 %).
    private const int TravelFirstStepMilli = 100;
    private const int TravelDecrementMilli = 5;
    private const int TravelMinStepMilli = 20;

    // Lo que suma el Fuego Nuevo número k (1, 2, 3...) a la chance de /travel: 0,10 el primero y menos cada vez.
    public static double TravelStep(int k) => k < 1 ? 0 : Math.Max(TravelMinStepMilli, TravelFirstStepMilli - (TravelDecrementMilli * (k - 1))) / 1000.0;

    // Todo lo que sumaron los primeros N Fuegos Nuevos a la chance de /travel (0,10 + 0,095 + ...).
    public static double TravelStepsTotal(int fuegoNuevo)
    {
        int sum = 0;
        for (int k = 1; k <= fuegoNuevo; k++)
        {
            sum += Math.Max(TravelMinStepMilli, TravelFirstStepMilli - (TravelDecrementMilli * (k - 1)));
        }

        return sum / 1000.0;
    }

    public static double HuntDropMultiplier(int fuegoNuevo) => 1 + (HuntDropStep * Math.Max(0, fuegoNuevo));

    public static double TravelDropMultiplier(int fuegoNuevo) => 1 + TravelStepsTotal(fuegoNuevo);

    public static double GatherMultiplier(int fuegoNuevo) => 1 + (GatherStep * Math.Max(0, fuegoNuevo));

    public static double XpMultiplier(int fuegoNuevo) => 1 + (XpStep * Math.Max(0, fuegoNuevo));

    // Lo que da hoy ese Fuego Nuevo, un renglón por cosa (para el perfil y /fuegonuevo). Son los multiplicadores sobre la base; la chance real de /travel la topa el 100 %.
    public static string BonusLines(int fuegoNuevo) =>
        $"🏹 Chance de drop en /hunt: **{PercentText(HuntDropMultiplier(fuegoNuevo))}**\n" +
        $"🗺️ Chance de drop en /travel: **{PercentText(TravelDropMultiplier(fuegoNuevo))}**\n" +
        $"🪓⛏️ Cantidad en /chop y /mine: **{PercentText(GatherMultiplier(fuegoNuevo))}**\n" +
        $"📊 EXP de las peleas: **{PercentText(XpMultiplier(fuegoNuevo))}**";

    // "+30 %" de un multiplicador (1,3 → "+30 %"), con coma decimal y un decimal como mucho.
    public static string PercentText(double multiplier) =>
        $"+{((multiplier - 1) * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} %";
}
