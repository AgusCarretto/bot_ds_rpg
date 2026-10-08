using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// El resultado de crear una cuenta con /start: el jugador y si en ESTA llamada se le entregó el kit inicial (GameData/StarterKit.cs; false si la cuenta ya existía).
public sealed record NewAccountResult(User Player, bool KitGranted);

public interface IUserRepository
{
    // Devuelve null si el jugador todavía no existe en la base.
    Task<User?> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Devuelve el jugador existente o lo crea con los valores por defecto
    // (Nivel 1, 0 EXP, 50 de oro, 100/100 HP, clase indicada o "Guerrero" por defecto).
    Task<User> GetOrCreateUserAsync(ulong discordId, string? chosenClass = null, CancellationToken cancellationToken = default);

    // Crea al jugador con la clase elegida si no existe, o se la actualiza si ya existe. Es lo que usa /start; desde la v0.12.0 /class NO la usa (ver TrySetClassWhileFreshAsync):
    // este método pisa la clase sin preguntar.
    Task<User> SetClassAsync(ulong discordId, string className, CancellationToken cancellationToken = default);

    // Lo que usa /start desde la v0.14.6: lo mismo que SetClassAsync (crea la cuenta con la clase elegida) y, SOLO si la fila de users es realmente nueva, le entrega el kit inicial
    // (GameData/StarterKit.cs) en la MISMA instrucción SQL: un doble clic o dos botones a la vez no lo duplican, y una cuenta que ya existía no lo vuelve a recibir.
    Task<NewAccountResult> CreateAccountAsync(ulong discordId, string className, CancellationToken cancellationToken = default);

    // Cambia la clase SOLO si el jugador está empezando de cero (nivel 1 y 0 de EXP, GameData/FuegoNuevoRules.CanChangeClass): un solo UPDATE con la condición adentro, así
    // nadie la cambia a mitad de una vuelta aunque un botón viejo siga en pantalla. null = no existe o ya no está en cero (el llamador lo explica). Con un Fuego Nuevo la clase se
    // vuelve a elegir adentro de la transacción del reinicio (IFuegoNuevoRepository.RenewAsync), no por acá.
    Task<User?> TrySetClassWhileFreshAsync(ulong discordId, string className, CancellationToken cancellationToken = default);

    // Restaura HP (tope: max_hp) — usado por /heal y /use fuera de combate; el consumible ya se
    // descontó del inventario antes de llamar acá (ver IInventoryRepository.TryConsumeAsync). Ya
    // no existe una curación "a oro" directa: siempre hace falta tener un Consumable comprado.
    Task<User> RestoreHpAsync(ulong discordId, int hpRestored, CancellationToken cancellationToken = default);

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

    // Cambia la zona actual del jugador (/zona ya validó min_level antes de llamar acá) — a partir
    // de esto, /hunt caza monstruos de la nueva zona (ver Repositories/IMonsterRepository.cs).
    // También lo saca del Fogón (users.in_gate = false): viajar a cualquier zona normal es salir de la puerta.
    Task<User> ChangeZoneAsync(ulong discordId, int zoneId, CancellationToken cancellationToken = default);

    // Lo para (true) o lo saca (false) de El Fogón Eterno (/zona 0). Su zona actual NO cambia: sigue siendo la última normal.
    Task<User> SetInGateAsync(ulong discordId, bool inGate, CancellationToken cancellationToken = default);

    // La penalidad por morir (GameData/DeathPenalty.cs): la EXP del nivel actual vuelve a 0 y se pierde el 5 % del oro de la billetera (el banco no se toca;
    // el nivel tampoco baja). Atómica. Devuelve lo que se perdió, o null si el jugador no existe.
    Task<DeathPenaltyOutcome?> ApplyDeathPenaltyAsync(ulong discordId, CancellationToken cancellationToken = default);
}
