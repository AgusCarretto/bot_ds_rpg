using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// Los Regalos de la temporada de un jugador: lo que tiene para gastar y lo que juntó en total (el total no baja al gastar; es para logros y rankings).
public sealed record GiftBalance(long Balance, long Earned);

public enum AdventClaimStatus
{
    Ok,
    AlreadyClaimed,
    NoAccount,
}

// Receipt: lo que pagó el RewardPayer (oro/caja), null si ese día no traía nada de eso.
public sealed record AdventClaimOutcome(AdventClaimStatus Status, AdventDay? Day = null, long GiftsAfter = 0, RewardReceipt? Receipt = null);

// Navidad en el Fogón (v0.57.0): tablas event_gifts y event_advent_claims (Database/add_christmas.sql).
public interface IChristmasRepository
{
    Task<GiftBalance> GetAsync(ulong discordId, string season, CancellationToken cancellationToken = default);

    // Suma Regalos (amount > 0) y devuelve el saldo nuevo. Un upsert atómico: dos victorias a la vez suman las dos.
    Task<long> GrantAsync(ulong discordId, string season, int amount, CancellationToken cancellationToken = default);

    // Los días del calendario que ya reclamó en esta temporada.
    Task<IReadOnlySet<int>> GetClaimedDaysAsync(ulong discordId, string season, CancellationToken cancellationToken = default);

    // Reclama ESE día del calendario y paga todo en una sola transacción (Regalos, oro/caja con el RewardPayer, Polvo). Si ya lo reclamó, no paga nada.
    Task<AdventClaimOutcome> ClaimAdventAsync(ulong discordId, string season, AdventDay day, int zoneRank, CancellationToken cancellationToken = default);
}

public sealed class ChristmasRepository(IDbConnectionFactory connectionFactory) : IChristmasRepository
{
    private const string GrantSql =
        """
        INSERT INTO event_gifts (discord_id, season, balance, earned)
        VALUES (@DiscordId, @Season, @Amount, @Amount)
        ON CONFLICT (discord_id, season) DO UPDATE
            SET balance = event_gifts.balance + EXCLUDED.balance, earned = event_gifts.earned + EXCLUDED.earned
        RETURNING balance;
        """;

    public async Task<GiftBalance> GetAsync(ulong discordId, string season, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<GiftBalance>(new CommandDefinition(
            "SELECT balance AS \"Balance\", earned AS \"Earned\" FROM event_gifts WHERE discord_id = @DiscordId AND season = @Season;",
            new { DiscordId = (long)discordId, Season = season }, cancellationToken: cancellationToken)) ?? new GiftBalance(0, 0);
    }

    public async Task<long> GrantAsync(ulong discordId, string season, int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Solo se suman Regalos positivos.");
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            GrantSql, new { DiscordId = (long)discordId, Season = season, Amount = (long)amount }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlySet<int>> GetClaimedDaysAsync(ulong discordId, string season, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var days = await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT day::int FROM event_advent_claims WHERE discord_id = @DiscordId AND season = @Season;",
            new { DiscordId = (long)discordId, Season = season }, cancellationToken: cancellationToken));
        return days.ToHashSet();
    }

    public async Task<AdventClaimOutcome> ClaimAdventAsync(ulong discordId, string season, AdventDay day, int zoneRank, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La fila del jugador bloqueada: el oro y la XP del RewardPayer salen de acá, y frena dos reclamos a la vez del mismo jugador.
            var player = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdventClaimOutcome(AdventClaimStatus.NoAccount);
            }

            // Update guardado: la marca del día entra una sola vez (0 filas = ya lo había reclamado).
            int inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO event_advent_claims (discord_id, season, day) VALUES (@DiscordId, @Season, @Day)
                ON CONFLICT (discord_id, season, day) DO NOTHING;
                """,
                new { DiscordId = (long)discordId, Season = season, Day = (short)day.Day }, transaction: transaction, cancellationToken: cancellationToken));
            if (inserted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new AdventClaimOutcome(AdventClaimStatus.AlreadyClaimed, day);
            }

            long giftsAfter = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                GrantSql, new { DiscordId = (long)discordId, Season = season, Amount = (long)day.Gifts }, transaction: transaction, cancellationToken: cancellationToken));

            RewardReceipt? receipt = day.Reward is null
                ? null
                : await RewardPayer.PayAsync(connection, transaction, player, day.Reward, zoneRank, cancellationToken);

            if (day.Dust > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE users SET dust = dust + @Dust WHERE discord_id = @DiscordId;",
                    new { DiscordId = (long)discordId, Dust = day.Dust }, transaction: transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return new AdventClaimOutcome(AdventClaimStatus.Ok, day, giftsAfter, receipt);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
