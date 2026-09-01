namespace BotDsRpg.Repositories;

public interface ICooldownRepository
{
    // Tiempo restante para poder volver a ejecutar el comando; null si ya puede ejecutarlo ahora.
    // Es de solo lectura: la validación "oficial" (a prueba de condiciones de carrera) ocurre
    // en IAdventureRepository.ApplyRewardAsync al momento de aplicar la recompensa.
    Task<TimeSpan?> GetRemainingAsync(ulong discordId, string commandName, TimeSpan cooldownDuration, CancellationToken cancellationToken = default);
}
