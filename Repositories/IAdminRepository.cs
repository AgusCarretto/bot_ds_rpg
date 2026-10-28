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
}
