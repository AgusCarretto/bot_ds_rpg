using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Lo que dejó un desmantelado: el Polvo que tiene ahora y cuántas unidades le quedan de ese ítem (0 = se acabó).
public sealed record DismantleOutcome(int DustAfter, int QuantityLeft);

public enum DustSpendStatus { Ok, NoAccount, NotEnoughDust }

// Lo que dejó una compra con Polvo: el Polvo que le queda (solo con Ok).
public sealed record DustSpendOutcome(DustSpendStatus Status, int DustAfter);

public enum EnchantStatus
{
    Ok,
    NoPlayer,
    NoGear,          // no tiene puesta la pieza de ese lado (arma o amuleto)
    NotEnoughGold,
    NotEnoughDust,
    NeedsConfirm,    // pidió OTRO efecto que el que tiene la pieza y esa pieza ya tiene un tier: cambiar lo reemplaza, así que hace falta confirmar (no se cobró nada)
}

// PreviousTier: el tier que tenía antes; RolledTier: el que salió en el sorteo; NewTier: con el que queda (el mayor de los dos: nunca baja).
// User: el jugador después de pagar (solo si Status == Ok).
// PreviousKind / NewKind (v0.22.0): el efecto que tenía la pieza y el que quedó (GameData/Enchantments.cs).
public sealed record EnchantOutcome(EnchantStatus Status, int PreviousTier, int RolledTier, int NewTier, User? User, string? PreviousKind = null, string? NewKind = null);

// El Polvo (GameData/Dismantling.cs y GameData/Enchantments.cs): desmantelar materiales para juntarlo y gastarlo en encantar. Las dos operaciones son UNA transacción
// con guardas (nada se descuenta si la precondición ya no vale), así que dos clicks simultáneos no gastan lo que no hay.
public interface IDustRepository
{
    // Cuántas unidades del ítem tiene el jugador (0 si no tiene): para "desmantelar todo". Es solo lectura; la resta de DismantleAsync sigue siendo guardada.
    Task<int> GetQuantityAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);

    // Rompe `quantity` unidades del ítem y suma `dust` de Polvo. null si ya no tiene esa cantidad (no se toca nada).
    Task<DismantleOutcome?> DismantleAsync(ulong discordId, int itemId, int quantity, int dust, CancellationToken cancellationToken = default);

    // Compra con Polvo (el Tabernero del Inframundo, GameData/DeepShop.cs): cobra `dustCost` y suma `quantity` unidades del ítem en UNA transacción. La resta es guardada
    // (WHERE dust >= costo): dos compras a la vez con Polvo para una sola pagan una sola, y si no alcanza no se toca nada.
    Task<DustSpendOutcome> BuyWithDustAsync(ulong discordId, int itemId, int quantity, int dustCost, CancellationToken cancellationToken = default);

    // Un intento de encantamiento: cobra oro y Polvo SIEMPRE (salga lo que salga) y deja el mayor entre el tier que tenía y el que salió (rolledTier). slot: "weapon" o "amulet".
    Task<EnchantOutcome> TryEnchantAsync(
        ulong discordId, string slot, int rolledTier, int goldCost, int dustCost, string? kind = null, bool allowSwitch = false, CancellationToken cancellationToken = default);
}
