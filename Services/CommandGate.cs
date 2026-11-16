using System.Collections.Concurrent;

namespace BotDsRpg.Services;

// Un comando a la vez por jugador (v0.58.2, el dueño 2026-11-16: «bloquear todo cuando hay otro comando en proceso, así no se confunde el bot»). Mientras el bot procesa un comando o un botón
// de un jugador, lo siguiente que mande ESE jugador (comando de barra, botón, menú o «aa …») recibe «esperá un toque» y no se ejecuta: así dos clics rápidos o un comando tirado en medio de otro
// no se pisan. Es por jugador (los demás no esperan a nadie) y en memoria. Se suelta cuando el comando termina (Program.cs: InteractionExecuted para barra y botones, el final de
// ExecuteAsync para el texto); por si algo no avisa que terminó, un candado vence solo a los MaxHold y nadie queda trabado.
public sealed class CommandGate
{
    public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<ulong, DateTime> _busy = new();
    private readonly Func<DateTime> _now;

    public CommandGate(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    // true = el jugador estaba libre y ahora queda ocupado; false = ya tiene un comando en proceso (no hay que ejecutar este).
    public bool TryEnter(ulong discordId)
    {
        DateTime now = _now();
        while (true)
        {
            if (_busy.TryAdd(discordId, now))
            {
                return true;
            }

            if (!_busy.TryGetValue(discordId, out DateTime since))
            {
                continue; // se soltó entre medio: probar de nuevo
            }

            if (now - since < MaxHold)
            {
                return false;
            }

            // Venció (algo no avisó que terminó): se toma, solo si nadie lo tomó antes.
            if (_busy.TryUpdate(discordId, now, since))
            {
                return true;
            }
        }
    }

    public void Exit(ulong discordId) => _busy.TryRemove(discordId, out _);

    public bool IsBusy(ulong discordId) => _busy.TryGetValue(discordId, out DateTime since) && _now() - since < MaxHold;
}
