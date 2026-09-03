using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IInventoryRepository
{
    // Lista vacía si el jugador todavía no tiene ningún ítem.
    Task<IReadOnlyList<InventoryEntry>> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Cuánto tiene el jugador de un ítem puntual (0 si no lo tiene, no null).
    Task<int> GetQuantityAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);

    // Ítems en inventario filtrados por tipo, con el Item completo resuelto y ordenados por
    // buy_price ascendente — para /heal, que necesita StatValue de cada uno para elegir
    // automáticamente el consumible más barato que el jugador tenga (no desperdiciar uno caro
    // en una curación chica). Lista vacía si no tiene ninguno de ese tipo.
    Task<IReadOnlyList<OwnedItem>> GetOwnedByTypeAsync(ulong discordId, string type, CancellationToken cancellationToken = default);

    // Descuenta "quantity" de un ítem de forma atómica (guarda: si no tiene esa cantidad, no
    // aplica ningún cambio y devuelve false). Limpia la fila si llega a 0. Usado por /use para
    // gastar un consumible del inventario.
    Task<bool> TryConsumeAsync(ulong discordId, int itemId, int quantity, CancellationToken cancellationToken = default);
}
