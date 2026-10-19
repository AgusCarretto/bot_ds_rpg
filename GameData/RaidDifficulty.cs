namespace BotDsRpg.GameData;

// Dificultad del jefe de un RAID respecto del mismo jefe peleado solo con /boss. Todos los números
// de balance del raid viven acá: para retocarlo alcanza con cambiar una constante.
//
// Los multiplicadores dependen de la POSICIÓN de la zona (por dificultad, no por zone_id) porque los jefes de
// zona no son igual de duros entre sí:
//   - Zona 1: el Rey Jabalí es un jefe "suave" (~40% de la vida con su equipo propio). Medido con el
//     CombatTurnResolver real (4 clases mezcladas, cada jugador con su vida y sin curaciones — en el raid no hay
//     /use mid-fight), nivel 6 con un arma +5: solo gana ~20%, de a dos ~65%, de a tres ~79%, de a cuatro ~91%;
//     con un arma +15 hasta uno solo gana ~75%. Antes de este ajuste (jefe idéntico al de /boss, HP fijo) un
//     jugador solo lo ganaba 98% de las veces y tres jugadores el 100% en ~2 rondas.
//   - Zona 2 en adelante: los jefes ya están en el estándar de la ESCALERA DE ZONAS (con el equipo propio de la
//     zona cuestan ~70% de la vida y se pierden ~16% de las veces: ver MEJORAS.md). Encima de eso los
//     multiplicadores de Zona 1 (x2.2 / x1.35) dejaban el raid IMPOSIBLE (0-3% de victoria con cualquier cantidad
//     de jugadores). Con x1.5 / x1.1, al nivel del jefe y con el equipo de la zona (arma de afinidad + amuleto
//     "bajo"), medido en las zonas 2, 3, 4 y 5 con sus jefes reales: solo gana 25-28%, de a dos 55-59%, de a tres
//     63-70%, de a cuatro 80-87% — la misma curva en las cuatro, por eso alcanza un único multiplicador. Con el
//     equipo de la zona anterior es imposible (~0%); con el amuleto barato en vez del de la zona, cae mucho.
public static class RaidDifficulty
{
    // Cuánto más duro es el jefe de raid que el /boss solitario de la misma zona.
    public sealed record Scale(double HpMultiplier, double DamageMultiplier);

    // v0.17.1: la vida GUARDADA de los jefes subió ×1,25 (Database/rebalance_boss_hp.sql) y los multiplicadores de VIDA de abajo se dividieron por 1,25 (eran 2,2 y 1,5), así que el jefe del RAID queda
    // exactamente con la misma vida y la misma dificultad que ya estaba medida arriba (2,2 / 1,25 × 1,25 = 2,2; 1,2 × 1,25 = 1,5). Si se vuelve a tocar la vida de los jefes, tocar esto con el mismo factor
    // (o decidir a propósito un raid más duro: con ×1,25 de más, un grupo de tres de las zonas 2 a 5 pasaba de ganar ~49 % a ~16 %).
    //
    // v0.18.6: la vida del raid baja ×0,75 (1,76 → 1,32 y 1,2 → 0,9; el daño NO se toca). El dueño: «la idea del raid es que de a 2 sea más fácil que solo». Medido con el combate real (tools/balance2, parte K, equipo propio,
    // sin comida porque el raid no tiene curación, 400 raids por celda): a nivel de llegada a la zona (mín+3) el raid de 2 jugadores ganaba solo 18-28 % en las zonas 2 a 5 (el de 4, 44-62 %) mientras el jefe SOLO con una
    // comida y la habilidad se gana ~79 / 79 / 70 / 45 %: el raid era la forma más difícil de pelear al llegar. Con ×0,75: 2 jugadores 66-71 %, 3 jugadores 83-92 %, 4 jugadores 85-98 %, y a mín+8 todo ≥ 99 % con 2 o más.
    // Ojo: con ×0,75 los 2 jugadores igualan al jefe solo en las zonas 4 y 5 pero quedan un poco debajo en las zonas 2 y 3 (66-70 % contra 79 %); ×0,65 los deja por encima en las cuatro (76 / 89 / 90 / 92 %) y ×0,6 ya es
    // demasiado fácil. Si se quiere «siempre más fácil de a dos», es cambiar estas dos constantes.
    private static readonly Scale FirstZone = new(1.32, 1.35);
    private static readonly Scale LadderZones = new(0.9, 1.1);

    // zoneRank: posición 1-indexada de la zona por dificultad (ZoneRanking.RankOf); 0 (desconocida) cuenta como 1.
    public static Scale ForZoneRank(int zoneRank) => zoneRank <= 1 ? FirstZone : LadderZones;

    // Cada jugador ADEMÁS del primero suma este porcentaje del HP base: con 3 jugadores el jefe tiene
    // 1 + 2 * 0.5 = 2x su HP. Menos que proporcional a propósito — así un grupo grande sí lo tiene más
    // fácil por cabeza que uno chico (si fuera 1.0 por jugador, sumar gente no ayudaría en nada).
    public const double HpPerExtraParticipant = 0.5;

    // "baseHp" es el HP sorteado del jefe tal como está en la base (monsters.min_hp..max_hp).
    public static int BossHp(int baseHp, int participants, int zoneRank) =>
        (int)Math.Round(baseHp * ForZoneRank(zoneRank).HpMultiplier * (1 + HpPerExtraParticipant * (Math.Max(1, participants) - 1)));

    public static int BossDamage(int baseDamage, int zoneRank) =>
        (int)Math.Round(baseDamage * ForZoneRank(zoneRank).DamageMultiplier);
}
