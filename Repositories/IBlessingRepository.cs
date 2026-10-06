namespace BotDsRpg.Repositories;

// La oferta de bendiciones de un Fuego Nuevo: cuál fue (1 = el primero) y las claves sorteadas (BlessingCatalog).
public sealed record BlessingOffer(int FuegoNuevoNo, IReadOnlyList<string> Keys);

public enum ChooseStatus
{
    Ok,
    NoAccount,      // el jugador no existe
    NoOffer,        // no hay oferta de ese Fuego Nuevo
    AlreadyChosen,  // ya eligió (un doble click, o un botón viejo): no se suma otra
    NotOffered,     // esa bendición no estaba entre las 3 sorteadas
    MaxLevel,       // ya la tiene al nivel máximo (no debería ofrecerse, es una guarda)
}

// Key: la que quedó elegida. NewLevel: su nivel ahora. PetFoodGranted/BoxesGranted: lo que dio la Alforja del Fogonero al elegirla (0 con cualquier otra).
public sealed record ChooseOutcome(ChooseStatus Status, string? Key, int NewLevel, int PetFoodGranted, int BoxesGranted);

// Las bendiciones de cada jugador (v0.12.0, GameData/BlessingCatalog.cs). Elegir es UNA transacción con guardas: la oferta se marca elegida y el nivel sube en el mismo COMMIT,
// así un doble click o dos botones a la vez suman UNA sola bendición.
public interface IBlessingRepository
{
    // Clave → nivel (1 a 5) de las bendiciones que tiene el jugador. Vacío si no tiene ninguna.
    Task<IReadOnlyDictionary<string, int>> GetLevelsAsync(ulong discordId, CancellationToken cancellationToken = default);

    // La oferta que todavía no eligió (la más vieja si hubiera más de una), o null.
    Task<BlessingOffer?> GetPendingOfferAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Elige una de las 3 de la oferta de ese Fuego Nuevo: sube su nivel (la primera vez queda en I) y, si es la Alforja, le da lo suyo en la misma transacción.
    Task<ChooseOutcome> ChooseAsync(ulong discordId, int fuegoNuevoNo, string key, CancellationToken cancellationToken = default);
}
