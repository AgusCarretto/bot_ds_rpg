using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class TitleRepository(IDbConnectionFactory connectionFactory) : ITitleRepository
{
    private sealed record StatRow(string StatKey, long Value);

    private sealed record FlagRow(bool Corteza, bool Meteorito, bool EnchantV);

    public async Task<TitleContext> GetContextAsync(ulong discordId, IReadOnlyList<Zone> zonesByDifficulty, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        long id = (long)discordId;

        var stats = (await connection.QueryAsync<StatRow>(new CommandDefinition(
            "SELECT stat_key AS StatKey, value AS Value FROM player_stats WHERE discord_id = @Id;",
            new { Id = id }, cancellationToken: cancellationToken))).ToDictionary(r => r.StatKey, r => r.Value);

        // El nivel más alto alcanzado alguna vez: el nivel de la cuenta se reinicia con cada Fuego Nuevo, pero cada subida dejó un evento «level_up» con el nivel nuevo de detalle.
        int maxLevel = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT GREATEST(
                COALESCE((SELECT level FROM users WHERE discord_id = @Id), 1),
                COALESCE((SELECT MAX(detail::int) FROM game_events WHERE discord_id = @Id AND kind = 'level_up' AND detail ~ '^[0-9]{1,4}$'), 1));
            """,
            new { Id = id }, cancellationToken: cancellationToken));

        // Los jefes de la escalera que venció, en cualquier vuelta (un evento de victoria lleva la zona en que estaba parado). El rango es la posición por dificultad.
        var zoneIds = await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT DISTINCT zone_id FROM game_events WHERE discord_id = @Id AND kind = ANY(@Kinds) AND zone_id IS NOT NULL;",
            new { Id = id, Kinds = new[] { GameEventKinds.BossWin, GameEventKinds.RaidWin } }, cancellationToken: cancellationToken));
        var ranks = new HashSet<int>();
        foreach (int zoneId in zoneIds)
        {
            int index = zonesByDifficulty.ToList().FindIndex(z => z.ZoneId == zoneId);
            if (index >= 0)
            {
                ranks.Add(index + 1);
            }
        }

        // Los hallazgos únicos: las veces que salió una Corteza o un Meteorito quedaron en el detalle de /chop y /mine, y un encantamiento tier V en el de «enchant» («weapon:5» / «amulet:5»).
        var flagRow = await connection.QuerySingleAsync<FlagRow>(new CommandDefinition(
            """
            SELECT
                EXISTS (SELECT 1 FROM game_events WHERE discord_id = @Id AND kind = 'chop' AND detail LIKE @Corteza) AS Corteza,
                EXISTS (SELECT 1 FROM game_events WHERE discord_id = @Id AND kind = 'mine' AND detail LIKE @Meteorito) AS Meteorito,
                EXISTS (SELECT 1 FROM game_events WHERE discord_id = @Id AND kind = 'enchant' AND detail LIKE '%:5') AS EnchantV;
            """,
            new { Id = id, Corteza = "%Corteza del Árbol de Vida%", Meteorito = "%Fragmento de Meteorito%" }, cancellationToken: cancellationToken));
        var flags = new HashSet<string>();
        if (flagRow.Corteza) { flags.Add(TitleCatalog.FlagCorteza); }
        if (flagRow.Meteorito) { flags.Add(TitleCatalog.FlagMeteorito); }
        if (flagRow.EnchantV) { flags.Add(TitleCatalog.FlagEnchantV); }

        int colorLevel = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(level), 0) FROM player_blessings WHERE discord_id = @Id AND blessing_key = @Key;",
            new { Id = id, Key = BlessingCatalog.ColorKey }, cancellationToken: cancellationToken));

        return new TitleContext(stats, maxLevel, ranks, flags, colorLevel);
    }

    public async Task<bool> SetEquippedAsync(ulong discordId, string? titleKey, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        int rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET equipped_title = @Key WHERE discord_id = @Id;",
            new { Id = (long)discordId, Key = titleKey }, cancellationToken: cancellationToken));
        return rows > 0;
    }
}
