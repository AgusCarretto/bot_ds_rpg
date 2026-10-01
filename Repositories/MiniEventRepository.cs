using System.Data.Common;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class MiniEventRepository(IDbConnectionFactory connectionFactory) : IMiniEventRepository
{
    public async Task<bool> PayAsync(ulong discordId, int? itemId, int quantity, int gold, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // El oro y el material en la misma transacción. Si el jugador no existe, el UPDATE no devuelve fila y no se paga nada.
        int? paid = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "UPDATE users SET gold = gold + @Gold WHERE discord_id = @DiscordId RETURNING 1;",
            new { Gold = gold, DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

        if (paid is null)
        {
            return false;
        }

        if (itemId is int id && quantity > 0)
        {
            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, id, quantity, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
