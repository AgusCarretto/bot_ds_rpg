using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

// Invitar amigos (ver GameData/Referrals.cs). Mismo patrón que el resto del juego: una conexión, una transacción y guardas en la base, nunca "leer y después escribir".
public sealed class ReferralRepository(IDbConnectionFactory connectionFactory) : IReferralRepository
{
    public async Task<ReferralOutcome> RegisterAsync(ulong inviteeId, ulong inviterId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Las dos filas bloqueadas, siempre en el mismo orden (por discord_id): dos anotaciones cruzadas no se trancan entre sí.
            var rows = (await connection.QueryAsync<(long DiscordId, int Level, int FuegoNuevo)>(new CommandDefinition(
                "SELECT discord_id, level, fuego_nuevo FROM users WHERE discord_id = ANY(@Ids) ORDER BY discord_id FOR UPDATE;",
                new { Ids = new[] { (long)inviteeId, (long)inviterId } }, transaction: transaction, cancellationToken: cancellationToken))).ToList();

            var invitee = rows.FirstOrDefault(r => r.DiscordId == (long)inviteeId);
            if (invitee.DiscordId == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ReferralOutcome(ReferralStatus.NoAccount);
            }

            if (!rows.Any(r => r.DiscordId == (long)inviterId))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ReferralOutcome(ReferralStatus.InviterNoAccount);
            }

            bool reached = ReferralRules.ReachedGoal(invitee.Level, invitee.FuegoNuevo);

            // La guarda de «una vez por cuenta»: la clave es el invitado, así que la segunda anotación (o dos a la vez) no inserta nada.
            bool inserted = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                """
                INSERT INTO referrals (invitee_id, inviter_id, level_paid_at)
                VALUES (@Invitee, @Inviter, CASE WHEN @Reached THEN now() END)
                ON CONFLICT (invitee_id) DO NOTHING
                RETURNING true;
                """,
                new { Invitee = (long)inviteeId, Inviter = (long)inviterId, Reached = reached }, transaction: transaction, cancellationToken: cancellationToken)) ?? false;

            if (!inserted)
            {
                long previous = await connection.QuerySingleAsync<long>(new CommandDefinition(
                    "SELECT inviter_id FROM referrals WHERE invitee_id = @Invitee;", new { Invitee = (long)inviteeId }, transaction: transaction, cancellationToken: cancellationToken));
                await transaction.RollbackAsync(cancellationToken);
                return new ReferralOutcome(ReferralStatus.AlreadyRegistered, InviterId: (ulong)previous);
            }

            int inviterGold = ReferralRules.InviterStartGold + (reached ? ReferralRules.InviterLevelGold : 0);
            await AddGoldAsync(connection, transaction, inviteeId, ReferralRules.NewPlayerGold, cancellationToken);
            await AddGoldAsync(connection, transaction, inviterId, inviterGold, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new ReferralOutcome(ReferralStatus.Registered, ReferralRules.NewPlayerGold, inviterGold, reached, inviterId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ulong?> PayLevelBonusAsync(ulong inviteeId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Una sola instrucción: marca el hito como pagado SOLO si no lo estaba y el invitado ya llegó (la guarda: dos subidas de nivel a la vez pagan una vez), y le suma el oro al que invitó.
        // Si el que invitó ya no tiene cuenta, el hito queda marcado igual y no se le paga a nadie.
        long? inviter = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            """
            WITH paid AS (
                UPDATE referrals r SET level_paid_at = now()
                FROM users u
                WHERE r.invitee_id = @Invitee AND r.level_paid_at IS NULL
                  AND u.discord_id = r.invitee_id AND (u.level >= @Level OR u.fuego_nuevo > 0)
                RETURNING r.inviter_id
            )
            UPDATE users SET gold = LEAST(users.gold::bigint + @Gold, 2147483647)::int
            FROM paid WHERE users.discord_id = paid.inviter_id
            RETURNING users.discord_id;
            """,
            new { Invitee = (long)inviteeId, Level = ReferralRules.RequiredLevel, Gold = ReferralRules.InviterLevelGold }, cancellationToken: cancellationToken));

        return inviter is null ? null : (ulong)inviter.Value;
    }

    public async Task<ReferralSummary> GetSummaryAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var row = await connection.QuerySingleAsync<(long? InvitedBy, long Invited, long Reached)>(new CommandDefinition(
            """
            SELECT (SELECT inviter_id FROM referrals WHERE invitee_id = @Id),
                   (SELECT COUNT(*) FROM referrals WHERE inviter_id = @Id),
                   (SELECT COUNT(*) FROM referrals WHERE inviter_id = @Id AND level_paid_at IS NOT NULL);
            """,
            new { Id = (long)discordId }, cancellationToken: cancellationToken));

        return new ReferralSummary(row.InvitedBy is null ? null : (ulong)row.InvitedBy.Value, (int)row.Invited, (int)row.Reached);
    }

    private static Task AddGoldAsync(DbConnection connection, DbTransaction transaction, ulong discordId, int amount, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET gold = LEAST(gold::bigint + @Amount, 2147483647)::int WHERE discord_id = @Id;",
            new { Id = (long)discordId, Amount = amount }, transaction: transaction, cancellationToken: cancellationToken));
}
