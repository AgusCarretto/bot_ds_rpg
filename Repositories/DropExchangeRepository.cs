using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class DropExchangeRepository(IDbConnectionFactory connectionFactory) : IDropExchangeRepository
{
    // Un "drop de zona" es un Material que suelta un monstruo que NO es jefe (el jefe suelta el cofre de su zona, que es una Caja). Es la ÚNICA definición: la lista y la
    // validación del trueque salen de acá.
    private const string DropsSql = """
        SELECT DISTINCT i.item_id AS ItemId, i.name AS Name, i.emoji AS Emoji, i.rarity AS Rarity, z.zone_id AS ZoneId, z.name AS ZoneName, z.min_level AS MinLevel
        FROM monster_drops d
        JOIN monsters m ON m.monster_id = d.monster_id
        JOIN zones z ON z.zone_id = m.zone_id
        JOIN items i ON i.item_id = d.item_id
        WHERE NOT m.is_boss AND i.type = 'Material'
        """;

    public async Task<IReadOnlyList<ExchangeDrop>> GetExchangeableDropsAsync(CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var drops = await connection.QueryAsync<ExchangeDrop>(new CommandDefinition(
            DropsSql + " ORDER BY z.min_level, i.name;", cancellationToken: cancellationToken));
        return drops.ToList();
    }

    public async Task<ExchangeOutcome> ExchangeAsync(ulong discordId, int giveItemId, int getItemId, int times, CancellationToken cancellationToken = default)
    {
        if (times < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(times), "Tiene que ser al menos 1 cambio.");
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (giveItemId == getItemId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExchangeOutcome(ExchangeStatus.SameItem);
            }

            // Las reglas de la base, dentro de la transacción: los dos son drops de zona y de la MISMA zona.
            var pair = (await connection.QueryAsync<ExchangeDrop>(new CommandDefinition(
                DropsSql + " AND i.item_id IN (@Give, @Get);",
                new { Give = giveItemId, Get = getItemId }, transaction: transaction, cancellationToken: cancellationToken))).ToList();

            var give = pair.FirstOrDefault(d => d.ItemId == giveItemId);
            var get = pair.FirstOrDefault(d => d.ItemId == getItemId);
            if (give is null || get is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExchangeOutcome(ExchangeStatus.NotExchangeable);
            }

            if (give.ZoneId != get.ZoneId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExchangeOutcome(ExchangeStatus.DifferentZones);
            }

            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT count(*)::int FROM users WHERE discord_id = @DiscordId;",
                    new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken)) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExchangeOutcome(ExchangeStatus.NoAccount);
            }

            int giveTotal = DropExchange.GiveAmount * times;
            int getTotal = DropExchange.GetAmount * times;

            // La guarda de siempre (mismo patrón que InventoryRepository.TryConsumeAsync): si ya no tiene esa cantidad no devuelve fila y no se toca nada.
            int? giveLeft = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE inventory
                SET quantity = quantity - @GiveTotal
                WHERE discord_id = @DiscordId AND item_id = @Give AND quantity >= @GiveTotal
                RETURNING quantity;
                """,
                new { DiscordId = (long)discordId, Give = giveItemId, GiveTotal = giveTotal }, transaction: transaction, cancellationToken: cancellationToken));

            if (giveLeft is null)
            {
                int have = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                    "SELECT quantity FROM inventory WHERE discord_id = @DiscordId AND item_id = @Give;",
                    new { DiscordId = (long)discordId, Give = giveItemId }, transaction: transaction, cancellationToken: cancellationToken)) ?? 0;
                await transaction.RollbackAsync(cancellationToken);
                return new ExchangeOutcome(ExchangeStatus.NotEnough, have);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @Give AND quantity <= 0;",
                new { DiscordId = (long)discordId, Give = giveItemId }, transaction: transaction, cancellationToken: cancellationToken));

            int getNow = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                INSERT INTO inventory (discord_id, item_id, quantity)
                VALUES (@DiscordId, @Get, @GetTotal)
                ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + EXCLUDED.quantity
                RETURNING quantity;
                """,
                new { DiscordId = (long)discordId, Get = getItemId, GetTotal = getTotal }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new ExchangeOutcome(ExchangeStatus.Ok, giveLeft.Value, getNow);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
