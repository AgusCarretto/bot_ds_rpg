namespace BotDsRpg.Repositories;

// El dragón de un jugador (v0.30.0, tabla player_dragons). Wait = lo que falta para que pueda volver a criar (cero si ya puede; medido con el reloj de la base).
public sealed record PlayerDragon(string Kind, int Tier, int FailStreak, TimeSpan Wait)
{
    public bool CanBreed => Wait <= TimeSpan.Zero;
}

public enum DragonBuyStatus { Bought, AlreadyHave, NotEnoughGold, NoAccount }

public sealed record DragonBuyResult(DragonBuyStatus Status, PlayerDragon? Dragon = null);

public enum DragonBreedStatus { Bred, NoDragon, TierMismatch, MaxTier, OnCooldown }

// Success: si el intento subió de tier a los dos. Tier: el tier de los dos ANTES del intento. BlockedId: de quién es el dragón que impidió criar (sin dragón, en cooldown). A y B son los dragones DESPUÉS del intento.
public sealed record DragonBreedResult(DragonBreedStatus Status, bool Success = false, int Tier = 0, PlayerDragon? A = null, PlayerDragon? B = null, ulong? BlockedId = null, TimeSpan Wait = default);

public interface IDragonRepository
{
    // Null si no tiene dragón.
    Task<PlayerDragon?> GetAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Compra el dragón en UNA transacción (bloquea la fila del jugador): sin cuenta, con uno ya comprado o sin oro no se toca nada; si no, descuenta el oro y lo crea del tipo dado. De varios clicks a la vez sale uno solo.
    Task<DragonBuyResult> BuyAsync(ulong discordId, int price, string kind, CancellationToken cancellationToken = default);

    // Cría los dragones de dos jugadores en UNA transacción: bloquea las dos filas en orden de discord_id (dos crías cruzadas no se traban), valida (los dos con dragón, mismo tier, tier < 10, los dos descansados), sortea
    // con DragonRules.RollBreed (la piedad es la del que más fallos seguidos lleva) y, sea cual sea el resultado, pone el cooldown a los dos. Si sale bien suben los dos de tier y los fallos vuelven a 0; si no, los dos suman un fallo.
    Task<DragonBreedResult> BreedAsync(ulong firstId, ulong secondId, Random? rng = null, CancellationToken cancellationToken = default);
}
