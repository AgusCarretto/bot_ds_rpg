using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// El repositorio del comando de administrador (v0.34.0, «aa admingive»). Ver IAdminRepository.
public sealed class AdminRepository(IDbConnectionFactory connectionFactory) : IAdminRepository
{
    private sealed class BeforeAfter
    {
        public int Before { get; set; }
        public int After { get; set; }
    }

    public Task<AdminGiveResult> GiveGoldAsync(ulong discordId, int amount, CancellationToken cancellationToken = default) =>
        AddToUserColumnAsync("gold", discordId, amount, cancellationToken);

    public Task<AdminGiveResult> GiveDustAsync(ulong discordId, int amount, CancellationToken cancellationToken = default) =>
        AddToUserColumnAsync("dust", discordId, amount, cancellationToken);

    // UNA sentencia: bloquea la fila, suma en bigint y recorta en int.MaxValue (así un total enorme no revienta con «integer out of range»). column es siempre uno de dos nombres fijos: seguro interpolarlo.
    private async Task<AdminGiveResult> AddToUserColumnAsync(string column, ulong discordId, int amount, CancellationToken cancellationToken)
    {
        if (column is not ("gold" or "dust"))
        {
            throw new ArgumentException("Solo se puede sumar oro o Polvo.", nameof(column));
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        string sql = $"""
            WITH old AS (SELECT {column} AS before FROM users WHERE discord_id = @DiscordId FOR UPDATE)
            UPDATE users u SET {column} = LEAST(old.before::bigint + @Amount, 2147483647)::int
            FROM old
            WHERE u.discord_id = @DiscordId
            RETURNING old.before AS Before, u.{column} AS After;
            """;
        var row = await connection.QuerySingleOrDefaultAsync<BeforeAfter>(
            new CommandDefinition(sql, new { DiscordId = (long)discordId, Amount = amount }, cancellationToken: cancellationToken));

        return row is null
            ? new AdminGiveResult(AdminGiveStatus.NoAccount)
            : new AdminGiveResult(AdminGiveStatus.Given, row.After, Capped: (long)row.Before + amount > int.MaxValue);
    }

    public async Task<AdminGiveResult> GiveItemAsync(ulong discordId, Item item, int quantity, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Sin cuenta no se da nada (y no se crea: el comando nunca inventa jugadores). La fila queda bloqueada hasta el commit.
            bool exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM users WHERE discord_id = @DiscordId FOR UPDATE);",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
            if (!exists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminGiveResult(AdminGiveStatus.NoAccount);
            }

            AdminGiveResult result;
            string? toolKind = ToolRules.KindOfItemType(item.Type);
            if (item.Type is "Weapon" or "Amulet")
            {
                // Igual que forjarlo: la pieza nueva pisa a la vieja y arranca sin encantar (el encantamiento es de la pieza). Los nombres de columna son fijos.
                bool isWeapon = item.Type == "Weapon";
                string equipSql = $"UPDATE users SET {(isWeapon ? "weapon_id" : "amulet_id")} = @ItemId, {(isWeapon ? "weapon_enchant" : "amulet_enchant")} = 0, {(isWeapon ? "weapon_enchant_kind" : "amulet_enchant_kind")} = {(isWeapon ? "'filo'" : "'guarda'")} WHERE discord_id = @DiscordId;";
                await connection.ExecuteAsync(new CommandDefinition(
                    equipSql, new { DiscordId = (long)discordId, ItemId = item.ItemId }, transaction: transaction, cancellationToken: cancellationToken));
                result = new AdminGiveResult(AdminGiveStatus.Given, 1, Slot: isWeapon ? "arma" : "amuleto");
            }
            else if (toolKind is not null)
            {
                // La herramienta queda puesta (una por tipo; la nueva pisa a la vieja, también si es peor: es para probar).
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO player_tools (discord_id, kind, item_id) VALUES (@DiscordId, @Kind, @ItemId)
                    ON CONFLICT (discord_id, kind) DO UPDATE SET item_id = EXCLUDED.item_id;
                    """,
                    new { DiscordId = (long)discordId, Kind = toolKind, ItemId = item.ItemId }, transaction: transaction, cancellationToken: cancellationToken));
                result = new AdminGiveResult(AdminGiveStatus.Given, 1, Slot: "herramienta");
            }
            else
            {
                int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@DiscordId, @ItemId, @Quantity)
                    ON CONFLICT (discord_id, item_id) DO UPDATE
                        SET quantity = LEAST(inventory.quantity::bigint + EXCLUDED.quantity, 2147483647)::int
                    RETURNING quantity;
                    """,
                    new { DiscordId = (long)discordId, ItemId = item.ItemId, Quantity = quantity }, transaction: transaction, cancellationToken: cancellationToken));
                result = new AdminGiveResult(AdminGiveStatus.Given, total, Capped: total == int.MaxValue);
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AdminBotStats> GetBotStatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT (SELECT count(*)::int FROM users) AS "Players",
                   (SELECT count(DISTINCT discord_id)::int FROM game_events WHERE occurred_at > now() - interval '24 hours') AS "Active24h",
                   (SELECT count(DISTINCT discord_id)::int FROM game_events WHERE occurred_at > now() - interval '7 days') AS "Active7d",
                   (SELECT count(*)::int FROM users WHERE fuego_nuevo > 0) AS "WithFuegoNuevo";
            """;
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleAsync<AdminBotStats>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> SearchItemNamesAsync(string typed, int limit, CancellationToken cancellationToken = default)
    {
        // Lo escrito va como texto literal dentro del patrón: se escapan \, % y _ (si no, «100%» o «_» harían de comodín).
        string escaped = typed.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        if (escaped.Length == 0)
        {
            return [];
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM items WHERE name ILIKE @Pattern ESCAPE '\\' ORDER BY name LIMIT @Limit;",
            new { Pattern = $"%{escaped}%", Limit = Math.Clamp(limit, 1, 25) }, cancellationToken: cancellationToken));
        return names.ToList();
    }

    private sealed class DragonLevels
    {
        public int Tier { get; set; }
        public int Level { get; set; }
    }

    public async Task<AdminDragonSetResult> SetDragonTierAsync(ulong discordId, int tier, CancellationToken cancellationToken = default)
    {
        if (tier is < 1 or > DragonRules.MaxTier)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), tier, $"El tier va de 1 a {DragonRules.MaxTier}.");
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La fila del dragón bloqueada hasta el commit: un cambio de tier y uno de nivel a la vez se ordenan, así el nivel nunca queda por encima del tope del tier que quedó.
            var before = await connection.QuerySingleOrDefaultAsync<DragonLevels>(new CommandDefinition(
                "SELECT tier AS Tier, level AS Level FROM player_dragons WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (before is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminDragonSetResult(AdminDragonSetStatus.NoDragon);
            }

            int cap = DragonRules.LevelCap(tier);
            int level = Math.Min(before.Level, cap);

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE player_dragons SET tier = @Tier, level = @Level, fail_streak = 0, solo_fail_streak = 0 WHERE discord_id = @DiscordId;",
                new { DiscordId = (long)discordId, Tier = tier, Level = level }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new AdminDragonSetResult(AdminDragonSetStatus.Set, before.Tier, before.Level, tier, level, LevelClamped: level < before.Level, LevelCap: cap);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AdminDragonSetResult> SetDragonLevelAsync(ulong discordId, int level, CancellationToken cancellationToken = default)
    {
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "El nivel no puede ser negativo.");
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var before = await connection.QuerySingleOrDefaultAsync<DragonLevels>(new CommandDefinition(
                "SELECT tier AS Tier, level AS Level FROM player_dragons WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (before is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminDragonSetResult(AdminDragonSetStatus.NoDragon);
            }

            int cap = DragonRules.LevelCap(before.Tier);
            if (level > cap)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdminDragonSetResult(AdminDragonSetStatus.AboveCap, before.Tier, before.Level, before.Tier, before.Level, LevelCap: cap);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE player_dragons SET level = @Level WHERE discord_id = @DiscordId;",
                new { DiscordId = (long)discordId, Level = level }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new AdminDragonSetResult(AdminDragonSetStatus.Set, before.Tier, before.Level, before.Tier, level, LevelCap: cap);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
