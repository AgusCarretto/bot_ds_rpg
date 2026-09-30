using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public interface IMonsterRepository
{
    // Todos los monstruos de /hunt cargados para esa zona (EXCLUYE al jefe y al monstruo de /travel,
    // ver GetBossByZoneAsync / GetTravelMonsterByZoneAsync), con sus drops ya resueltos (ver
    // Database/seed_zones_and_monsters.sql). Lista vacía si la zona todavía no tiene ninguno.
    Task<IReadOnlyList<MonsterTemplate>> GetMonstersByZoneAsync(int zoneId, CancellationToken cancellationToken = default);

    // El jefe de esa zona (ver Database/seed_zone_bosses.sql y Modules/AdventureModule.cs, comando
    // /boss), o null si la zona todavía no tiene uno cargado.
    Task<MonsterTemplate?> GetBossByZoneAsync(int zoneId, CancellationToken cancellationToken = default);

    // El monstruo dedicado de /travel de esa zona (ver Database/seed_travel_monsters.sql): uno solo,
    // separado del pool de /hunt, con sus propios drops. Null si la zona todavía no tiene uno cargado.
    Task<MonsterTemplate?> GetTravelMonsterByZoneAsync(int zoneId, CancellationToken cancellationToken = default);

    // TODOS los monstruos de TODAS las zonas (hunt, jefes y travel), cada uno con su zona y su tipo y sus drops
    // ya resueltos, en dos consultas. Lo usa /drops para listar qué suelta cada uno.
    Task<IReadOnlyList<ZoneMonster>> GetAllAsync(CancellationToken cancellationToken = default);
}
