using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IUserRepository
{
    // Devuelve null si el jugador todavía no existe en la base.
    Task<User?> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Devuelve el jugador existente o lo crea con los valores por defecto
    // (Nivel 1, 0 EXP, 50 de oro, 100/100 HP, clase indicada o "Guerrero" por defecto).
    Task<User> GetOrCreateUserAsync(ulong discordId, string? chosenClass = null, CancellationToken cancellationToken = default);

    // Crea al jugador con la clase elegida si no existe, o se la actualiza si ya existe
    // (permite re-elegir clase desde /class en cualquier momento).
    Task<User> SetClassAsync(ulong discordId, string className, CancellationToken cancellationToken = default);

    // Gasta oro para restaurar HP (tope: max_hp). Devuelve null si no le alcanza el oro
    // (no aplica ningún cambio en ese caso). Bloqueado durante combate por TavernModule
    // (no se puede gastar oro en pleno combate, ver /use para curación desde el inventario).
    Task<User?> HealAsync(ulong discordId, int goldCost, int hpRestored, CancellationToken cancellationToken = default);

    // Restaura HP sin costo de oro (tope: max_hp) — usado por /use fuera de combate, el consumible
    // ya se descontó del inventario antes de llamar acá (ver IInventoryRepository.TryConsumeAsync).
    Task<User> RestoreHpAsync(ulong discordId, int hpRestored, CancellationToken cancellationToken = default);

    // Equipa un arma/amuleto ya validado como poseído por el llamador (EquipModule verifica el
    // inventario antes de llamar). No descuenta nada del inventario, solo actualiza el puntero.
    Task<User> EquipWeaponAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);
    Task<User> EquipAmuletAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);

    // Top jugadores por progreso (nivel, y XP dentro del nivel actual como desempate).
    // "xp" es el progreso hacia el próximo nivel (resetea al subir), no un total histórico,
    // por eso el orden real es por nivel primero.
    Task<IReadOnlyList<LeaderboardEntry>> GetTopPlayersAsync(int limit, CancellationToken cancellationToken = default);

    // Aplica el delta neto de HP (daño - curación) acumulado en memoria durante un combate por
    // turnos (huida, derrota o timeout: ver CombatState.PlayerStartingHp) sobre el HP REAL más
    // reciente en base, no sobre un snapshot — así una curación con /use a mitad de combate no se
    // pierde si el HP en base cambió por otra vía mientras la pelea seguía abierta. Transaccional
    // con FOR UPDATE para que no se pise con una operación concurrente sobre la misma fila.
    Task<User> ApplyCombatHpDeltaAsync(ulong discordId, int hpDelta, CancellationToken cancellationToken = default);
}
