namespace BotDsRpg.GameData;

// Una comida que el jugador tiene y puede usar para curarse en plena pelea (una opción del desplegable).
public sealed record HealOption(string Name, int HealHp, int Quantity);

// Reglas PURAS de la curación en combate (el desplegable "Curar" y /use dentro de una pelea).
//
// En /travel y /boss te podés curar UNA sola vez por pelea (CombatState.HealUsed): si fueran dos o más, comer
// hasta ganar sería demasiado fácil — las peleas caras son las que tienen que costar. En /hunt el desplegable no
// existe (se farmea en cantidad y curarse con comida entre peleas es justo el ciclo de recursos) y en el raid
// tampoco (el mensaje es compartido). Para no dejar una puerta trasera, /use escrito en plena pelea de
// /travel o /boss cuenta como ESA curación: botón y comando comparten el mismo límite.
public static class CombatHeal
{
    public const string MenuCustomId = "combat_heal";

    // Discord permite hasta 25 opciones por desplegable.
    public const int MaxOptions = 25;

    // true = este tipo de pelea tiene el desplegable y el límite de una curación.
    public static bool IsLimited(string commandName) => commandName is "travel" or "boss";

    // Las comidas del desplegable: las que MÁS curan primero (lo que uno quiere elegir en una pelea grande), y las
    // que no curan nada (stat_value 0) ni tienen stock, afuera. Recorta a las que entran en un desplegable.
    public static IReadOnlyList<HealOption> BuildOptions(IEnumerable<HealOption> owned) =>
        owned.Where(o => o.HealHp > 0 && o.Quantity > 0)
             .OrderByDescending(o => o.HealHp)
             .ThenBy(o => o.Name, StringComparer.CurrentCulture)
             .Take(MaxOptions)
             .ToList();

    // "+100 HP · tenés 3": la descripción de cada opción (Discord la limita a 100 caracteres).
    public static string Describe(HealOption option) => $"+{option.HealHp} HP · tenés {option.Quantity}";
}
