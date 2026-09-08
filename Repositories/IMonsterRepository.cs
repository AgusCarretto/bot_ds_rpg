using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public interface IMonsterRepository
{
    // Todos los monstruos de /hunt cargados para esa zona (EXCLUYE al jefe, ver GetBossByZoneAsync),
    // con sus drops ya resueltos (ver Database/seed_zones_and_monsters.sql). Lista vacía si la zona
    // todavía no tiene ninguno.
    Task<IReadOnlyList<MonsterTemplate>> GetMonstersByZoneAsync(int zoneId, CancellationToken cancellationToken = default);

    // El jefe de esa zona (ver Database/seed_zone_bosses.sql y Modules/AdventureModule.cs, comando
    // /boss), o null si la zona todavía no tiene uno cargado.
    Task<MonsterTemplate?> GetBossByZoneAsync(int zoneId, CancellationToken cancellationToken = default);
}
