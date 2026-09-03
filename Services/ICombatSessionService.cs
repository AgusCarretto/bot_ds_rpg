using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// Estado de un combate por turnos en curso. Vive solo en memoria (ConcurrentDictionary en
// CombatSessionService) — la base de datos recién se toca cuando el combate termina.
public sealed record CombatState(
    string CommandName, // "hunt" o "travel": define qué tabla de recompensas aplica al ganar
    string MonsterName,
    string MonsterEmoji,
    int MonsterMaxHp,
    int MonsterCurrentHp,
    int MonsterDamage,
    // Nombres exactos (tabla items) de lo que este monstruo puede soltar al ganar — vacío para
    // /travel, que todavía resuelve su drop por rareza sorteada. Ver GameData/MonsterCatalog.cs y
    // Modules/AdventureModule.ResolveDroppedItemAsync.
    IReadOnlyList<string> MonsterDropItemNames,
    // PlayerMaxHp/PlayerCurrentHp/PlayerStartingHp ya vienen escalados por Passives.MaxHpMultiplier
    // (Guerrero ×1.2) si corresponde — ver Services/AdventureCombatStarter.cs y ToDbHpDelta abajo.
    int PlayerMaxHp,
    int PlayerCurrentHp,
    int PlayerStartingHp, // HP (ya escalado) que tenía al arrancar el combate: nunca cambia durante la pelea,
                          // es la base contra la que se calcula el delta de HP al resolver (ver ToDbHpDelta)
    int PlayerDamage,
    int PlayerDefense,
    int PlayerLevel,
    ClassPassiveProfile Passives, // pasivas de la clase del jugador, resueltas una sola vez al iniciar el combate (ver GameData/ClassPassives.cs)
    // Acumulados de toda la pelea (no de este turno), para el resumen final al terminar el
    // combate (victoria, derrota o huida) — ver Modules/AdventureModule.BuildCombatSummaryLine.
    // Con valor por defecto para no tener que tocar la construcción inicial en AdventureCombatStarter.
    int TurnsElapsed = 0,
    int DodgeCount = 0,
    int CritCount = 0,
    int TotalDamageDealt = 0,
    int TotalDamageTaken = 0,
    int TotalHealed = 0)
{
    // Convierte un delta de HP en unidades de COMBATE (ya escaladas por Passives.MaxHpMultiplier)
    // a unidades reales de base de datos, para pasarlo a IUserRepository.ApplyCombatHpDeltaAsync /
    // IAdventureRepository.ApplyVictoryAsync. Con multiplicador 1.0 (cualquier clase sin este
    // pasivo) es un no-op. Sin esto, un Guerrero con HP de combate más alto persistiría un delta
    // "inflado" que no corresponde a su HP real en la base.
    public int ToDbHpDelta(int combatHpDelta) => (int)Math.Round(combatHpDelta / Passives.MaxHpMultiplier);
}

// ReplyTarget: dónde editar el mensaje del combate más adelante (ver ICombatReplyTarget).
// TimeoutCts: se cancela y se reemplaza en cada turno para "reiniciar el reloj" de 30s.
public sealed class CombatSession
{
    public required CombatState State { get; init; }
    public required ICombatReplyTarget ReplyTarget { get; init; }
    public required CancellationTokenSource TimeoutCts { get; init; }
}

public interface ICombatSessionService
{
    // false si el jugador ya tiene un combate en curso (no se pisa el existente).
    bool TryStart(ulong discordId, CombatState state, ICombatReplyTarget replyTarget);

    // Lectura sin efectos secundarios: null si no hay combate activo para ese jugador.
    CombatSession? Peek(ulong discordId);

    // Avanza (o termina) un combate de forma atómica: solo tiene efecto si "expectedSession" es
    // exactamente la sesión vigente (protege contra procesar el mismo turno dos veces si hay
    // doble click o una carrera contra el timeout). nextState/nextReplyTarget en null = termina
    // el combate (victoria, derrota o huida); no-null = el combate sigue con ese nuevo estado.
    bool TryAdvance(ulong discordId, CombatSession expectedSession, CombatState? nextState, ICombatReplyTarget? nextReplyTarget);
}
