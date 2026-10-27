namespace BotDsRpg.Repositories;

// El dragón de un jugador (v0.30.0, tabla player_dragons). Name: el nombre que le puso su dueño (null = sin nombre). Level: su nivel de entrenamiento (0 hasta tier × 10). Wait = lo que falta para que pueda volver a criar (cero si ya puede; medido con el reloj de la base).
public sealed record PlayerDragon(string Kind, int Tier, int FailStreak, TimeSpan Wait, int Level = 0, string? Name = null)
{
    public bool CanBreed => Wait <= TimeSpan.Zero;
}

public enum DragonBuyStatus { Bought, AlreadyHave, NotEnoughGold, NoAccount }

public sealed record DragonBuyResult(DragonBuyStatus Status, PlayerDragon? Dragon = null);

public enum DragonBreedStatus { Bred, NoDragon, TierMismatch, MaxTier, OnCooldown }

// Success: si subió de tier el dragón A (el de quien invita; el único si se crió solo). SuccessB: si subió el B (solo en pareja): cada dragón tira su propia suerte, puede subir uno y el otro no. Tier: el tier de los dos ANTES del intento.
// BlockedId: de quién es el dragón que impidió criar (sin dragón, en cooldown). A y B son los dragones DESPUÉS del intento.
// Lo que pasó con el TIPO de un dragón después de un intento de criar (v0.32.0, en cada intento): From es el tipo que tenía; FoodKept = su dueño tenía Comida de Dragón y se gastó una, así que el tipo NO cambió. Si no, el tipo nuevo (que puede ser el mismo) está en el dragón de A o B.
public sealed record DragonTypeChange(string From, bool FoodKept);

public sealed record DragonBreedResult(DragonBreedStatus Status, bool Success = false, int Tier = 0, PlayerDragon? A = null, PlayerDragon? B = null, ulong? BlockedId = null, TimeSpan Wait = default, bool SuccessB = false, DragonTypeChange? TypeA = null, DragonTypeChange? TypeB = null);

public enum DragonTrainStatus { Trained, NoDragon, AtCap, NotEnoughGold }

// LevelsGained / GoldSpent: lo que se entrenó en este pedido. Dragon: cómo quedó. NextCost: lo que cuesta el próximo nivel (0 si ya está en el tope del tier). GoldLeft: el oro del jugador después (o el que tenía, si no alcanzó).
public sealed record DragonTrainResult(DragonTrainStatus Status, int LevelsGained = 0, long GoldSpent = 0, PlayerDragon? Dragon = null, int NextCost = 0, int GoldLeft = 0);

public enum DragonRenameStatus { Renamed, NoDragon, SameName, NotEnoughGold }

public sealed record DragonRenameResult(DragonRenameStatus Status, PlayerDragon? Dragon = null, int GoldLeft = 0);

public interface IDragonRepository
{
    // Null si no tiene dragón.
    Task<PlayerDragon?> GetAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Compra el dragón en UNA transacción (bloquea la fila del jugador): sin cuenta, con uno ya comprado o sin oro no se toca nada; si no, descuenta el oro y lo crea del tipo dado. De varios clicks a la vez sale uno solo.
    Task<DragonBuyResult> BuyAsync(ulong discordId, int price, string kind, CancellationToken cancellationToken = default);

    // Cría los dragones de dos jugadores en UNA transacción: bloquea las dos filas en orden de discord_id (dos crías cruzadas no se traban), valida (los dos con dragón, mismo tier, tier < 10, los dos descansados), sortea
    // UNA tirada por dragón con DragonRules.RollBreed, cada uno con SU chance y SU contador de fallos seguidos (puede subir uno y el otro no), y, sea cual sea el resultado, pone el cooldown a los dos. El que sube pasa de tier y reinicia sus fallos; el que no, suma un fallo.
    Task<DragonBreedResult> BreedAsync(ulong firstId, ulong secondId, Random? rng = null, CancellationToken cancellationToken = default);

    // Cría el dragón SOLO (v0.31.0), sin pareja, en UNA transacción: valida (con dragón, tier < 10, descansado), sortea con UN TERCIO de la chance del tier (DragonRules.SoloChanceFactor; la piedad cuenta con el triple de fallos) y, salga como salga,
    // pone el descanso. Sube de tier solo ese dragón; si no, suma un fallo. El resultado trae el dragón en A (B va vacío).
    Task<DragonBreedResult> BreedSoloAsync(ulong discordId, Random? rng = null, CancellationToken cancellationToken = default);

    // Entrena el dragón hasta `times` niveles en UNA transacción (bloquea la fila del jugador y la del dragón): cada nivel cuesta DragonRules.TrainCost(nivel actual), siempre más que el anterior, y se entrena hasta donde alcance el oro
    // y el tope del tier (tier × 10). Si no se puede ni uno, no se toca nada (AtCap / NotEnoughGold). El cobro es un UPDATE guardado: nunca deja el oro en negativo.
    Task<DragonTrainResult> TrainAsync(ulong discordId, int times, CancellationToken cancellationToken = default);

    // Le pone nombre al dragón (o se lo cambia) por `price` de oro, en UNA transacción: sin dragón o con el mismo nombre que ya tiene no se toca nada ni se cobra; sin oro tampoco. El nombre ya viene limpio (DragonRules.CleanName).
    Task<DragonRenameResult> RenameAsync(ulong discordId, string name, int price, CancellationToken cancellationToken = default);

    // Cuántas Comidas de Dragón tiene en la mochila (0 si no tiene o si el ítem todavía no está en la base).
    Task<int> CountFoodAsync(ulong discordId, CancellationToken cancellationToken = default);
}
