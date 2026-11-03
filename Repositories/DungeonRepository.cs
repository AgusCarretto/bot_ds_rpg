using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// La Reliquia de una zona (la que pide su mazmorra): el ítem de tipo «Reliquia» cuya receta es de esa zona (Database/add_zones_6_10.sql).
public sealed record RelicInfo(int ItemId, string Name, string? Emoji);

// Cómo terminó una corrida, para guardarla: la vida REAL que perdió, si ganó y, si ganó una zona intermedia, qué zona se marca como superada y su premio.
public sealed record DungeonFinishRequest(ulong DiscordId, int RelicItemId, int RealHpLoss, bool Won, int? ClearedZoneId, int Gold, int XpPercent, string? BoxName);

// RelicSpent: se gastó la Reliquia (si ya no estaba, una corrida ganada no cuenta). Won: ganó de verdad (Reliquia gastada). Outcome: el jugador después (nivel, vida).
public sealed record DungeonFinishOutcome(bool RelicSpent, bool Won, int Xp, LevelUpOutcome Outcome, bool BoxGranted);

public interface IDungeonRepository
{
    Task<RelicInfo?> GetRelicAsync(int zoneId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default);

    Task<DungeonFinishOutcome> FinishAsync(DungeonFinishRequest request, CancellationToken cancellationToken = default);
}

public sealed class DungeonRepository(IDbConnectionFactory connectionFactory) : IDungeonRepository
{
    public async Task<RelicInfo?> GetRelicAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<RelicInfo>(new CommandDefinition(
            """
            SELECT i.item_id AS "ItemId", i.name AS "Name", i.emoji AS "Emoji"
            FROM recipes r JOIN items i ON i.item_id = r.result_item_id
            WHERE r.zone_id = @ZoneId AND i.type = 'Reliquia'
            ORDER BY i.item_id LIMIT 1;
            """,
            new { ZoneId = zoneId }, cancellationToken: cancellationToken));
    }

    public async Task<int> CountAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(SUM(quantity), 0)::int FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId;",
            new { DiscordId = (long)discordId, ItemId = itemId }, cancellationToken: cancellationToken));
    }

    // UNA transacción: la fila del jugador bloqueada, la Reliquia gastada (con guarda: si ya no estaba, la corrida no se puede ganar), la vida que se llevó la mazmorra y, si ganó una
    // zona intermedia, la zona superada (GREATEST, nunca baja), el cooldown del jefe completo, el oro, la EXP (con su subida de nivel) y la caja.
    public async Task<DungeonFinishOutcome> FinishAsync(DungeonFinishRequest request, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
            $"SELECT {UserSql.SelectColumns} FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
            new { DiscordId = (long)request.DiscordId }, transaction: transaction, cancellationToken: cancellationToken));

        int? left = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "UPDATE inventory SET quantity = quantity - 1 WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= 1 RETURNING quantity;",
            new { DiscordId = (long)request.DiscordId, ItemId = request.RelicItemId }, transaction: transaction, cancellationToken: cancellationToken));
        bool spent = left is not null;
        if (left == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity <= 0;",
                new { DiscordId = (long)request.DiscordId, ItemId = request.RelicItemId }, transaction: transaction, cancellationToken: cancellationToken));
        }

        bool won = request.Won && spent;
        bool pays = won && request.ClearedZoneId is not null;
        int xp = pays ? LevelingCalculator.RequiredXpForLevel(current.Level) * request.XpPercent / 100 : 0;
        var outcome = await LevelingApplier.ApplyAsync(connection, transaction, current, xp, -Math.Max(0, request.RealHpLoss), cancellationToken);

        bool boxGranted = false;
        if (pays)
        {
            var user = await connection.QuerySingleAsync<User>(new CommandDefinition(
                $"""
                UPDATE users
                SET gold = LEAST(gold::bigint + @Gold, 2147483647)::int, highest_zone_cleared = GREATEST(highest_zone_cleared, @ZoneId)
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """,
                new { DiscordId = (long)request.DiscordId, Gold = request.Gold, ZoneId = request.ClearedZoneId }, transaction: transaction, cancellationToken: cancellationToken));
            outcome = outcome with { Player = user };

            // Ganar la mazmorra es la victoria del «jefe» de la zona: completa el cooldown (se reclamó al entrar dejando solo el reintento).
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE cooldowns SET last_executed_at = now() WHERE discord_id = @DiscordId AND command_name = @CommandName;",
                new { DiscordId = (long)request.DiscordId, CommandName = CooldownCatalog.Boss.CommandName }, transaction: transaction, cancellationToken: cancellationToken));

            if (request.BoxName is not null
                && await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT item_id FROM items WHERE name = @Name;", new { Name = request.BoxName }, transaction: transaction, cancellationToken: cancellationToken)) is int boxId)
            {
                await InventoryUpsert.AddItemAsync(connection, transaction, request.DiscordId, boxId, 1, cancellationToken);
                boxGranted = true;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new DungeonFinishOutcome(spent, won, xp, outcome, boxGranted);
    }
}
