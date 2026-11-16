using System.Data.Common;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class TransferRepository(IDbConnectionFactory connectionFactory) : ITransferRepository
{
    public async Task<SwapStatus> SwapItemsAsync(
        ulong firstId, int firstGivesItemId, ulong secondId, int secondGivesItemId, int quantity = 1, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Un cambio es de al menos 1 unidad.");
        }

        // Cada uno da quantity y recibe quantity. Orden fijo (jugador, ítem): dos cambios cruzados toman las filas en el mismo orden y no se traban.
        var operations = new List<(ulong Owner, int ItemId, int Delta, SwapStatus IfMissing)>
        {
            (firstId, firstGivesItemId, -quantity, SwapStatus.FirstMissing),
            (secondId, secondGivesItemId, -quantity, SwapStatus.SecondMissing),
            (firstId, secondGivesItemId, quantity, SwapStatus.Ok),
            (secondId, firstGivesItemId, quantity, SwapStatus.Ok),
        };

        foreach (var (owner, itemId, delta, ifMissing) in operations.OrderBy(o => o.Owner).ThenBy(o => o.ItemId))
        {
            if (delta > 0)
            {
                await InventoryUpsert.AddItemAsync(connection, transaction, owner, itemId, delta, cancellationToken);
                continue;
            }

            int? left = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE inventory SET quantity = quantity - @Qty
                WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= @Qty
                RETURNING quantity;
                """,
                new { DiscordId = (long)owner, ItemId = itemId, Qty = -delta }, transaction: transaction, cancellationToken: cancellationToken));

            if (left is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ifMissing;
            }

            if (left == 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity = 0;",
                    new { DiscordId = (long)owner, ItemId = itemId }, transaction: transaction, cancellationToken: cancellationToken));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return SwapStatus.Ok;
    }

    public async Task<TransferOutcome> TransferGoldAsync(
        ulong fromDiscordId, ulong toDiscordId, int amount, CancellationToken cancellationToken = default)
    {
        if (amount < 1)
        {
            return new TransferOutcome(TransferStatus.InvalidAmount);
        }

        if (fromDiscordId == toDiscordId)
        {
            return new TransferOutcome(TransferStatus.SameUser);
        }

        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Las dos filas bloqueadas EN ORDEN de id: si A->B y B->A llegan a la vez, ambas toman primero la fila de menor id y la
        // segunda espera, en vez de bloquearse una a la otra (deadlock).
        var rows = (await connection.QueryAsync<GoldRow>(new CommandDefinition(
            """
            SELECT discord_id AS DiscordId, gold AS Gold
            FROM users
            WHERE discord_id = ANY(@Ids)
            ORDER BY discord_id
            FOR UPDATE;
            """,
            new { Ids = new[] { (long)fromDiscordId, (long)toDiscordId } },
            transaction: transaction, cancellationToken: cancellationToken))).ToList();

        var sender = rows.FirstOrDefault(r => r.DiscordId == (long)fromDiscordId);
        var recipient = rows.FirstOrDefault(r => r.DiscordId == (long)toDiscordId);

        if (sender is null)
        {
            return new TransferOutcome(TransferStatus.SenderMissing);
        }

        if (recipient is null)
        {
            return new TransferOutcome(TransferStatus.RecipientMissing);
        }

        if (sender.Gold < amount)
        {
            return new TransferOutcome(TransferStatus.InsufficientGold, sender.Gold, recipient.Gold);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET gold = gold - @Amount WHERE discord_id = @FromId;",
            new { Amount = amount, FromId = (long)fromDiscordId }, transaction: transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET gold = gold + @Amount WHERE discord_id = @ToId;",
            new { Amount = amount, ToId = (long)toDiscordId }, transaction: transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new TransferOutcome(TransferStatus.Ok, sender.Gold - amount, recipient.Gold + amount);
    }

    private sealed record GoldRow(long DiscordId, int Gold);
}
