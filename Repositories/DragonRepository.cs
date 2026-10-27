using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class DragonRepository(IDbConnectionFactory connectionFactory) : IDragonRepository
{
    // Una fila de player_dragons tal como la lee Dapper; WaitSeconds sale del reloj de la base (cooldown de DragonRules.BreedCooldown).
    private sealed class DragonRow
    {
        public long DiscordId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public int Tier { get; set; }
        public int FailStreak { get; set; }
        public double WaitSeconds { get; set; }
    }

    private const string SelectColumns = """
        discord_id AS DiscordId, kind AS Kind, tier AS Tier, fail_streak AS FailStreak,
        COALESCE(GREATEST(0, EXTRACT(EPOCH FROM (last_bred_at + make_interval(secs => @Cooldown) - now()))), 0)::float8 AS WaitSeconds
        """;

    private static PlayerDragon ToDragon(DragonRow row) => new(row.Kind, row.Tier, row.FailStreak, TimeSpan.FromSeconds(row.WaitSeconds));

    public async Task<PlayerDragon?> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<DragonRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM player_dragons WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId, Cooldown = DragonRules.BreedCooldown.TotalSeconds }, cancellationToken: cancellationToken));

        return row is null ? null : ToDragon(row);
    }

    public async Task<DragonBuyResult> BuyAsync(ulong discordId, int price, string kind, CancellationToken cancellationToken = default)
    {
        if (!DragonRules.IsKind(kind))
        {
            throw new ArgumentException($"Tipo de dragón desconocido: {kind}", nameof(kind));
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La fila del jugador bloqueada hasta el commit: dos compras a la vez se ordenan y la segunda ya ve el dragón de la primera.
            int? gold = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "SELECT gold FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (gold is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBuyResult(DragonBuyStatus.NoAccount);
            }

            bool has = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM player_dragons WHERE discord_id = @DiscordId);",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (has)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBuyResult(DragonBuyStatus.AlreadyHave);
            }

            // El cobro es un UPDATE guardado: si el oro no alcanza no vuelve ninguna fila y no se descuenta nada.
            int? left = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "UPDATE users SET gold = gold - @Price WHERE discord_id = @DiscordId AND gold >= @Price RETURNING gold;",
                new { DiscordId = (long)discordId, Price = price }, transaction: transaction, cancellationToken: cancellationToken));

            if (left is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBuyResult(DragonBuyStatus.NotEnoughGold);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO player_dragons (discord_id, kind) VALUES (@DiscordId, @Kind);",
                new { DiscordId = (long)discordId, Kind = kind }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new DragonBuyResult(DragonBuyStatus.Bought, new PlayerDragon(kind, 1, 0, TimeSpan.Zero));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DragonBreedResult> BreedAsync(ulong firstId, ulong secondId, Random? rng = null, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Las dos filas bloqueadas EN ORDEN de discord_id (el ORDER BY va antes del bloqueo): dos crías cruzadas (A con B y B con A) nunca se traban entre sí.
            var rows = (await connection.QueryAsync<DragonRow>(new CommandDefinition(
                $"SELECT {SelectColumns} FROM player_dragons WHERE discord_id = ANY(@Ids) ORDER BY discord_id FOR UPDATE;",
                new { Ids = new[] { (long)firstId, (long)secondId }, Cooldown = DragonRules.BreedCooldown.TotalSeconds },
                transaction: transaction, cancellationToken: cancellationToken))).ToList();

            var a = rows.FirstOrDefault(r => r.DiscordId == (long)firstId);
            var b = rows.FirstOrDefault(r => r.DiscordId == (long)secondId);
            if (firstId == secondId || a is null || b is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBreedResult(DragonBreedStatus.NoDragon, BlockedId: a is null ? firstId : secondId);
            }

            if (a.Tier != b.Tier)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBreedResult(DragonBreedStatus.TierMismatch, Tier: a.Tier, A: ToDragon(a), B: ToDragon(b));
            }

            if (a.Tier >= DragonRules.MaxTier)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBreedResult(DragonBreedStatus.MaxTier, Tier: a.Tier, A: ToDragon(a), B: ToDragon(b));
            }

            var resting = a.WaitSeconds > 0 ? a : b.WaitSeconds > 0 ? b : null;
            if (resting is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new DragonBreedResult(DragonBreedStatus.OnCooldown, Tier: a.Tier, A: ToDragon(a), B: ToDragon(b), BlockedId: (ulong)resting.DiscordId, Wait: TimeSpan.FromSeconds(resting.WaitSeconds));
            }

            // La piedad es la del que más fallos seguidos lleva: un compañero con mala racha le da la suerte al otro.
            bool success = DragonRules.RollBreed(a.Tier, Math.Max(a.FailStreak, b.FailStreak), rng);

            await connection.ExecuteAsync(new CommandDefinition(
                success
                    ? "UPDATE player_dragons SET tier = tier + 1, fail_streak = 0, last_bred_at = now() WHERE discord_id = ANY(@Ids);"
                    : "UPDATE player_dragons SET fail_streak = fail_streak + 1, last_bred_at = now() WHERE discord_id = ANY(@Ids);",
                new { Ids = new[] { (long)firstId, (long)secondId } }, transaction: transaction, cancellationToken: cancellationToken));

            var after = (await connection.QueryAsync<DragonRow>(new CommandDefinition(
                $"SELECT {SelectColumns} FROM player_dragons WHERE discord_id = ANY(@Ids);",
                new { Ids = new[] { (long)firstId, (long)secondId }, Cooldown = DragonRules.BreedCooldown.TotalSeconds },
                transaction: transaction, cancellationToken: cancellationToken))).ToList();

            await transaction.CommitAsync(cancellationToken);
            return new DragonBreedResult(
                DragonBreedStatus.Bred, success, a.Tier,
                ToDragon(after.First(r => r.DiscordId == (long)firstId)), ToDragon(after.First(r => r.DiscordId == (long)secondId)));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
