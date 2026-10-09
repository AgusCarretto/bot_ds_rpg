using System.Data;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class CooldownRepository(IDbConnectionFactory connectionFactory) : ICooldownRepository
{
    public async Task<TimeSpan?> GetRemainingAsync(ulong discordId, string commandName, TimeSpan cooldownDuration, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT last_executed_at
            FROM cooldowns
            WHERE discord_id = @DiscordId AND command_name = @CommandName;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, CommandName = commandName },
            cancellationToken: cancellationToken);

        DateTime? lastExecutedAt = await connection.QuerySingleOrDefaultAsync<DateTime?>(command);
        if (lastExecutedAt is null)
        {
            return null; // nunca ejecutó este comando, no hay cooldown que esperar
        }

        var remaining = cooldownDuration - (DateTime.UtcNow - lastExecutedAt.Value);
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private sealed class CooldownDbRow
    {
        public string Command { get; set; } = string.Empty;
        public DateTime LastExecutedAt { get; set; }
    }

    public async Task<IReadOnlyList<CooldownRow>> GetAllAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT command_name AS "Command", last_executed_at AS "LastExecutedAt"
            FROM cooldowns
            WHERE discord_id = @DiscordId;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<CooldownDbRow>(
            new CommandDefinition(sql, new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));
        return rows.Select(r => new CooldownRow(r.Command, DateTime.SpecifyKind(r.LastExecutedAt, DateTimeKind.Utc))).ToList();
    }
}
