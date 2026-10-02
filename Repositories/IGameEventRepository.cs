using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IGameEventRepository
{
    // Registra el evento en game_events y suma "amount" al contador del jugador (player_stats) en UNA transacción, así
    // los dos nunca quedan desfasados. Devuelve el valor NUEVO del contador (lo que leen las misiones y los logros).
    // Precondición: el jugador existe (player_stats tiene FK a users).
    Task<long> RecordAsync(
        ulong discordId, string kind, int? zoneId, long amount, string? detail, CancellationToken cancellationToken = default);

    // Cuántas veces pasó cada tipo de evento y cuánto sumó, separado por detalle: (kind, detail) -> cantidad de eventos y suma de amount. Es lo que
    // lee el historial por juego. Solo los tipos pedidos.
    Task<IReadOnlyList<EventTotal>> GetBreakdownAsync(ulong discordId, IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default);

    // Los últimos eventos de esos tipos, del más nuevo al más viejo.
    Task<IReadOnlyList<RecentEvent>> GetRecentAsync(ulong discordId, IReadOnlyCollection<string> kinds, int limit, CancellationToken cancellationToken = default);

    // Todos los contadores de un jugador (clave -> valor). Vacío si todavía no tiene ninguno.
    Task<IReadOnlyDictionary<string, long>> GetStatsAsync(ulong discordId, CancellationToken cancellationToken = default);
}
