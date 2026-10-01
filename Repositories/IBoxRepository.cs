using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

// El oro del jugador DESPUÉS de abrir la caja.
public sealed record BoxOpenOutcome(int GoldAfter);

public interface IBoxRepository
{
    // La caja con todo su botín, o null si ese ítem no es una caja (no está en la tabla boxes).
    Task<BoxDefinition?> GetDefinitionAsync(int boxItemId, CancellationToken cancellationToken = default);

    // Abre UNA caja de forma atómica: gasta la caja del inventario (con guarda: si no tiene, no pasa nada) y, en la MISMA
    // transacción, suma el oro y los ítems que salieron. Devuelve null si el jugador no tenía esa caja. Así una caja nunca se
    // gasta sin dar su botín ni da botín sin gastarse, aunque se abra con doble click o dos comandos al mismo tiempo.
    Task<BoxOpenOutcome?> OpenAsync(ulong discordId, int boxItemId, BoxLootResult loot, CancellationToken cancellationToken = default);
}
