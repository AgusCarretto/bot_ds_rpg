namespace BotDsRpg.Repositories;

public enum ReferralStatus { Registered, NoAccount, InviterNoAccount, AlreadyRegistered }

// Registered: InviterGold = lo que cobró el que invitó en este momento (20k, o 100k si el invitado ya había llegado al nivel 5 o a un Fuego Nuevo). AlreadyRegistered: InviterId = a quién ya había anotado.
public sealed record ReferralOutcome(ReferralStatus Status, int NewPlayerGold = 0, int InviterGold = 0, bool LevelBonusPaid = false, ulong? InviterId = null);

// Lo que muestra `aa invitedby` sin nadie: quién te invitó, a cuántos invitaste y cuántos de ellos ya llegaron al nivel 5.
public sealed record ReferralSummary(ulong? InvitedBy, int Invited, int ReachedLevel);

// Invitar amigos (v0.43.0, GameData/Referrals.cs). Cada pago es UNA instrucción o UNA transacción con guarda: una cuenta anota a quien la invitó una sola vez y el premio del nivel 5 se paga una sola vez.
public interface IReferralRepository
{
    // Anota quién invitó al jugador y paga los premios en la misma transacción. Nunca crea cuentas.
    Task<ReferralOutcome> RegisterAsync(ulong inviteeId, ulong inviterId, CancellationToken cancellationToken = default);

    // Si el jugador fue invitado, ya está en el nivel 5 (o hizo un Fuego Nuevo) y el premio de ese hito no se pagó, se lo paga al que lo invitó y devuelve su ID. Si no, null.
    Task<ulong?> PayLevelBonusAsync(ulong inviteeId, CancellationToken cancellationToken = default);

    Task<ReferralSummary> GetSummaryAsync(ulong discordId, CancellationToken cancellationToken = default);
}
