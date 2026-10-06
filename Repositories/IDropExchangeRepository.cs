using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public enum ExchangeStatus
{
    Ok,
    NoAccount,
    NotExchangeable,  // alguno de los dos no es un drop de monstruo de cacería o de viaje
    SameItem,         // entregar y recibir el mismo ítem no cambia nada
    DifferentZones,   // el trueque es dentro de la MISMA zona
    NotEnough,        // no tiene los 3 (o los 3 por cada cambio) que pide
}

// GiveLeft: cuántos le quedan del que entregó (si no alcanzó, cuántos tiene). GetNow: cuántos tiene ahora del que recibió.
public sealed record ExchangeOutcome(ExchangeStatus Status, int GiveLeft = 0, int GetNow = 0);

// El trueque con el tabernero (GameData/DropExchange.cs). Las reglas de la base (que los dos sean drops y de la misma zona) y el cobro son UNA transacción con guarda:
// dos pedidos a la vez con lo que alcanza para uno solo cambian una sola vez y nunca dejan el inventario en negativo.
public interface IDropExchangeRepository
{
    // Los drops que se pueden cambiar, con su zona, de la zona más fácil a la más difícil.
    Task<IReadOnlyList<ExchangeDrop>> GetExchangeableDropsAsync(CancellationToken cancellationToken = default);

    // Entrega GiveAmount × times del ítem giveItemId y recibe GetAmount × times del getItemId.
    Task<ExchangeOutcome> ExchangeAsync(ulong discordId, int giveItemId, int getItemId, int times, CancellationToken cancellationToken = default);
}
