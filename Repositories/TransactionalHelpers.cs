using System.Data.Common;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// Helpers internos compartidos por los repositorios que necesitan renovar un cooldown y/o
// tocar el inventario como parte de una transacción más grande (AdventureRepository,
// GatheringRepository). No son un repositorio en sí: no abren conexión propia, operan
// sobre la conexión/transacción que les pasa el que los llama.
internal static class CooldownGuard
{
    // Upsert "guardado": si ya hay un cooldown vigente para este comando, el WHERE bloquea
    // la actualización y no devuelve fila. La validación del cooldown y su renovación quedan
    // así como una sola operación atómica, a prueba de dos ejecuciones casi simultáneas
    // del mismo comando (ej. doble click).
    public static async Task<bool> TryClaimAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO cooldowns (discord_id, command_name, last_executed_at)
            VALUES (@DiscordId, @CommandName, now())
            ON CONFLICT (discord_id, command_name) DO UPDATE
                SET last_executed_at = EXCLUDED.last_executed_at
                WHERE cooldowns.last_executed_at <= now() - @CooldownInterval
            RETURNING last_executed_at;
            """;

        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, CommandName = commandName, CooldownInterval = cooldownDuration },
            transaction: transaction,
            cancellationToken: cancellationToken);

        DateTime? applied = await connection.QuerySingleOrDefaultAsync<DateTime?>(command);
        return applied is not null;
    }
}

internal static class InventoryUpsert
{
    public static async Task AddItemAsync(
        DbConnection connection,
        DbTransaction transaction,
        ulong discordId,
        int itemId,
        int quantity,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO inventory (discord_id, item_id, quantity)
            VALUES (@DiscordId, @ItemId, @Quantity)
            ON CONFLICT (discord_id, item_id) DO UPDATE
                SET quantity = inventory.quantity + EXCLUDED.quantity;
            """;

        var command = new CommandDefinition(
            sql,
            new { DiscordId = (long)discordId, ItemId = itemId, Quantity = quantity },
            transaction: transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}

// Aplica la fórmula de nivelado (GameData/LevelingCalculator) sobre la fila de "users" que el que
// llama ya bloqueó con FOR UPDATE, para que UserRepository.AddXpAsync (vía ProgressionRepository),
// ProgressionRepository.ClaimDailyAsync y AdventureRepository.ApplyVictoryAsync compartan
// exactamente el mismo cálculo y el mismo UPDATE en vez de repetir el bloque tres veces.
// No abre transacción propia ni hace commit: opera sobre la conexión/transacción que le pasan.
internal static class LevelingApplier
{
    // hpDelta: daño neto (negativo) o curación neta (positiva) a aplicar ANTES de calcular el
    // nivelado (ver CombatState.PlayerStartingHp); pasar 0 si la operación no afecta HP de combate
    // (ej. /daily). Se aplica sobre current.CurrentHp (el HP real más reciente en base, no un
    // snapshot en memoria), así que compone bien con cualquier curación que haya ocurrido
    // mientras tanto (ej. "aa use" durante un combate por turnos).
    public static async Task<LevelUpOutcome> ApplyAsync(
        DbConnection connection,
        DbTransaction transaction,
        User current,
        int xpGained,
        int hpDelta,
        CancellationToken cancellationToken)
    {
        int hpBeforeLeveling = Math.Clamp(current.CurrentHp + hpDelta, 0, current.MaxHp);
        var leveling = LevelingCalculator.ApplyXpGain(current.Level, current.Xp, current.MaxHp, hpBeforeLeveling, xpGained);

        string sql = $"""
            UPDATE users
            SET level = @Level, xp = @Xp, max_hp = @MaxHp, current_hp = @CurrentHp
            WHERE discord_id = @DiscordId
            RETURNING {UserSql.SelectColumns};
            """;

        var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
            sql,
            new
            {
                DiscordId = current.DiscordId,
                Level = leveling.Level,
                Xp = leveling.Xp,
                MaxHp = leveling.MaxHp,
                CurrentHp = leveling.CurrentHp,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        return new LevelUpOutcome(updated, leveling.LevelsGained);
    }
}
