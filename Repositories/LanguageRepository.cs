using System.Data.Common;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class LanguageRepository(IDbConnectionFactory connectionFactory) : ILanguageRepository
{
    public async Task<string?> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT language FROM player_language WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));
    }

    public async Task SetAsync(ulong discordId, string language, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO player_language (discord_id, language)
            VALUES (@DiscordId, @Language)
            ON CONFLICT (discord_id) DO UPDATE SET language = EXCLUDED.language, updated_at = now();
            """,
            new { DiscordId = (long)discordId, Language = language }, cancellationToken: cancellationToken));
    }
}
