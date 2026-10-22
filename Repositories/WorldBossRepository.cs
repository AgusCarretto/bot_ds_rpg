using System.Data.Common;
using System.Globalization;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class WorldBossRepository(IDbConnectionFactory connectionFactory) : IWorldBossRepository
{
    // El día viaja como texto «yyyy-MM-dd» y se castea a date en el SQL (igual que la Arena): evita depender de cómo Dapper/Npgsql mapean DateOnly.
    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<WorldBossJoinOutcome> JoinAsync(
        DateOnly day, ulong discordId, string displayName, ulong channelId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        bool hasAccount = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM users WHERE discord_id = @DiscordId);",
            new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
        if (!hasAccount)
        {
            return new WorldBossJoinOutcome(WorldBossJoinStatus.NoAccount, TimeSpan.Zero);
        }

        // El primero que se anota crea el día (y deja el canal para anunciar); después se bloquea la fila: los anotados de un mismo día pasan de a uno.
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO worldboss_days (day, channel_id) VALUES (@Day::date, @ChannelId) ON CONFLICT (day) DO NOTHING;",
            new { Day = Key(day), ChannelId = (long)channelId }, transaction: transaction, cancellationToken: cancellationToken));

        string? status = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT status FROM worldboss_days WHERE day = @Day::date FOR UPDATE;",
            new { Day = Key(day) }, transaction: transaction, cancellationToken: cancellationToken));
        if (status != "open")
        {
            return new WorldBossJoinOutcome(WorldBossJoinStatus.Closed, TimeSpan.Zero);
        }

        bool already = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM worldboss_entries WHERE day = @Day::date AND discord_id = @DiscordId);",
            new { Day = Key(day), DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
        if (already)
        {
            return new WorldBossJoinOutcome(WorldBossJoinStatus.AlreadyJoined, TimeSpan.Zero);
        }

        // Anotarse gasta el cooldown del jefe, con la misma guarda que /boss: si todavía corre no se anota (y la transacción se descarta sin tocar nada).
        var boss = CooldownCatalog.Boss;
        if (!await CooldownGuard.TryClaimAsync(connection, transaction, discordId, boss.CommandName, boss.Duration, cancellationToken))
        {
            double seconds = await connection.ExecuteScalarAsync<double?>(new CommandDefinition(
                "SELECT EXTRACT(EPOCH FROM (last_executed_at + @Interval - now()))::float8 FROM cooldowns WHERE discord_id = @DiscordId AND command_name = @CommandName;",
                new { DiscordId = (long)discordId, CommandName = boss.CommandName, Interval = boss.Duration }, transaction: transaction, cancellationToken: cancellationToken)) ?? 0;
            return new WorldBossJoinOutcome(WorldBossJoinStatus.OnCooldown, TimeSpan.FromSeconds(Math.Max(0, seconds)));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO worldboss_entries (day, discord_id, display_name) VALUES (@Day::date, @DiscordId, @Name);",
            new { Day = Key(day), DiscordId = (long)discordId, Name = displayName }, transaction: transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new WorldBossJoinOutcome(WorldBossJoinStatus.Joined, TimeSpan.Zero);
    }

    public async Task<IReadOnlyList<WorldBossEntry>> GetEntriesAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            "SELECT discord_id AS DiscordId, display_name AS DisplayName, joined_at AS JoinedAt FROM worldboss_entries WHERE day = @Day::date ORDER BY joined_at, discord_id;",
            new { Day = Key(day) }, cancellationToken: cancellationToken));

        return rows.Select(r => new WorldBossEntry((ulong)r.DiscordId, r.DisplayName, DateTime.SpecifyKind(r.JoinedAt, DateTimeKind.Utc))).ToList();
    }

    private const string DayColumns =
        "to_char(day, 'YYYY-MM-DD') AS Day, status AS Status, channel_id AS ChannelId, boss_key AS BossKey, participants AS Participants, " +
        "total_damage_pct::float8 AS TotalDamagePercent, defeated AS Defeated, hunts AS Hunts, resolved_at AS ResolvedAt";

    public async Task<WorldBossDayInfo?> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<DayRow>(new CommandDefinition(
            $"SELECT {DayColumns} FROM worldboss_days WHERE day = @Day::date;", new { Day = Key(day) }, cancellationToken: cancellationToken));
        return row is null ? null : ToInfo(row);
    }

    public async Task<WorldBossDayInfo?> GetLatestResolvedAsync(CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<DayRow>(new CommandDefinition(
            $"SELECT {DayColumns} FROM worldboss_days WHERE status = 'resolved' ORDER BY day DESC LIMIT 1;", cancellationToken: cancellationToken));
        return row is null ? null : ToInfo(row);
    }

    public async Task<IReadOnlyList<DateOnly>> GetOpenDaysBeforeAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var days = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT to_char(day, 'YYYY-MM-DD') FROM worldboss_days WHERE status = 'open' AND day < @Today::date ORDER BY day;",
            new { Today = Key(today) }, cancellationToken: cancellationToken));
        return days.Select(ParseDay).ToList();
    }

    public async Task<bool> CancelDayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        int? updated = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "UPDATE worldboss_days SET status = 'cancelled', resolved_at = now() WHERE day = @Day::date AND status = 'open' RETURNING 1;",
            new { Day = Key(day) }, cancellationToken: cancellationToken));
        return updated is not null;
    }

    public async Task<WorldBossResolveOutcome> ResolveDayAsync(
        DateOnly day, string bossKey, WorldBossOutcome outcome, IReadOnlyDictionary<ulong, int> zoneRanks, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // La guarda: solo cierra el día quien lo encuentra abierto. Si el reloj y otro proceso se cruzan, uno solo llega a pagar.
        int? closed = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            UPDATE worldboss_days
            SET status = 'resolved', boss_key = @BossKey, participants = @Participants, total_damage_pct = @Total, defeated = @Defeated, hunts = @Hunts, resolved_at = now()
            WHERE day = @Day::date AND status = 'open'
            RETURNING 1;
            """,
            new { Day = Key(day), BossKey = bossKey, Participants = outcome.Hits.Count, Total = (decimal)outcome.TotalPercent, outcome.Defeated, outcome.Hunts },
            transaction: transaction, cancellationToken: cancellationToken));
        if (closed is null)
        {
            return new WorldBossResolveOutcome(false, []);
        }

        var paid = new List<WorldBossPaid>();
        var spec = new RewardSpec(GoldUnits: outcome.Hunts, XpPercent: 0);
        foreach (var hit in outcome.Hits)
        {
            // Bloquear al jugador primero (de ahí sale su nivel para el pago). Si borró su cuenta mientras tanto, se salta.
            var user = await RewardPayer.LockUserAsync(connection, transaction, hit.DiscordId, cancellationToken);
            if (user is null)
            {
                continue;
            }

            int zoneRank = zoneRanks.TryGetValue(hit.DiscordId, out int rank) ? Math.Max(1, rank) : Math.Max(1, hit.ZoneRank);
            var receipt = await RewardPayer.PayAsync(connection, transaction, user, spec, zoneRank, cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE worldboss_entries SET damage_pct = @Damage, reward_gold = @Gold WHERE day = @Day::date AND discord_id = @DiscordId;",
                new { Day = Key(day), DiscordId = (long)hit.DiscordId, Damage = (decimal)hit.DamagePercent, Gold = receipt.Reward.Gold }, transaction: transaction, cancellationToken: cancellationToken));

            paid.Add(new WorldBossPaid(hit.DiscordId, hit.Name, zoneRank, hit.DamagePercent, receipt.Reward.Gold));
        }

        await transaction.CommitAsync(cancellationToken);
        return new WorldBossResolveOutcome(true, paid);
    }

    private static WorldBossDayInfo ToInfo(DayRow r) => new(
        ParseDay(r.Day), r.Status, r.ChannelId is long c ? (ulong)c : null, r.BossKey, r.Participants, r.TotalDamagePercent, r.Defeated, r.Hunts,
        r.ResolvedAt is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null);

    private sealed class EntryRow
    {
        public long DiscordId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public DateTime JoinedAt { get; init; }
    }

    private sealed class DayRow
    {
        public string Day { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public long? ChannelId { get; init; }
        public string? BossKey { get; init; }
        public int Participants { get; init; }
        public double? TotalDamagePercent { get; init; }
        public bool? Defeated { get; init; }
        public int? Hunts { get; init; }
        public DateTime? ResolvedAt { get; init; }
    }
}
