using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// Los códigos canjeables (v0.37.0). Ver IRedeemCodeRepository. Canjear sigue el mismo orden de candados que el resto del juego: PRIMERO la fila del jugador (FOR UPDATE) y después la del código (FOR UPDATE), así dos personas
// que canjean el mismo código se turnan en la fila del código sin trabarse entre sí; con la fila del código tomada se mira el tope, se marca el canje (clave primaria código+jugador: «una vez por persona»), se suma
// el uso y se paga todo en la misma transacción.
public sealed class RedeemCodeRepository(IDbConnectionFactory connectionFactory) : IRedeemCodeRepository
{
    private sealed class CodeRow
    {
        public int CodeId { get; set; }
        public int? MaxUses { get; set; }
        public int Uses { get; set; }
        public bool Active { get; set; }
        public bool Expired { get; set; }
    }

    private sealed class RewardRow
    {
        public string Kind { get; set; } = string.Empty;
        public int Amount { get; set; }
        public string? ItemName { get; set; }
        public string? ItemEmoji { get; set; }
        public int? ItemId { get; set; }
    }

    private static RedeemRewardKind KindOf(string kind) => kind switch
    {
        "gold" => RedeemRewardKind.Gold,
        "dust" => RedeemRewardKind.Dust,
        "xp" => RedeemRewardKind.Xp,
        _ => RedeemRewardKind.Item,
    };

    private static string KindName(RedeemRewardKind kind) => kind switch
    {
        RedeemRewardKind.Gold => "gold",
        RedeemRewardKind.Dust => "dust",
        RedeemRewardKind.Xp => "xp",
        _ => "item",
    };

    public async Task<CreateCodeResult> CreateAsync(RedeemCodeSpec spec, ulong createdBy, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Todos los ítems tienen que existir (por el nombre exacto del catálogo) antes de crear nada.
            var itemIds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in spec.Rewards.Where(r => r.Kind == RedeemRewardKind.Item).Select(r => r.ItemName!).Distinct())
            {
                int? id = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT item_id FROM items WHERE name = @Name;", new { Name = name }, transaction: transaction, cancellationToken: cancellationToken));
                if (id is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new CreateCodeResult(CreateCodeStatus.ItemMissing, name);
                }

                itemIds[name] = id.Value;
            }

