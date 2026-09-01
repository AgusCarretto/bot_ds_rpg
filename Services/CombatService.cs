using BotDsRpg.GameData;

namespace BotDsRpg.Services;

public sealed class CombatService : ICombatService
{
    // Ambos comandos usan la misma fórmula de poder del jugador (nivel + azar);
    // lo que cambia es qué tan duro pega el monstruo, cuánto HP cuesta pelear y qué tan
    // grande es el premio.
    public CombatResult SimulateHunt(int playerLevel)
    {
        var monster = MonsterCatalog.RollFrom(MonsterCatalog.HuntMonsters);

        int playerPower = PlayerPower(playerLevel);
        int monsterPower = Random.Shared.Next(8, 19);

        if (playerPower < monsterPower)
        {
            int defeatDamage = Random.Shared.Next(15, 31);
            return new CombatResult(false, monster.Name, monster.Emoji, 0, 0, defeatDamage, null);
        }

        int gold = Random.Shared.Next(5, 16) + (playerLevel * 2);
        int xp = Random.Shared.Next(8, 21) + playerLevel;
        int victoryDamage = Random.Shared.Next(0, 11);

        // /hunt no dropea materiales, solo oro y experiencia (a diferencia de /travel).
        return new CombatResult(true, monster.Name, monster.Emoji, gold, xp, victoryDamage, null);
    }

    public CombatResult SimulateTravel(int playerLevel)
    {
        var monster = MonsterCatalog.RollFrom(MonsterCatalog.TravelMonsters);

        int playerPower = PlayerPower(playerLevel);
        int monsterPower = Random.Shared.Next(18, 33); // rango más duro que /hunt

        if (playerPower < monsterPower)
        {
            int defeatDamage = Random.Shared.Next(25, 46);
            return new CombatResult(false, monster.Name, monster.Emoji, 0, 0, defeatDamage, null);
        }

        int gold = Random.Shared.Next(30, 61) + (playerLevel * 4);
        int xp = Random.Shared.Next(30, 56) + (playerLevel * 2);
        int victoryDamage = Random.Shared.Next(5, 21);

        // 40% de probabilidad de dropear un material (de rareza sorteada) al ganar.
        string? droppedRarity = Random.Shared.Next(100) < 40 ? RarityCatalog.RollTravelRarity() : null;

        return new CombatResult(true, monster.Name, monster.Emoji, gold, xp, victoryDamage, droppedRarity);
    }

    private static int PlayerPower(int playerLevel) =>
        12 + (playerLevel * 2) + Random.Shared.Next(0, 11);
}
