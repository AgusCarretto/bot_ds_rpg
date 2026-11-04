using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public enum AdminGiveStatus { Given, NoAccount }

// Lo que dejó un «admingive»: NewTotal = cómo quedó el oro / el Polvo / la cantidad del ítem; Capped = el total llegó al tope de la columna (int.MaxValue) y se recortó;
// Slot = "arma", "amuleto" o "herramienta" cuando el ítem se PUSO (el equipo y las herramientas no van a la mochila), null si fue a la mochila.
public sealed record AdminGiveResult(AdminGiveStatus Status, long NewTotal = 0, bool Capped = false, string? Slot = null);

// Las operaciones del comando de administrador (v0.34.0, «aa admingive»): dar sin tener. Cada una es UNA transacción o UNA sentencia que bloquea la fila del jugador, y ninguna crea cuentas (quien no tiene cuenta
// devuelve NoAccount): este comando nunca inventa jugadores.
public interface IAdminRepository
{
    // Suma oro (columna users.gold). Recorta en int.MaxValue en lugar de fallar por desborde.
    Task<AdminGiveResult> GiveGoldAsync(ulong discordId, int amount, CancellationToken cancellationToken = default);

    // Suma Polvo (users.dust). Igual que el oro.
    Task<AdminGiveResult> GiveDustAsync(ulong discordId, int amount, CancellationToken cancellationToken = default);

    // Da un ítem del catálogo: un arma o un amuleto se PONE (pisa lo que llevaba, sin encantar, como forjarlo), una herramienta se PONE en su tipo (hacha o pico) y todo lo demás
    // (cajas, materiales, comida, huevos, comida de mascotas y de dragón...) se suma a la mochila. Para el equipo y las herramientas la cantidad no cuenta.
    Task<AdminGiveResult> GiveItemAsync(ulong discordId, Item item, int quantity, CancellationToken cancellationToken = default);

    // Hasta `limit` nombres de ítems que contienen lo escrito (sin importar mayúsculas), para decir «¿quisiste decir…?» cuando el nombre no existe.
    Task<IReadOnlyList<string>> SearchItemNamesAsync(string typed, int limit, CancellationToken cancellationToken = default);

    // «aa adminset dragon tier» (v0.48.1): fija el TIER del dragón (1 a 10, sin pasar por la cría). El nivel de entrenamiento se recorta al tope del tier nuevo (tier × 10) y los DOS contadores de piedad vuelven
    // a 0 (eran los fallos seguidos con las chances del tier viejo). No toca el tipo, el nombre ni el descanso de 12 horas. NoDragon si el jugador no tiene dragón: nunca lo crea.
    Task<AdminDragonSetResult> SetDragonTierAsync(ulong discordId, int tier, CancellationToken cancellationToken = default);

    // «aa adminset dragon level»: fija el nivel de entrenamiento (0 hasta el tope de su tier actual). Por encima del tope devuelve AboveCap sin cambiar nada (no recorta en silencio).
    Task<AdminDragonSetResult> SetDragonLevelAsync(ulong discordId, int level, CancellationToken cancellationToken = default);
}

public enum AdminDragonSetStatus { Set, NoDragon, AboveCap }

// Cómo quedó el dragón: Before* es lo que tenía y Tier/Level lo que tiene ahora (en AboveCap, lo que sigue teniendo); LevelClamped = al cambiar el tier se recortó el nivel; LevelCap = el tope de su tier actual.
public sealed record AdminDragonSetResult(
    AdminDragonSetStatus Status, int BeforeTier = 0, int BeforeLevel = 0, int Tier = 0, int Level = 0, bool LevelClamped = false, int LevelCap = 0);
