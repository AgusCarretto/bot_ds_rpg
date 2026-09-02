using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class CasinoRepository(IDbConnectionFactory connectionFactory) : ICasinoRepository
{
    public async Task<User?> PlaceBetAsync(ulong discordId, int bet, int payout, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Update guardado: si no le alcanza el oro para apostar, el WHERE bloquea la
            // actualización y no devuelve fila (validación atómica contra la base, no una
            // lectura previa separada que podría desactualizarse).
            string deductSql = $"""
                UPDATE users
                SET gold = gold - @Bet
                WHERE discord_id = @DiscordId AND gold >= @Bet
                RETURNING {UserSql.SelectColumns};
                """;

            var afterBet = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                deductSql,
                new { DiscordId = (long)discordId, Bet = bet },
                transaction: transaction,
                cancellationToken: cancellationToken));

            if (afterBet is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            User final = afterBet;

            if (payout > 0)
            {
                string creditSql = $"""
                    UPDATE users
                    SET gold = gold + @Payout
                    WHERE discord_id = @DiscordId
                    RETURNING {UserSql.SelectColumns};
                    """;

                final = await connection.QuerySingleAsync<User>(new CommandDefinition(
                    creditSql,
                    new { DiscordId = (long)discordId, Payout = payout },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return final;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
