using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public enum BankStatus
{
    Ok,
    NoPlayer,           // el jugador no existe
    AlreadyHasAccount,  // abrir: ya tiene la cuenta
    NoAccount,          // depositar o retirar sin haber comprado la cuenta
    NotEnoughGold,      // no alcanza el oro de la billetera (para abrir la cuenta o para depositar)
    NotEnoughBank,      // retirar más de lo que hay en el banco
}

// User: el jugador DESPUÉS de la operación (solo cuando salió bien, Status == Ok).
public sealed record BankOutcome(BankStatus Status, User? User);

// El banco (/bank, GameData/BankRules.cs): comprar la cuenta y mover oro entre la billetera y el banco. Cada operación es UN UPDATE guardado (la
// precondición va en el WHERE), así que dos clicks simultáneos nunca pasan de lo que hay: ni se deposita de más ni se abre la cuenta dos veces.
public interface IBankRepository
{
    Task<BankOutcome> OpenAccountAsync(ulong discordId, int price, CancellationToken cancellationToken = default);
    Task<BankOutcome> DepositAsync(ulong discordId, int amount, CancellationToken cancellationToken = default);
    Task<BankOutcome> WithdrawAsync(ulong discordId, int amount, CancellationToken cancellationToken = default);
}
