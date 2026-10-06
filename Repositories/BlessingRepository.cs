using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class BlessingRepository(IDbConnectionFactory connectionFactory) : IBlessingRepository
{
    public async Task<IReadOnlyDictionary<string, int>> GetLevelsAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<LevelRow>(new CommandDefinition(
            "SELECT blessing_key AS \"Key\", level AS \"Level\" FROM player_blessings WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return rows.ToDictionary(row => row.Key, row => row.Level, StringComparer.Ordinal);
    }

    public async Task<BlessingOffer?> GetPendingOfferAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var row = await connection.QueryFirstOrDefaultAsync<OfferRow>(new CommandDefinition(
            """
            SELECT fuego_nuevo_no AS "FuegoNuevoNo", offered_keys AS "OfferedKeys", chosen_key AS "ChosenKey"
            FROM blessing_offers
            WHERE discord_id = @DiscordId AND chosen_key IS NULL
            ORDER BY fuego_nuevo_no
            LIMIT 1;
            """,
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return row is null ? null : new BlessingOffer(row.FuegoNuevoNo, SplitKeys(row.OfferedKeys));
    }

    public async Task<ChooseOutcome> ChooseAsync(ulong discordId, int fuegoNuevoNo, string key, CancellationToken cancellationToken = default)
    {
        if (BlessingCatalog.Find(key) is null)
        {
            return new ChooseOutcome(ChooseStatus.NotOffered, null, 0, 0, 0);
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La fila del jugador queda tomada hasta el final: dos botones a la vez se hacen uno detrás del otro, y el segundo ya ve la oferta elegida.
            long? lockedPlayer = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                "SELECT discord_id FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (lockedPlayer is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChooseOutcome(ChooseStatus.NoAccount, null, 0, 0, 0);
            }

            var offer = await connection.QuerySingleOrDefaultAsync<OfferRow>(new CommandDefinition(
                """
                SELECT fuego_nuevo_no AS "FuegoNuevoNo", offered_keys AS "OfferedKeys", chosen_key AS "ChosenKey"
                FROM blessing_offers
                WHERE discord_id = @DiscordId AND fuego_nuevo_no = @No
                FOR UPDATE;
                """,
                new { DiscordId = (long)discordId, No = fuegoNuevoNo }, transaction: transaction, cancellationToken: cancellationToken));

            if (offer is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChooseOutcome(ChooseStatus.NoOffer, null, 0, 0, 0);
            }

            if (offer.ChosenKey is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChooseOutcome(ChooseStatus.AlreadyChosen, offer.ChosenKey, 0, 0, 0);
            }

            if (!SplitKeys(offer.OfferedKeys).Contains(key, StringComparer.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChooseOutcome(ChooseStatus.NotOffered, null, 0, 0, 0);
            }

            int currentLevel = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "SELECT level FROM player_blessings WHERE discord_id = @DiscordId AND blessing_key = @Key;",
                new { DiscordId = (long)discordId, Key = key }, transaction: transaction, cancellationToken: cancellationToken)) ?? 0;

            if (currentLevel >= BlessingCatalog.MaxLevel)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChooseOutcome(ChooseStatus.MaxLevel, key, currentLevel, 0, 0);
            }

            // Guarda de la oferta: solo se marca si sigue sin elegir (con el FOR UPDATE de arriba no debería pasar nada, pero es la regla de la casa).
            int? marked = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE blessing_offers SET chosen_key = @Key
                WHERE discord_id = @DiscordId AND fuego_nuevo_no = @No AND chosen_key IS NULL
                RETURNING 1;
                """,
                new { DiscordId = (long)discordId, No = fuegoNuevoNo, Key = key }, transaction: transaction, cancellationToken: cancellationToken));

            if (marked is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ChooseOutcome(ChooseStatus.AlreadyChosen, null, 0, 0, 0);
            }

            int newLevel = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                INSERT INTO player_blessings (discord_id, blessing_key, level) VALUES (@DiscordId, @Key, 1)
                ON CONFLICT (discord_id, blessing_key) DO UPDATE SET level = LEAST(@MaxLevel, player_blessings.level + 1)
                RETURNING level;
                """,
                new { DiscordId = (long)discordId, Key = key, MaxLevel = BlessingCatalog.MaxLevel }, transaction: transaction, cancellationToken: cancellationToken));

            var (petFood, boxes) = key == BlessingCatalog.SatchelKey
                ? await SatchelGrant.GrantAsync(connection, transaction, discordId, newLevel, cancellationToken)
                : (0, 0);

            await transaction.CommitAsync(cancellationToken);
            return new ChooseOutcome(ChooseStatus.Ok, key, newLevel, petFood, boxes);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Las claves de la oferta, guardadas como texto separado por comas.
    internal static IReadOnlyList<string> SplitKeys(string offeredKeys) =>
        offeredKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Dapper arma los records posicionales por el constructor entero: cada consulta tiene que devolver TODAS las columnas (aunque ChosenKey venga siempre NULL).
    private sealed record OfferRow(int FuegoNuevoNo, string OfferedKeys, string? ChosenKey);

    private sealed record LevelRow(string Key, int Level);
}
