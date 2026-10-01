using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Logros por tramos. El logro no tiene estado propio: "el contador de player_stats llegó a tal número" (GameData/AchievementCatalog.cs).
// Lo único que se escribe es qué tramos ya se cobraron (achievement_claims).
public interface IAchievementRepository
{
    // Los tramos cobrados de cada logro (clave del logro -> números de tramo 1..3). Los logros sin ninguno no aparecen.
    Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetClaimedTiersAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Cobra UN tramo: comprueba que el contador ya llegó, lo marca cobrado y paga el premio, todo en una transacción (la marca es
    // un INSERT ... ON CONFLICT DO NOTHING: dos cobros a la vez pagan una sola vez).
    Task<ClaimOutcome> ClaimTierAsync(
        ulong discordId, AchievementDefinition achievement, int tier, int zoneRank, CancellationToken cancellationToken = default);
}
