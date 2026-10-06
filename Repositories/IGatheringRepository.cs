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

    // Lo mismo para varios materiales a la vez (la recolección avanzada de los oficios, v0.13.0: 4 sorteos): UN solo cooldown y TODOS los materiales en la misma
    // transacción, así que o entra todo o no entra nada.
    Task<bool> ApplyGatheringBatchAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        IReadOnlyList<(int ItemId, int Quantity)> items,
        CancellationToken cancellationToken = default);
}
