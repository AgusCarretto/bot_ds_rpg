namespace BotDsRpg.Repositories;

public interface IMiniEventRepository
{
    // Le paga su parte del minievento a un participante, de forma atómica: el material (itemId x quantity) y/o el oro, en UNA transacción.
    // Devuelve false si el jugador ya no existe (no se paga nada).
    Task<bool> PayAsync(ulong discordId, int? itemId, int quantity, int gold, CancellationToken cancellationToken = default);
}
