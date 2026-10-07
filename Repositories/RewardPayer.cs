using System.Data.Common;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// Paga un premio de misión o de logro (oro + XP con subida de nivel + una caja) DENTRO de la transacción del que lo llama, para
// que cobrar la misión y recibir el premio sean una sola operación: si algo falla, no queda ni cobrada ni pagada. Lo comparten
// MissionRepository y AchievementRepository. No abre conexión ni hace commit.
internal static class RewardPayer
{
    // "current" tiene que ser la fila de users ya bloqueada con FOR UPDATE por quien llama: el nivel sale de ahí, y el bloqueo
    // es lo que impide que dos cobros del mismo jugador a la vez se pisen el XP.
    public static async Task<RewardReceipt> PayAsync(
        DbConnection connection, DbTransaction transaction, User current, RewardSpec spec, int zoneRank, CancellationToken cancellationToken)
    {
        // La caja de la zona sale de zone_boxes, leída en ESTA transacción (el premio se paga con la misma tabla que se lee). Los premios sin caja de zona no la consultan.
        var boxes = spec.Box is BoxGrant.None or BoxGrant.Mythic
            ? ZoneBoxTable.Empty
            : await ZoneBoxQueries.LoadAsync(connection, transaction, cancellationToken);
        var reward = MissionRewards.Resolve(spec, zoneRank, current.Level, boxes);

        int levelsGained = 0;
        int newLevel = current.Level;
        if (reward.Xp > 0)
        {
            var leveled = await LevelingApplier.ApplyAsync(connection, transaction, current, reward.Xp, hpDelta: 0, cancellationToken);
            levelsGained = leveled.LevelsGained;
            newLevel = leveled.Player.Level;
        }

        int goldAfter = current.Gold;
        if (reward.Gold > 0)
        {
            goldAfter = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "UPDATE users SET gold = gold + @Gold WHERE discord_id = @DiscordId RETURNING gold;",
                new { Gold = reward.Gold, DiscordId = current.DiscordId }, transaction: transaction, cancellationToken: cancellationToken));
        }

        if (reward.BoxName is not null)
        {
            int? boxItemId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "SELECT item_id FROM items WHERE name = @Name AND type = 'Caja';",
                new { Name = reward.BoxName }, transaction: transaction, cancellationToken: cancellationToken));

            // Una caja que no existe es un error de carga de datos, no algo para tragarse: tira la excepción, se revierte
            // el cobro entero (la misión queda sin cobrar) y queda en el log, en vez de marcar el premio como pagado sin la caja.
            if (boxItemId is null)
            {
                throw new InvalidOperationException($"El premio pide la caja \"{reward.BoxName}\" pero no existe en items (type = 'Caja').");
            }

            await InventoryUpsert.AddItemAsync(
                connection, transaction, (ulong)current.DiscordId, boxItemId.Value, reward.BoxQuantity, cancellationToken);
        }

        return new RewardReceipt(reward, goldAfter, levelsGained, newLevel);
    }

    // La fila del jugador bloqueada hasta el final de la transacción. Null si no existe la cuenta.
    public static Task<User?> LockUserAsync(
        DbConnection connection, DbTransaction transaction, ulong discordId, CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<User?>(new CommandDefinition(
            $"""
            SELECT {UserSql.SelectColumns}
            FROM users
            WHERE discord_id = @DiscordId
            FOR UPDATE;
            """,
            new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
}
