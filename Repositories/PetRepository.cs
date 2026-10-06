using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Services;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class PetRepository(IDbConnectionFactory connectionFactory) : IPetRepository
{
    public async Task<IReadOnlyList<PetSpecies>> GetSpeciesAsync(CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<PetSpeciesRow>(new CommandDefinition(
            $"SELECT {PetSql.SpeciesColumns} FROM {PetSql.SpeciesFrom} ORDER BY z.min_level, s.zone_id;", cancellationToken: cancellationToken));

        return rows.Select(row => row.ToSpecies()).ToList();
    }

    public async Task<IReadOnlyList<OwnedPet>> GetOwnedAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        return await QueryOwnedAsync(connection, null, discordId, speciesId: null, lockRows: false, cancellationToken);
    }

    public async Task<PetBonuses> GetBonusesAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        try
        {
            return PetRules.Total(await GetOwnedAsync(discordId, cancellationToken));
        }
        catch (Exception ex)
        {
            // Un extra de la pelea: si falla la lectura el jugador pelea sin bonus, no se queda sin pelear (ni se cae el resultado de una victoria).
            BotLog.Warn(ex);
            return PetBonuses.None;
        }
    }

    public async Task<HatchOutcome> HatchAsync(ulong discordId, int eggItemId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var species = (await connection.QuerySingleOrDefaultAsync<PetSpeciesRow>(new CommandDefinition(
                $"SELECT {PetSql.SpeciesColumns} FROM {PetSql.SpeciesFrom} WHERE s.egg_item_id = @EggItemId;",
                new { EggItemId = eggItemId }, transaction: transaction, cancellationToken: cancellationToken)))?.ToSpecies();

            if (species is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new HatchOutcome(HatchStatus.NotAnEgg, null);
            }

            // La fila del jugador queda tomada hasta el final: dos aperturas a la vez se hacen una detrás de la otra.
            long? lockedPlayer = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                "SELECT discord_id FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (lockedPlayer is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new HatchOutcome(HatchStatus.NoAccount, species);
            }

            // Guarda del huevo: el mismo patrón de siempre (sin fila = no tenía, y no se toca nada).
            int? eggsLeft = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE inventory SET quantity = quantity - 1
                WHERE discord_id = @DiscordId AND item_id = @EggItemId AND quantity >= 1
                RETURNING quantity;
                """,
                new { DiscordId = (long)discordId, EggItemId = eggItemId }, transaction: transaction, cancellationToken: cancellationToken));

            if (eggsLeft is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new HatchOutcome(HatchStatus.NoEgg, species);
            }

            // Si ya la tenía no se hace nada (ON CONFLICT DO NOTHING) y el rollback le devuelve el huevo.
            int? born = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                INSERT INTO player_pets (discord_id, species_id) VALUES (@DiscordId, @SpeciesId)
                ON CONFLICT (discord_id, species_id) DO NOTHING
                RETURNING species_id;
                """,
                new { DiscordId = (long)discordId, SpeciesId = species.SpeciesId }, transaction: transaction, cancellationToken: cancellationToken));

            if (born is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new HatchOutcome(HatchStatus.AlreadyOwned, species);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @EggItemId AND quantity <= 0;",
                new { DiscordId = (long)discordId, EggItemId = eggItemId }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new HatchOutcome(HatchStatus.Ok, species);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<FeedOutcome> FeedAsync(ulong discordId, int speciesId, TimeSpan? cooldown = null, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE sobre la fila de ESA mascota: dos alimentaciones a la vez se hacen una detrás de la otra, y la segunda ve el last_fed_at de la primera.
            var pet = (await QueryOwnedAsync(connection, transaction, discordId, speciesId, lockRows: true, cancellationToken)).FirstOrDefault();

            if (pet is null)
            {
                bool hasAccount = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM users WHERE discord_id = @DiscordId);",
                    new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
                await transaction.RollbackAsync(cancellationToken);
                return new FeedOutcome(hasAccount ? FeedStatus.NotOwned : FeedStatus.NoAccount, null, 0, 0, TimeSpan.Zero);
            }

            int levelBefore = PetRules.LevelFor(pet.FeedPoints);
            if (levelBefore >= PetRules.MaxLevel)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new FeedOutcome(FeedStatus.MaxLevel, pet, levelBefore, 0, TimeSpan.Zero);
            }

            // El cooldown se mide con el reloj de la base (el mismo que escribe last_fed_at), no con el del bot: así no depende de que las dos horas coincidan.
            double secondsLeft = await connection.ExecuteScalarAsync<double>(new CommandDefinition(
                """
                SELECT COALESCE(GREATEST(0, EXTRACT(EPOCH FROM (last_fed_at + @Cooldown - now()))), 0)::double precision
                FROM player_pets WHERE discord_id = @DiscordId AND species_id = @SpeciesId;
                """,
                new { DiscordId = (long)discordId, SpeciesId = speciesId, Cooldown = cooldown ?? PetRules.FeedCooldown }, transaction: transaction, cancellationToken: cancellationToken));

            if (secondsLeft > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new FeedOutcome(FeedStatus.OnCooldown, pet, levelBefore, 0, TimeSpan.FromSeconds(secondsLeft));
            }

            // Guarda de la comida: sin fila = no tenía (o la gastó otro comando un instante antes).
            int? foodLeft = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE inventory SET quantity = quantity - 1
                WHERE discord_id = @DiscordId AND quantity >= 1 AND item_id = (SELECT item_id FROM items WHERE name = @FoodName)
                RETURNING quantity;
                """,
                new { DiscordId = (long)discordId, FoodName = PetRules.FoodItemName }, transaction: transaction, cancellationToken: cancellationToken));

            if (foodLeft is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new FeedOutcome(FeedStatus.NoFood, pet, levelBefore, 0, TimeSpan.Zero);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND quantity <= 0 AND item_id = (SELECT item_id FROM items WHERE name = @FoodName);",
                new { DiscordId = (long)discordId, FoodName = PetRules.FoodItemName }, transaction: transaction, cancellationToken: cancellationToken));

            var fed = await connection.QuerySingleAsync<FedRow>(new CommandDefinition(
                """
                UPDATE player_pets SET feed_points = feed_points + 1, last_fed_at = now()
                WHERE discord_id = @DiscordId AND species_id = @SpeciesId
                RETURNING feed_points AS "FeedPoints", last_fed_at AS "LastFedAt";
                """,
                new { DiscordId = (long)discordId, SpeciesId = speciesId }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            var after = new OwnedPet(pet.Species, fed.FeedPoints, DateTime.SpecifyKind(fed.LastFedAt, DateTimeKind.Utc));
            return new FeedOutcome(FeedStatus.Ok, after, levelBefore, foodLeft.Value, cooldown ?? PetRules.FeedCooldown);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Las mascotas de un jugador (o solo la de una especie), con su especie. lockRows: toma la fila de player_pets con FOR UPDATE (solo dentro de una transacción).
    private static async Task<IReadOnlyList<OwnedPet>> QueryOwnedAsync(
        DbConnection connection, DbTransaction? transaction, ulong discordId, int? speciesId, bool lockRows, CancellationToken cancellationToken)
    {
        // FOR UPDATE OF p: bloquea solo la fila de la mascota (las otras tablas del JOIN son catálogo y no hace falta).
        string sql = $"""
            SELECT {PetSql.SpeciesColumns}, p.feed_points AS "FeedPoints", p.last_fed_at AS "LastFedAt"
            FROM player_pets p
            JOIN {PetSql.SpeciesFrom} ON s.species_id = p.species_id
            WHERE p.discord_id = @DiscordId AND (CAST(@SpeciesId AS integer) IS NULL OR p.species_id = CAST(@SpeciesId AS integer))
            ORDER BY z.min_level, s.zone_id
            {(lockRows ? "FOR UPDATE OF p" : string.Empty)};
            """;

        var rows = await connection.QueryAsync<OwnedPetRow>(new CommandDefinition(
            sql, new { DiscordId = (long)discordId, SpeciesId = speciesId }, transaction: transaction, cancellationToken: cancellationToken));

        return rows
            .Select(row => new OwnedPet(
                row.ToSpecies(), row.FeedPoints, row.LastFedAt is { } fedAt ? DateTime.SpecifyKind(fedAt, DateTimeKind.Utc) : null))
            .ToList();
    }

    private sealed record FedRow(int FeedPoints, DateTime LastFedAt);
}
