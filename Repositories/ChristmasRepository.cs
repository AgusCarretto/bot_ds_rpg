using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// Los Regalos de la temporada de un jugador: lo que tiene para gastar, lo que juntó en total (no baja al gastar; para logros y rankings), las Campanas
// sin usar y hasta cuándo le dura la Vela (null o pasado = apagada).
public sealed record GiftBalance(long Balance, long Earned, int Bells = 0, DateTime? CandleUntil = null)
{
    public bool CandleLit(DateTime utcNow) => CandleUntil is { } until && DateTime.SpecifyKind(until, DateTimeKind.Utc) > utcNow;
}

public enum AdventClaimStatus
{
    Ok,
    AlreadyClaimed,
    NoAccount,
}

// Receipt: lo que pagó el RewardPayer (oro/caja), null si ese día no traía nada de eso.
public sealed record AdventClaimOutcome(AdventClaimStatus Status, AdventDay? Day = null, long GiftsAfter = 0, RewardReceipt? Receipt = null, RewardReceipt? Sock = null);

public enum ChristmasBuyStatus
{
    Ok,
    NotEnoughGifts,
    AlreadyOwned,
    NoAccount,
}

// Lo que dejó una compra: el saldo de Regalos después, las Campanas que tiene, hasta cuándo la Vela y lo que pagó el Calcetín.
public sealed record ChristmasBuyOutcome(
    ChristmasBuyStatus Status, ChristmasShopItem Item, int Quantity = 0, long GiftsAfter = 0, int BellsAfter = 0, DateTime? CandleUntil = null, RewardReceipt? Receipt = null, int Dust = 0);

public enum BellUseStatus
{
    Ok,
    NoBells,
    NotNeeded,   // /hunt ya está listo: no se gasta la Campana
}

// Navidad en el Fogón (v0.57.0): tablas event_gifts y event_advent_claims (Database/add_christmas.sql).
public interface IChristmasRepository
{
    Task<GiftBalance> GetAsync(ulong discordId, string season, CancellationToken cancellationToken = default);

    // Suma Regalos (amount > 0; con la Vela prendida, +50 % redondeado para arriba). Devuelve los que se sumaron de verdad. Un upsert atómico.
    Task<int> GrantAsync(ulong discordId, string season, int amount, CancellationToken cancellationToken = default);

    // Los días del calendario que ya reclamó en esta temporada.
    Task<IReadOnlySet<int>> GetClaimedDaysAsync(ulong discordId, string season, CancellationToken cancellationToken = default);

    // Reclama ESE día del calendario y paga todo en una sola transacción (Regalos, oro/caja con el RewardPayer, Polvo). Si ya lo reclamó, no paga nada.
    Task<AdventClaimOutcome> ClaimAdventAsync(ulong discordId, string season, AdventDay day, int zoneRank, CancellationToken cancellationToken = default);

    // Compra en la tienda: cobra los Regalos (update guardado) y entrega en la MISMA transacción. quantity solo vale para las Campanas (las demás, 1).
    Task<ChristmasBuyOutcome> BuyAsync(ulong discordId, string season, ChristmasShopItem item, int quantity, int zoneRank, CancellationToken cancellationToken = default);

    // Usa una Campana: si /hunt está esperando, borra su cooldown y descuenta la Campana; si ya está listo, no gasta nada.
    Task<(BellUseStatus Status, int BellsLeft)> UseBellAsync(ulong discordId, string season, TimeSpan huntCooldown, CancellationToken cancellationToken = default);
}

