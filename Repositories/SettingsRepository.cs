using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class SettingsRepository(IDbConnectionFactory connectionFactory) : ISettingsRepository
{
    // Una fila de player_settings tal como la lee Dapper (una propiedad por columna que se pide).
    private sealed class SettingsRow
    {
        public bool ShowFightSummary { get; set; }
        public bool FullRecipeBook { get; set; }
    }

    public async Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition(
            "SELECT show_fight_summary AS ShowFightSummary, full_recipe_book AS FullRecipeBook FROM player_settings WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return row is null ? null : new PlayerSettings(row.ShowFightSummary, row.FullRecipeBook);
    }

    // Cada guardado toca SOLO su columna: el INSERT deja el valor por defecto de las demás (una fila nueva) y el UPDATE de un conflicto no las nombra, así que elegir una opción nunca pisa otra.
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

    public async Task SetFullRecipeBookAsync(ulong discordId, bool value, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO player_settings (discord_id, full_recipe_book)
            VALUES (@DiscordId, @Value)
            ON CONFLICT (discord_id) DO UPDATE SET full_recipe_book = EXCLUDED.full_recipe_book, updated_at = now();
            """,
            new { DiscordId = (long)discordId, Value = value }, cancellationToken: cancellationToken));
    }
}
