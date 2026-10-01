using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Misiones diarias y semanales. El progreso NO se guarda: es la suma de lo registrado en game_events desde el inicio del período
// (el registro de eventos ES el progreso), y qué misiones tocan es una función pura (GameData/MissionCatalog.cs). Lo único que se
// escribe es qué ya se cobró (mission_claims).
public interface IMissionRepository
{
    // Cuánto lleva el jugador de cada tipo de evento desde "sinceUtc" (clave = el kind; los que no tienen eventos no aparecen).
    Task<IReadOnlyDictionary<string, long>> GetProgressAsync(
        ulong discordId, DateTime sinceUtc, IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default);

    // Las claves de las misiones (y '_bonus') que ya cobró en ese día/semana.
    Task<IReadOnlySet<string>> GetClaimedAsync(
        ulong discordId, MissionPeriod period, DateTime periodStartUtc, CancellationToken cancellationToken = default);

    // Cobra UNA misión: comprueba la meta con los eventos de la base, la marca cobrada y paga el premio, todo en una transacción
    // (la marca es un INSERT ... ON CONFLICT DO NOTHING, así que dos cobros simultáneos pagan una sola vez). zoneRank: la posición
    // de la zona del jugador (el premio escala con ella).
    Task<ClaimOutcome> ClaimMissionAsync(
        ulong discordId, MissionTemplate mission, DateTime periodStartUtc, DateTime periodEndUtc, int zoneRank,
        CancellationToken cancellationToken = default);

    // Cobra el premio por completar TODAS las misiones del período (hay que haber cobrado cada una antes).
    Task<ClaimOutcome> ClaimBonusAsync(
        ulong discordId, MissionPeriod period, IReadOnlyList<MissionTemplate> missions, DateTime periodStartUtc, DateTime periodEndUtc,
        int zoneRank, CancellationToken cancellationToken = default);
}
