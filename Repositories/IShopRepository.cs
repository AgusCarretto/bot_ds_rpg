using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IShopRepository
{
    // Descuenta el oro y suma el ítem al inventario de forma atómica. Devuelve null si no
    // le alcanza el oro (no aplica ningún cambio en ese caso). Precondición: el usuario ya
    // debe existir (llamar antes a IUserRepository.GetOrCreateUserAsync).
    Task<User?> BuyItemAsync(ulong discordId, int itemId, int quantity, int totalCost, CancellationToken cancellationToken = default);

    // Descuenta la cantidad del inventario y suma el oro de forma atómica (a razón de
    // items.sell_price, calculado por el llamador). Devuelve null si no tiene esa cantidad
    // (no aplica ningún cambio en ese caso).
    Task<User?> SellItemAsync(ulong discordId, int itemId, int quantity, int totalRefund, CancellationToken cancellationToken = default);

    // Vende TODO el inventario del jugador de una sola vez. Devuelve null si no tenía nada para vender.
    Task<SellAllOutcome?> SellAllAsync(ulong discordId, CancellationToken cancellationToken = default);
}
