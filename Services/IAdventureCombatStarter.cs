using BotDsRpg.GameData;

namespace BotDsRpg.Services;

public enum CombatStartStatus { Started, AlreadyInCombat, OnCooldown, NoHp, RaceLost, NoMonstersInZone, NoBossInZone, NotLeveledForBoss }

// CooldownRemaining: solo si Status == OnCooldown. State: solo si Status == Started.
// RequiredLevel: solo si Status == NotLeveledForBoss (nivel mínimo de la PRÓXIMA zona, ver
// AdventureCombatStarter.PrepareBossAsync).
public sealed record CombatStartOutcome(CombatStartStatus Status, TimeSpan? CooldownRemaining, CombatState? State, int? RequiredLevel = null);

// Toda la lógica de negocio para arrancar un /hunt, /travel o /boss (validaciones, reclamo de
// cooldown, resolución de arma/sinergia/defensa, sorteo de monstruo) en un solo lugar, para que el
// módulo de slash commands y el de comandos de texto ("aa hunt") no dupliquen absolutamente nada
// de esto — cada uno solo se encarga de cómo enviar la respuesta.
public interface IAdventureCombatStarter
{
    // /travel: pool fijo (GameData/MonsterCatalog.TravelMonsters), no depende de zona.
    Task<CombatStartOutcome> PrepareAsync(ulong discordId, CooldownDefinition definition, IReadOnlyList<MonsterTemplate> monsterPool, CancellationToken cancellationToken = default);

    // /hunt: el pool depende de la zona ACTUAL del jugador (users.current_zone_id), así que se
    // resuelve internamente después de conocerlo — ver Repositories/IMonsterRepository.cs.
    Task<CombatStartOutcome> PrepareHuntAsync(ulong discordId, CancellationToken cancellationToken = default);

    // /boss: enfrenta ESPECÍFICAMENTE al jefe de la zona actual del jugador (nunca sale al azar
    // de /hunt). CombatStartStatus.NoBossInZone si esa zona todavía no tiene uno cargado.
    // CombatStartStatus.NotLeveledForBoss si el jugador todavía no llegó al nivel mínimo de la
    // PRÓXIMA zona (lo que el jefe permite cruzar) — no tiene sentido dejar desafiarlo antes de
    // estar a la altura de a dónde te lleva ganarle.
    Task<CombatStartOutcome> PrepareBossAsync(ulong discordId, CancellationToken cancellationToken = default);
}
