using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public enum HatchStatus
{
    Ok,
    NoAccount,     // el jugador no existe
    NotAnEgg,      // el ítem no es el huevo de ninguna especie
    NoEgg,         // no tiene ese huevo en la mochila (o se lo gastó otro click un instante antes)
    AlreadyOwned,  // ya tiene esa mascota: el huevo NO se gasta
}

// Species: la que nació (solo con Ok) o, con AlreadyOwned, la que ya tenía.
public sealed record HatchOutcome(HatchStatus Status, PetSpecies? Species);

public enum FeedStatus
{
    Ok,
    NoAccount,   // el jugador no existe
    NotOwned,    // no tiene esa mascota
    MaxLevel,    // ya está en el nivel máximo de su tier (10, 20 o 30): no se gasta comida, hay que evolucionarla
    OnCooldown,  // se la alimentó hace menos de una hora (PetRules.FeedCooldown)
    NoFood,      // no tiene las "Comida para Mascotas" que gasta esa alimentada
}

// Pet: la mascota DESPUÉS de comer (Ok) o como estaba (el resto de los rechazos con mascota). LevelBefore: su nivel antes de comer, para avisar "¡subió de nivel!".
// FoodCost (v0.40.0): las comidas que gasta esa alimentada (1 hasta el nivel 10, 3 hasta el 20 y 6 hasta el 30: PetRules.FoodPerFeed). FoodLeft: comidas que le quedan en la mochila (solo con Ok). Remaining: lo que falta para poder alimentarla otra vez (Ok: el cooldown entero; OnCooldown: lo que queda).
public sealed record FeedOutcome(FeedStatus Status, OwnedPet? Pet, int LevelBefore, int FoodLeft, TimeSpan Remaining, int FoodCost = 1);

public enum EvolveStatus
{
    Ok,
    NoAccount,        // el jugador no existe
    NotOwned,         // no tiene esa mascota
    MaxTier,          // ya está en el tier III
    NotAtCap,         // todavía no está en el nivel máximo de su tier (Have = su nivel, Need = el tope)
    NotEnoughHours,   // le faltan horas de expedición (Have / Need)
    NotEnoughEggs,    // le faltan huevos sobrantes de su especie (Have / Need)
}

// Pet: la mascota DESPUÉS de evolucionar (Ok) o como estaba. Have / Need: lo que tiene y lo que pide la condición que falló (nivel, horas o huevos).
public sealed record EvolveOutcome(EvolveStatus Status, OwnedPet? Pet, int Have, int Need);

// Las mascotas (v0.10.0, GameData/PetRules.cs). Abrir un huevo y alimentar son transacciones con guardas (nada de "leo y después escribo"): el huevo se gasta y
// la mascota nace en el mismo COMMIT, y la comida se gasta y suma el punto en el mismo COMMIT, así que un doble click o dos comandos a la vez nunca duplican ni pierden nada.
public interface IPetRepository
{
    // Las 5 especies, por zona (el catálogo es casi estático: el que llama puede guardarlo).
    Task<IReadOnlyList<PetSpecies>> GetSpeciesAsync(CancellationToken cancellationToken = default);

    // Las mascotas que tiene el jugador, de la zona más baja a la más alta.
    Task<IReadOnlyList<OwnedPet>> GetOwnedAsync(ulong discordId, CancellationToken cancellationToken = default);

    // La suma de todos sus bonus. NUNCA tira: es un extra de las peleas y no puede romper una (ante un error loguea y devuelve PetBonuses.None).
    Task<PetBonuses> GetBonusesAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Abre el huevo (un ítem de la mochila): lo gasta y le da al jugador su mascota, nivel 1.
    Task<HatchOutcome> HatchAsync(ulong discordId, int eggItemId, CancellationToken cancellationToken = default);

    // Le da UNA "Comida para Mascotas" a la mascota de esa especie: gasta la comida, suma un punto y arranca el cooldown de una hora de ESA mascota
    // (el reloj lo manda la base de datos, no el del bot).
    // cooldown: la espera de ESE jugador (la bendición Buen Pienso la acorta, PlayerBonuses.PetFeedCooldown); null = la hora de siempre (PetRules.FeedCooldown).
    Task<FeedOutcome> FeedAsync(ulong discordId, int speciesId, TimeSpan? cooldown = null, CancellationToken cancellationToken = default);

    // Evoluciona a la mascota (v0.40.0): sube un tier (el tope de nivel pasa de 10 a 20 y a 30). Pide estar en el nivel máximo de su tier, las horas de expedición acumuladas de ESA mascota
    // (PetRules.EvolveHours) y los huevos sobrantes de su especie (PetRules.EvolveEggs), que se GASTAN. Todo en una transacción con las guardas de siempre: dos clics evolucionan una vez.
    Task<EvolveOutcome> EvolveAsync(ulong discordId, int speciesId, CancellationToken cancellationToken = default);
}
