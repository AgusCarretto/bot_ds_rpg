using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

// Cómo está el Muñeco ahora: la vuelta, su vida, cuántas veces cayó y, del que mira, cuánto le pegó en esta vuelta y en qué puesto va (0 = todavía no le pegó).
public sealed record SnowmanState(int Round, int Hp, int MaxHp, int Kills, long MyDamage, int MyRank, int Participants);

public enum SnowThrowStatus
{
    Ok,
    NotEnoughGifts,
    Dead,          // cayó y no vuelve (se terminó el farmeo): no se cobra nada
}

// Lo que pasó al morir: la vuelta que cayó, quién dio el último golpe, cuántos cobraron, el top 10 y la vida de la vuelta nueva (null si no vuelve).
public sealed record SnowmanKill(int Round, ulong KillerId, int Participants, IReadOnlyList<SnowmanRank> Top, int? NextMaxHp);

public sealed record SnowThrowOutcome(
    SnowThrowStatus Status, SnowProjectile Projectile, int Count = 0, int Damage = 0, long GiftsAfter = 0, SnowmanState? State = null, SnowmanKill? Kill = null);

// El Muñeco de Nieve Gigante (v0.57.0): tablas event_snowman y event_snowman_hits (Database/add_christmas.sql).
public interface ISnowmanRepository
{
    // El estado de la vuelta actual (abre la primera con initialHp si todavía no existe).
    Task<SnowmanState> GetStateAsync(ulong viewerId, string season, int initialHp, CancellationToken cancellationToken = default);

    // Tira count proyectiles: cobra los Regalos (update guardado), baja la vida y anota el daño, todo en UNA transacción con la fila del Muñeco bloqueada (dos tiros a la vez
    // no se pisan y uno solo da el último golpe). Si muere, en la MISMA transacción paga a todos los de esa vuelta y, si canRespawn, abre la siguiente con ×1,5 de vida.
    Task<SnowThrowOutcome> ThrowAsync(
        ulong discordId, string season, SnowProjectile projectile, int count, int initialHp, bool canRespawn, CancellationToken cancellationToken = default);
}

public sealed class SnowmanRepository(IDbConnectionFactory connectionFactory) : ISnowmanRepository
{
    private sealed record Row(int Round, int Hp, int MaxHp);

