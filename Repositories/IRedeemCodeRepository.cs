using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public enum RedeemStatus { Redeemed, NoAccount, UnknownCode, Inactive, Expired, Exhausted, AlreadyRedeemed, NoRewards }

// Un premio ya resuelto (para mostrarlo): el tipo, la cantidad y, si es un ítem, su nombre y su emoji.
public sealed record RedeemedReward(RedeemRewardKind Kind, int Amount, string? ItemName, string? ItemEmoji);

// Lo que pasó al canjear: el estado y, si salió bien, los premios pagados y cuántos niveles subió con la EXP (NewLevel = el nivel con el que quedó). Capped = algún total llegó al tope de la columna y se recortó.
public sealed record RedeemResult(RedeemStatus Status, IReadOnlyList<RedeemedReward> Rewards, int LevelsGained = 0, int NewLevel = 0, bool Capped = false);

public enum CreateCodeStatus { Created, Duplicate, ItemMissing }

public sealed record CreateCodeResult(CreateCodeStatus Status, string? Detail = null);

// Un código con todo lo que se ve desde el panel del administrador.
public sealed record RedeemCodeInfo(
    string Code, int? MaxUses, int Uses, DateTime? ExpiresAt, bool Active, DateTime CreatedAt, ulong CreatedBy, IReadOnlyList<RedeemedReward> Rewards);

// Los códigos canjeables (v0.37.0, `aa code` / `/code` y el panel `aa admincode`). Canjear es UNA transacción (ver RedeemCodeRepository): sin carreras (el tope de personas y «una vez por persona» se garantizan
// en la base) y sin pagar a medias.
public interface IRedeemCodeRepository
{
    // Crea el código con sus premios en una transacción. Los nombres de los ítems tienen que ser los del catálogo (el módulo los resuelve antes).
    Task<CreateCodeResult> CreateAsync(RedeemCodeSpec spec, ulong createdBy, CancellationToken cancellationToken = default);

    // Canjea el código (ya normalizado) para ese jugador. Nunca crea cuentas.
    Task<RedeemResult> RedeemAsync(ulong discordId, string code, CancellationToken cancellationToken = default);

    Task<RedeemCodeInfo?> GetAsync(string code, CancellationToken cancellationToken = default);

    // Los más nuevos primero.
    Task<IReadOnlyList<RedeemCodeInfo>> ListAsync(int limit, CancellationToken cancellationToken = default);

    // Prende o apaga un código. False si no existe.
    Task<bool> SetActiveAsync(string code, bool active, CancellationToken cancellationToken = default);
}
