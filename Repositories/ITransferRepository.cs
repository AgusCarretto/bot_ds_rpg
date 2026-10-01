namespace BotDsRpg.Repositories;

public enum TransferStatus { Ok, InvalidAmount, SameUser, SenderMissing, RecipientMissing, InsufficientGold }

// SenderGold / RecipientGold: el oro de cada uno DESPUÉS de la transferencia (solo con Status == Ok).
public sealed record TransferOutcome(TransferStatus Status, int SenderGold = 0, int RecipientGold = 0);

public interface ITransferRepository
{
    // Pasa oro de un jugador a otro de forma atómica: o se mueve todo o no se mueve nada. Bloquea las dos filas (siempre en
    // el mismo orden, por id) y recién ahí chequea el saldo, así que dos transferencias simultáneas — incluso cruzadas, A->B
    // y B->A — no pueden gastar el mismo oro dos veces ni trabarse entre sí. No cobra impuesto: el oro que sale es el que entra.
    Task<TransferOutcome> TransferGoldAsync(ulong fromDiscordId, ulong toDiscordId, int amount, CancellationToken cancellationToken = default);
}
