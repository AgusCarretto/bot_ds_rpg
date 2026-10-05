using System.Data;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class BankRepository(IDbConnectionFactory connectionFactory) : IBankRepository
{
    public Task<BankOutcome> OpenAccountAsync(ulong discordId, int price, CancellationToken cancellationToken = default) =>
        RunAsync(
            $"""
            UPDATE users SET gold = gold - @Amount, has_bank = true
            WHERE discord_id = @DiscordId AND has_bank = false AND gold >= @Amount
            RETURNING {UserSql.SelectColumns};
            """,
            discordId, price,
            // El UPDATE no devolvió fila: o ya la tenía, o no le alcanza el oro.
            player => player.HasBank ? BankStatus.AlreadyHasAccount : BankStatus.NotEnoughGold,
            cancellationToken);

    public Task<BankOutcome> DepositAsync(ulong discordId, int amount, CancellationToken cancellationToken = default) =>
        RunAsync(
            $"""
            UPDATE users SET gold = gold - @Amount, bank_gold = bank_gold + @Amount
            WHERE discord_id = @DiscordId AND has_bank AND gold >= @Amount
            RETURNING {UserSql.SelectColumns};
            """,
            discordId, amount,
            player => player.HasBank ? BankStatus.NotEnoughGold : BankStatus.NoAccount,
            cancellationToken);

    public Task<BankOutcome> WithdrawAsync(ulong discordId, int amount, CancellationToken cancellationToken = default) =>
        RunAsync(
            $"""
            UPDATE users SET gold = gold + @Amount, bank_gold = bank_gold - @Amount
            WHERE discord_id = @DiscordId AND has_bank AND bank_gold >= @Amount
            RETURNING {UserSql.SelectColumns};
            """,
            discordId, amount,
            player => player.HasBank ? BankStatus.NotEnoughBank : BankStatus.NoAccount,
            cancellationToken);

    // Corre el UPDATE guardado. Si no devuelve fila, mira al jugador para decir POR QUÉ no se pudo (después del hecho: el motivo es solo para el mensaje,
    // la decisión de no cobrar ya la tomó el WHERE).
    private async Task<BankOutcome> RunAsync(
        string guardedUpdateSql, ulong discordId, int amount, Func<User, BankStatus> reasonWhenRejected, CancellationToken cancellationToken)
    {
        using IDbConnection connection = connectionFactory.CreateConnection();

        var updated = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            guardedUpdateSql, new { DiscordId = (long)discordId, Amount = amount }, cancellationToken: cancellationToken));
        if (updated is not null)
        {
            return new BankOutcome(BankStatus.Ok, updated);
        }

        var current = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            $"SELECT {UserSql.SelectColumns} FROM users WHERE discord_id = @DiscordId;",
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        return current is null ? new BankOutcome(BankStatus.NoPlayer, null) : new BankOutcome(reasonWhenRejected(current), null);
    }
}
