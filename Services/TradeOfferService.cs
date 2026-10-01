using System.Collections.Concurrent;
using BotDsRpg.Models;

namespace BotDsRpg.Services;

// Una propuesta de cambio pendiente: FromId le ofrece 1 de GiveItem a ToId a cambio de 1 de GetItem.
public sealed record TradeOffer(
    Guid Id, ulong FromId, ulong ToId, int GiveItemId, string GiveName, int GetItemId, string GetName, DateTime ExpiresAtUtc);

public interface ITradeOfferService
{
    // Crea la propuesta (vence a los pocos minutos). Cada jugador tiene UNA propuesta abierta: una nueva reemplaza a la anterior.
    TradeOffer Create(ulong fromId, ulong toId, Item give, Item get);

    // Mirar sin sacarla (null si no existe o ya venció).
    TradeOffer? Peek(Guid id);

    // La saca de forma atómica: de varios clicks simultáneos en "Aceptar" solo UNO la recibe (los demás reciben null), así un cambio
    // nunca se ejecuta dos veces. Null también si ya venció.
    TradeOffer? TryTake(Guid id);
}

// En memoria, igual que los combates y los raids: una propuesta es algo de minutos, no vale la pena guardarla. Si el bot se reinicia
// se pierden las pendientes y quien propuso las vuelve a mandar.
public sealed class TradeOfferService : ITradeOfferService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, TradeOffer> _offers = new();

    public TradeOffer Create(ulong fromId, ulong toId, Item give, Item get)
    {
        Purge();

        foreach (var old in _offers.Values.Where(o => o.FromId == fromId))
        {
            _offers.TryRemove(old.Id, out _);
        }

        var offer = new TradeOffer(Guid.NewGuid(), fromId, toId, give.ItemId, give.Name, get.ItemId, get.Name, DateTime.UtcNow + Lifetime);
        _offers[offer.Id] = offer;
        return offer;
    }

    public TradeOffer? Peek(Guid id) =>
        _offers.TryGetValue(id, out var offer) && offer.ExpiresAtUtc > DateTime.UtcNow ? offer : null;

    public TradeOffer? TryTake(Guid id) =>
        _offers.TryRemove(id, out var offer) && offer.ExpiresAtUtc > DateTime.UtcNow ? offer : null;

    private void Purge()
    {
        var now = DateTime.UtcNow;
        foreach (var expired in _offers.Values.Where(o => o.ExpiresAtUtc <= now))
        {
            _offers.TryRemove(expired.Id, out _);
        }
    }
}
