namespace BotDsRpg.Repositories;

public interface IGameEventRepository
{
    // Registra el evento en game_events y suma "amount" al contador del jugador (player_stats) en UNA transacción, así
    // los dos nunca quedan desfasados. Devuelve el valor NUEVO del contador (lo que leen las misiones y los logros).
    // Precondición: el jugador existe (player_stats tiene FK a users).
    Task<long> RecordAsync(
        ulong discordId, string kind, int? zoneId, long amount, string? detail, CancellationToken cancellationToken = default);

    // Todos los contadores de un jugador (clave -> valor). Vacío si todavía no tiene ninguno.
    Task<IReadOnlyDictionary<string, long>> GetStatsAsync(ulong discordId, CancellationToken cancellationToken = default);
}
