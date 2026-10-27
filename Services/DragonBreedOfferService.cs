using System.Collections.Concurrent;

namespace BotDsRpg.Services;

// Una invitación a criar pendiente: FromId invita a ToId (los dos con dragón del mismo tier).
public sealed record DragonBreedOffer(Guid Id, ulong FromId, ulong ToId, DateTime ExpiresAtUtc);

public interface IDragonBreedOfferService
{
    // Crea la invitación (vence a los 2 minutos). Cada jugador tiene UNA invitación abierta: una nueva reemplaza a la anterior.
    DragonBreedOffer Create(ulong fromId, ulong toId);

    // Mirar sin sacarla (null si no existe o ya venció).
    DragonBreedOffer? Peek(Guid id);

    // La saca de forma atómica: de varios clicks a la vez en «Aceptar» solo UNO la recibe (los demás reciben null), así una cría nunca se hace dos veces. Null también si ya venció.
    DragonBreedOffer? TryTake(Guid id);
}

// En memoria, igual que las propuestas de /trade: una invitación es algo de minutos. Si el bot se reinicia se pierden las pendientes y quien invitó la vuelve a mandar.
public sealed class DragonBreedOfferService : IDragonBreedOfferService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, DragonBreedOffer> _offers = new();

    public DragonBreedOffer Create(ulong fromId, ulong toId)
    {
        Purge();

        foreach (var old in _offers.Values.Where(o => o.FromId == fromId))
        {
            _offers.TryRemove(old.Id, out _);
        }

        var offer = new DragonBreedOffer(Guid.NewGuid(), fromId, toId, DateTime.UtcNow + Lifetime);
        _offers[offer.Id] = offer;
        return offer;
    }

    public DragonBreedOffer? Peek(Guid id) =>
        _offers.TryGetValue(id, out var offer) && offer.ExpiresAtUtc > DateTime.UtcNow ? offer : null;

    public DragonBreedOffer? TryTake(Guid id) =>
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
