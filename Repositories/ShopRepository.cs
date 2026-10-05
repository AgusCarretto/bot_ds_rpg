using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ShopRepository(IDbConnectionFactory connectionFactory) : IShopRepository
{
    public async Task<User?> BuyItemAsync(ulong discordId, int itemId, int quantity, int totalCost, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Update guardado: si no le alcanza el oro, el WHERE bloquea la actualización
            // y no devuelve fila.
            string deductGoldSql = $"""
                UPDATE users
                SET gold = gold - @TotalCost
                WHERE discord_id = @DiscordId AND gold >= @TotalCost
                RETURNING {UserSql.SelectColumns};
                """;

            var buyer = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                deductGoldSql,
                new { DiscordId = (long)discordId, TotalCost = totalCost },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (buyer is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, itemId, quantity, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return buyer;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CooldownBuyOutcome> BuyItemWithCooldownAsync(
        ulong discordId, int itemId, int quantity, int totalCost, string cooldownCommand, TimeSpan cooldownDuration,
        CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // El cooldown va PRIMERO: si todavía no se puede comprar, el jugador tiene que enterarse de eso (y no de que le falta oro).
            // La guarda es el upsert de siempre: con el cooldown vigente no devuelve fila y no cambia nada.
            if (!await CooldownGuard.TryClaimAsync(connection, transaction, discordId, cooldownCommand, cooldownDuration, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CooldownBuyOutcome(null, await RemainingAsync(connection, discordId, cooldownCommand, cooldownDuration, cancellationToken));
            }

            string deductGoldSql = $"""
                UPDATE users
                SET gold = gold - @TotalCost
                WHERE discord_id = @DiscordId AND gold >= @TotalCost
                RETURNING {UserSql.SelectColumns};
                """;

            var buyer = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                deductGoldSql,
                new { DiscordId = (long)discordId, TotalCost = totalCost },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (buyer is null)
            {
                // Sin oro: se descarta TODO, incluida la marca del cooldown que se acaba de reclamar.
                await transaction.RollbackAsync(cancellationToken);
                return new CooldownBuyOutcome(null, null);
            }

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, itemId, quantity, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new CooldownBuyOutcome(buyer, null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Cuánto falta para que venza el cooldown (null si ya venció). Se lee DESPUÉS del rollback, con la fila que dejó la compra anterior.
    private static async Task<TimeSpan?> RemainingAsync(
        DbConnection connection, ulong discordId, string commandName, TimeSpan duration, CancellationToken cancellationToken)
    {
        DateTime? last = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT last_executed_at FROM cooldowns WHERE discord_id = @DiscordId AND command_name = @CommandName;",
            new { DiscordId = (long)discordId, CommandName = commandName }, cancellationToken: cancellationToken));

        if (last is null)
        {
            return null;
        }

        var remaining = duration - (DateTime.UtcNow - DateTime.SpecifyKind(last.Value, DateTimeKind.Utc));
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    }

    public async Task<User?> SellEquippedAsync(ulong discordId, int itemId, int refund, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        // Una sola sentencia con la guarda en el WHERE: si no lo tiene puesto (o ya lo vendió en otro click) no devuelve fila y no cambia nada.
        string sql = $"""
            UPDATE users
            SET weapon_id = CASE WHEN weapon_id = @ItemId THEN NULL ELSE weapon_id END,
                amulet_id = CASE WHEN amulet_id = @ItemId THEN NULL ELSE amulet_id END,
                -- El encantamiento es de la PIEZA: al venderla se pierde (GameData/Enchantments.cs).
                weapon_enchant = CASE WHEN weapon_id = @ItemId THEN 0 ELSE weapon_enchant END,
                amulet_enchant = CASE WHEN amulet_id = @ItemId THEN 0 ELSE amulet_enchant END,
                gold = gold + @Refund
            WHERE discord_id = @DiscordId AND (weapon_id = @ItemId OR amulet_id = @ItemId)
            RETURNING {UserSql.SelectColumns};
            """;

        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            sql, new { DiscordId = (long)discordId, ItemId = itemId, Refund = refund }, cancellationToken: cancellationToken));
    }

    public async Task<User?> SellItemAsync(ulong discordId, int itemId, int quantity, int totalRefund, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Update guardado: si no tiene esa cantidad, el WHERE bloquea la actualización
            // y no devuelve fila.
            const string deductInventorySql = """
                UPDATE inventory
                SET quantity = quantity - @Quantity
                WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= @Quantity
                RETURNING quantity;
                """;

            int? remaining = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                deductInventorySql,
                new { DiscordId = (long)discordId, ItemId = itemId, Quantity = quantity },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (remaining is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            if (remaining == 0)
            {
                // Limpiamos la fila para no acumular basura en el inventario.
                const string cleanupSql = "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity <= 0;";
                await connection.ExecuteAsync(new CommandDefinition(
                    cleanupSql, new { DiscordId = (long)discordId, ItemId = itemId }, transaction: transaction, cancellationToken: cancellationToken));
            }

            string creditGoldSql = $"""
                UPDATE users
                SET gold = gold + @Refund
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var seller = await connection.QuerySingleAsync<User>(new CommandDefinition(
                creditGoldSql, new { DiscordId = (long)discordId, Refund = totalRefund }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return seller;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SellAllOutcome?> SellAllAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE OF inv: solo bloqueamos las filas de inventory (que vamos a borrar),
            // no las de items (tabla de referencia compartida por todos los jugadores).
            // sell_price > 0: lo que la tienda no compra (las cajas valen 0 desde la v0.8.0, y los premios como el Arca del Soberano) se queda en la
            // mochila: antes se borraba "a cambio de 0" y un /shop sellall se llevaba las cajas gratis.
            const string selectInventorySql = """
                SELECT inv.quantity AS "Quantity", i.sell_price AS "SellPrice"
                FROM inventory inv
                JOIN items i ON i.item_id = inv.item_id
                WHERE inv.discord_id = @DiscordId AND i.sell_price > 0
                FOR UPDATE OF inv;
                """;

            var rows = (await connection.QueryAsync<InventoryValueRow>(new CommandDefinition(
                selectInventorySql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken))).AsList();

            if (rows.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            int totalRefund = rows.Sum(row => row.Quantity * row.SellPrice);
            int itemsSoldCount = rows.Sum(row => row.Quantity);

            // Solo lo que se vendió (la misma condición de arriba, y las filas ya están bloqueadas por el SELECT ... FOR UPDATE).
            const string clearInventorySql = """
                DELETE FROM inventory inv
                USING items i
                WHERE inv.discord_id = @DiscordId AND i.item_id = inv.item_id AND i.sell_price > 0;
                """;
            await connection.ExecuteAsync(new CommandDefinition(
                clearInventorySql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            string creditGoldSql = $"""
                UPDATE users
                SET gold = gold + @Refund
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var seller = await connection.QuerySingleAsync<User>(new CommandDefinition(
                creditGoldSql, new { DiscordId = (long)discordId, Refund = totalRefund }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new SellAllOutcome(seller, itemsSoldCount, totalRefund);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private sealed record InventoryValueRow(int Quantity, int SellPrice);
}
