using System.Data.Common;
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
