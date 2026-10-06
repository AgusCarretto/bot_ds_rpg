using System.Data;
using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
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
        // la clase existente (/start; /class pasa por TrySetClassWhileFreshAsync).
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

    public async Task<User?> TrySetClassWhileFreshAsync(ulong discordId, string className, CancellationToken cancellationToken = default)
    {
        // La condición va en el WHERE (la misma de FuegoNuevoRules.CanChangeClass: nivel 1 y 0 de EXP): si ya no se cumple no vuelve ninguna fila y no se toca nada.
        string sql = $"""
            UPDATE users SET class = @Class
            WHERE discord_id = @DiscordId AND level <= 1 AND xp <= 0
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, Class = className },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(command);
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
            SET current_zone_id = @ZoneId, in_gate = false
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

    public async Task<User> SetInGateAsync(ulong discordId, bool inGate, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            UPDATE users
            SET in_gate = @InGate
            WHERE discord_id = @DiscordId
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<User>(new CommandDefinition(
            sql, new { DiscordId = (long)discordId, InGate = inGate }, cancellationToken: cancellationToken));
    }

    public async Task<DeathPenaltyOutcome?> ApplyDeathPenaltyAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        // UNA sola sentencia: el CTE toma la fila con FOR UPDATE y el UPDATE usa los valores de ANTES para calcular lo que se pierde, así que dos derrotas
        // simultáneas se aplican una detrás de otra (nunca se pisan) y lo que se informa es exactamente lo que se descontó. Solo toca la billetera (gold) y la
        // EXP del nivel actual: el banco (bank_gold) y el nivel quedan como están. El porcentaje es el mismo de GameData/DeathPenalty.cs.
        const string sql = """
            WITH old AS (
                SELECT discord_id, gold, xp FROM users WHERE discord_id = @DiscordId FOR UPDATE
            )
            UPDATE users u
            SET xp = 0,
                gold = u.gold - (old.gold::bigint * @GoldPercent / 100)::integer
            FROM old
            WHERE u.discord_id = old.discord_id
            RETURNING (old.gold - u.gold) AS "GoldLost", old.xp AS "XpLost", u.gold AS "GoldAfter";
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<DeathPenaltyOutcome>(new CommandDefinition(
            sql, new { DiscordId = (long)discordId, GoldPercent = DeathPenalty.GoldPercent }, cancellationToken: cancellationToken));
    }
}
