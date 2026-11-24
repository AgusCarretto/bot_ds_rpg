namespace BotDsRpg.Repositories;

public interface IMiniEventRepository
{
    // Le paga su parte del minievento a un participante, de forma atómica: los materiales (cada uno itemId x quantity) y/o el oro, en UNA transacción.
    // Devuelve false si el jugador ya no existe (no se paga nada).
    Task<bool> PayAsync(ulong discordId, IReadOnlyList<(int ItemId, int Quantity)> items, int gold, CancellationToken cancellationToken = default);
}
