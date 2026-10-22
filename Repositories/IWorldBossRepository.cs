using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public enum WorldBossJoinStatus { Joined, AlreadyJoined, OnCooldown, NoAccount, Closed }

// CooldownLeft solo importa con OnCooldown: lo que falta del cooldown del jefe, que es el que se gasta al anotarse.
public sealed record WorldBossJoinOutcome(WorldBossJoinStatus Status, TimeSpan CooldownLeft);

public sealed record WorldBossEntry(ulong DiscordId, string DisplayName, DateTime JoinedAt);

public sealed record WorldBossDayInfo(
    DateOnly Day, string Status, ulong? ChannelId, string? BossKey, int Participants, double? TotalDamagePercent, bool? Defeated, int? Hunts, DateTime? ResolvedAt);

// Lo que cobró cada anotado al cerrar el día (Gold ya está en oro: cacerías × el oro de una cacería de SU zona).
public sealed record WorldBossPaid(ulong DiscordId, string Name, int ZoneRank, double DamagePercent, int Gold);

public sealed record WorldBossResolveOutcome(bool Resolved, IReadOnlyList<WorldBossPaid> Paid);

// El jefe mundial (GameData/WorldBoss.cs). La pelea la resuelve Services/WorldBossService.cs con lógica pura; acá solo se guarda: quién se anotó (gastando el cooldown del jefe), el cierre del día y el pago.
// Todos los «día» son el día de Uruguay (GameData/ArenaRules.DayOf).
public interface IWorldBossRepository
{
    // Anota al jugador en el jefe de ese día (lo crea si es el primero; channelId = dónde se anotó, para anunciar ahí). UNA transacción: comprueba la cuenta y que no esté anotado, gasta el cooldown del jefe
    // (la misma guarda de /boss: si todavía corre, no se anota y no se toca nada) e inserta la anotación. Diez clicks a la vez anotan una sola vez y gastan el cooldown una sola vez.
    Task<WorldBossJoinOutcome> JoinAsync(DateOnly day, ulong discordId, string displayName, ulong channelId, CancellationToken cancellationToken = default);

    // Los anotados en orden de llegada.
    Task<IReadOnlyList<WorldBossEntry>> GetEntriesAsync(DateOnly day, CancellationToken cancellationToken = default);

    Task<WorldBossDayInfo?> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default);

    // El último jefe que se peleó (null si todavía no hubo ninguno).
    Task<WorldBossDayInfo?> GetLatestResolvedAsync(CancellationToken cancellationToken = default);

    // Los días anteriores a hoy que siguen abiertos, del más viejo al más nuevo (el reloj los pelea en ese orden).
    Task<IReadOnlyList<DateOnly>> GetOpenDaysBeforeAsync(DateOnly today, CancellationToken cancellationToken = default);

    // Cierra sin pagar un día que quedó sin nadie a quien pagar (todos borraron su cuenta). False si ya no estaba abierto.
    Task<bool> CancelDayAsync(DateOnly day, CancellationToken cancellationToken = default);

    // Cierra el día y paga, TODO en una transacción: la guarda (solo cierra quien lo encuentra abierto, así el reloj y otro proceso no pagan dos veces), el pago de cada anotado en cacerías de oro de SU zona
    // (zoneRanks: el rango de zona de cada jugador) y lo que sacó cada uno. Si un jugador ya no tiene cuenta, se salta.
    Task<WorldBossResolveOutcome> ResolveDayAsync(
        DateOnly day, string bossKey, WorldBossOutcome outcome, IReadOnlyDictionary<ulong, int> zoneRanks, CancellationToken cancellationToken = default);
}
