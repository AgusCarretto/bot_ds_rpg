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

    private static readonly Scale FirstZone = new(2.2, 1.35);
    private static readonly Scale LadderZones = new(1.5, 1.1);

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
