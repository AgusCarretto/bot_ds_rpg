using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Una expedición en curso (tabla pet_expeditions, una fila por jugador). Remaining sale del reloj de la BASE (el mismo que escribió ends_at), no del bot: 0 = ya volvió.
public sealed record ActiveExpedition(PetSpecies Species, int Hours, DateTime StartedUtc, DateTime EndsUtc, TimeSpan Remaining)
{
    public bool IsReady => Remaining <= TimeSpan.Zero;
}

public enum ExpeditionStartStatus
{
    Ok,
    NoAccount,        // el jugador no existe
    BadDuration,      // la duración no es una de PetExpeditions.Options
    AlreadyOut,       // ya tiene una mascota de expedición (de a una por vez)
    NotOwned,         // no tiene esa mascota
    NotMaxLevel,      // la mascota no está en el nivel 10
    NoFood,           // no tiene las Comidas para Mascotas que cuesta esa duración
}

// Expedition: la que salió (Ok) o la que ya estaba afuera (AlreadyOut). Species: la mascota de los rechazos que dependen de ella. FoodHave: las comidas que tiene (NoFood) o que le quedan (Ok).
public sealed record ExpeditionStartOutcome(ExpeditionStartStatus Status, ActiveExpedition? Expedition, PetSpecies? Species, int FoodHave);

public enum ExpeditionClaimStatus
{
    Ok,
    NoAccount,      // el jugador no existe
    NoExpedition,   // no hay ninguna expedición (o la cobró otro clic un instante antes)
    NotReady,       // todavía no volvió: Remaining dice cuánto falta
}

// Expedition: la que se cobró (Ok) o la que sigue afuera (NotReady). Receipt: el oro y la EXP pagados (con la subida de nivel). Find: el hallazgo si salió. Remaining: lo que falta (NotReady).
public sealed record ExpeditionClaimOutcome(
    ExpeditionClaimStatus Status, ActiveExpedition? Expedition, RewardReceipt? Receipt, ExpeditionFindResult? Find, TimeSpan Remaining);

// Las expediciones de las mascotas (v0.39.0, GameData/PetExpeditions.cs). Salir y volver son transacciones con guardas: la fila del jugador se toma con FOR UPDATE, la comida se gasta con un UPDATE guardado,
// la expedición se cobra con un DELETE ... WHERE ends_at <= now() (dos clics a la vez cobran UNA vez) y el premio se paga en la MISMA transacción. El premio NO depende de la zona del jugador.
public interface IPetExpeditionRepository
{
    // La expedición en curso del jugador, o null. Nunca crea nada.
    Task<ActiveExpedition?> GetAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Manda a la mascota (nivel 10) de expedición por esas horas: gasta las comidas de esa duración (PetExpeditions.Options) y arranca el reloj. De a una por jugador.
    Task<ExpeditionStartOutcome> StartAsync(ulong discordId, int speciesId, int hours, CancellationToken cancellationToken = default);

    // Cobra la expedición si ya volvió: paga el oro (un valor del rango de esa duración) y la EXP (% de su nivel) y el hallazgo de la especie si salió. rng se inyecta para probarlo.
    Task<ExpeditionClaimOutcome> ClaimAsync(ulong discordId, Random? rng = null, CancellationToken cancellationToken = default);
}
