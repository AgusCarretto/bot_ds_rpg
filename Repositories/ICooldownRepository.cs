namespace BotDsRpg.Repositories;

public interface ICooldownRepository
{
    // Tiempo restante para poder volver a ejecutar el comando; null si ya puede ejecutarlo ahora.
    // Es de solo lectura: la validación "oficial" (a prueba de condiciones de carrera) ocurre en
    // GatheringRepository.ApplyGatheringRewardAsync o IAdventureRepository.TryClaimCooldownAsync
    // al momento de reclamar el cooldown.
    Task<TimeSpan?> GetRemainingAsync(ulong discordId, string commandName, TimeSpan cooldownDuration, CancellationToken cancellationToken = default);
}
