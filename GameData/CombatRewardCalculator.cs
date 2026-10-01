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
    public const int TravelDropChancePercent = 20;
    // El jefe (solitario y de raid) antes usaba la misma fórmula que /hunt y, con UN solo drop por monstruo, cada
    // ítem del jefe salía el doble de seguido que antes; 15% mantiene el ritmo de siempre (~200 min por unidad).
    public const int BossDropChancePercent = 15;

    // monsterGoldBonus/monsterXpBonus: bonus fijo del monstruo (ver GameData/MonsterCatalog.cs y
    // Repositories/IMonsterRepository.cs) que se SUMA a la fórmula de siempre, no la reemplaza —
    // así un monstruo de Zona 1 con bonus 0/0 da exactamente lo mismo que antes de que existieran
    // las Zonas, y solo las zonas más difíciles (Bosque de Cenizas en adelante) suben la recompensa.
    public static CombatReward RollHuntReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, multiplier: 1, HuntDropChancePercent);

    // Jefe de zona (/boss y /raid): la fórmula de /hunt (con el bono GRANDE del jefe, ver Database/seed_zone_bosses.sql) x6, y su propia
    // chance de drop. Era x1 hasta la v0.6.0 y el jefe pagaba la MITAD que un /travel (en Zona 2: ~280 XP contra ~520): una pelea mucho más
    // dura y con 30 minutos de cooldown rendía menos que una de 10. Con x6 paga ~3,2 veces un viaje de su zona (oro y XP) — un poco más que
    // los 3 viajes que caben en sus 30 minutos, que es lo justo por arriesgarse a perder — y ronda 7/10 de un nivel en Zona 2-5. En un raid
    // cada participante cobra esto entero.
    public const int BossRewardMultiplier = 6;

    public static CombatReward RollBossReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, BossRewardMultiplier, BossDropChancePercent);

    // /travel tiene 10 minutos de cooldown (10 veces el de /hunt) y enfrenta a un monstruo élite (HP
    // x1.25 / daño x1.1 de los comunes de la zona, ver Database/seed_travel_monsters.sql): la
    // recompensa tiene que hacerlo valer. Antes era un monto fijo que NO seguía a la zona (~150 oro /
    // ~155 XP a nivel 5), así que en Zona 4-5 rendía mucho menos que UNA cacería común (~217 / ~168 y
    // ~377 / ~285) y nadie lo usaba. Ahora es la fórmula entera de /hunt (nivel + bonus del monstruo)
    // x10 — lo que diez cacerías darían en ese cooldown — así que escala con la zona igual que ellas.
    // En Zona 1 (bonus 0/0) queda casi igual que antes (~120 oro / ~150 XP a nivel 1).
    public const int TravelRewardMultiplier = 10;

    public static CombatReward RollTravelReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, TravelRewardMultiplier, TravelDropChancePercent);

    private static CombatReward Roll(int playerLevel, int monsterGoldBonus, int monsterXpBonus, int multiplier, int dropChancePercent)
    {
        int gold = (Random.Shared.Next(5, 16) + (playerLevel * 2) + monsterGoldBonus) * multiplier;
        int xp = (Random.Shared.Next(8, 21) + playerLevel + monsterXpBonus) * multiplier;

        bool droppedSomething = Random.Shared.Next(100) < dropChancePercent;

        return new CombatReward(gold, xp, droppedSomething);
    }
}
