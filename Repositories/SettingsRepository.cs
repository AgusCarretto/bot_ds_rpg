using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class SettingsRepository(IDbConnectionFactory connectionFactory) : ISettingsRepository
{
    public async Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        bool? showFightSummary = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT show_fight_summary FROM player_settings WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return showFightSummary is bool value ? new PlayerSettings(value) : null;
    }

    public async Task SetShowFightSummaryAsync(ulong discordId, bool value, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO player_settings (discord_id, show_fight_summary)
            VALUES (@DiscordId, @Value)
            ON CONFLICT (discord_id) DO UPDATE SET show_fight_summary = EXCLUDED.show_fight_summary, updated_at = now();
            """,
            new { DiscordId = (long)discordId, Value = value }, cancellationToken: cancellationToken));
    }
}
