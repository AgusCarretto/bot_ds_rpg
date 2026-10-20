using BotDsRpg.GameData;
using BotDsRpg.Repositories;

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
    // set: un banquete comido en plena pelea lo recalcula (ver RaidModule.ExecuteEatAsync).
    public required int Damage { get; set; }

    // El +% de ataque de banquete que YA tiene aplicado en Damage (el que traía activo al unirse, o el del banquete que comió en la pelea): sirve para que un segundo banquete no apile +15 % sobre +15 %.
    public int AttackBuffPercent { get; set; }
    public required int Defense { get; init; }
    public required int Level { get; init; }
    public required string PlayerClass { get; init; } // qué habilidad activa le toca, ver GameData/ClassAbilities.cs
    public required ClassPassiveProfile Passives { get; init; }
    public required int MaxHp { get; init; } // ya escalado por Passives.MaxHpMultiplier, ver PlayerCombatProfileCalculator
    public required int StartingHp { get; init; } // HP de combate al unirse — nunca cambia, es la base del delta al persistir
    public int CurrentHp { get; set; }

    // Todo lo permanente del jugador (mascotas, Fuego Nuevo, bendiciones) al unirse (como el banquete, queda fijo para ese raid): ataque, defensa y vida ya están dentro de Damage/Defense/MaxHp; el oro y la EXP se aplican al cobrar. Null = nada.
    public PlayerBonuses? Bonuses { get; init; }

    // Estado de la habilidad de clase de ESTE participante (cada uno tiene el suyo, se actualiza
    // bajo RaidSession.Lock junto con el resto) — ver GameData/CombatTurnResolver.cs.
    public AbilityState Ability { get; set; }

    public bool HasFled { get; set; }
    public bool Contributed { get; set; } // pegó al menos un golpe — si el jefe cae, esto decide si cobra recompensa aunque ya lo hayan derribado antes
    public int TurnsTaken { get; set; }

    // Turnos seguidos que dejó vencer sin jugar (v0.19.0, ver RaidModule.OnTurnTimeoutAsync): el primero pierde el turno, el segundo lo retira del combate. Jugar lo pone en 0.
    public int MissedTurns { get; set; }

    // Comidas que se comió en esta pelea (RaidSettings.MealsPerFight por jugador, v0.19.0): comer gasta el turno y cura, como en /travel y /boss.
    public int MealsEaten { get; set; }
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

// A quién se invitó a pelear el jefe en equipo (v0.19.0: "/boss @amigo", ver RaidModule.PreparePartyAsync): solo ellos pueden aceptar. El orden de la lista es el de las menciones y
// después define el orden de los turnos.
public sealed record RaidInvite(ulong DiscordId, string DisplayName);

// Lo que el reloj de turnos necesita para actuar solo (nadie hizo clic, así que no hay ningún módulo vivo con sus repositorios). Lo deja la sesión al armarse (null = sin reloj:
// las pruebas que arman sesiones a mano no lo tienen y no quedan temporizadores sueltos).
public sealed record RaidTurnContext(IUserRepository Users, IGameEvents Events, IRaidSessionService Raids);

// Jefe de zona en equipo ("/boss @amigo", antes "/raid"): varios RaidParticipant comparten el HP del jefe (BossCurrentHp) y pegan POR TURNOS — pega uno, el jefe le responde a ese
// jugador, y le toca al siguiente (v0.19.0; antes cada uno pegaba cuando quería). Un clic fuera de turno se rechaza dentro del lock (ver Modules/RaidModule.cs). BossDamage se
// sortea UNA sola vez al arrancar (igual que CombatState.MonsterDamage en combate solitario) y se reusa en cada contraataque.
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

    // El canal donde se armó el lobby (0 = no se sabe): los recordatorios de cooldown de TODOS los participantes se avisan ahí (Services/ReminderService.cs); solo el que pega el último golpe
    // tiene una interacción propia al terminar, así que los demás no se podrían enterar de otra forma.
    public ulong ChannelId { get; set; }

    public int BossCurrentHp { get; set; }
    public RaidPhase Phase { get; set; } = RaidPhase.Lobby;

    // Los invitados (sin contar a quien arrancó) y quienes ya dijeron que no. Quien está en Invited y no figura en Participants ni en Declined sigue "pendiente". Vacío = sesión armada
    // sin invitaciones (las pruebas): el orden de los turnos es el de Participants.
    public List<RaidInvite> Invited { get; } = [];
    public HashSet<ulong> Declined { get; } = [];

    // La zona máxima (por dificultad, ver ZoneRanking.MaxUnlockedRank) de quien arrancó: todos los del grupo tienen que tener la MISMA, también al aceptar (0 = no se exige).
    public int MaxZoneRank { get; set; }

    // Turnos (v0.19.0). TurnIndex es la posición en Participants de a quién le toca; TurnNumber sube con CADA cambio de turno, y el temporizador de un turno viejo lo ve distinto y se
    // descarta solo. Todo se toca bajo Lock.
    public int TurnIndex { get; set; }
    public int TurnNumber { get; set; }
    public CancellationTokenSource? TurnCts { get; set; }

    // Número de turno (TurnNumber) en el que quien tiene el turno está COMIENDO (gastando la comida en la base, que es I/O y no puede ir adentro del lock): mientras valga lo mismo que TurnNumber, ni su
    // ataque ni el reloj de turnos tocan ese turno. 0 = nadie está comiendo.
    public int EatingTurnNumber { get; set; }
    public RaidTurnContext? Turns { get; set; }

    // Cómo borrar el último aviso de turno ("🎯 @fulano ¡es tu turno!") que el bot mandó: cada turno nuevo borra el anterior, así el canal no se llena (ver RaidModule.PingTurnAsync).
    public Func<Task>? TurnPingDeleter { get; set; }

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

    // true si el jugador está en un jefe en equipo que YA ESTÁ PELEANDO (no en la sala de espera de las invitaciones). Es la «pelea en curso» que bloquea /use, /heal y la taberna: ahí se come con el botón del combate.
    bool IsInActiveFight(ulong discordId);

    // El id del raid en el que está el jugador (lobby o pelea activa), o null si no está en ninguno. Lo usan "aa attack", "aa ability" y "aa flee" (no hay botón que lleve el id).
    Guid? RaidIdOf(ulong discordId);

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
