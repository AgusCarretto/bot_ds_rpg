using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

// El cobro de un voto (ver GameData/Vote.cs). Mismo patrón que el resto del juego: una conexión, una transacción y una guarda en el UPDATE, nunca "leer y después escribir".
public sealed class VoteRepository(IDbConnectionFactory connectionFactory) : IVoteRepository
{
    public async Task<VoteClaimOutcome> ClaimAsync(ulong discordId, DateTime voteCreatedUtc, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La guarda: la fila 'vote' guarda el momento del último voto COBRADO, y solo se pisa con uno más nuevo. El mismo voto pedido dos veces (doble click, dos comandos a la vez)
            // no devuelve fila la segunda vez, y esa queda como "ya cobrado". Si es la primera vez que vota, el INSERT entra directo.
            DateTime? claimed = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
                """
                INSERT INTO cooldowns (discord_id, command_name, last_executed_at)
                VALUES (@DiscordId, @CommandName, @VotedAt)
                ON CONFLICT (discord_id, command_name) DO UPDATE
                    SET last_executed_at = EXCLUDED.last_executed_at
                    WHERE cooldowns.last_executed_at < EXCLUDED.last_executed_at
                RETURNING last_executed_at;
                """,
                new { DiscordId = (long)discordId, CommandName = CooldownCatalog.Vote.CommandName, VotedAt = DateTime.SpecifyKind(voteCreatedUtc, DateTimeKind.Utc) },
                transaction: transaction, cancellationToken: cancellationToken));

            if (claimed is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new VoteClaimOutcome(VoteClaimStatus.AlreadyClaimed);
            }

            int? boxId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "SELECT item_id FROM items WHERE name = @Name AND type = 'Caja';",
                new { Name = VoteRules.RewardBoxName }, transaction: transaction, cancellationToken: cancellationToken));

            if (boxId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new VoteClaimOutcome(VoteClaimStatus.BoxMissing);
            }

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, boxId.Value, VoteRules.RewardBoxes, cancellationToken);

            // El jefe y el raid son UN solo cooldown ('boss'): se borra la fila. Cuenta como "reseteo" solo si todavía corría (un jefe que ya estaba listo no gana nada).
            bool bossReset = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                WITH gone AS (
                    DELETE FROM cooldowns
                    WHERE discord_id = @DiscordId AND command_name = @Boss
                    RETURNING last_executed_at
                )
                SELECT COALESCE(bool_or(last_executed_at > now() - @BossDuration), false) FROM gone;
                """,
                new { DiscordId = (long)discordId, Boss = CooldownCatalog.Boss.CommandName, BossDuration = CooldownCatalog.Boss.Duration },
                transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new VoteClaimOutcome(VoteClaimStatus.Claimed, bossReset);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
