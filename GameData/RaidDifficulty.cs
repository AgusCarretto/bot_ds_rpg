namespace BotDsRpg.GameData;

// Dificultad del jefe de un RAID respecto del mismo jefe peleado solo con /boss. Todos los números
// de balance del raid viven acá: para retocarlo alcanza con cambiar una constante.
//
// Medido con el CombatTurnResolver real (4 clases mezcladas, cada jugador con su propia vida y sin
// curaciones — en el raid no hay /use mid-fight), contra el Rey Jabalí de zona 1, nivel 6 con un arma
// +5 (la más barata): solo gana ~20%, de a dos ~65%, de a tres ~79%, de a cuatro ~91%. Con un arma
// +15 hasta uno solo gana ~75%. Sin arma, nivel 6: solo 1%, de a dos 13%, de a cuatro 41% (a propósito:
// el raid asume equipo). Antes de este ajuste (jefe idéntico al de /boss, HP fijo) un jugador solo lo ganaba 98% de las
// veces y tres jugadores el 100% en ~2 rondas.
public static class RaidDifficulty
{
    // Cuánto más duro es el jefe de raid que el /boss solitario de la misma zona.
    public const double BossHpMultiplier = 2.2;
    public const double BossDamageMultiplier = 1.35;

    // Cada jugador ADEMÁS del primero suma este porcentaje del HP base: con 3 jugadores el jefe tiene
    // 1 + 2 * 0.5 = 2x su HP. Menos que proporcional a propósito — así un grupo grande sí lo tiene más
    // fácil por cabeza que uno chico (si fuera 1.0 por jugador, sumar gente no ayudaría en nada).
    public const double HpPerExtraParticipant = 0.5;

    // "baseHp" es el HP sorteado del jefe tal como está en la base (monsters.min_hp..max_hp).
    public static int BossHp(int baseHp, int participants) =>
        (int)Math.Round(baseHp * BossHpMultiplier * (1 + HpPerExtraParticipant * (Math.Max(1, participants) - 1)));

    public static int BossDamage(int baseDamage) =>
        (int)Math.Round(baseDamage * BossDamageMultiplier);
}
