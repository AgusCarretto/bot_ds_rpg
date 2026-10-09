namespace BotDsRpg.GameData;

// La regla de vida de /autohunt. Medido con el CombatTurnResolver real y los monstruos reales de la base (v0.9.3, simulación de calibración): una cacería común con el
// equipo de la zona cuesta ~16 % de la vida, así que quien auto-caza varias veces seguidas sin curarse llega a las últimas peleas con poca vida, y /autohunt PELEA SOLO y
// no puede huir. La chance de PERDER (= pagar la penalidad por muerte: la EXP del nivel y el 5 % del oro) con ataque básico, en las zonas 2 a 5:
//     vida 90 % → 0-0,1 %    80 % → 0-0,3 %    70 % → 0,1-1,2 %    65 % → 0,1-1,8 %    60 % → 0,4-3,3 %    50 % → 3-10 %    35 % → 16-30 %    25 % → 38-57 %
// (v0.15.1: con el amuleto de la zona puesto una común cuesta ~3 % de la vida en vez de ~16 %, ver MEJORAS.md; el piso de 65 % se dejó como está.)
// Una muerte cuesta ~20 cacerías de EXP (la mitad de un nivel, en promedio) más el 5 % del oro, y una cacería rinde 1: para que valga la pena la chance de perder tiene que ser
// MUCHO menor al 5 %. Con 65 % el riesgo no pasa de ~2 % en ninguna zona. /hunt (a mano) no tiene esta regla: ahí se ve la vida y se puede huir.
public static class AutoHuntRules
{
    // % de la vida máxima con el que /autohunt deja pelear. Por debajo se pide curarse (o pelear a mano) y NO se gasta el cooldown.
    public const int MinHpPercent = 65;

    // Con vida 0 el mensaje es el de siempre ("sin HP"), así que acá no cuenta como "demasiado herido".
    public static bool IsTooHurt(int currentHp, int maxHp) => currentHp > 0 && maxHp > 0 && currentHp * 100 < MinHpPercent * maxHp;
}
