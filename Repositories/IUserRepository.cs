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

    // Suma XP y aplica la fórmula de nivelado (puede subir varios niveles de una), devolviendo
    // el jugador actualizado y cuántos niveles subió. Al subir de nivel, max_hp +15 y cura al máximo.
    Task<LevelUpOutcome> AddXpAsync(ulong discordId, int xpGained, CancellationToken cancellationToken = default);

    // Gasta oro para restaurar HP (tope: max_hp). Devuelve null si no le alcanza el oro
    // (no aplica ningún cambio en ese caso).
    Task<User?> HealAsync(ulong discordId, int goldCost, int hpRestored, CancellationToken cancellationToken = default);

    // Equipa un arma/amuleto ya validado como poseído por el llamador (EquipModule verifica el
    // inventario antes de llamar). No descuenta nada del inventario, solo actualiza el puntero.
    Task<User> EquipWeaponAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);
    Task<User> EquipAmuletAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);

    // Evalúa y aplica /daily de forma atómica (ver GameData/DailyRewardCalculator): si todavía
    // no pasaron 24h, no aplica ningún cambio y Result queda en null.
    Task<DailyClaimOutcome> ClaimDailyAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Top jugadores por progreso (nivel, y XP dentro del nivel actual como desempate).
    // "xp" es el progreso hacia el próximo nivel (resetea al subir), no un total histórico,
    // por eso el orden real es por nivel primero.
    Task<IReadOnlyList<LeaderboardEntry>> GetTopPlayersAsync(int limit, CancellationToken cancellationToken = default);
}
