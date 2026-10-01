namespace BotDsRpg.Repositories;

public enum TransferStatus { Ok, InvalidAmount, SameUser, SenderMissing, RecipientMissing, InsufficientGold }

// SenderGold / RecipientGold: el oro de cada uno DESPUÉS de la transferencia (solo con Status == Ok).
public sealed record TransferOutcome(TransferStatus Status, int SenderGold = 0, int RecipientGold = 0);

public enum SwapStatus { Ok, FirstMissing, SecondMissing }

public interface ITransferRepository
{
    // Pasa oro de un jugador a otro de forma atómica: o se mueve todo o no se mueve nada. Bloquea las dos filas (siempre en
    // el mismo orden, por id) y recién ahí chequea el saldo, así que dos transferencias simultáneas — incluso cruzadas, A->B
    // y B->A — no pueden gastar el mismo oro dos veces ni trabarse entre sí. No cobra impuesto: el oro que sale es el que entra.
    // Cambia 1 unidad de un ítem de cada jugador por 1 unidad del otro (el primero da "firstGivesItemId", el segundo "secondGivesItemId") en UNA
    // transacción: o se mueven los dos o no se mueve ninguno. Las cuatro operaciones se hacen siempre en el mismo orden (por jugador y por ítem),
    // así dos cambios cruzados no se traban entre sí, y cada descuento es un UPDATE guardado (si ya no tiene la unidad, no se aplica nada).
    // Las reglas (misma rareza, solo recolección) las valida quien llama (GameData/TradeRules.cs).
    Task<SwapStatus> SwapItemsAsync(ulong firstId, int firstGivesItemId, ulong secondId, int secondGivesItemId, CancellationToken cancellationToken = default);

    Task<TransferOutcome> TransferGoldAsync(ulong fromDiscordId, ulong toDiscordId, int amount, CancellationToken cancellationToken = default);
}
