using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class BuffRepository(IDbConnectionFactory connectionFactory) : IBuffRepository
{
    private const string AttackKey = "attack";

    public async Task<IReadOnlyDictionary<int, ItemBuff>> GetItemBuffsAsync(CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ItemBuffRow>(new CommandDefinition(
            "SELECT item_id AS ItemId, attack_percent AS AttackPercent, minutes AS Minutes FROM item_buffs;",
            cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.ItemId, r => new ItemBuff(r.AttackPercent, r.Minutes));
    }

    public async Task<ItemBuff?> GetItemBuffAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ItemBuffRow>(new CommandDefinition(
            "SELECT item_id AS ItemId, attack_percent AS AttackPercent, minutes AS Minutes FROM item_buffs WHERE item_id = @ItemId;",
            new { ItemId = itemId }, cancellationToken: cancellationToken));

        return row is null ? null : new ItemBuff(row.AttackPercent, row.Minutes);
    }

    public async Task<ActiveBuff> ActivateAttackAsync(
        ulong discordId, int percent, int minutes, string? source, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        // El vencimiento lo calcula la base (now() + minutos): un solo reloj, sin depender de la hora de esta máquina.
        var expires = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            """
            INSERT INTO player_buffs (discord_id, buff_key, percent, expires_at, source)
            VALUES (@DiscordId, @Key, @Percent, now() + make_interval(mins => @Minutes), @Source)
            ON CONFLICT (discord_id, buff_key) DO UPDATE
                SET percent = EXCLUDED.percent, expires_at = EXCLUDED.expires_at, source = EXCLUDED.source
            RETURNING expires_at;
            """,
            new { DiscordId = (long)discordId, Key = AttackKey, Percent = percent, Minutes = minutes, Source = source },
            cancellationToken: cancellationToken));

        return new ActiveBuff(percent, DateTime.SpecifyKind(expires, DateTimeKind.Utc), source);
    }

    public async Task<ActiveBuff?> GetActiveAttackAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<ActiveRow>(new CommandDefinition(
            """
            SELECT percent AS Percent, expires_at AS ExpiresAt, source AS Source
            FROM player_buffs
            WHERE discord_id = @DiscordId AND buff_key = @Key AND expires_at > now();
            """,
            new { DiscordId = (long)discordId, Key = AttackKey }, cancellationToken: cancellationToken));

        return row is null ? null : new ActiveBuff(row.Percent, DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc), row.Source);
    }

    private sealed record ItemBuffRow(int ItemId, int AttackPercent, int Minutes);

    private sealed record ActiveRow(int Percent, DateTime ExpiresAt, string? Source);
}
