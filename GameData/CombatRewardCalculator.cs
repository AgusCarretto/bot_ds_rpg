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
    // 40% desde la v0.10.2 (el dueño: «60 es una banda», 2026-10-06). Era 60% hasta la v0.10.1 y 20% con el travel de 10 minutos: con el cooldown en 30 minutos, el 60% daba 2% de drop por minuto de
    // cooldown, que es lo contra lo que se calibraron las recetas; con 40% son 1,33% por minuto, así que los materiales de viaje (los «escasos» de las armas de clase y los amuletos) tardan 1,5 veces más
    // y el camino de recetas de las 5 zonas pasa de ~41 h a ~52 h de juego perfecto (medido con report_recipe_pacing.sql; las cantidades de las recetas NO se tocaron a propósito).
    public const int TravelDropChancePercent = 40;
    // El jefe de zona ya no suelta un material sino un COFRE de su zona (monster_drops del jefe apunta a la caja, ver
    // Database/rework_drops_and_recipes.sql): la primera vez que ESE jugador lo derrota siempre cae; las siguientes, 40%.
    // Vale para el combate solitario y, por participante, para el raid.
    public const int BossChestFirstClearPercent = 100;
    public const int BossChestRepeatPercent = 40;

    public static int BossChestChancePercent(bool firstClear) => firstClear ? BossChestFirstClearPercent : BossChestRepeatPercent;

    // monsterGoldBonus/monsterXpBonus: bonus fijo del monstruo (ver GameData/MonsterCatalog.cs y
    // Repositories/IMonsterRepository.cs) que se SUMA a la fórmula de siempre, no la reemplaza —
    // así un monstruo de Zona 1 con bonus 0/0 da exactamente lo mismo que antes de que existieran
    // las Zonas, y solo las zonas más difíciles (Bosque de Cenizas en adelante) suben la recompensa.
    // pets (v0.10.0, GameData/PetRules.cs): los bonus de las mascotas del jugador. Suben el oro y la EXP de TODA pelea contra un monstruo (cacería, viaje, jefe, raid, /autohunt) y,
    // solo en cacería y viaje, la chance de drop (relativa: +6 % sobre un 6 % da 6,36 %). El cofre del jefe NO lo toca, y null = sin mascotas = las cuentas de siempre.
    public static CombatReward RollHuntReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0, PetBonuses? pets = null) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, multiplier: 1, HuntDropChance(pets), pets);

    // Las chances de drop CON el bonus de mascotas (lo que de verdad se tira); sin mascotas son HuntDropChancePercent / TravelDropChancePercent.
    public static double HuntDropChance(PetBonuses? pets) => WithDropBonus(HuntDropChancePercent, pets);

    public static double TravelDropChance(PetBonuses? pets) => WithDropBonus(TravelDropChancePercent, pets);

    private static double WithDropBonus(int basePercent, PetBonuses? pets) => Math.Min(100.0, basePercent * (1 + (pets?.DropPercent ?? 0) / 100.0));

    // Jefe de zona (/boss y /raid): la fórmula de /hunt (con el bono GRANDE del jefe, ver Database/seed_zone_bosses.sql) x6, y la chance del
    // cofre de la zona. Era x1 hasta la v0.6.0 y el jefe pagaba la MITAD que un /travel (en Zona 2: ~280 XP contra ~520): una pelea mucho más
    // dura y con 30 minutos de cooldown rendía menos que una de 10. Con x6 paga ~3,2 veces un viaje de su zona (oro y XP) — un poco más que
    // los 3 viajes que caben en sus 30 minutos, que es lo justo por arriesgarse a perder — y ronda 7/10 de un nivel en Zona 2-5. En un raid
    // cada participante cobra esto entero.
    // v0.8.0: x15 (era x6 con 30 minutos de cooldown). Con 1 hora de cooldown la paridad por minuto sería x12; el 25% de más es el "un poco más" que pidió el
    // dueño. Después el cooldown del jefe volvió a 5 horas (CooldownCatalog.Boss) y el dueño pidió NO tocar las recompensas: sigue en x15, así que por
    // minuto de cooldown ahora rinde bastante menos que un viaje (es el valor a subir si el jefe tiene que volver a sentirse "vale la espera").
    public const int BossRewardMultiplier = 15;

    public static CombatReward RollBossReward(int playerLevel, int monsterGoldBonus, int monsterXpBonus, bool firstClear, PetBonuses? pets = null) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, BossRewardMultiplier, BossChestChancePercent(firstClear), pets);

    // /travel tiene 30 minutos de cooldown (30 veces el de /hunt; 10 hasta la v0.7.1) y enfrenta a un monstruo élite (HP
    // x1.25 / daño x1.1 de los comunes de la zona, ver Database/seed_travel_monsters.sql): la
    // recompensa tiene que hacerlo valer. Antes era un monto fijo que NO seguía a la zona (~150 oro /
    // ~155 XP a nivel 5), así que en Zona 4-5 rendía mucho menos que UNA cacería común (~217 / ~168 y
    // ~377 / ~285) y nadie lo usaba. Ahora es la fórmula entera de /hunt (nivel + bonus del monstruo)
    // x30 — lo que treinta cacerías darían en ese cooldown — así que escala con la zona igual que ellas.
    // En Zona 1 (bonus 0/0) queda casi igual que antes (~120 oro / ~150 XP a nivel 1).
    public const int TravelRewardMultiplier = 30;

    public static CombatReward RollTravelReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0, PetBonuses? pets = null) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, TravelRewardMultiplier, TravelDropChance(pets), pets);

    private static CombatReward Roll(
        int playerLevel, int monsterGoldBonus, int monsterXpBonus, int multiplier, double dropChancePercent, PetBonuses? pets)
    {
        // El bonus de oro/EXP de las mascotas va sobre la recompensa ENTERA (con su multiplicador de viaje o de jefe), redondeado al más cercano.
        int gold = PetRules.Boost((Random.Shared.Next(5, 16) + (playerLevel * 2) + monsterGoldBonus) * multiplier, pets?.GoldPercent ?? 0);
        int xp = PetRules.Boost((Random.Shared.Next(8, 21) + playerLevel + monsterXpBonus) * multiplier, pets?.XpPercent ?? 0);

        // NextDouble() está en [0, 1): con 100 % siempre cae, con 0 % nunca (y con 6 % cae el 6 % de las veces, igual que con el viejo Next(100) < 6).
        bool droppedSomething = Random.Shared.NextDouble() * 100 < dropChancePercent;

        return new CombatReward(gold, xp, droppedSomething);
    }
}
