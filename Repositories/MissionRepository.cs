using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class MissionRepository(IDbConnectionFactory connectionFactory) : IMissionRepository
{
    public async Task<IReadOnlyDictionary<string, long>> GetProgressAsync(
        ulong discordId, DateTime sinceUtc, IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default)
    {
        if (kinds.Count == 0)
        {
            return new Dictionary<string, long>();
        }

        await using DbConnection connection = connectionFactory.CreateConnection();

        // OJO: SUM(bigint) es numeric en Postgres y Dapper no lo convierte solo a long: de ahí el ::bigint (acá y en el cobro).
        var rows = await connection.QueryAsync<ProgressRow>(new CommandDefinition(
            """
            SELECT kind AS Kind, COALESCE(SUM(amount), 0)::bigint AS Total
            FROM game_events
            WHERE discord_id = @DiscordId AND occurred_at >= @Since AND kind = ANY(@Kinds)
            GROUP BY kind;
            """,
            new { DiscordId = (long)discordId, Since = sinceUtc, Kinds = kinds.ToArray() }, cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.Kind, r => r.Total);
    }

    public async Task<IReadOnlySet<string>> GetClaimedAsync(
        ulong discordId, MissionPeriod period, DateTime periodStartUtc, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var keys = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT mission_key
            FROM mission_claims
            WHERE discord_id = @DiscordId AND period = @Period AND period_start = @Start;
            """,
            new { DiscordId = (long)discordId, Period = MissionCatalog.PeriodKey(period), Start = periodStartUtc },
            cancellationToken: cancellationToken));

        return keys.ToHashSet();
    }

    public async Task<ClaimOutcome> ClaimMissionAsync(
        ulong discordId, MissionTemplate mission, DateTime periodStartUtc, DateTime periodEndUtc, int zoneRank,
        CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Bloquear la fila del jugador primero: serializa los cobros de la misma persona (un doble click o dos botones a la vez
        // hacen fila en vez de pisarse) y es de donde sale su nivel para el XP.
        var user = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
        if (user is null)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        // Un botón viejo de ayer no puede cobrar la misión de ayer: el período ya cerró.
        if (DateTime.UtcNow >= periodEndUtc)
        {
            return new ClaimOutcome(ClaimStatus.Expired);
        }

        // La meta se comprueba con los eventos de la base, no con lo que mostró la pantalla: un botón armado antes de completar la
        // misión no la cobra, y nadie puede inventar un cobro.
        long progress = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(amount), 0)::bigint
            FROM game_events
            WHERE discord_id = @DiscordId AND kind = @Kind AND occurred_at >= @Start AND occurred_at < @End;
            """,
            new { DiscordId = (long)discordId, mission.Kind, Start = periodStartUtc, End = periodEndUtc },
            transaction: transaction, cancellationToken: cancellationToken));

        if (progress < mission.Target)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        if (!await TryMarkClaimedAsync(connection, transaction, discordId, mission.Period, periodStartUtc, mission.Key, cancellationToken))
        {
            return new ClaimOutcome(ClaimStatus.AlreadyClaimed);
        }

        var receipt = await RewardPayer.PayAsync(connection, transaction, user, mission.Reward, zoneRank, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ClaimOutcome(ClaimStatus.Claimed, receipt);
    }

    public async Task<ClaimOutcome> ClaimBonusAsync(
        ulong discordId, MissionPeriod period, IReadOnlyList<MissionTemplate> missions, DateTime periodStartUtc, DateTime periodEndUtc,
        int zoneRank, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var user = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
        if (user is null)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        if (DateTime.UtcNow >= periodEndUtc)
        {
            return new ClaimOutcome(ClaimStatus.Expired);
        }

        // Se cobra el premio mayor solo cuando ya cobró TODAS las misiones del período (cada una pasó por su propia meta).
        int claimed = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM mission_claims
            WHERE discord_id = @DiscordId AND period = @Period AND period_start = @Start AND mission_key = ANY(@Keys);
            """,
            new
            {
                DiscordId = (long)discordId, Period = MissionCatalog.PeriodKey(period), Start = periodStartUtc,
                Keys = missions.Select(m => m.Key).ToArray(),
            },
            transaction: transaction, cancellationToken: cancellationToken));

        if (missions.Count == 0 || claimed < missions.Count)
        {
            return new ClaimOutcome(ClaimStatus.NotCompleted);
        }

        if (!await TryMarkClaimedAsync(connection, transaction, discordId, period, periodStartUtc, MissionCatalog.BonusKey, cancellationToken))
        {
            return new ClaimOutcome(ClaimStatus.AlreadyClaimed);
        }

        var receipt = await RewardPayer.PayAsync(connection, transaction, user, MissionCatalog.BonusFor(period), zoneRank, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ClaimOutcome(ClaimStatus.Claimed, receipt);
    }

    // INSERT ... ON CONFLICT DO NOTHING RETURNING: si la fila ya estaba, no devuelve nada y el cobro se descarta. Es la guarda
    // atómica de siempre (ver CLAUDE.md, "Transactions"): sin lectura-y-después-escritura que dos cobros puedan ganar a la vez.
    private static async Task<bool> TryMarkClaimedAsync(
        DbConnection connection, DbTransaction transaction, ulong discordId, MissionPeriod period, DateTime periodStartUtc, string key,
        CancellationToken cancellationToken)
    {
        int? inserted = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            INSERT INTO mission_claims (discord_id, period, period_start, mission_key)
            VALUES (@DiscordId, @Period, @Start, @Key)
            ON CONFLICT DO NOTHING
            RETURNING 1;
            """,
            new { DiscordId = (long)discordId, Period = MissionCatalog.PeriodKey(period), Start = periodStartUtc, Key = key },
            transaction: transaction, cancellationToken: cancellationToken));

        return inserted is not null;
    }

    private sealed record ProgressRow(string Kind, long Total);
}
