using System.Data;
using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class AdventureRepository(IDbConnectionFactory connectionFactory) : IAdventureRepository
{
    public async Task<bool> TryClaimCooldownAsync(
        ulong discordId, string commandName, TimeSpan cooldownDuration, TimeSpan? remainingIfNotCompleted = null, CancellationToken cancellationToken = default)
    {
        // Mismo upsert "guardado" que CooldownGuard (Repositories/TransactionalHelpers.cs), pero
        // standalone: acá no hace falta una transacción explícita, la sentencia ya es atómica.
        // remainingIfNotCompleted (el jefe): se reclama dejando solo ese tiempo (last_executed_at corrido hacia atrás); el cooldown COMPLETO recién
        // lo pone una victoria (ApplyVictoryInternalAsync). La guarda de abajo sigue pidiendo que haya pasado la duración entera.
        const string sql = """
            INSERT INTO cooldowns (discord_id, command_name, last_executed_at)
            VALUES (@DiscordId, @CommandName, now() - @Shift)
            ON CONFLICT (discord_id, command_name) DO UPDATE
                SET last_executed_at = EXCLUDED.last_executed_at
                WHERE cooldowns.last_executed_at <= now() - @CooldownInterval
            RETURNING last_executed_at;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            sql,
            new
            {
                DiscordId = (long)discordId,
                CommandName = commandName,
                CooldownInterval = cooldownDuration,
                Shift = remainingIfNotCompleted is { } remaining ? cooldownDuration - remaining : TimeSpan.Zero,
            },
            cancellationToken: cancellationToken);

        DateTime? applied = await connection.QuerySingleOrDefaultAsync<DateTime?>(command);
        return applied is not null;
    }

    public Task<LevelUpOutcome> ApplyVictoryAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        CancellationToken cancellationToken = default) =>
        ApplyVictoryInternalAsync(discordId, goldReward, xpReward, hpDelta, droppedItemId, droppedItemQuantity, clearedZoneId: null, cancellationToken);

    public Task<LevelUpOutcome> ApplyBossVictoryAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        int clearedZoneId,
        CancellationToken cancellationToken = default) =>
        ApplyVictoryInternalAsync(discordId, goldReward, xpReward, hpDelta, droppedItemId, droppedItemQuantity, clearedZoneId, cancellationToken);

    private async Task<LevelUpOutcome> ApplyVictoryInternalAsync(
        ulong discordId,
        int goldReward,
        int xpReward,
        int hpDelta,
        int? droppedItemId,
        int droppedItemQuantity,
        int? clearedZoneId,
        CancellationToken cancellationToken)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: bloquea la fila hasta el commit para que otra operación concurrente
            // sobre el mismo usuario no pise este cálculo.
            string selectSql = $"""
                SELECT {UserSql.SelectColumns}
                FROM users
                WHERE discord_id = @DiscordId
                FOR UPDATE;
                """;

            var current = await connection.QuerySingleAsync<User>(new CommandDefinition(
                selectSql, new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            var leveled = await LevelingApplier.ApplyAsync(connection, transaction, current, xpReward, hpDelta, cancellationToken);

            // Segunda UPDATE solo para el oro (no es parte del nivelado): mantiene LevelingApplier
            // genérico y reusable por ProgressionRepository, que no reparte oro. Si es un jefe
            // (clearedZoneId no nulo), la MISMA sentencia sube highest_zone_cleared con GREATEST
            // (nunca lo baja) — atómico con el resto, sin una tercera ida a la base.
            string updateGoldSql = $"""
                UPDATE users
                SET gold = gold + @Gold,
                    highest_zone_cleared = CASE
                        WHEN @ClearedZoneId IS NOT NULL THEN GREATEST(highest_zone_cleared, @ClearedZoneId)
                        ELSE highest_zone_cleared
                    END
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """;

            // Ganarle al jefe completa el cooldown (se reclamó al empezar dejando solo la mitad, ver TryClaimCooldownAsync): la MISMA transacción que el
            // premio, así una victoria nunca queda sin su cooldown entero ni un cooldown entero sin victoria.
            if (clearedZoneId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE cooldowns SET last_executed_at = now() WHERE discord_id = @DiscordId AND command_name = @CommandName;",
                    new { DiscordId = (long)discordId, CommandName = BotDsRpg.GameData.CooldownCatalog.Boss.CommandName },
                    transaction: transaction, cancellationToken: cancellationToken));
            }

            var finalUser = await connection.QuerySingleAsync<User>(new CommandDefinition(
                updateGoldSql,
                new { DiscordId = (long)discordId, Gold = goldReward, ClearedZoneId = clearedZoneId },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (droppedItemId is not null)
            {
                await InventoryUpsert.AddItemAsync(connection, transaction, discordId, droppedItemId.Value, droppedItemQuantity, cancellationToken);
            }

            // El huevo de la zona (v0.10.0): la PRIMERA vez que este jugador vence a su jefe (current es la fila bloqueada, con el highest_zone_cleared de ANTES de esta
            // victoria), en la misma transacción que el cofre. Si por algún motivo ya tiene esa mascota no se le da otro huevo.
            PetSpecies? eggGranted = null;
            if (clearedZoneId is { } clearedZone && current.HighestZoneCleared < clearedZone)
            {
                var species = await PetSql.FindByZoneAsync(connection, transaction, clearedZone, cancellationToken);
                bool alreadyOwned = species is not null && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM player_pets WHERE discord_id = @DiscordId AND species_id = @SpeciesId);",
                    new { DiscordId = (long)discordId, SpeciesId = species.SpeciesId }, transaction: transaction, cancellationToken: cancellationToken));

                if (species is not null && !alreadyOwned)
                {
                    await InventoryUpsert.AddItemAsync(connection, transaction, discordId, species.EggItemId, 1, cancellationToken);
                    eggGranted = species;
                }
            }

            // El Fogón Eterno (v0.11.0), en la MISMA transacción que el premio: ganarle al Asador (la zona puerta) deja gate_cleared en true, lo saca de la puerta y lo manda de vuelta
            // a la última zona de la escalera (no toca highest_zone_cleared: el 0 de la puerta no pesa en el GREATEST de arriba). Y la primera vez que vence al jefe de la última
            // zona se avisa que la puerta se abrió (GameData/FogonRules.IsGateOpen sale de ese mismo highest_zone_cleared).
            var gate = GateEvent.None;
            if (clearedZoneId is { } fought)
            {
                bool isGateFight = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM zones WHERE zone_id = @ZoneId AND kind = 'gate');",
                    new { ZoneId = fought }, transaction: transaction, cancellationToken: cancellationToken));

                if (isGateFight)
                {
                    finalUser = await connection.QuerySingleAsync<User>(new CommandDefinition(
                        $"""
                        UPDATE users
                        SET gate_cleared = true, in_gate = false,
                            current_zone_id = (SELECT zone_id FROM zones WHERE kind = 'normal' ORDER BY min_level DESC LIMIT 1)
                        WHERE discord_id = @DiscordId
                        RETURNING {UserSql.SelectColumns};
                        """,
                        new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
                    gate = GateEvent.Cleared;
                }
                else if (current.HighestZoneCleared < fought && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT @ZoneId = (SELECT zone_id FROM zones WHERE kind = 'normal' ORDER BY min_level DESC LIMIT 1);",
                    new { ZoneId = fought }, transaction: transaction, cancellationToken: cancellationToken)))
                {
                    gate = GateEvent.Opened;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new LevelUpOutcome(finalUser, leveled.LevelsGained, eggGranted, gate);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
