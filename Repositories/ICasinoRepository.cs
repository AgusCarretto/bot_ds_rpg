using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface ICasinoRepository
{
    // Descuenta la apuesta y, si corresponde, acredita el premio, todo en una transacción.
    // Devuelve null si no le alcanza el oro para apostar (no aplica ningún cambio en ese caso).
    // Precondición: el usuario ya debe existir (llamar antes a IUserRepository.GetOrCreateUserAsync).
    Task<User?> PlaceBetAsync(ulong discordId, int bet, int payout, CancellationToken cancellationToken = default);
}
