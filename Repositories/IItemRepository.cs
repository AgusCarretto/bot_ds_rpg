using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IItemRepository
{
    // Elige al azar un ítem del catálogo de un tipo y rareza dados (ej. "Madera" para /chop,
    // "Mineral" para /mine, "Material" para el drop de rareza sorteada de /travel — ver
    // Modules/AdventureModule.ResolveDroppedItemAsync). Devuelve null si todavía no hay ítems
    // cargados para esa combinación tipo+rareza.
    Task<Item?> GetRandomByTypeAndRarityAsync(string type, string rarity, CancellationToken cancellationToken = default);

    // Búsqueda por nombre sin distinguir mayúsculas (usada por /shop, /craft y /equip,
    // donde el nombre lo escribe el usuario). Devuelve null si no existe.
    Task<Item?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    // Devuelve null si el item_id no existe (ej. un weapon_id/amulet_id huérfano).
    Task<Item?> GetByIdAsync(int itemId, CancellationToken cancellationToken = default);

    // Catálogo completo de un tipo (ej. "Consumable" para /shop view). Lista vacía si no hay ninguno.
    Task<IReadOnlyList<Item>> GetAllByTypeAsync(string type, CancellationToken cancellationToken = default);
}
