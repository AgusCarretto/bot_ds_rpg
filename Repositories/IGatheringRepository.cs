namespace BotDsRpg.Repositories;

public interface IGatheringRepository
{
    // Aplica de forma atómica el resultado de un /chop o /mine: valida y renueva el cooldown,
    // y agrega el material obtenido al inventario (+1 si ya lo tenía). Devuelve false si el
    // cooldown seguía vigente (perdió la carrera contra otra ejecución concurrente); en ese
    // caso no se aplicó ningún cambio.
    Task<bool> ApplyGatheringRewardAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        int itemId,
        int quantity,
        CancellationToken cancellationToken = default);
}
