using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// La Arena (torneo diario). La llave y las peleas las resuelve Services/ArenaService.cs con lógica pura; acá solo se guarda: quién se anotó,
// el cierre del día y el premio. Todos los "día" son el día de Uruguay (GameData/ArenaRules.DayOf).
public interface IArenaRepository
{
    // Anota al jugador en el torneo de ese día (lo crea si es el primero; channelId = dónde se anotó, para anunciar el resultado ahí).
    // Una transacción con el día bloqueado: dos anotados a la vez no pasan del cupo.
    Task<ArenaJoinStatus> JoinAsync(
        DateOnly day, ulong discordId, string displayName, ulong channelId, int maxPlayers, CancellationToken cancellationToken = default);

    // Los anotados en orden de llegada, con su clase y nivel actuales.
    Task<IReadOnlyList<ArenaEntry>> GetEntriesAsync(DateOnly day, CancellationToken cancellationToken = default);

    Task<ArenaDayInfo?> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default);

    // El último torneo que se jugó (null si todavía no hubo ninguno).
    Task<ArenaDayInfo?> GetLatestResolvedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArenaMatchRow>> GetMatchesAsync(DateOnly day, CancellationToken cancellationToken = default);

    // Los días anteriores a "today" que siguen abiertos: los que ya hay que jugar (de a uno por día, el más viejo primero).
    Task<IReadOnlyList<DateOnly>> GetOpenDaysBeforeAsync(DateOnly today, CancellationToken cancellationToken = default);

    // Cierra el día sin jugarlo (menos de 2 anotados). False si ya no estaba abierto.
    Task<bool> CancelDayAsync(DateOnly day, int participants, CancellationToken cancellationToken = default);

    // Cierra el día jugado: guarda la llave y paga el premio al campeón, TODO en una transacción. El cierre es un UPDATE ... WHERE status = 'open'
    // (si dos procesos intentan cerrar el mismo día, solo uno lo consigue y el otro no paga nada). zoneRank: la zona del campeón (el premio escala).
    Task<ArenaResolveOutcome> ResolveDayAsync(
        DateOnly day, IReadOnlyList<ArenaMatchRow> matches, int participants, int rounds, ulong winnerId, string winnerName,
        RewardSpec reward, int zoneRank, CancellationToken cancellationToken = default);
}
