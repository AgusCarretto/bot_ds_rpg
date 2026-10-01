using System.Collections.Concurrent;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// progressNotifier es opcional a propósito: sin él esto es solo el registro (las pruebas del registro lo arman así); con él, cada
// evento guardado también avanza las misiones y los logros y deja sus avisos en la cola.
public sealed class GameEventService(IGameEventRepository eventRepository, IProgressNotifier? progressNotifier = null) : IGameEvents
{
    // Avisos pendientes por jugador. En memoria a propósito: si el bot se reinicia entre el evento y el aviso, no pasa nada
    // (el progreso ya está guardado en la base; solo se pierde el cartelito).
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<GameNotice>> _notices = new();

    public async Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null)
    {
        try
        {
            long newTotal = await eventRepository.RecordAsync(discordId, kind, zoneId, amount, detail);

            // El evento YA está guardado: lo que falle desde acá (calcular un aviso) no lo deshace, solo se pierde el cartelito.
            if (progressNotifier is not null)
            {
                foreach (var notice in await progressNotifier.OnEventAsync(discordId, kind, amount, newTotal, DateTime.UtcNow))
                {
                    Enqueue(discordId, notice);
                }
            }
        }
        catch (Exception ex)
        {
            // Nunca rompe el comando que lo llamó (ver IGameEvents).
            BotLog.Warn(ex);
        }
    }

    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId)
    {
        if (!_notices.TryGetValue(discordId, out var queue))
        {
            return [];
        }

        var taken = new List<GameNotice>();
        while (queue.TryDequeue(out var notice))
        {
            taken.Add(notice);
        }

        return taken;
    }

    // Para el sistema de misiones y logros: deja un aviso en la cola del jugador.
    internal void Enqueue(ulong discordId, GameNotice notice) =>
        _notices.GetOrAdd(discordId, _ => new ConcurrentQueue<GameNotice>()).Enqueue(notice);
}
