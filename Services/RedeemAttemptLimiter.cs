using System.Collections.Concurrent;
using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// El freno de los códigos canjeables (v0.37.0): quien prueba códigos que NO existen se frena solo, para que nadie los adivine a fuerza bruta (RedeemCodeRules: 5 fallos en 10 minutos = 10 minutos sin
// poder probar). Solo cuentan los códigos que no existen; un código que existe pero ya canjeaste, venció o se agotó no suma. Vive en memoria: reiniciar el bot lo limpia (alcanza para frenar
// a alguien que está escribiendo; no es una defensa contra un ataque distribuido, que además tendría que adivinar códigos de hasta 32 caracteres).
public interface IRedeemAttemptLimiter
{
    // Cuánto falta para que ese jugador pueda volver a probar; null si puede probar ya.
    TimeSpan? LockedFor(ulong discordId);

    // Anota un fallo (un código que no existe); al quinto en la ventana queda bloqueado.
    void RegisterFailure(ulong discordId);

    // Un canje que salió bien borra los fallos.
    void Clear(ulong discordId);
}

public sealed class RedeemAttemptLimiter(Func<DateTime>? clock = null) : IRedeemAttemptLimiter
{
    private sealed class State
    {
        public readonly object Gate = new();
        public readonly List<DateTime> Failures = [];
        public DateTime? LockedUntil;
    }

    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<ulong, State> _states = new();

    public TimeSpan? LockedFor(ulong discordId)
    {
        if (!_states.TryGetValue(discordId, out var state))
        {
            return null;
        }

        lock (state.Gate)
        {
            if (state.LockedUntil is { } until && until > _now())
            {
                return until - _now();
            }

            return null;
        }
    }

    public void RegisterFailure(ulong discordId)
    {
        var state = _states.GetOrAdd(discordId, _ => new State());
        lock (state.Gate)
        {
            var now = _now();
            state.Failures.RemoveAll(t => now - t > RedeemCodeRules.AttemptWindow);
            state.Failures.Add(now);
            if (state.Failures.Count >= RedeemCodeRules.FailedAttemptsLimit)
            {
                state.LockedUntil = now + RedeemCodeRules.LockDuration;
                state.Failures.Clear();
            }
        }
    }

    public void Clear(ulong discordId) => _states.TryRemove(discordId, out _);
}