public sealed class ChristmasRepository(IDbConnectionFactory connectionFactory) : IChristmasRepository
{
    // La Vela se mira en la misma sentencia: con ella prendida se suma +50 % redondeado para arriba.
    private const string GrantSql =
        """
        INSERT INTO event_gifts (discord_id, season, balance, earned)
        VALUES (@DiscordId, @Season, @Amount, @Amount)
        ON CONFLICT (discord_id, season) DO UPDATE
            SET balance = event_gifts.balance + CASE WHEN event_gifts.candle_until > now() THEN CEIL(EXCLUDED.balance * @CandleFactor)::bigint ELSE EXCLUDED.balance END,
                earned  = event_gifts.earned  + CASE WHEN event_gifts.candle_until > now() THEN CEIL(EXCLUDED.balance * @CandleFactor)::bigint ELSE EXCLUDED.balance END
        RETURNING CASE WHEN candle_until > now() THEN CEIL(@Amount * @CandleFactor)::int ELSE @Amount END;
        """;

    // El calendario no se multiplica con la Vela: es un premio fijo.
    private const string PlainGrantSql =
        """
        INSERT INTO event_gifts (discord_id, season, balance, earned)
        VALUES (@DiscordId, @Season, @Amount, @Amount)
        ON CONFLICT (discord_id, season) DO UPDATE SET balance = event_gifts.balance + EXCLUDED.balance, earned = event_gifts.earned + EXCLUDED.earned
        RETURNING balance;
        """;

    private static readonly double CandleFactor = (100 + ChristmasShop.CandleGiftPercent) / 100.0;

    public async Task<GiftBalance> GetAsync(ulong discordId, string season, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<GiftBalance>(new CommandDefinition(
            """
            SELECT balance AS "Balance", earned AS "Earned", bells AS "Bells", candle_until AS "CandleUntil"
            FROM event_gifts WHERE discord_id = @DiscordId AND season = @Season;
            """,
            new { DiscordId = (long)discordId, Season = season }, cancellationToken: cancellationToken)) ?? new GiftBalance(0, 0);
    }

