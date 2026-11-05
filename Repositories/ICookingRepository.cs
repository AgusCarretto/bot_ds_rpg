using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public enum CookStatus
{
    Cooked,     // se cocinó y se comió: se descontó todo y el contador subió
    Missing,    // falta algún ingrediente o combustible: no cambió NADA
    NoAccount,  // el jugador no existe (cocinar nunca le crea una cuenta)
}

// Lo que falta de un ítem: lo que tiene y lo que pide.
public sealed record CookShortage(string ItemName, int Have, int Need);

// NewCount: cuántos lleva de ESE plato en esta vuelta, ya con lo recién cocinado (0 si no se cocinó).
public sealed record CookOutcome(CookStatus Status, int NewCount, IReadOnlyList<CookShortage> Missing);

public interface ICookingRepository
{
    // Clave del plato → cuántos lleva cocinados en esta vuelta (vacío sin ninguno).
    Task<IReadOnlyDictionary<string, int>> GetCookedAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Cocina `times` veces el plato y se lo come: en UNA transacción bloquea la fila del jugador, descuenta cada ingrediente y el combustible con el UPDATE protegido de siempre (si algo no alcanza se deshace TODO y dice
    // qué falta) y suma al contador. Varios pedidos a la vez del mismo jugador se ordenan por el bloqueo y nada queda en negativo.
    Task<CookOutcome> CookAsync(ulong discordId, Dish dish, int times, CancellationToken cancellationToken = default);
}