    public async Task<SnowmanState> GetStateAsync(ulong viewerId, string season, int initialHp, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureFirstRoundAsync(connection, null, season, initialHp, cancellationToken);
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            "SELECT round AS \"Round\", hp AS \"Hp\", max_hp AS \"MaxHp\" FROM event_snowman WHERE season = @Season ORDER BY round DESC LIMIT 1;",
            new { Season = season }, cancellationToken: cancellationToken));
        return await StateAsync(connection, null, viewerId, season, row, cancellationToken);
    }

    public async Task<SnowThrowOutcome> ThrowAsync(
        ulong discordId, string season, SnowProjectile projectile, int count, int initialHp, bool canRespawn, CancellationToken cancellationToken = default)
    {
        int n = Math.Clamp(count, 1, SnowmanRules.MaxThrowsPerCommand);
        long cost = (long)projectile.Price * n;
        int damage = projectile.Damage * n;

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await EnsureFirstRoundAsync(connection, null, season, initialHp, cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La vuelta actual, bloqueada: todos los tiros pasan de a uno por acá.
            var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
                "SELECT round AS \"Round\", hp AS \"Hp\", max_hp AS \"MaxHp\" FROM event_snowman WHERE season = @Season ORDER BY round DESC LIMIT 1 FOR UPDATE;",
                new { Season = season }, transaction: transaction, cancellationToken: cancellationToken));
            // Si esperó el bloqueo mientras otro tiro lo mataba, la fila que agarró es la vuelta vieja (caída): se vuelve a leer, que ahora ve la vuelta nueva si la hay.
            if (row.Hp <= 0)
            {
                row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
                    "SELECT round AS \"Round\", hp AS \"Hp\", max_hp AS \"MaxHp\" FROM event_snowman WHERE season = @Season ORDER BY round DESC LIMIT 1 FOR UPDATE;",
                    new { Season = season }, transaction: transaction, cancellationToken: cancellationToken));
            }

            if (row.Hp <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SnowThrowOutcome(SnowThrowStatus.Dead, projectile);
            }

            long? giftsAfter = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                """
                UPDATE event_gifts SET balance = balance - @Cost
                WHERE discord_id = @DiscordId AND season = @Season AND balance >= @Cost
                RETURNING balance;
                """,
                new { DiscordId = (long)discordId, Season = season, Cost = cost }, transaction: transaction, cancellationToken: cancellationToken));
            if (giftsAfter is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new SnowThrowOutcome(SnowThrowStatus.NotEnoughGifts, projectile, n);
            }

            // El daño que cuenta es el que entró (el último golpe no "sobra" en el ranking).
            int applied = Math.Min(damage, row.Hp);
            int hpAfter = row.Hp - applied;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE event_snowman SET hp = @Hp WHERE season = @Season AND round = @Round;",
                new { Hp = hpAfter, Season = season, row.Round }, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO event_snowman_hits (season, round, discord_id, damage, spent) VALUES (@Season, @Round, @DiscordId, @Damage, @Cost)
                ON CONFLICT (season, round, discord_id) DO UPDATE
                    SET damage = event_snowman_hits.damage + EXCLUDED.damage, spent = event_snowman_hits.spent + EXCLUDED.spent;
                """,
                new { Season = season, row.Round, DiscordId = (long)discordId, Damage = (long)applied, Cost = cost }, transaction: transaction, cancellationToken: cancellationToken));
            await AddStatAsync(connection, transaction, "SELECT @DiscordId AS discord_id, @Amount::bigint AS amount", SnowmanRules.DamageStat,
                new { DiscordId = (long)discordId, Amount = (long)applied }, cancellationToken);

            SnowmanKill? kill = null;
            var current = row with { Hp = hpAfter };
            if (hpAfter == 0)
            {
                kill = await PayKillAsync(connection, transaction, discordId, season, row, canRespawn, cancellationToken);
                if (kill.NextMaxHp is int next)
                {
                    current = new Row(row.Round + 1, next, next);
                }
            }

            var state = await StateAsync(connection, transaction, discordId, season, current, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SnowThrowOutcome(SnowThrowStatus.Ok, projectile, n, applied, giftsAfter.Value, state, kill);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Paga la vuelta que cayó a TODOS los que le pegaron, de una (sentencias por conjunto, no una por jugador). El ranking: más daño primero; empate, el id más chico.
    private static async Task<SnowmanKill> PayKillAsync(
        DbConnection connection, DbTransaction transaction, ulong killerId, string season, Row row, bool canRespawn, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE event_snowman SET killed_at = now(), killer_id = @Killer WHERE season = @Season AND round = @Round;",
            new { Killer = (long)killerId, Season = season, row.Round }, transaction: transaction, cancellationToken: cancellationToken));

        const string ranked =
            """
            SELECT discord_id, damage, ROW_NUMBER() OVER (ORDER BY damage DESC, discord_id) AS rn
            FROM event_snowman_hits WHERE season = @Season AND round = @Round AND damage > 0
            """;
        var args = new
        {
            Season = season,
            row.Round,
            All = (long)SnowmanRules.AllGifts,
            Top = (long)SnowmanRules.Top10Gifts,
            TopN = SnowmanRules.TopRewarded,
            TopEscoria = SnowmanRules.TopEscoria,
            AllBox = SnowmanRules.AllBox,
            TopBox = SnowmanRules.TopBox,
            PerDust = SnowmanRules.DamagePerDust,
            MinDust = SnowmanRules.MinDust,
            MaxDust = SnowmanRules.MaxDust,
        };

        // Regalos: todos AllGifts y el top 10 además Top10Gifts.
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            WITH r AS ({ranked})
            INSERT INTO event_gifts (discord_id, season, balance, earned)
            SELECT discord_id, @Season, @All + CASE WHEN rn <= @TopN THEN @Top ELSE 0 END, @All + CASE WHEN rn <= @TopN THEN @Top ELSE 0 END FROM r
            ON CONFLICT (discord_id, season) DO UPDATE SET balance = event_gifts.balance + EXCLUDED.balance, earned = event_gifts.earned + EXCLUDED.earned;
            """, args, transaction: transaction, cancellationToken: cancellationToken));

        // Un Cofre de Oro para todos y un Cofre de Escoria para el top 3. Una caja que no existe es un error de datos: tira y se revierte todo (el Muñeco no muere a medias).
        int boxes = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            WITH r AS ({ranked})
            INSERT INTO inventory (discord_id, item_id, quantity)
            SELECT r.discord_id, i.item_id, 1 FROM r JOIN items i ON i.name = @AllBox
            UNION ALL
            SELECT r.discord_id, i.item_id, 1 FROM r JOIN items i ON i.name = @TopBox WHERE r.rn <= @TopEscoria
            ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + EXCLUDED.quantity;
            """, args, transaction: transaction, cancellationToken: cancellationToken));

        // Polvo según el daño (1 cada 10, entre 10 y 500).
        await connection.ExecuteAsync(new CommandDefinition(
            $"""
            WITH r AS ({ranked})
            UPDATE users u SET dust = u.dust + LEAST(GREATEST(r.damage / @PerDust, @MinDust), @MaxDust)
            FROM r WHERE u.discord_id = r.discord_id;
            """, args, transaction: transaction, cancellationToken: cancellationToken));

        // El contador de vueltas ganadas (los títulos).
        await AddStatAsync(connection, transaction, $"SELECT discord_id, 1::bigint AS amount FROM ({ranked}) r", SnowmanRules.KillStat, args, cancellationToken);

        var everyone = (await connection.QueryAsync<(long DiscordId, long Damage, long Rn)>(new CommandDefinition(
            ranked + " ORDER BY rn;", args, transaction: transaction, cancellationToken: cancellationToken))).ToList();
        if (boxes != everyone.Count + Math.Min(SnowmanRules.TopEscoria, everyone.Count))
        {
            throw new InvalidOperationException($"El premio del Muñeco pide «{SnowmanRules.AllBox}» y «{SnowmanRules.TopBox}», pero no están en items.");
        }

        int? next = null;
        if (canRespawn)
        {
            next = SnowmanRules.RespawnHp(row.MaxHp);
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO event_snowman (season, round, max_hp, hp) VALUES (@Season, @Round, @Hp, @Hp);",
                new { Season = season, Round = row.Round + 1, Hp = next.Value }, transaction: transaction, cancellationToken: cancellationToken));
        }

        var top = everyone.Take(SnowmanRules.TopRewarded).Select(e => new SnowmanRank((ulong)e.DiscordId, e.Damage, (int)e.Rn)).ToList();
        return new SnowmanKill(row.Round, killerId, everyone.Count, top, next);
    }

    // Suma a un contador de player_stats para cada fila de "source" (discord_id, amount).
    private static Task AddStatAsync(DbConnection connection, DbTransaction transaction, string source, string statKey, object args, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(args);
        parameters.Add("StatKey", statKey);
        return connection.ExecuteAsync(new CommandDefinition(
            $"""
            INSERT INTO player_stats (discord_id, stat_key, value)
            SELECT s.discord_id, @StatKey, s.amount FROM ({source}) s
            ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = player_stats.value + EXCLUDED.value;
            """, parameters, transaction: transaction, cancellationToken: cancellationToken));
    }

    private static Task EnsureFirstRoundAsync(DbConnection connection, DbTransaction? transaction, string season, int initialHp, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO event_snowman (season, round, max_hp, hp) VALUES (@Season, 1, @Hp, @Hp) ON CONFLICT (season, round) DO NOTHING;",
            new { Season = season, Hp = Math.Max(1, initialHp) }, transaction: transaction, cancellationToken: cancellationToken));

    private static async Task<SnowmanState> StateAsync(
        DbConnection connection, DbTransaction? transaction, ulong viewerId, string season, Row row, CancellationToken cancellationToken)
    {
        var info = await connection.QuerySingleAsync<(long Kills, long MyDamage, long MyRank, long Participants)>(new CommandDefinition(
            """
            SELECT
                (SELECT count(*) FROM event_snowman WHERE season = @Season AND killed_at IS NOT NULL),
                COALESCE((SELECT damage FROM event_snowman_hits WHERE season = @Season AND round = @Round AND discord_id = @Viewer), 0),
                COALESCE((SELECT 1 + count(*) FROM event_snowman_hits h
                          WHERE h.season = @Season AND h.round = @Round AND h.damage > (SELECT damage FROM event_snowman_hits WHERE season = @Season AND round = @Round AND discord_id = @Viewer)), 0),
                (SELECT count(*) FROM event_snowman_hits WHERE season = @Season AND round = @Round AND damage > 0);
            """,
            new { Season = season, row.Round, Viewer = (long)viewerId }, transaction: transaction, cancellationToken: cancellationToken));
        int myRank = info.MyDamage > 0 ? (int)info.MyRank : 0;
        return new SnowmanState(row.Round, row.Hp, row.MaxHp, (int)info.Kills, info.MyDamage, myRank, (int)info.Participants);
    }
}
