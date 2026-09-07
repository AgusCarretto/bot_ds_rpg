using System.Data;
using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class UserRepository(IDbConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<User?> GetByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {UserSql.SelectColumns}
            FROM users
            WHERE discord_id = @DiscordId;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { DiscordId = (long)discordId }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(command);
    }

    public async Task<User> GetOrCreateUserAsync(ulong discordId, string? chosenClass = null, CancellationToken cancellationToken = default)
    {
        // Upsert atómico en una sola ida a la base: si el discord_id ya existe, el DO UPDATE
        // es un no-op sobre su propia PK y RETURNING nos trae la fila existente tal cual está;
        // si no existe, la INSERT la crea con los valores iniciales. Esto evita condiciones de
        // carrera si el jugador dispara dos comandos casi al mismo tiempo (ej. doble click).
        string sql = $"""
            INSERT INTO users (discord_id, class, level, xp, gold, max_hp, current_hp)
            VALUES (@DiscordId, COALESCE(@Class, 'Guerrero'), 1, 0, 50, 100, 100)
            ON CONFLICT (discord_id) DO UPDATE SET discord_id = EXCLUDED.discord_id
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, Class = chosenClass },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<User>(command);
    }

    public async Task<User> SetClassAsync(ulong discordId, string className, CancellationToken cancellationToken = default)
    {
        // Mismo upsert atómico que GetOrCreateUserAsync, pero acá el DO UPDATE sí pisa
        // la clase existente: /class permite al jugador re-elegir clase en cualquier momento.
        string sql = $"""
            INSERT INTO users (discord_id, class, level, xp, gold, max_hp, current_hp)
            VALUES (@DiscordId, @Class, 1, 0, 50, 100, 100)
            ON CONFLICT (discord_id) DO UPDATE SET class = EXCLUDED.class
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, Class = className },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<User>(command);
    }

    public async Task<User> RestoreHpAsync(ulong discordId, int hpRestored, CancellationToken cancellationToken = default)
    {
        // Sin costo de oro ni guarda de "alcanza o no": el llamador (/heal o /use) ya validó y
        // descontó el consumible del inventario antes de llegar acá, así que esto siempre aplica.
        string sql = $"""
            UPDATE users
            SET current_hp = LEAST(max_hp, current_hp + @HpRestored)
            WHERE discord_id = @DiscordId
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, HpRestored = hpRestored },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<User>(command);
    }

    public Task<User> EquipWeaponAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default) =>
        SetEquippedSlotAsync(discordId, "weapon_id", itemId, cancellationToken);

    public Task<User> EquipAmuletAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default) =>
        SetEquippedSlotAsync(discordId, "amulet_id", itemId, cancellationToken);

    private async Task<User> SetEquippedSlotAsync(ulong discordId, string columnName, int itemId, CancellationToken cancellationToken)
    {
        // columnName viene fijo desde EquipWeaponAsync/EquipAmuletAsync (nunca de input de usuario),
        // por eso es seguro interpolarlo directo en el SQL en vez de parametrizarlo.
        string sql = $"""
            UPDATE users
            SET {columnName} = @ItemId
            WHERE discord_id = @DiscordId
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, ItemId = itemId },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<User>(command);
    }

    public async Task<IReadOnlyList<LeaderboardEntry>> GetTopPlayersAsync(int limit, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT discord_id AS "DiscordId", class AS "Class", level AS "Level", xp AS "Xp"
            FROM users
            ORDER BY level DESC, xp DESC
            LIMIT @Limit;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { Limit = limit }, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<LeaderboardEntry>(command);
        return rows.AsList();
    }

    public async Task<User> ApplyCombatHpDeltaAsync(ulong discordId, int hpDelta, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila hasta el commit para que el delta se aplique sobre el
            // HP más reciente en base (no sobre el snapshot en memoria que tenía el combate al
            // arrancar), y ninguna operación concurrente sobre la misma fila lo pise.
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            int newHp = Math.Clamp(current.CurrentHp + hpDelta, 0, current.MaxHp);

            string updateSql = $"""
                UPDATE users
                SET current_hp = @CurrentHp
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateSql, new { DiscordId = (long)discordId, CurrentHp = newHp }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<User> ChangeZoneAsync(ulong discordId, int zoneId, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            UPDATE users
            SET current_zone_id = @ZoneId
            WHERE discord_id = @DiscordId
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, ZoneId = zoneId },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<User>(command);
    }
}
