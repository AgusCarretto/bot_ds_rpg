using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public interface IMonsterRepository
{
    // Todos los monstruos de /hunt cargados para esa zona, con sus drops ya resueltos (ver
    // Database/seed_zones_and_monsters.sql). Lista vacía si la zona todavía no tiene ninguno.
    Task<IReadOnlyList<MonsterTemplate>> GetMonstersByZoneAsync(int zoneId, CancellationToken cancellationToken = default);
}
