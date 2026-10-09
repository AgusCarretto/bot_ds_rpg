namespace BotDsRpg.GameData;

// Una comida que el jugador tiene y puede usar para curarse en plena pelea (una opción del desplegable).
public sealed record HealOption(string Name, int HealHp, int Quantity, int BuffPercent = 0);

// Reglas PURAS de la curación en combate (el desplegable "Curar" y /use dentro de una pelea).
//
// En /hunt, /travel y /boss te podés curar UNA sola vez por pelea (CombatState.HealUsed): si fueran dos o más, comer
// hasta ganar sería demasiado fácil — las peleas son las que tienen que costar. El desplegable "Curar" existe solo en
// /travel y /boss (en /hunt la curación es el /use escrito: se farmea en cantidad y el mensaje no lleva menús) y en el
// raid no existe (el mensaje es compartido; ahí /use no toca la vida de la pelea, que vive en memoria). Para no dejar
// una puerta trasera, /use escrito en plena pelea cuenta como ESA curación: botón y comando comparten el mismo límite.
// (Hasta la v0.16.0 el /use de /hunt no tenía límite: era una decisión mía de la v0.8 — «el desplegable no existe ahí,
// no que se prohíba curarse» — y el dueño la corrigió: una vez por pelea, en todas.)
public static class CombatHeal
{
    public const string MenuCustomId = "combat_heal";

    // Discord permite hasta 25 opciones por desplegable.
    public const int MaxOptions = 25;

    // true = este tipo de pelea tiene el desplegable "Curar" (/travel y /boss).
    public static bool HasMenu(string commandName) => commandName is "travel" or "boss";

    // true = en este tipo de pelea te curás UNA sola vez, con el desplegable o con /use (/hunt, /travel y /boss).
    public static bool IsLimited(string commandName) => commandName is "hunt" or "travel" or "boss";

    // Las comidas del desplegable: las que MÁS curan primero (lo que uno quiere elegir en una pelea grande), y las
    // que no curan nada (stat_value 0) ni tienen stock, afuera. Recorta a las que entran en un desplegable.
    public static IReadOnlyList<HealOption> BuildOptions(IEnumerable<HealOption> owned) =>
        owned.Where(o => o.HealHp > 0 && o.Quantity > 0)
             .OrderByDescending(o => o.HealHp)
             .ThenBy(o => o.Name, StringComparer.CurrentCulture)
             .Take(MaxOptions)
             .ToList();

    // "+100 HP · tenés 3": la descripción de cada opción (Discord la limita a 100 caracteres).
    public static string Describe(HealOption option) =>
        option.BuffPercent > 0 ? $"+{option.HealHp} HP y +{option.BuffPercent}% ATQ · tenés {option.Quantity}" : $"+{option.HealHp} HP · tenés {option.Quantity}";
}
