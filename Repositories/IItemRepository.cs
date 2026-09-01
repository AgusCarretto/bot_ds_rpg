using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IItemRepository
{
    // Elige al azar un ítem del catálogo que tenga la rareza indicada (para resolver drops de /travel).
    // Devuelve null si todavía no hay ítems cargados para esa rareza.
    Task<Item?> GetRandomByRarityAsync(string rarity, CancellationToken cancellationToken = default);

    // Igual que el anterior, pero además filtra por tipo (ej. "Madera" para /chop, "Mineral" para /mine).
    Task<Item?> GetRandomByTypeAndRarityAsync(string type, string rarity, CancellationToken cancellationToken = default);
}
