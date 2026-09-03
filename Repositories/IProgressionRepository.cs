using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Progresión del jugador (XP/nivelado y racha diaria): separado de IUserRepository para que ese
// no siga absorbiendo cada mecánica nueva que toque la fila de "users". Precondición común a
// ambos métodos: el usuario ya debe existir (llamar antes a IUserRepository.GetOrCreateUserAsync).
public interface IProgressionRepository
{
    // Suma XP y aplica la fórmula de nivelado (puede subir varios niveles de una), devolviendo
    // el jugador actualizado y cuántos niveles subió. Al subir de nivel, max_hp +15 y cura al máximo.
    Task<LevelUpOutcome> AddXpAsync(ulong discordId, int xpGained, CancellationToken cancellationToken = default);

    // Evalúa y aplica /daily de forma atómica (ver GameData/DailyRewardCalculator): si todavía
    // no pasaron 24h, no aplica ningún cambio y Result queda en null.
    Task<DailyClaimOutcome> ClaimDailyAsync(ulong discordId, CancellationToken cancellationToken = default);
}
