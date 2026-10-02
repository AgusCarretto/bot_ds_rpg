using System.Data.Common;
using System.Globalization;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ArenaRepository(IDbConnectionFactory connectionFactory) : IArenaRepository
{
    // El día viaja como texto "yyyy-MM-dd" y se castea a date en el SQL: evita depender de cómo Dapper/Npgsql mapean DateOnly.
    private static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<ArenaJoinStatus> JoinAsync(
        DateOnly day, ulong discordId, string displayName, ulong channelId, int maxPlayers, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        bool hasAccount = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM users WHERE discord_id = @DiscordId);",
            new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
        if (!hasAccount)
        {
            return ArenaJoinStatus.NoAccount;
        }

        // El primero que se anota crea el día (y deja el canal para anunciar); después se bloquea la fila: los anotados de un mismo día
        // pasan de a uno, así el cupo no se pasa aunque lleguen 40 a la vez.
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO arena_days (day, channel_id) VALUES (@Day::date, @ChannelId) ON CONFLICT (day) DO NOTHING;",
            new { Day = Key(day), ChannelId = (long)channelId }, transaction: transaction, cancellationToken: cancellationToken));

        string? status = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT status FROM arena_days WHERE day = @Day::date FOR UPDATE;",
            new { Day = Key(day) }, transaction: transaction, cancellationToken: cancellationToken));
        if (status != "open")
        {
            return ArenaJoinStatus.Closed;
        }

        bool already = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM arena_entries WHERE day = @Day::date AND discord_id = @DiscordId);",
            new { Day = Key(day), DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
        if (already)
        {
            return ArenaJoinStatus.AlreadyJoined;
        }

        int count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*)::int FROM arena_entries WHERE day = @Day::date;",
            new { Day = Key(day) }, transaction: transaction, cancellationToken: cancellationToken));
        if (count >= maxPlayers)
        {
            return ArenaJoinStatus.Full;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO arena_entries (day, discord_id, display_name) VALUES (@Day::date, @DiscordId, @Name);",
            new { Day = Key(day), DiscordId = (long)discordId, Name = displayName }, transaction: transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return ArenaJoinStatus.Joined;
    }

    public async Task<IReadOnlyList<ArenaEntry>> GetEntriesAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            """
            SELECT e.discord_id AS DiscordId, e.display_name AS DisplayName, u.class AS PlayerClass, u.level AS Level, e.joined_at AS JoinedAt
            FROM arena_entries e
            JOIN users u ON u.discord_id = e.discord_id
            WHERE e.day = @Day::date
            ORDER BY e.joined_at, e.discord_id;
            """,
            new { Day = Key(day) }, cancellationToken: cancellationToken));

        return rows
            .Select(r => new ArenaEntry((ulong)r.DiscordId, r.DisplayName, r.PlayerClass, r.Level, DateTime.SpecifyKind(r.JoinedAt, DateTimeKind.Utc)))
            .ToList();
    }

    private const string DayColumns =
        "to_char(day, 'YYYY-MM-DD') AS Day, status AS Status, channel_id AS ChannelId, winner_id AS WinnerId, winner_name AS WinnerName, " +
        "participants AS Participants, rounds AS Rounds, reward_text AS RewardText, resolved_at AS ResolvedAt";

    public async Task<ArenaDayInfo?> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<DayRow>(new CommandDefinition(
            $"SELECT {DayColumns} FROM arena_days WHERE day = @Day::date;",
            new { Day = Key(day) }, cancellationToken: cancellationToken));

        return row is null ? null : ToInfo(row);
    }

    public async Task<ArenaDayInfo?> GetLatestResolvedAsync(CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<DayRow>(new CommandDefinition(
            $"SELECT {DayColumns} FROM arena_days WHERE status = 'resolved' ORDER BY day DESC LIMIT 1;",
            cancellationToken: cancellationToken));

        return row is null ? null : ToInfo(row);
    }

    public async Task<IReadOnlyList<ArenaMatchRow>> GetMatchesAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<MatchRow>(new CommandDefinition(
            """
            SELECT round AS Round, slot AS Slot, p1_id AS P1Id, p1_name AS P1Name, p2_id AS P2Id, p2_name AS P2Name,
                   winner_id AS WinnerId, actions AS Actions, winner_hp_pct AS WinnerHpPercent
            FROM arena_matches
            WHERE day = @Day::date
            ORDER BY round, slot;
            """,
            new { Day = Key(day) }, cancellationToken: cancellationToken));

        return rows
            .Select(r => new ArenaMatchRow(r.Round, r.Slot, (ulong)r.P1Id, r.P1Name, r.P2Id is long p2 ? (ulong)p2 : null, r.P2Name, (ulong)r.WinnerId, r.Actions, r.WinnerHpPercent))
            .ToList();
    }

    public async Task<IReadOnlyList<DateOnly>> GetOpenDaysBeforeAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var days = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT to_char(day, 'YYYY-MM-DD') FROM arena_days WHERE status = 'open' AND day < @Today::date ORDER BY day;",
            new { Today = Key(today) }, cancellationToken: cancellationToken));

        return days.Select(ParseDay).ToList();
    }

    public async Task<bool> CancelDayAsync(DateOnly day, int participants, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        int? updated = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            UPDATE arena_days
            SET status = 'cancelled', participants = @Participants, resolved_at = now()
            WHERE day = @Day::date AND status = 'open'
            RETURNING 1;
            """,
            new { Day = Key(day), Participants = participants }, cancellationToken: cancellationToken));

        return updated is not null;
    }

    public async Task<ArenaResolveOutcome> ResolveDayAsync(
        DateOnly day, IReadOnlyList<ArenaMatchRow> matches, int participants, int rounds, ulong winnerId, string winnerName,
        RewardSpec reward, int zoneRank, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // La guarda: solo cierra el día quien lo encuentra abierto. Si el scheduler y un /arena results se cruzan, uno solo llega a pagar.
        int? closed = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            UPDATE arena_days
            SET status = 'resolved', winner_id = @WinnerId, winner_name = @WinnerName, participants = @Participants,
                rounds = @Rounds, resolved_at = now()
            WHERE day = @Day::date AND status = 'open'
            RETURNING 1;
            """,
            new { Day = Key(day), WinnerId = (long)winnerId, WinnerName = winnerName, Participants = participants, Rounds = rounds },
            transaction: transaction, cancellationToken: cancellationToken));

        if (closed is null)
        {
            return new ArenaResolveOutcome(false, null);
        }

        foreach (var match in matches)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO arena_matches (day, round, slot, p1_id, p1_name, p2_id, p2_name, winner_id, actions, winner_hp_pct)
                VALUES (@Day::date, @Round, @Slot, @P1Id, @P1Name, @P2Id, @P2Name, @WinnerId, @Actions, @WinnerHpPercent);
                """,
                new
                {
                    Day = Key(day), match.Round, match.Slot, P1Id = (long)match.P1Id, match.P1Name,
                    P2Id = match.P2Id is ulong p2 ? (long?)p2 : null, match.P2Name, WinnerId = (long)match.WinnerId, match.Actions, match.WinnerHpPercent,
                },
                transaction: transaction, cancellationToken: cancellationToken));
        }

        RewardReceipt? receipt = null;

        // Bloquear al campeón primero (de ahí sale su nivel para el XP). Si borró su cuenta mientras tanto, el torneo igual queda jugado
        // y simplemente no hay a quién pagarle.
        var champion = await RewardPayer.LockUserAsync(connection, transaction, winnerId, cancellationToken);
        if (champion is not null)
        {
            receipt = await RewardPayer.PayAsync(connection, transaction, champion, reward, zoneRank, cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE arena_days SET reward_text = @Text WHERE day = @Day::date;",
                new { Day = Key(day), Text = MissionRewards.Describe(receipt.Reward) }, transaction: transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return new ArenaResolveOutcome(true, receipt);
    }

    private static ArenaDayInfo ToInfo(DayRow r) => new(
        ParseDay(r.Day), r.Status, r.ChannelId is long c ? (ulong)c : null, r.WinnerId is long w ? (ulong)w : null, r.WinnerName,
        r.Participants, r.Rounds, r.RewardText, r.ResolvedAt is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null);

    private sealed class EntryRow
    {
        public long DiscordId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string PlayerClass { get; init; } = string.Empty;
        public int Level { get; init; }
        public DateTime JoinedAt { get; init; }
    }

    private sealed class DayRow
    {
        public string Day { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public long? ChannelId { get; init; }
        public long? WinnerId { get; init; }
        public string? WinnerName { get; init; }
        public int Participants { get; init; }
        public int Rounds { get; init; }
        public string? RewardText { get; init; }
        public DateTime? ResolvedAt { get; init; }
    }

    private sealed class MatchRow
    {
        public int Round { get; init; }
        public int Slot { get; init; }
        public long P1Id { get; init; }
        public string P1Name { get; init; } = string.Empty;
        public long? P2Id { get; init; }
        public string? P2Name { get; init; }
        public long WinnerId { get; init; }
        public int Actions { get; init; }
        public int WinnerHpPercent { get; init; }
    }
}
