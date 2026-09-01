using System.Data.Common;
using BotDsRpg.Data;

namespace BotDsRpg.Repositories;

public sealed class GatheringRepository(IDbConnectionFactory connectionFactory) : IGatheringRepository
{
    public async Task<bool> ApplyGatheringRewardAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        int itemId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            bool claimed = await CooldownGuard.TryClaimAsync(connection, transaction, discordId, commandName, cooldownDuration, cancellationToken);
            if (!claimed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, itemId, quantity, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
