namespace BotDsRpg.Repositories;

public enum VoteClaimStatus
{
    Claimed,        // se cobró: la caja ya está en el inventario
    AlreadyClaimed, // ese voto (o uno más nuevo) ya se cobró antes
    BoxMissing,     // la caja del premio no existe en el catálogo: se revierte todo y no se marca el voto
}

// bossReset: el cooldown del jefe estaba corriendo y se borró (false = el jefe ya estaba listo, no había nada que resetear).
public sealed record VoteClaimOutcome(VoteClaimStatus Status, bool BossReset = false);

public interface IVoteRepository
{
    // Cobra un voto de top.gg en UNA transacción: marca el voto como cobrado (solo si es más nuevo que el último), suma la caja al inventario y borra el cooldown del jefe/raid.
    // Cualquier falla revierte todo, así que nunca queda un voto marcado sin su premio. voteCreatedUtc es el created_at que dio top.gg.
    Task<VoteClaimOutcome> ClaimAsync(ulong discordId, DateTime voteCreatedUtc, CancellationToken cancellationToken = default);
}
