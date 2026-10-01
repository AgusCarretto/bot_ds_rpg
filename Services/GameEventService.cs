using System.Collections.Concurrent;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

public sealed class GameEventService(IGameEventRepository eventRepository) : IGameEvents
{
    // Avisos pendientes por jugador. En memoria a propósito: si el bot se reinicia entre el evento y el aviso, no pasa nada
    // (el progreso ya está guardado en la base; solo se pierde el cartelito).
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<GameNotice>> _notices = new();

    public async Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null)
    {
        try
        {
            await eventRepository.RecordAsync(discordId, kind, zoneId, amount, detail);
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
