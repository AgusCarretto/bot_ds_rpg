namespace BotDsRpg.GameData;

// DroppedSomething: si hay que entregar algún material al ganar — QUÉ ítem exactamente se
// resuelve aparte (ver Modules/AdventureModule.ResolveDroppedItemAsync), porque depende de si es
// /hunt (drop fijo del monstruo, ver MonsterCatalog) o /travel (rareza sorteada).
public sealed record CombatReward(int Gold, int Xp, bool DroppedSomething);

// Recompensa al ganar un combate (mismos rangos que tenía el viejo CombatService de
// resolución instantánea, ahora aplicados una sola vez al derrotar al monstruo).
public static class CombatRewardCalculator
{
    // monsterGoldBonus/monsterXpBonus: bonus fijo del monstruo (ver GameData/MonsterCatalog.cs y
    // Repositories/IMonsterRepository.cs) que se SUMA a la fórmula de siempre, no la reemplaza —
    // así un monstruo de Zona 1 con bonus 0/0 da exactamente lo mismo que antes de que existieran
    // las Zonas, y solo las zonas más difíciles (Bosque de Cenizas en adelante) suben la recompensa.
    public static CombatReward RollHuntReward(int playerLevel, int monsterGoldBonus = 0, int monsterXpBonus = 0)
    {
        int gold = Random.Shared.Next(5, 16) + (playerLevel * 2) + monsterGoldBonus;
        int xp = Random.Shared.Next(8, 21) + playerLevel + monsterXpBonus;

        // 30% de probabilidad de dropear un material del monstruo al ganar.
        bool droppedSomething = Random.Shared.Next(100) < 30;

        return new CombatReward(gold, xp, droppedSomething);
    }

    // /travel tiene 10 minutos de cooldown (10 veces el de /hunt) y sus monstruos son más duros que
    // los de zona 1: la recompensa tiene que hacerlo valer. Con los valores anteriores (30-60 oro /
    // 30-55 XP) rendía MENOS XP por minuto que farmear /hunt, así que nadie lo usaba salvo por el
    // drop. Ahora paga ~2.5x en oro y ~3x en XP (a nivel 5: ~150 oro / ~155 XP contra ~65 / ~52).
    public static CombatReward RollTravelReward(int playerLevel)
    {
        int gold = Random.Shared.Next(90, 151) + (playerLevel * 6);
        int xp = Random.Shared.Next(100, 151) + (playerLevel * 6);

        // 50% de probabilidad de dropear un material al ganar.
        bool droppedSomething = Random.Shared.Next(100) < 50;

        return new CombatReward(gold, xp, droppedSomething);
    }
}