            int? codeId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                """
                INSERT INTO redeem_codes (code, max_uses, expires_at, created_by)
                VALUES (@Code, @MaxUses, CASE WHEN @Minutes IS NULL THEN NULL ELSE now() + (@Minutes * interval '1 minute') END, @CreatedBy)
                ON CONFLICT (code) DO NOTHING
                RETURNING code_id;
                """,
                new { Code = spec.Code, MaxUses = spec.MaxUses, Minutes = spec.Duration is { } d ? (double?)d.TotalMinutes : null, CreatedBy = (long)createdBy },
                transaction: transaction, cancellationToken: cancellationToken));
            if (codeId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateCodeResult(CreateCodeStatus.Duplicate, spec.Code);
            }

            foreach (var reward in spec.Rewards)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO redeem_code_rewards (code_id, kind, item_id, amount) VALUES (@CodeId, @Kind, @ItemId, @Amount);",
                    new
                    {
                        CodeId = codeId.Value,
                        Kind = KindName(reward.Kind),
                        ItemId = reward.Kind == RedeemRewardKind.Item ? (int?)itemIds[reward.ItemName!] : null,
                        Amount = reward.Amount,
                    },
                    transaction: transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return new CreateCodeResult(CreateCodeStatus.Created, spec.Code);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<RedeemResult> RedeemAsync(ulong discordId, string code, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. La cuenta (nunca se crea) y su fila tomada.
            var player = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                $"SELECT {UserSql.SelectColumns} FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Fail(RedeemStatus.NoAccount);
            }

            // 2. El código, con su fila tomada: lo que sigue (tope, canje, uso) no puede cruzarse con otro canje del mismo código.
            var row = await connection.QuerySingleOrDefaultAsync<CodeRow>(new CommandDefinition(
                """
                SELECT code_id AS CodeId, max_uses AS MaxUses, uses AS Uses, active AS Active, (expires_at IS NOT NULL AND expires_at <= now()) AS Expired
                FROM redeem_codes WHERE code = @Code FOR UPDATE;
                """,
                new { Code = code }, transaction: transaction, cancellationToken: cancellationToken));
            if (row is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Fail(RedeemStatus.UnknownCode);
            }

            RedeemStatus? blocked = !row.Active ? RedeemStatus.Inactive
                : row.Expired ? RedeemStatus.Expired
                : row.MaxUses is int max && row.Uses >= max ? RedeemStatus.Exhausted
                : null;
            if (blocked is { } why)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Fail(why);
            }

            var rewards = (await connection.QueryAsync<RewardRow>(new CommandDefinition(
                """
                SELECT r.kind AS Kind, r.amount AS Amount, i.name AS ItemName, i.emoji AS ItemEmoji, r.item_id AS ItemId
                FROM redeem_code_rewards r LEFT JOIN items i ON i.item_id = r.item_id
                WHERE r.code_id = @CodeId ORDER BY r.reward_id;
                """,
                new { row.CodeId }, transaction: transaction, cancellationToken: cancellationToken))).ToList();
            if (rewards.Count == 0)
            {
                // Un código sin premios (todos sus ítems se borraron del catálogo): no se quema el canje de nadie.
                await transaction.RollbackAsync(cancellationToken);
                return Fail(RedeemStatus.NoRewards);
            }

            // 3. El canje: la clave primaria (código, jugador) garantiza «una vez por persona» aunque el jugador mande dos pedidos a la vez.
            int? claimed = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "INSERT INTO redeem_code_claims (code_id, discord_id) VALUES (@CodeId, @DiscordId) ON CONFLICT DO NOTHING RETURNING 1;",
                new { row.CodeId, DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
            if (claimed is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Fail(RedeemStatus.AlreadyRedeemed);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE redeem_codes SET uses = uses + 1 WHERE code_id = @CodeId;", new { row.CodeId }, transaction: transaction, cancellationToken: cancellationToken));

            // 4. Los premios, todos en la misma transacción (si algo falla, no queda ni el canje).
            bool capped = false;
            int levelsGained = 0;
            var current = player;
            foreach (var reward in rewards)
            {
                switch (KindOf(reward.Kind))
                {
                    case RedeemRewardKind.Gold:
                    case RedeemRewardKind.Dust:
                    {
                        // Los nombres de columna son dos fijos; el total se recorta en int.MaxValue en vez de fallar por desborde.
                        string column = KindOf(reward.Kind) == RedeemRewardKind.Gold ? "gold" : "dust";
                        int after = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                            $"UPDATE users SET {column} = LEAST({column}::bigint + @Amount, 2147483647)::int WHERE discord_id = @DiscordId RETURNING {column};",
                            new { reward.Amount, DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
                        capped |= after == int.MaxValue;
                        break;
                    }

                    case RedeemRewardKind.Xp:
                    {
                        var outcome = await LevelingApplier.ApplyAsync(connection, transaction, current, reward.Amount, 0, cancellationToken);
                        levelsGained += outcome.LevelsGained;
                        current = outcome.Player;
                        break;
                    }

                    default:
                    {
                        int total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                            """
                            INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@DiscordId, @ItemId, @Amount)
                            ON CONFLICT (discord_id, item_id) DO UPDATE
                                SET quantity = LEAST(inventory.quantity::bigint + EXCLUDED.quantity, 2147483647)::int
                            RETURNING quantity;
                            """,
                            new { DiscordId = (long)discordId, ItemId = reward.ItemId, reward.Amount }, transaction: transaction, cancellationToken: cancellationToken));
                        capped |= total == int.MaxValue;
                        break;
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new RedeemResult(
                RedeemStatus.Redeemed,
                rewards.Select(r => new RedeemedReward(KindOf(r.Kind), r.Amount, r.ItemName, r.ItemEmoji)).ToList(),
                levelsGained, current.Level, capped);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static RedeemResult Fail(RedeemStatus status) => new(status, []);

    public async Task<RedeemCodeInfo?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var list = await LoadAsync("WHERE c.code = @Code", new { Code = code }, 1, cancellationToken);
        return list.FirstOrDefault();
    }

    public Task<IReadOnlyList<RedeemCodeInfo>> ListAsync(int limit, CancellationToken cancellationToken = default) =>
        LoadAsync(string.Empty, new { }, Math.Clamp(limit, 1, 50), cancellationToken);

    private sealed class InfoRow
    {
        public int CodeId { get; set; }
        public string Code { get; set; } = string.Empty;
        public int? MaxUses { get; set; }
        public int Uses { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public long CreatedBy { get; set; }
    }

    private async Task<IReadOnlyList<RedeemCodeInfo>> LoadAsync(string where, object parameters, int limit, CancellationToken cancellationToken)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // `where` es siempre uno de dos textos fijos de este archivo: seguro interpolarlo.
        var rows = (await connection.QueryAsync<InfoRow>(new CommandDefinition(
            $"""
            SELECT c.code_id AS CodeId, c.code AS Code, c.max_uses AS MaxUses, c.uses AS Uses, c.expires_at AS ExpiresAt, c.active AS Active, c.created_at AS CreatedAt, c.created_by AS CreatedBy
            FROM redeem_codes c {where} ORDER BY c.code_id DESC LIMIT {limit};
            """,
            parameters, cancellationToken: cancellationToken))).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var rewardRows = (await connection.QueryAsync<(int CodeId, string Kind, int Amount, string? ItemName, string? ItemEmoji)>(new CommandDefinition(
            """
            SELECT r.code_id, r.kind, r.amount, i.name, i.emoji
            FROM redeem_code_rewards r LEFT JOIN items i ON i.item_id = r.item_id
            WHERE r.code_id = ANY(@Ids) ORDER BY r.reward_id;
            """,
            new { Ids = rows.Select(r => r.CodeId).ToArray() }, cancellationToken: cancellationToken))).ToList();

        return rows.Select(r => new RedeemCodeInfo(
            r.Code, r.MaxUses, r.Uses, r.ExpiresAt, r.Active, r.CreatedAt, (ulong)r.CreatedBy,
            rewardRows.Where(x => x.CodeId == r.CodeId).Select(x => new RedeemedReward(KindOf(x.Kind), x.Amount, x.ItemName, x.ItemEmoji)).ToList())).ToList();
    }

    public async Task<bool> SetActiveAsync(string code, bool active, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        int rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE redeem_codes SET active = @Active WHERE code = @Code;", new { Active = active, Code = code }, cancellationToken: cancellationToken));
        return rows > 0;
    }
}
