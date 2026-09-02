using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IItemRepository
{
    // Elige al azar un ítem del catálogo que tenga la rareza indicada (para resolver drops de /travel).
    // Devuelve null si todavía no hay ítems cargados para esa rareza.
    Task<Item?> GetRandomByRarityAsync(string rarity, CancellationToken cancellationToken = default);

    // Igual que el anterior, pero además filtra por tipo (ej. "Madera" para /chop, "Mineral" para /mine).
    Task<Item?> GetRandomByTypeAndRarityAsync(string type, string rarity, CancellationToken cancellationToken = default);

    // Búsqueda por nombre sin distinguir mayúsculas (usada por /shop, /craft y /equip,
    // donde el nombre lo escribe el usuario). Devuelve null si no existe.
    Task<Item?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    // Devuelve null si el item_id no existe (ej. un weapon_id/amulet_id huérfano).
    Task<Item?> GetByIdAsync(int itemId, CancellationToken cancellationToken = default);

    // Catálogo completo de un tipo (ej. "Consumable" para /shop view). Lista vacía si no hay ninguno.
    Task<IReadOnlyList<Item>> GetAllByTypeAsync(string type, CancellationToken cancellationToken = default);
}
