using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IInventoryRepository
{
    // Lista vacía si el jugador todavía no tiene ningún ítem.
    Task<IReadOnlyList<InventoryEntry>> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Cuánto tiene el jugador de un ítem puntual (0 si no lo tiene, no null).
    Task<int> GetQuantityAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);
}