    public async Task<int> GrantAsync(ulong discordId, string season, int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Solo se suman Regalos positivos.");
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            GrantSql, new { DiscordId = (long)discordId, Season = season, Amount = amount, CandleFactor }, cancellationToken: cancellationToken));
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
                PlainGrantSql, new { DiscordId = (long)discordId, Season = season, Amount = (long)day.Gifts }, transaction: transaction, cancellationToken: cancellationToken));

            RewardReceipt? receipt = day.Reward is null
                ? null
                : await RewardPayer.PayAsync(connection, transaction, player, day.Reward, zoneRank, cancellationToken);

            if (day.Dust > 0)
            {
                await AddDustAsync(connection, transaction, discordId, day.Dust, cancellationToken);
            }

            if (day.Bells > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE event_gifts SET bells = bells + @Bells WHERE discord_id = @DiscordId AND season = @Season;",
                    new { DiscordId = (long)discordId, Season = season, day.Bells }, transaction: transaction, cancellationToken: cancellationToken));
            }

            // El Calcetín del 24 se abre en el momento, igual que el de la tienda. El jugador ya cambió (la caja del día subió su XP): se vuelve a leer bloqueado.
            RewardReceipt? sock = null;
            if (day.Sock)
            {
                var current = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken) ?? player;
                sock = await RewardPayer.PayAsync(connection, transaction, current, ChristmasShop.SockReward, zoneRank, cancellationToken);
                await AddDustAsync(connection, transaction, discordId, ChristmasShop.SockDust, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new AdventClaimOutcome(AdventClaimStatus.Ok, day, giftsAfter, receipt, sock);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ChristmasBuyOutcome> BuyAsync(
        ulong discordId, string season, ChristmasShopItem item, int quantity, int zoneRank, CancellationToken cancellationToken = default)
    {
        int count = item.Kind == ChristmasShopKind.Bell ? Math.Clamp(quantity, 1, ChristmasShop.MaxBellsPerPurchase) : 1;
        long cost = (long)item.Price * count;

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var player = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChristmasBuyOutcome(ChristmasBuyStatus.NoAccount, item);
            }

            // Lo de una sola vez (títulos y el Reno): con la fila del jugador bloqueada, dos compras a la vez no pasan las dos.
            if (item.OneTime)
            {
                long owned = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                    "SELECT value FROM player_stats WHERE discord_id = @DiscordId AND stat_key = @Key;",
                    new { DiscordId = (long)discordId, Key = item.UnlockStat }, transaction: transaction, cancellationToken: cancellationToken)) ?? 0;
                if (owned > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new ChristmasBuyOutcome(ChristmasBuyStatus.AlreadyOwned, item);
                }
            }

            // Update guardado: cobra solo si le alcanza (0 filas = no le alcanza o no tiene Regalos).
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
                return new ChristmasBuyOutcome(ChristmasBuyStatus.NotEnoughGifts, item);
            }

            int bells = 0;
            DateTime? candle = null;
            RewardReceipt? receipt = null;
            int dust = 0;

            switch (item.Kind)
            {
                case ChristmasShopKind.Bell:
                    bells = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                        "UPDATE event_gifts SET bells = bells + @Count WHERE discord_id = @DiscordId AND season = @Season RETURNING bells;",
                        new { DiscordId = (long)discordId, Season = season, Count = count }, transaction: transaction, cancellationToken: cancellationToken));
                    break;

                case ChristmasShopKind.Candle:
                    // Una Vela nueva se suma a la que ya está prendida (no se pisa el tiempo que quedaba).
                    candle = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                        """
                        UPDATE event_gifts SET candle_until = GREATEST(COALESCE(candle_until, now()), now()) + @Duration
                        WHERE discord_id = @DiscordId AND season = @Season RETURNING candle_until;
                        """,
                        new { DiscordId = (long)discordId, Season = season, Duration = ChristmasShop.CandleDuration }, transaction: transaction, cancellationToken: cancellationToken));
                    break;

                case ChristmasShopKind.Sock:
                    receipt = await RewardPayer.PayAsync(connection, transaction, player, ChristmasShop.SockReward, zoneRank, cancellationToken);
                    dust = ChristmasShop.SockDust;
                    await AddDustAsync(connection, transaction, discordId, dust, cancellationToken);
                    break;

                case ChristmasShopKind.Unlock:
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@DiscordId, @Key, 1)
                        ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = GREATEST(player_stats.value, 1);
                        """,
                        new { DiscordId = (long)discordId, Key = item.UnlockStat }, transaction: transaction, cancellationToken: cancellationToken));
                    break;
            }

            await transaction.CommitAsync(cancellationToken);
            return new ChristmasBuyOutcome(ChristmasBuyStatus.Ok, item, count, giftsAfter.Value, bells, candle, receipt, dust);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<(BellUseStatus Status, int BellsLeft)> UseBellAsync(ulong discordId, string season, TimeSpan huntCooldown, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Primero la Campana (update guardado): sin ninguna, no se toca nada.
            int? bellsLeft = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "UPDATE event_gifts SET bells = bells - 1 WHERE discord_id = @DiscordId AND season = @Season AND bells > 0 RETURNING bells;",
                new { DiscordId = (long)discordId, Season = season }, transaction: transaction, cancellationToken: cancellationToken));
            if (bellsLeft is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (BellUseStatus.NoBells, 0);
            }

            // Solo borra el cooldown de /hunt si TODAVÍA corre; si ya está listo, se devuelve la Campana (rollback).
            int cleared = await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM cooldowns
                WHERE discord_id = @DiscordId AND command_name = @Command AND last_executed_at + @Cooldown > now();
                """,
                new { DiscordId = (long)discordId, Command = CooldownCatalog.Hunt.CommandName, Cooldown = huntCooldown }, transaction: transaction, cancellationToken: cancellationToken));
            if (cleared == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (BellUseStatus.NotNeeded, bellsLeft.Value + 1);
            }

            await transaction.CommitAsync(cancellationToken);
            return (BellUseStatus.Ok, bellsLeft.Value);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static Task AddDustAsync(DbConnection connection, DbTransaction transaction, ulong discordId, int dust, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET dust = dust + @Dust WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId, Dust = dust }, transaction: transaction, cancellationToken: cancellationToken));
}
