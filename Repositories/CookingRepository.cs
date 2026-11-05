using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class CookingRepository(IDbConnectionFactory connectionFactory) : ICookingRepository
{
    private sealed class CookedRow
    {
        public string DishKey { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class ItemRow
    {
        public string Name { get; set; } = string.Empty;
        public int ItemId { get; set; }
    }

    private sealed class HaveRow
    {
        public string Name { get; set; } = string.Empty;
        public int Have { get; set; }
    }

    public async Task<IReadOnlyDictionary<string, int>> GetCookedAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CookedRow>(new CommandDefinition(
            "SELECT dish_key AS DishKey, count AS Count FROM player_cooked WHERE discord_id = @DiscordId AND count > 0;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.DishKey, r => r.Count, StringComparer.Ordinal);
    }

    public async Task<CookOutcome> CookAsync(ulong discordId, Dish dish, int times, CancellationToken cancellationToken = default)
    {
        times = Math.Clamp(times, 1, CookingCatalog.MaxTimes);
        var needs = CookingCatalog.Needs(dish, times);

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Se bloquea la fila del jugador: dos pedidos suyos a la vez se ponen en fila (nada se descuenta dos veces) y sin cuenta no se crea una.
            long? exists = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                "SELECT discord_id FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
            if (exists is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CookOutcome(CookStatus.NoAccount, 0, []);
            }

            var ids = (await connection.QueryAsync<ItemRow>(new CommandDefinition(
                "SELECT name AS Name, item_id AS ItemId FROM items WHERE name = ANY(@Names);",
                new { Names = needs.Select(n => n.ItemName).ToArray() }, transaction: transaction, cancellationToken: cancellationToken)))
                .ToDictionary(r => r.Name, r => r.ItemId, StringComparer.Ordinal);

            bool lacking = false;
            foreach (var need in needs)
            {
                // El descuento protegido: si ya no alcanza no vuelve fila y se deshace todo (nunca hay un chequeo y después una resta).
                int? left = ids.TryGetValue(need.ItemName, out int itemId)
                    ? await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                        """
                        UPDATE inventory
                        SET quantity = quantity - @Quantity
                        WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= @Quantity
                        RETURNING quantity;
                        """,
                        new { DiscordId = (long)discordId, ItemId = itemId, Quantity = need.Quantity }, transaction: transaction, cancellationToken: cancellationToken))
                    : null;
                if (left is null)
                {
                    lacking = true;
                    break;
                }
            }

            if (lacking)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CookOutcome(CookStatus.Missing, 0, await ShortagesAsync(connection, discordId, needs, cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND quantity <= 0;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            int count = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                INSERT INTO player_cooked (discord_id, dish_key, count)
                VALUES (@DiscordId, @DishKey, @Times)
                ON CONFLICT (discord_id, dish_key) DO UPDATE SET count = player_cooked.count + EXCLUDED.count
                RETURNING count;
                """,
                new { DiscordId = (long)discordId, DishKey = dish.Key, Times = times }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new CookOutcome(CookStatus.Cooked, count, []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    // Lo que falta, ítem por ítem, en el orden de la receta: lo que el jugador tiene AHORA (la transacción ya se deshizo) contra lo que pide.
    private static async Task<IReadOnlyList<CookShortage>> ShortagesAsync(DbConnection connection, ulong discordId, IReadOnlyList<DishIngredient> needs, CancellationToken cancellationToken)
    {
        var have = (await connection.QueryAsync<HaveRow>(new CommandDefinition(
            """
            SELECT i.name AS Name, COALESCE(SUM(inv.quantity), 0)::int AS Have
            FROM items i
            LEFT JOIN inventory inv ON inv.item_id = i.item_id AND inv.discord_id = @DiscordId
            WHERE i.name = ANY(@Names)
            GROUP BY i.name;
            """,
            new { DiscordId = (long)discordId, Names = needs.Select(n => n.ItemName).ToArray() }, cancellationToken: cancellationToken)))
            .ToDictionary(r => r.Name, r => r.Have, StringComparer.Ordinal);

        return needs
            .Select(n => new CookShortage(n.ItemName, have.GetValueOrDefault(n.ItemName), n.Quantity))
            .Where(s => s.Have < s.Need)
            .ToList();
    }
}
