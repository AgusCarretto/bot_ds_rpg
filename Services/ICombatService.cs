namespace BotDsRpg.Services;

// DroppedRarity: rareza sorteada para un material a otorgar (null = no dropea nada esta vez).
// La resolución de qué ítem concreto corresponde a esa rareza es responsabilidad del repositorio,
// no de este servicio, que se mantiene puro (sin dependencias de base de datos) y testeable.
public sealed record CombatResult(
    bool Victory,
    string MonsterName,
    string MonsterEmoji,
    int GoldReward,
    int XpReward,
    int HpLost,
    string? DroppedRarity);

public interface ICombatService
{
    CombatResult SimulateHunt(int playerLevel, int weaponDamage);
    CombatResult SimulateTravel(int playerLevel, int weaponDamage);
}
