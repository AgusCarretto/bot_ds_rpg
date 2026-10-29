using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class PetExpeditionRepository(IDbConnectionFactory connectionFactory) : IPetExpeditionRepository
{
    public async Task<ActiveExpedition?> GetAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        return await QueryActiveAsync(connection, null, discordId, cancellationToken);
    }

    public async Task<ExpeditionStartOutcome> StartAsync(ulong discordId, int speciesId, int hours, CancellationToken cancellationToken = default)
    {
        if (PetExpeditions.OptionFor(hours) is not { } startOption)
        {
            return new ExpeditionStartOutcome(ExpeditionStartStatus.BadDuration, null, null, 0);
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La fila del jugador queda tomada hasta el final: dos salidas a la vez se hacen una detrás de la otra y la segunda ve a la primera.
            var player = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionStartOutcome(ExpeditionStartStatus.NoAccount, null, null, 0);
            }

            var current = await QueryActiveAsync(connection, transaction, discordId, cancellationToken);
            if (current is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionStartOutcome(ExpeditionStartStatus.AlreadyOut, current, current.Species, 0);
            }

            var pet = await connection.QuerySingleOrDefaultAsync<OwnedPetRow>(new CommandDefinition(
                $"""
                SELECT {PetSql.SpeciesColumns}, p.feed_points AS "FeedPoints", p.last_fed_at AS "LastFedAt", p.tier::int AS "Tier", p.expedition_hours AS "ExpeditionHours"
                FROM player_pets p
                JOIN {PetSql.SpeciesFrom} ON s.species_id = p.species_id
                WHERE p.discord_id = @DiscordId AND p.species_id = @SpeciesId
                FOR UPDATE OF p;
                """,
                new { DiscordId = (long)discordId, SpeciesId = speciesId }, transaction: transaction, cancellationToken: cancellationToken));

            if (pet is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionStartOutcome(ExpeditionStartStatus.NotOwned, null, null, 0);
            }

            var species = pet.ToSpecies();
            if (PetRules.LevelFor(pet.FeedPoints) < PetExpeditions.RequiredLevel)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionStartOutcome(ExpeditionStartStatus.NotMaxLevel, null, species, 0);
            }

            // Guarda de la comida: sin fila = no alcanzaba (o la gastó otro comando un instante antes) y no se toca nada.
            int? foodLeft = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE inventory SET quantity = quantity - @Cost
                WHERE discord_id = @DiscordId AND quantity >= @Cost AND item_id = (SELECT item_id FROM items WHERE name = @FoodName)
                RETURNING quantity;
                """,
                new { DiscordId = (long)discordId, Cost = startOption.FoodCost, FoodName = PetRules.FoodItemName }, transaction: transaction, cancellationToken: cancellationToken));

            if (foodLeft is null)
            {
                int have = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COALESCE(SUM(quantity), 0)::int FROM inventory
                    WHERE discord_id = @DiscordId AND item_id = (SELECT item_id FROM items WHERE name = @FoodName);
                    """,
                    new { DiscordId = (long)discordId, FoodName = PetRules.FoodItemName }, transaction: transaction, cancellationToken: cancellationToken));
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionStartOutcome(ExpeditionStartStatus.NoFood, null, species, have);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND quantity <= 0 AND item_id = (SELECT item_id FROM items WHERE name = @FoodName);",
                new { DiscordId = (long)discordId, FoodName = PetRules.FoodItemName }, transaction: transaction, cancellationToken: cancellationToken));

            // El final de la expedición lo calcula la base con SU reloj (el mismo que la cobra). ON CONFLICT es la segunda red: la fila del jugador ya ordena las salidas.
            int? inserted = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                INSERT INTO pet_expeditions (discord_id, species_id, hours, started_at, ends_at)
                VALUES (@DiscordId, @SpeciesId, @Hours, now(), now() + make_interval(hours => @Hours))
                ON CONFLICT (discord_id) DO NOTHING
                RETURNING hours;
                """,
                new { DiscordId = (long)discordId, SpeciesId = speciesId, Hours = hours }, transaction: transaction, cancellationToken: cancellationToken));

            if (inserted is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionStartOutcome(ExpeditionStartStatus.AlreadyOut, await GetAsync(discordId, cancellationToken), species, 0);
            }

            var started = await QueryActiveAsync(connection, transaction, discordId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ExpeditionStartOutcome(ExpeditionStartStatus.Ok, started, species, foodLeft.Value);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ExpeditionClaimOutcome> ClaimAsync(ulong discordId, Random? rng = null, CancellationToken cancellationToken = default)
    {
        rng ??= Random.Shared;
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var player = await RewardPayer.LockUserAsync(connection, transaction, discordId, cancellationToken);
            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionClaimOutcome(ExpeditionClaimStatus.NoAccount, null, null, null, TimeSpan.Zero);
            }

            // Cobrar = borrar la fila SI ya volvió, con el reloj de la base: de dos clics a la vez solo uno recibe la fila. Antes de borrar se la lee (el DELETE no puede unir la especie).
            var active = await QueryActiveAsync(connection, transaction, discordId, cancellationToken);
            if (active is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionClaimOutcome(ExpeditionClaimStatus.NoExpedition, null, null, null, TimeSpan.Zero);
            }

            if (!active.IsReady)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionClaimOutcome(ExpeditionClaimStatus.NotReady, active, null, null, active.Remaining);
            }

            int deleted = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM pet_expeditions WHERE discord_id = @DiscordId AND ends_at <= now();",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
            if (deleted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ExpeditionClaimOutcome(ExpeditionClaimStatus.NoExpedition, null, null, null, TimeSpan.Zero);
            }

            var option = PetExpeditions.OptionFor(active.Hours)
                ?? throw new InvalidOperationException($"La expedición guardada tiene {active.Hours} horas y PetExpeditions.Options no la conoce.");
            var species = active.Species;
            var kind = PetExpeditions.FindKindOf(species.BonusKind);
            bool found = PetExpeditions.RollFind(option, rng);

            // El tier de la mascota (I, II, III) multiplica el premio ×1 / ×2 / ×4, y las horas de esta expedición suman a las que la mascota lleva acumuladas (cuentan para evolucionarla).
            int tier = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT tier FROM player_pets WHERE discord_id = @DiscordId AND species_id = @SpeciesId;",
                new { DiscordId = (long)discordId, SpeciesId = species.SpeciesId }, transaction: transaction, cancellationToken: cancellationToken)) ?? 1;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE player_pets SET expedition_hours = expedition_hours + @Hours WHERE discord_id = @DiscordId AND species_id = @SpeciesId;",
                new { Hours = active.Hours, DiscordId = (long)discordId, SpeciesId = species.SpeciesId }, transaction: transaction, cancellationToken: cancellationToken));

            // El premio NO depende de la zona ni de la especie: el oro es un valor del rango de esa duración y la EXP un % del nivel del jugador (con subida de nivel), todo en la misma transacción.
            // Solo el hallazgo de oro o de EXP suma un 50 % a su parte. La EXP va por RewardPayer (sin oro: el oro de una expedición no son "cacerías de la zona") y el oro se suma acá, frenado en int.MaxValue.
            int gold = PetExpeditions.RollGold(option, found && kind == ExpeditionFindKind.GoldBag, rng, tier);
            var xpReceipt = await RewardPayer.PayAsync(
                connection, transaction, player, new RewardSpec(0, PetExpeditions.XpPercentFor(option, found && kind == ExpeditionFindKind.XpBoost, tier)), 1, cancellationToken);
            int goldAfter = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "UPDATE users SET gold = LEAST(gold::bigint + @Gold, 2147483647) WHERE discord_id = @DiscordId RETURNING gold;",
                new { Gold = gold, DiscordId = player.DiscordId }, transaction: transaction, cancellationToken: cancellationToken));
            var receipt = new RewardReceipt(new ResolvedReward(gold, xpReceipt.Reward.Xp, null, 0), goldAfter, xpReceipt.LevelsGained, xpReceipt.NewLevel);

            ExpeditionFindResult? find = null;
            if (found)
            {
                find = await ApplyFindAsync(connection, transaction, player, kind, option, rng, tier, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ExpeditionClaimOutcome(ExpeditionClaimStatus.Ok, active, receipt, find, TimeSpan.Zero);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // El hallazgo que no es oro ni EXP. Si no hay nada que dar (una zona sin drops cargados) devuelve un resultado vacío: la expedición igual se cobró y no se rompe nada.
    private static async Task<ExpeditionFindResult> ApplyFindAsync(
        DbConnection connection, DbTransaction transaction, User player, ExpeditionFindKind kind, ExpeditionOption option, Random rng, int tier, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ExpeditionFindKind.GoldBag or ExpeditionFindKind.XpBoost:
                return new ExpeditionFindResult(kind, 0, []);

            case ExpeditionFindKind.Dust:
                // El Polvo no se desborda: se frena en int.MaxValue como el resto del juego.
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE users SET dust = LEAST(dust::bigint + @Dust, 2147483647) WHERE discord_id = @DiscordId;",
                    new { Dust = PetExpeditions.DustFor(option, tier), DiscordId = player.DiscordId }, transaction: transaction, cancellationToken: cancellationToken));
                return new ExpeditionFindResult(kind, PetExpeditions.DustFor(option, tier), []);

            case ExpeditionFindKind.HuntDrops:
            {
                // Los drops de los monstruos de cacería de la zona donde está el jugador (uno de los dos al azar).
                var drops = (await connection.QueryAsync<(int ItemId, string Name)>(new CommandDefinition(
                    """
                    SELECT DISTINCT i.item_id, i.name
                    FROM monsters m
                    JOIN monster_drops md ON md.monster_id = m.monster_id
                    JOIN items i ON i.item_id = md.item_id
                    WHERE m.zone_id = @ZoneId AND NOT m.is_boss AND NOT m.is_travel AND i.type = 'Material'
                    ORDER BY i.item_id;
                    """,
                    new { ZoneId = player.CurrentZoneId }, transaction: transaction, cancellationToken: cancellationToken))).ToList();

                if (drops.Count == 0)
                {
                    return new ExpeditionFindResult(kind, 0, []);
                }

                var chosen = drops[rng.Next(drops.Count)];
                int dropQuantity = PetExpeditions.DropQuantityFor(option, tier);
                await InventoryUpsert.AddItemAsync(connection, transaction, (ulong)player.DiscordId, chosen.ItemId, dropQuantity, cancellationToken);
                return new ExpeditionFindResult(kind, dropQuantity, [(chosen.Name, dropQuantity)]);
            }

            case ExpeditionFindKind.Gather:
            {
                var pool = (await connection.QueryAsync<GatherCandidate>(new CommandDefinition(
                    """
                    SELECT item_id AS "ItemId", name AS "Name", rarity AS "Rarity", emoji AS "Emoji", type AS "Type"
                    FROM items WHERE type IN ('Madera', 'Mineral') AND rarity <> 'Mítico'
                    ORDER BY item_id;
                    """,
                    transaction: transaction, cancellationToken: cancellationToken))).ToList();

                var rolled = PetExpeditions.RollGather(pool, PetExpeditions.GatherRollsFor(option, tier), rng);
                foreach (var (item, quantity) in rolled)
                {
                    await InventoryUpsert.AddItemAsync(connection, transaction, (ulong)player.DiscordId, item.ItemId, quantity, cancellationToken);
                }

                return new ExpeditionFindResult(kind, rolled.Sum(r => r.Quantity), rolled.Select(r => (r.Item.Name, r.Quantity)).ToList());
            }

            default:
                return new ExpeditionFindResult(kind, 0, []);
        }
    }

    private static async Task<ActiveExpedition?> QueryActiveAsync(DbConnection connection, DbTransaction? transaction, ulong discordId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ActiveRow>(new CommandDefinition(
            $"""
            SELECT {PetSql.SpeciesColumns}, x.hours AS "Hours", x.started_at AS "StartedAt", x.ends_at AS "EndsAt",
                   GREATEST(0, EXTRACT(EPOCH FROM (x.ends_at - now())))::double precision AS "SecondsLeft"
            FROM pet_expeditions x
            JOIN {PetSql.SpeciesFrom} ON s.species_id = x.species_id
            WHERE x.discord_id = @DiscordId;
            """,
            new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new ActiveExpedition(
                row.ToSpecies(), row.Hours, DateTime.SpecifyKind(row.StartedAt, DateTimeKind.Utc), DateTime.SpecifyKind(row.EndsAt, DateTimeKind.Utc),
                TimeSpan.FromSeconds(row.SecondsLeft));
    }

    private sealed record ActiveRow(
        int SpeciesId, int ZoneId, string ZoneName, string Name, string? Emoji, string BonusKind, double MaxBonusPercent, int EggItemId, string EggName,
        int Hours, DateTime StartedAt, DateTime EndsAt, double SecondsLeft)
    {
        public PetSpecies ToSpecies() => new(SpeciesId, ZoneId, ZoneName, Name, Emoji, BonusKind, MaxBonusPercent, EggItemId, EggName);
    }
}
