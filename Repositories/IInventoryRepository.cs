using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IInventoryRepository
{
    // Lista vacía si el jugador todavía no tiene ningún ítem.
    Task<IReadOnlyList<InventoryEntry>> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default);
}
