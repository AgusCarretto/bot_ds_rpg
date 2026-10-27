using System.Data.Common;
using BotDsRpg.Data;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ToolRepository(IDbConnectionFactory connectionFactory) : IToolRepository
{
    // Una fila tal como la lee Dapper (una propiedad por columna que se pide).
    private sealed class ToolRow
    {
        public string Kind { get; set; } = string.Empty;
        public int ItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Emoji { get; set; }
        public string Rarity { get; set; } = string.Empty;
        public int Multiplier { get; set; }
    }

    public async Task<IReadOnlyList<PlayerTool>> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ToolRow>(new CommandDefinition(
            """
            SELECT t.kind AS Kind, i.item_id AS ItemId, i.name AS Name, i.emoji AS Emoji, i.rarity AS Rarity, i.stat_value AS Multiplier
            FROM player_tools t
            JOIN items i ON i.item_id = t.item_id
            WHERE t.discord_id = @DiscordId
            ORDER BY t.kind;
            """,
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return rows.Select(r => new PlayerTool(r.Kind, r.ItemId, r.Name, r.Emoji, r.Rarity, r.Multiplier)).ToList();
    }
}
