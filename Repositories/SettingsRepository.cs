using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;
using Npgsql;

namespace BotDsRpg.Repositories;

public sealed class SettingsRepository(IDbConnectionFactory connectionFactory) : ISettingsRepository
{
    // Una fila de player_settings tal como la lee Dapper (una propiedad por columna que se pide).
    private sealed class SettingsRow
    {
        public bool ShowFightSummary { get; set; }
        public bool FullRecipeBook { get; set; }
        public string? ProfileBanner { get; set; }
        public string? CdLayout { get; set; }
    }

    public async Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        // De la más nueva a la más vieja: una base a la que todavía le faltan las columnas nuevas (los scripts add_*.sql se corren a mano) sigue leyendo lo que tiene en vez de dejar a todos sin su configuración.
        string[] columnSets =
        [
            "show_fight_summary AS ShowFightSummary, full_recipe_book AS FullRecipeBook, profile_banner AS ProfileBanner, cd_layout AS CdLayout",
            "show_fight_summary AS ShowFightSummary, full_recipe_book AS FullRecipeBook, profile_banner AS ProfileBanner",
            "show_fight_summary AS ShowFightSummary, full_recipe_book AS FullRecipeBook",
        ];
        SettingsRow? row = null;
        for (int i = 0; i < columnSets.Length; i++)
        {
            try
            {
                row = await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition(
                    $"SELECT {columnSets[i]} FROM player_settings WHERE discord_id = @DiscordId;",
                    new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));
                break;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn && i < columnSets.Length - 1)
            {
                // falta una columna: se prueba con las que ya existen
            }
        }

        return row is null ? null : new PlayerSettings(row.ShowFightSummary, row.FullRecipeBook, row.ProfileBanner, row.CdLayout);
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

    public async Task SetProfileBannerAsync(ulong discordId, string? blessingKey, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO player_settings (discord_id, profile_banner)
            VALUES (@DiscordId, @Value)
            ON CONFLICT (discord_id) DO UPDATE SET profile_banner = EXCLUDED.profile_banner, updated_at = now();
            """,
            new { DiscordId = (long)discordId, Value = blessingKey }, cancellationToken: cancellationToken));
    }

    public async Task SetCdLayoutAsync(ulong discordId, string? layout, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO player_settings (discord_id, cd_layout)
            VALUES (@DiscordId, @Value)
            ON CONFLICT (discord_id) DO UPDATE SET cd_layout = EXCLUDED.cd_layout, updated_at = now();
            """,
            new { DiscordId = (long)discordId, Value = layout }, cancellationToken: cancellationToken));
    }
}
