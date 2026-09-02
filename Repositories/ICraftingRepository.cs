using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface ICraftingRepository
{
    // Valida y aplica una forja de forma atómica: descuenta oro, descuenta cada ingrediente
    // (elimina la fila de inventory si llega a 0) y suma el resultado. Si falta oro o algún
    // ingrediente no aplica ningún cambio y devuelve Success=false con el motivo en
    // FailureReason. Precondición: el usuario ya debe existir (llamar antes a
    // IUserRepository.GetOrCreateUserAsync).
    Task<CraftOutcome> CraftAsync(
        ulong discordId,
        int goldCost,
        IReadOnlyList<(int ItemId, string ItemName, int Quantity)> ingredients,
        int resultItemId,
        CancellationToken cancellationToken = default);
}
