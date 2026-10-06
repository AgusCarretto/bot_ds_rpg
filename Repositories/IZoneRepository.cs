using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IZoneRepository
{
    // Las zonas de la ESCALERA (kind = 'normal'), ordenadas por min_level (usado por /zonas). NO incluye El Fogón Eterno (la puerta, zona 0): así la escalera, el orden de
    // dificultad, las recetas por zona, los drops, las cajas y las mascotas no la ven. La puerta se pide con GetGateAsync.
    Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken cancellationToken = default);

    // Devuelve null si no existe ninguna zona con ese id (también devuelve la puerta si el id es el suyo).
    Task<Zone?> GetByIdAsync(int zoneId, CancellationToken cancellationToken = default);

    // El Fogón Eterno (la zona puerta, kind = 'gate', zone_id 0), o null si la base todavía no la tiene.
    Task<Zone?> GetGateAsync(CancellationToken cancellationToken = default);
}
