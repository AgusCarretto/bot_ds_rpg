namespace BotDsRpg.GameData;

// DroppedSomething: si hay que entregar algún material al ganar — QUÉ ítem exactamente se
// resuelve aparte (ver Modules/AdventureModule.ResolveDroppedItemAsync): el ÚNICO drop del monstruo
// que se enfrentó (ver MonsterCatalog y Database/finalize_monster_roster.sql).
public sealed record CombatReward(int Gold, int Xp, bool DroppedSomething);

// Recompensa al ganar un combate (mismos rangos que tenía el viejo CombatService de
// resolución instantánea, ahora aplicados una sola vez al derrotar al monstruo).
public static class CombatRewardCalculator
{
    // CHANCE DE DROP de cada tipo de pelea (% de ganar y llevarse el material del monstruo). Son los valores de la
    // run 1: están bajos A PROPÓSITO porque si no avanzar es demasiado fácil, y el reset que se desbloquea al
    // terminar la Zona 5 los va a ir subiendo. Las cantidades de las recetas (Database/seed_recipes.sql,
    // seed_zoneN_gear_and_recipes.sql) están calibradas contra ESTOS números: si se tocan acá, hay que recalcular
    // los minutos de farmeo de cada receta (ver MEJORAS.md). Los muestra /drops, así que no se desincronizan.
    // Era 10% hasta la v0.6.0: se bajó a 6% para que al llegar al nivel de una zona no sea tan fácil pasarla de largo, y haya que
    // quedarse un rato a farmear el equipo (las recetas que dependen de drops de /hunt tardan ~1,7 veces más).
    public const int HuntDropChancePercent = 6;
    // v0.36.0: el /travel YA NO suelta un material (el dueño: «si sacamos el drop solo del travel… que el travel sea esa suma de xp y gold con probabilidad de sacar una caja de la zona»). Hasta la v0.35.0 el
    // drop de viaje era el material «escaso» de las armas de clase y los amuletos (40 % por viaje, una unidad cada ~75 minutos); ahora esas recetas piden más drops de CACERÍA (Database/remove_travel_drops.sql)
    // y lo que puede caer al ganar un viaje es la caja de la zona: la fila "travel" de zone_boxes con SU chance (baja, 2-15 %: se puede viajar cada 30 minutos). Los bonus de chance de drop (Fuego Nuevo, mascotas,
    // bendición, dragón) multiplican esa chance, como antes multiplicaban la del drop. El tope sigue en 100 %.
    // El jefe de zona ya no suelta un material sino un COFRE (monster_drops del jefe apunta a la caja, ver Database/rework_drops_and_recipes.sql y seed_zone_boxes.sql): la primera vez
    // POR VUELTA que ESE jugador lo derrota siempre cae. Las repeticiones (v0.14.0) dan la caja de zone_boxes (rol "repeat") con SU chance, que sale de la base (GameData/ZoneBoxes.cs,
    // Services/BossChest.cs); era "40 % del cofre grande" para todas las zonas. Vale para el combate solitario y, por participante, para el raid.
    public const int BossChestFirstClearPercent = 100;

    // La chance (en %) de que caiga cofre: 100 la primera vez; en una repetición, la que diga la tabla de cajas de esa zona (0 = ninguna).
    public static int BossChestChancePercent(bool firstClear, int repeatChancePercent) =>
        firstClear ? BossChestFirstClearPercent : Math.Clamp(repeatChancePercent, 0, 100);

