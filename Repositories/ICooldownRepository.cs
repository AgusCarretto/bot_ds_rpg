namespace BotDsRpg.Repositories;

// Una fila de la tabla cooldowns: el comando y la última vez que se ejecutó (UTC).
public sealed record CooldownRow(string Command, DateTime LastExecutedUtc);

public interface ICooldownRepository
{
    // Tiempo restante para poder volver a ejecutar el comando; null si ya puede ejecutarlo ahora.
    // Es de solo lectura: la validación "oficial" (a prueba de condiciones de carrera) ocurre en
    // GatheringRepository.ApplyGatheringRewardAsync o IAdventureRepository.TryClaimCooldownAsync
    // al momento de reclamar el cooldown.
    Task<TimeSpan?> GetRemainingAsync(ulong discordId, string commandName, TimeSpan cooldownDuration, CancellationToken cancellationToken = default);

    // Todas las filas de cooldown del jugador (de solo lectura). Las usan los recordatorios (GameData/Reminders.cs) para saber cuáles esperas todavía corren.
    Task<IReadOnlyList<CooldownRow>> GetAllAsync(ulong discordId, CancellationToken cancellationToken = default);
}
