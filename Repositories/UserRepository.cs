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

    public async Task<LevelUpOutcome> AddXpAsync(ulong discordId, int xpGained, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila hasta el commit para que otra operación concurrente
            // sobre el mismo usuario (ej. /heal o un /hunt en simultáneo) no pise este cálculo.
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            var leveling = LevelingCalculator.ApplyXpGain(current.Level, current.Xp, current.MaxHp, current.CurrentHp, xpGained);

            string updateSql = $"""
                UPDATE users
                SET level = @Level, xp = @Xp, max_hp = @MaxHp, current_hp = @CurrentHp
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateSql,
                new { DiscordId = (long)discordId, Level = leveling.Level, Xp = leveling.Xp, MaxHp = leveling.MaxHp, CurrentHp = leveling.CurrentHp },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new LevelUpOutcome(updated, leveling.LevelsGained);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<User?> HealAsync(ulong discordId, int goldCost, int hpRestored, CancellationToken cancellationToken = default)
    {
        // Update guardado: si no le alcanza el oro, el WHERE bloquea la actualización y no
        // devuelve fila (atómico, no hace falta un SELECT previo ni transacción explícita).
        string sql = $"""
            UPDATE users
            SET gold = gold - @GoldCost,
                current_hp = LEAST(max_hp, current_hp + @HpRestored)
            WHERE discord_id = @DiscordId AND gold >= @GoldCost
            RETURNING {UserSql.SelectColumns};
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, GoldCost = goldCost, HpRestored = hpRestored },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(command);
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

    public async Task<DailyClaimOutcome> ClaimDailyAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila hasta el commit para que otra operación concurrente
            // sobre el mismo usuario no pise este cálculo (ej. doble click en /daily).
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            var now = DateTime.UtcNow;
            var calculation = DailyRewardCalculator.Evaluate(current.LastDailyClaim, current.DailyStreak, now);

            if (calculation.Status == DailyClaimStatus.TooSoon)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DailyClaimOutcome(calculation, null);
            }

            var leveling = LevelingCalculator.ApplyXpGain(current.Level, current.Xp, current.MaxHp, current.CurrentHp, calculation.XpReward);

            string updateSql = $"""
                UPDATE users
                SET gold = gold + @Gold, level = @Level, xp = @Xp, max_hp = @MaxHp, current_hp = @CurrentHp,
                    daily_streak = @Streak, last_daily_claim = @Now
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateSql,
                new
                {
                    DiscordId = (long)discordId,
                    Gold = calculation.GoldReward,
                    Level = leveling.Level,
                    Xp = leveling.Xp,
                    MaxHp = leveling.MaxHp,
                    CurrentHp = leveling.CurrentHp,
                    Streak = calculation.NewStreak,
                    Now = now,
                },
                transaction: transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new DailyClaimOutcome(calculation, new LevelUpOutcome(updated, leveling.LevelsGained));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
}
