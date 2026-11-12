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
                weapon_enchant_kind = CASE WHEN weapon_id = @ItemId THEN 'filo' ELSE weapon_enchant_kind END,
                amulet_enchant_kind = CASE WHEN amulet_id = @ItemId THEN 'guarda' ELSE amulet_enchant_kind END,
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

    public async Task<SellAllPreview> PreviewSellAllAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        string[] keptTypes = BotDsRpg.GameData.FuegoNuevoRules.KeptItemTypes.ToArray();

        // Solo mira: sin transacción y sin bloquear nada (lo que de verdad se vende lo decide SellAllAsync, que vuelve a medir todo dentro de su transacción).
        var bag = await connection.QuerySingleAsync<(long Items, long Gold)>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(inv.quantity), 0)::bigint AS "Items", COALESCE(SUM(inv.quantity::bigint * i.sell_price), 0)::bigint AS "Gold"
            FROM inventory inv
            JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = @DiscordId AND i.sell_price > 0 AND i.type <> ALL(@KeptTypes);
            """,
            new { DiscordId = (long)discordId, KeptTypes = keptTypes }, cancellationToken: cancellationToken));

        int kept = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = @DiscordId AND i.sell_price > 0 AND i.type = ANY(@KeptTypes);
            """,
            new { DiscordId = (long)discordId, KeptTypes = keptTypes }, cancellationToken: cancellationToken));

        var gear = await QueryEquippedGearAsync(connection, null, discordId, cancellationToken);
        return new SellAllPreview((int)Math.Min(bag.Items, int.MaxValue), (int)Math.Min(bag.Gold, int.MaxValue), kept, gear);
    }

    // El arma y el amuleto puestos que la tienda compra (sell_price > 0), el arma primero.
    private static async Task<IReadOnlyList<SellAllGear>> QueryEquippedGearAsync(DbConnection connection, DbTransaction? transaction, ulong discordId, CancellationToken cancellationToken)
    {
        const string weaponSql = """
            SELECT i.item_id AS "ItemId", i.name AS "Name", i.emoji AS "Emoji", TRUE AS "IsWeapon", i.sell_price AS "SellPrice"
            FROM users u JOIN items i ON i.item_id = u.weapon_id
            WHERE u.discord_id = @DiscordId AND i.sell_price > 0;
            """;
        const string amuletSql = """
            SELECT i.item_id AS "ItemId", i.name AS "Name", i.emoji AS "Emoji", FALSE AS "IsWeapon", i.sell_price AS "SellPrice"
            FROM users u JOIN items i ON i.item_id = u.amulet_id
            WHERE u.discord_id = @DiscordId AND i.sell_price > 0;
            """;

        var gear = new List<SellAllGear>();
        gear.AddRange(await connection.QueryAsync<SellAllGear>(new CommandDefinition(weaponSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken)));
        gear.AddRange(await connection.QueryAsync<SellAllGear>(new CommandDefinition(amuletSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken)));
        return gear;
    }

    public async Task<SellAllOutcome?> SellAllAsync(ulong discordId, bool includeEquipped = false, int? expectedGold = null, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE OF inv: solo bloqueamos las filas de inventory (que vamos a borrar),
            // no las de items (tabla de referencia compartida por todos los jugadores).
            // v0.38.0: tampoco se vende lo que SOBREVIVE al Fuego Nuevo (FuegoNuevoRules.KeptItemTypes: huevos y comida de mascotas y de dragón): el dueño quiere poder «venderlo todo» antes de renacer sin
            // cuidarse de lo que sí le sirve. Vender uno de esos a propósito sigue andando (/shop sell, aa sell <ítem>).
            // sell_price > 0: lo que la tienda no compra (las cajas valen 0 desde la v0.8.0, y los premios como el Arca del Soberano) se queda en la
            // mochila: antes se borraba "a cambio de 0" y un /shop sellall se llevaba las cajas gratis.
            const string selectInventorySql = """
                SELECT inv.quantity AS "Quantity", i.sell_price AS "SellPrice"
                FROM inventory inv
                JOIN items i ON i.item_id = inv.item_id
                WHERE inv.discord_id = @DiscordId AND i.sell_price > 0 AND i.type <> ALL(@KeptTypes)
                FOR UPDATE OF inv;
                """;

            string[] keptTypes = BotDsRpg.GameData.FuegoNuevoRules.KeptItemTypes.ToArray();
            var rows = (await connection.QueryAsync<InventoryValueRow>(new CommandDefinition(
                selectInventorySql, new { DiscordId = (long)discordId, KeptTypes = keptTypes }, transaction: transaction, cancellationToken: cancellationToken))).AsList();

            // v0.53.0: con includeEquipped también entran el arma y el amuleto PUESTOS. Se leen DESPUÉS de bloquear la mochila (el mismo orden que el resto: primero inventory, después users).
            var gear = includeEquipped ? await QueryEquippedGearAsync(connection, transaction, discordId, cancellationToken) : [];

            if (rows.Count == 0 && gear.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            int totalRefund = rows.Sum(row => row.Quantity * row.SellPrice) + gear.Sum(g => g.SellPrice);
            int itemsSoldCount = rows.Sum(row => row.Quantity);

            // Lo que el jugador vio en la confirmación ya no es lo real (cambió la mochila o el equipo): no se vende nada y se le pide que vuelva a mirar.
            if (expectedGold is { } expected && expected != totalRefund)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await ChangedOutcomeAsync(connection, discordId, cancellationToken);
            }

            // Solo lo que se vendió (la misma condición de arriba, y las filas ya están bloqueadas por el SELECT ... FOR UPDATE).
            if (rows.Count > 0)
            {
                const string clearInventorySql = """
                    DELETE FROM inventory inv
                    USING items i
                    WHERE inv.discord_id = @DiscordId AND i.item_id = inv.item_id AND i.sell_price > 0 AND i.type <> ALL(@KeptTypes);
                    """;
                await connection.ExecuteAsync(new CommandDefinition(
                    clearInventorySql, new { DiscordId = (long)discordId, KeptTypes = keptTypes }, transaction: transaction, cancellationToken: cancellationToken));
            }

            // El equipo se desequipa con la guarda de siempre (WHERE sigue siendo ESA pieza): si en el medio la cambió o la vendió en otro clic, no se vende nada. El encantamiento es de la PIEZA y se pierde.
            foreach (var piece in gear)
            {
                string unequipSql = piece.IsWeapon
                    ? "UPDATE users SET weapon_id = NULL, weapon_enchant = 0, weapon_enchant_kind = 'filo' WHERE discord_id = @DiscordId AND weapon_id = @ItemId;"
                    : "UPDATE users SET amulet_id = NULL, amulet_enchant = 0, amulet_enchant_kind = 'guarda' WHERE discord_id = @DiscordId AND amulet_id = @ItemId;";
                int unequipped = await connection.ExecuteAsync(new CommandDefinition(
                    unequipSql, new { DiscordId = (long)discordId, ItemId = piece.ItemId }, transaction: transaction, cancellationToken: cancellationToken));
                if (unequipped == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return await ChangedOutcomeAsync(connection, discordId, cancellationToken);
                }
            }

            // Cuántas unidades vendibles se quedaron por eso (para decirlo en la pantalla).
            int keptCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id
                WHERE inv.discord_id = @DiscordId AND i.sell_price > 0 AND i.type = ANY(@KeptTypes);
                """,
                new { DiscordId = (long)discordId, KeptTypes = keptTypes }, transaction: transaction, cancellationToken: cancellationToken));

            string creditGoldSql = $"""
                UPDATE users
                SET gold = gold + @Refund
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var seller = await connection.QuerySingleAsync<User>(new CommandDefinition(
                creditGoldSql, new { DiscordId = (long)discordId, Refund = totalRefund }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new SellAllOutcome(seller, itemsSoldCount, totalRefund, keptCount, gear);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // El resultado de «no se vendió nada porque cambió algo»: lleva al jugador tal cual está (ya revertida la transacción).
    private static async Task<SellAllOutcome> ChangedOutcomeAsync(DbConnection connection, ulong discordId, CancellationToken cancellationToken)
    {
        var player = await connection.QuerySingleAsync<User>(new CommandDefinition(
            $"SELECT {UserSql.SelectColumns} FROM users WHERE discord_id = @DiscordId;", new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));
        return new SellAllOutcome(player, 0, 0, 0, null, Changed: true);
    }

    private sealed record InventoryValueRow(int Quantity, int SellPrice);
}
