using System.Data.Common;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class TransferRepository(IDbConnectionFactory connectionFactory) : ITransferRepository
{
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
