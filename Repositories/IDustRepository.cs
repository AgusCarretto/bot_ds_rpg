using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Lo que dejó un desmantelado: el Polvo que tiene ahora y cuántas unidades le quedan de ese ítem (0 = se acabó).
public sealed record DismantleOutcome(int DustAfter, int QuantityLeft);

public enum EnchantStatus
{
    Ok,
    NoPlayer,
    NoGear,          // no tiene puesta la pieza de ese lado (arma o amuleto)
    NotEnoughGold,
    NotEnoughDust,
}

// PreviousTier: el tier que tenía antes; RolledTier: el que salió en el sorteo; NewTier: con el que queda (el mayor de los dos: nunca baja).
// User: el jugador después de pagar (solo si Status == Ok).
public sealed record EnchantOutcome(EnchantStatus Status, int PreviousTier, int RolledTier, int NewTier, User? User);

// El Polvo (GameData/Dismantling.cs y GameData/Enchantments.cs): desmantelar materiales para juntarlo y gastarlo en encantar. Las dos operaciones son UNA transacción
// con guardas (nada se descuenta si la precondición ya no vale), así que dos clicks simultáneos no gastan lo que no hay.
public interface IDustRepository
{
    // Rompe `quantity` unidades del ítem y suma `dust` de Polvo. null si ya no tiene esa cantidad (no se toca nada).
    Task<DismantleOutcome?> DismantleAsync(ulong discordId, int itemId, int quantity, int dust, CancellationToken cancellationToken = default);

    // Un intento de encantamiento: cobra oro y Polvo SIEMPRE (salga lo que salga) y deja el mayor entre el tier que tenía y el que salió (rolledTier). slot: "weapon" o "amulet".
    Task<EnchantOutcome> TryEnchantAsync(ulong discordId, string slot, int rolledTier, int goldCost, int dustCost, CancellationToken cancellationToken = default);
}
