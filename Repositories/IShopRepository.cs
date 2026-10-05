using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Resultado de una compra con cooldown (BuyItemWithCooldownAsync): Buyer con valor = se compró; CooldownRemaining con valor = todavía
// no se puede comprar (falta ese tiempo); los dos null = no le alcanzaba el oro. Nunca vienen los dos con valor.
public sealed record CooldownBuyOutcome(User? Buyer, TimeSpan? CooldownRemaining);

public interface IShopRepository
{
    // Descuenta el oro y suma el ítem al inventario de forma atómica. Devuelve null si no
    // le alcanza el oro (no aplica ningún cambio en ese caso). Precondición: el usuario ya
    // debe existir (llamar antes a IUserRepository.GetOrCreateUserAsync).
    Task<User?> BuyItemAsync(ulong discordId, int itemId, int quantity, int totalCost, CancellationToken cancellationToken = default);

    // Igual que BuyItemAsync, pero la compra tiene un cooldown (hoy: las cajas, una cada 2 horas). Todo en UNA transacción: se reclama el
    // cooldown con la guarda de siempre (CooldownGuard), se descuenta el oro y se suma el ítem; si el cooldown sigue vigente o no
    // alcanza el oro, se revierte todo, así que una compra fallida NUNCA gasta el cooldown, y dos compras simultáneas no pasan las dos.
    Task<CooldownBuyOutcome> BuyItemWithCooldownAsync(
        ulong discordId, int itemId, int quantity, int totalCost, string cooldownCommand, TimeSpan cooldownDuration,
        CancellationToken cancellationToken = default);

    // Descuenta la cantidad del inventario y suma el oro de forma atómica (a razón de
    // items.sell_price, calculado por el llamador). Devuelve null si no tiene esa cantidad
    // (no aplica ningún cambio en ese caso).
    Task<User?> SellItemAsync(ulong discordId, int itemId, int quantity, int totalRefund, CancellationToken cancellationToken = default);

    // Vende el arma o el amuleto EQUIPADO: itemId tiene que ser justo lo que tiene puesto. Lo desequipa y le paga el reembolso en UNA sola
    // sentencia (así un doble click no paga dos veces). Devuelve null si ese ítem no es lo que tiene equipado.
    Task<User?> SellEquippedAsync(ulong discordId, int itemId, int refund, CancellationToken cancellationToken = default);

    // Vende TODO el inventario del jugador de una sola vez (no toca lo equipado). Devuelve null si no tenía nada para vender.
    Task<SellAllOutcome?> SellAllAsync(ulong discordId, CancellationToken cancellationToken = default);
}
