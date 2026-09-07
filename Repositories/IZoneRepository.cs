using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IZoneRepository
{
    // Catálogo completo de zonas, ordenado por min_level (usado por /zonas).
    Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken cancellationToken = default);

    // Devuelve null si no existe ninguna zona con ese id.
    Task<Zone?> GetByIdAsync(int zoneId, CancellationToken cancellationToken = default);
}
