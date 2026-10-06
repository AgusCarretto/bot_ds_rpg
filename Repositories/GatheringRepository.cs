using System.Data.Common;
using BotDsRpg.Data;

namespace BotDsRpg.Repositories;

public sealed class GatheringRepository(IDbConnectionFactory connectionFactory) : IGatheringRepository
{
    public Task<bool> ApplyGatheringRewardAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        int itemId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        ApplyGatheringBatchAsync(discordId, commandName, cooldownDuration, [(itemId, quantity)], cancellationToken);

    public async Task<bool> ApplyGatheringBatchAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        IReadOnlyList<(int ItemId, int Quantity)> items,
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

            // Un ítem a la vez, siempre en el mismo orden (por id): dos recolecciones a la vez de un mismo jugador no se pisan, y el UPSERT suma si ya lo tenía.
            foreach (var (itemId, quantity) in items.GroupBy(i => i.ItemId).Select(g => (ItemId: g.Key, Quantity: g.Sum(i => i.Quantity))).OrderBy(i => i.ItemId))
            {
                await InventoryUpsert.AddItemAsync(connection, transaction, discordId, itemId, quantity, cancellationToken);
            }

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