    // monsterGoldBonus/monsterXpBonus: bonus fijo del monstruo (ver GameData/MonsterCatalog.cs y
    // Repositories/IMonsterRepository.cs) que se SUMA a la fórmula de siempre, no la reemplaza —
    // así un monstruo de Zona 1 con bonus 0/0 da exactamente lo mismo que antes de que existieran
    // las Zonas, y solo las zonas más difíciles (Bosque de Cenizas en adelante) suben la recompensa.
    // bonuses (v0.10.0 mascotas, v0.12.0 Fuego Nuevo y bendiciones, GameData/PlayerBonuses.cs): todo lo permanente del jugador. Sube el oro y la EXP de TODA pelea contra un monstruo
    // (cacería, viaje, jefe, raid, /autohunt) y, solo en cacería y viaje, la chance de drop (relativa: +6 % sobre un 6 % da 6,36 %). El cofre del jefe NO lo toca, y
    // null = sin nada de eso = las cuentas de siempre. (Un PetBonuses suelto también sirve: se convierte solo.)
    public static CombatReward RollHuntReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0, PlayerBonuses? bonuses = null) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, multiplier: 1, HuntDropChance(bonuses), bonuses);

    // Las chances de drop CON todos los bonus (lo que de verdad se tira); sin nada son HuntDropChancePercent y la de la fila "travel" de zone_boxes. Con tope en 100 %.
    public static double HuntDropChance(PlayerBonuses? bonuses) => Math.Min(100.0, HuntDropChancePercent * (bonuses?.HuntDropMultiplier ?? 1.0));

    // La chance de que caiga la caja de un viaje: la de la zona (zone_boxes, rol "travel") por los bonus de drop del jugador. 0 si la zona no tiene la fila.
    public static double TravelBoxChance(int boxChancePercent, PlayerBonuses? bonuses) => Math.Min(100.0, Math.Max(0, boxChancePercent) * (bonuses?.TravelDropMultiplier ?? 1.0));

    // Jefe de zona (/boss y /raid): la fórmula de /hunt (con el bono GRANDE del jefe, ver Database/seed_zone_bosses.sql) x6, y la chance del
    // cofre de la zona. Era x1 hasta la v0.6.0 y el jefe pagaba la MITAD que un /travel (en Zona 2: ~280 XP contra ~520): una pelea mucho más
    // dura y con 30 minutos de cooldown rendía menos que una de 10. Con x6 paga ~3,2 veces un viaje de su zona (oro y XP) — un poco más que
    // los 3 viajes que caben en sus 30 minutos, que es lo justo por arriesgarse a perder — y ronda 7/10 de un nivel en Zona 2-5. En un raid
    // cada participante cobra esto entero.
    // v0.8.0: x15 (era x6 con 30 minutos de cooldown). Con 1 hora de cooldown la paridad por minuto sería x12; el 25% de más es el "un poco más" que pidió el
    // dueño. Después el cooldown del jefe volvió a 5 horas (CooldownCatalog.Boss) y el dueño pidió NO tocar las recompensas: sigue en x15, así que por
    // minuto de cooldown ahora rinde bastante menos que un viaje (es el valor a subir si el jefe tiene que volver a sentirse "vale la espera").
    public const int BossRewardMultiplier = 15;

    // v0.18.1: la EXP de TODA pelea contra un monstruo (cacería, viaje, jefe, raid y /autohunt) vale la MITAD. El dueño notó que el jefe de la zona 3 era «muy fácil» con el equipo de la zona 2; medido con el
    // combate real, a nivel parejo es una pared (95-99 % de derrotas con ese equipo de nivel 10 a 13) y solo se vuelve fácil con ~15 niveles de más: en juego perfecto se llega al nivel 22 a las 6 h
    // y al 60 a las 31 h, que es lo que lleva juntar el equipo de clase de las cinco zonas, así que el nivel dejaba de frenar a las 6-7 h (la zona 5 pide nivel 20) y cualquier jefe se hacía a fuerza de nivel.
    // Con ×0,5: nivel 13 / 22 / 33 / 43 a las 6 / 12 / 20 / 31 h. Sube más lento TODO lo que viene de peleas, y nadie baja de nivel; el oro y los drops no cambian, y las misiones, los logros, el diario y el
    // premio de la Arena pagan su EXP aparte (sin escalar). Se aplica ANTES de los bonus del jugador (mascotas, Fuego Nuevo, bendiciones), que siguen multiplicando encima. Es el único lugar para retocarlo
    // (el dueño descartó ×0,33 por ser «una banda»). Si se toca, repetir la medición de nivel por hora (MEJORAS.md) y la de los jefes con el equipo anterior (balance2, parte J).
    public const double FightXpScale = 0.5;

    // repeatChancePercent: la chance de la caja de repetición de ESA zona (ZoneBoxRole.Repeat); solo cuenta si no es la primera vez.
    public static CombatReward RollBossReward(
        int playerLevel, int monsterGoldBonus, int monsterXpBonus, bool firstClear, PlayerBonuses? bonuses = null, int repeatChancePercent = 0) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, BossRewardMultiplier, BossChestChancePercent(firstClear, repeatChancePercent), bonuses);

    // /travel tiene 30 minutos de cooldown (30 veces el de /hunt; 10 hasta la v0.7.1) y enfrenta a un monstruo élite (HP
    // x1.25 / daño x1.1 de los comunes de la zona, ver Database/seed_travel_monsters.sql): la
    // recompensa tiene que hacerlo valer. Antes era un monto fijo que NO seguía a la zona (~150 oro /
    // ~155 XP a nivel 5), así que en Zona 4-5 rendía mucho menos que UNA cacería común (~217 / ~168 y
    // ~377 / ~285) y nadie lo usaba. Ahora es la fórmula entera de /hunt (nivel + bonus del monstruo)
    // x30 — lo que treinta cacerías darían en ese cooldown — así que escala con la zona igual que ellas.
    // En Zona 1 (bonus 0/0) queda casi igual que antes (~120 oro / ~150 XP a nivel 1).
    public const int TravelRewardMultiplier = 30;

    // boxChancePercent: la chance base de la caja de viaje de ESA zona (ZoneBoxRole.Travel); DroppedSomething = "cayó la caja".
    public static CombatReward RollTravelReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0, PlayerBonuses? bonuses = null, int boxChancePercent = 0) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, TravelRewardMultiplier, TravelBoxChance(boxChancePercent, bonuses), bonuses);

    private static CombatReward Roll(
        int playerLevel, int monsterGoldBonus, int monsterXpBonus, int multiplier, double dropChancePercent, PlayerBonuses? bonuses)
    {
        // Los bonus de oro/EXP van sobre la recompensa ENTERA (con su multiplicador de viaje o de jefe), redondeados al más cercano.
        int gold = PlayerBonuses.Scale((Random.Shared.Next(5, 16) + (playerLevel * 2) + monsterGoldBonus) * multiplier, bonuses?.GoldMultiplier ?? 1.0);
        int baseXp = (int)Math.Round((Random.Shared.Next(8, 21) + playerLevel + monsterXpBonus) * multiplier * FightXpScale, MidpointRounding.AwayFromZero);
        int xp = PlayerBonuses.Scale(baseXp, bonuses?.XpMultiplier ?? 1.0);

        // NextDouble() está en [0, 1): con 100 % siempre cae, con 0 % nunca (y con 6 % cae el 6 % de las veces, igual que con el viejo Next(100) < 6).
        bool droppedSomething = Random.Shared.NextDouble() * 100 < dropChancePercent;

        return new CombatReward(gold, xp, droppedSomething);
    }
}
