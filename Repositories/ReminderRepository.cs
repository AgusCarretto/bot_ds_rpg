using System.Data;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ReminderRepository(IDbConnectionFactory connectionFactory) : IReminderRepository
{
    private sealed class DueRow
    {
        public long DiscordId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public long ChannelId { get; set; }
        public DateTime DueAt { get; set; }
    }

    public async Task SyncAsync(ulong discordId, ulong channelId, IReadOnlyList<ReminderDue> candidates, CancellationToken cancellationToken = default)
    {
        // Una sola instrucción: se leen los tipos apagados, se borran de la cola los que estén apagados y se guardan los candidatos que no lo están. El ON CONFLICT solo toca la fila
        // si el momento CAMBIÓ (el cooldown se reinició: un comando nuevo, que toma su canal); con el mismo momento (el aviso ya existía para ESE cooldown, por ejemplo en cada botón de
        // una pelea) no escribe nada, así que el canal original se conserva y la tabla no se llena de versiones muertas.
        const string sql = """
            WITH off AS (
                SELECT COALESCE((SELECT off_kinds FROM reminder_settings WHERE discord_id = @DiscordId), ARRAY[]::text[]) AS kinds
            ), dropped AS (
                DELETE FROM reminders r USING off WHERE r.discord_id = @DiscordId AND r.kind = ANY (off.kinds)
            ), candidates AS (
                SELECT k.kind, d.due_at
                FROM unnest(@Kinds::text[]) WITH ORDINALITY AS k(kind, n)
                JOIN unnest(@Dues::timestamptz[]) WITH ORDINALITY AS d(due_at, n) USING (n)
            )
            INSERT INTO reminders (discord_id, kind, channel_id, due_at)
            SELECT @DiscordId, c.kind, @ChannelId, c.due_at
            FROM candidates c CROSS JOIN off
            WHERE NOT (c.kind = ANY (off.kinds))
            ON CONFLICT (discord_id, kind) DO UPDATE
            SET due_at = EXCLUDED.due_at, channel_id = EXCLUDED.channel_id
            WHERE reminders.due_at <> EXCLUDED.due_at;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                DiscordId = (long)discordId,
                ChannelId = (long)channelId,
                Kinds = candidates.Select(c => c.Kind).ToArray(),
                Dues = candidates.Select(c => DateTime.SpecifyKind(c.DueUtc, DateTimeKind.Utc)).ToArray(),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DueReminder>> ClaimDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM reminders
            WHERE due_at <= @Now
            RETURNING discord_id AS "DiscordId", kind AS "Kind", channel_id AS "ChannelId", due_at AS "DueAt";
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DueRow>(new CommandDefinition(
            sql, new { Now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc) }, cancellationToken: cancellationToken));
        return rows.Select(r => new DueReminder((ulong)r.DiscordId, r.Kind, (ulong)r.ChannelId, DateTime.SpecifyKind(r.DueAt, DateTimeKind.Utc))).ToList();
    }

    public async Task<IReadOnlyList<string>> GetOffKindsAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using IDbConnection connection = connectionFactory.CreateConnection();
        var kinds = await connection.QuerySingleOrDefaultAsync<string[]?>(new CommandDefinition(
            "SELECT off_kinds FROM reminder_settings WHERE discord_id = @DiscordId;", new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));
        return kinds ?? [];
    }

    public async Task SetOffKindsAsync(ulong discordId, IReadOnlyCollection<string> offKinds, CancellationToken cancellationToken = default)
    {
        // Solo se guardan claves que existen: una clave desconocida no puede quedar en la base.
        string[] valid = offKinds.Where(k => ReminderCatalog.Find(k) is not null).Distinct().ToArray();

        const string sql = """
            WITH saved AS (
                INSERT INTO reminder_settings (discord_id, off_kinds) VALUES (@DiscordId, @Kinds)
                ON CONFLICT (discord_id) DO UPDATE SET off_kinds = EXCLUDED.off_kinds
            )
            DELETE FROM reminders WHERE discord_id = @DiscordId AND kind = ANY (@Kinds);
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new { DiscordId = (long)discordId, Kinds = valid }, cancellationToken: cancellationToken));
    }
}
