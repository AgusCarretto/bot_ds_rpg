using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// Activating: transitoria, entre Lobby y Active, mientras se reclaman los cooldowns de cada
// participante (I/O, fuera del lock). Existe para que "Empezar ya" y el timeout del lobby, si
// coinciden, no ejecuten la activación dos veces en paralelo (la segunda fallaría al reclamar
// cooldowns ya reclamados y cancelaría un raid que estaba arrancando bien).
public enum RaidPhase { Lobby, Activating, Active, Resolved }

// Estado de combate de un participante dentro de un jefe cooperativo — equivalente a lo que
// CombatState guarda para un combate solitario, pero mutable: a diferencia del combate 1 jugador
// (donde CombatSessionService reemplaza el estado entero de forma atómica en cada turno, ver
// TryAdvance), acá varios jugadores mutan el MISMO RaidSession en paralelo, así que cada campo que
// cambia durante la pelea se actualiza en el lugar bajo el lock de RaidSession.Lock.
public sealed class RaidParticipant
{
    public required ulong DiscordId { get; init; }
    public required string DisplayName { get; init; }
    public required int Damage { get; init; }
    public required int Defense { get; init; }
    public required int Level { get; init; }
    public required string PlayerClass { get; init; } // qué habilidad activa le toca, ver GameData/ClassAbilities.cs
    public required ClassPassiveProfile Passives { get; init; }
    public required int MaxHp { get; init; } // ya escalado por Passives.MaxHpMultiplier, ver PlayerCombatProfileCalculator
    public required int StartingHp { get; init; } // HP de combate al unirse — nunca cambia, es la base del delta al persistir
    public int CurrentHp { get; set; }

    // Estado de la habilidad de clase de ESTE participante (cada uno tiene el suyo, se actualiza
    // bajo RaidSession.Lock junto con el resto) — ver GameData/CombatTurnResolver.cs.
    public AbilityState Ability { get; set; }

    public bool HasFled { get; set; }
    public bool Contributed { get; set; } // pegó al menos un golpe — si el jefe cae, esto decide si cobra recompensa aunque ya lo hayan derribado antes
    public int TurnsTaken { get; set; }
    public int DodgeCount { get; set; }
    public int CritCount { get; set; }
    public int TotalDamageDealt { get; set; }
    public int TotalDamageTaken { get; set; }

    public bool IsKnockedOut => CurrentHp <= 0;
    public bool IsActive => !HasFled && !IsKnockedOut;

    // Convierte un delta de HP de combate (posiblemente escalado, ej. Guerrero ×1.2) a unidades
    // reales de base — mismo criterio que CombatState.ToDbHpDelta.
    public int ToDbHpDelta(int combatHpDelta) => (int)Math.Round(combatHpDelta / Passives.MaxHpMultiplier);
}

// Jefe de zona cooperativo: varios RaidParticipant comparten el HP del jefe (BossCurrentHp), pero
// cada uno pelea "en paralelo" a su propio ritmo — un click de Atacar resuelve el turno de ESE
// jugador nomás, nunca espera a los demás (ver Modules/RaidModule.cs). BossDamage se sortea UNA
// sola vez al arrancar (igual que CombatState.MonsterDamage en combate solitario) y se reusa en
// cada contraataque.
public sealed class RaidSession
{
    public required Guid RaidId { get; init; }
    public required string BossName { get; init; }
    public required string BossEmoji { get; init; }
    // La cara del jefe (emoji de la aplicación, ver MonsterTemplate.Portrait): miniatura de los mensajes del raid. Null = sin cara.
    public string? BossPortrait { get; init; }
    // BossBaseHp: HP sorteado del jefe SIN escalar (tal cual está en la base). BossMaxHp es el HP real
    // del raid, que depende de cuántos jugadores terminan entrando: se calcula de nuevo al arrancar
    // (RaidModule.TryActivateAsync, con la lista ya definitiva) — por eso tiene setter. Mientras el
    // lobby está abierto, BossMaxHp es el valor para 1 jugador y el embed del lobby no lo muestra.
    public required int BossBaseHp { get; init; }

    // Posición de la zona del jefe por dificultad (1 = la primera): los multiplicadores del raid dependen de
    // ella (GameData/RaidDifficulty.cs), y hay que recalcular el HP con la MISMA posición al arrancar.
    public required int ZoneRank { get; init; }
    public required int BossMaxHp { get; set; }
    public required int BossDamage { get; init; }
    public required IReadOnlyList<string> BossDropItemNames { get; init; }
    public required int BossGoldBonus { get; init; }
    public required int BossXpBonus { get; init; }
    public required int ZoneId { get; init; }
    public required string ZoneName { get; init; }
    public required ulong StarterId { get; init; }

    // No "required": Modules/RaidModule.cs arma la sesión ANTES de tener el mensaje a editar en el
    // caso de "aa raid" (necesita el embed para mandar el mensaje, y recién ahí puede armar el
    // reply target) — se completa siempre ANTES de agregar la sesión al servicio (IRaidSessionService.
    // TryAdd), nunca queda sin asignar mientras el raid está en uso.
    public ICombatReplyTarget ReplyTarget { get; set; } = null!;

    public int BossCurrentHp { get; set; }
    public RaidPhase Phase { get; set; } = RaidPhase.Lobby;

    // Guarda el orden de llegada (Dictionary alcanza para lookup, pero la UI quiere mostrarlos en
    // el orden en que se sumaron).
    public List<RaidParticipant> Participants { get; } = [];

    // Sección crítica: TODA mutación sobre BossCurrentHp/Phase/Participants (incluyendo el HP de
    // cada participante) tiene que tomar este lock primero. Un solo lock para todo el raid en vez
    // de uno por campo: las operaciones son cortas (matemática en memoria), no vale la pena el
    // riesgo de bugs sutiles de locking fino por la ganancia de concurrencia que daría.
    public object Lock { get; } = new();

    public CancellationTokenSource? TimeoutCts { get; set; }
}

public interface IRaidSessionService
{
    // true si el jugador ya está en CUALQUIER raid (lobby o pelea activa) — también lo consultan
    // /hunt, /travel y /boss solitario (Services/AdventureCombatStarter.cs) antes de arrancar, así
    // nadie puede estar en dos combates a la vez.
    bool IsInAnyRaid(ulong discordId);

    // Registra a un jugador como ocupado en ese raid. false si ya estaba en otro (guarda atómica
    // final, aunque el llamador ya haya chequeado IsInAnyRaid antes).
    bool TryRegisterParticipant(ulong discordId, Guid raidId);

    // Libera a UN jugador puntual (huyó, o fue expulsado del lobby por no alcanzarle el cooldown)
    // sin tocar el resto del raid.
    void UnregisterParticipant(ulong discordId);

    // Agrega el raid al índice principal. false si por alguna razón ya existía ese Guid (no
    // debería pasar nunca, Guid.NewGuid() es efectivamente único).
    bool TryAdd(RaidSession session);

    RaidSession? Peek(Guid raidId);

    // Termina el raid: lo saca del índice principal y libera a TODOS los jugadores registrados en
    // él (según el índice jugador -> raid, no según session.Participants). Se llama al resolver
    // (victoria/wipe/abandono/lobby cancelado).
    void Remove(Guid raidId);
}
