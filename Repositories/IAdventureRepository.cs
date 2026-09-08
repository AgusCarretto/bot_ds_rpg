using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IAdventureRepository
{
    // Reclama el cooldown de /hunt o /travel de forma atómica (upsert guardado, a prueba de
    // ejecuciones concurrentes del mismo comando). Se cobra al INICIAR el combate, no al
    // terminarlo: si el jugador abandona o se le acaba el tiempo, igual "gastó" el intento.
    // Devuelve false si el cooldown seguía vigente (no aplica ningún cambio en ese caso).
    Task<bool> TryClaimCooldownAsync(ulong discordId, string commandName, TimeSpan cooldownDuration, CancellationToken cancellationToken = default);

    // Aplica de forma atómica el resultado de ganar un combate: suma oro, aplica la XP (con
    // nivelado y curación de HP al subir de nivel) y si corresponde agrega un material al
    // inventario. hpDelta es el daño neto (negativo) o curación neta (positiva) que el jugador
    // acumuló en memoria durante la pelea (ver CombatState.PlayerStartingHp) — se aplica sobre el
    // HP real más reciente en base, no sobre un snapshot, para no pisar una curación con /use a
    // mitad de combate. Precondición: el usuario ya debe existir en la tabla users (llamar antes
    // a IUserRepository.GetOrCreateUserAsync).
    Task<LevelUpOutcome> ApplyVictoryAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        CancellationToken cancellationToken = default);

    // Igual que ApplyVictoryAsync, pero para derrotar a un jefe de zona: en la MISMA transacción
    // también sube users.highest_zone_cleared a clearedZoneId (nunca lo baja — GREATEST — así
    // repetir el jefe de una zona ya superada no hace nada raro). clearedZoneId viene de
    // CombatState.BossZoneId, capturado al arrancar el combate, no de leer la zona actual de nuevo.
    Task<LevelUpOutcome> ApplyBossVictoryAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        int clearedZoneId,
        CancellationToken cancellationToken = default);
}
