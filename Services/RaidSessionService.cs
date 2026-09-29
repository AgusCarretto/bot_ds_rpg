using System.Collections.Concurrent;

namespace BotDsRpg.Services;

public sealed class RaidSessionService : IRaidSessionService
{
    private readonly ConcurrentDictionary<Guid, RaidSession> _raids = new();
    private readonly ConcurrentDictionary<ulong, Guid> _playerRaid = new();

    public bool IsInAnyRaid(ulong discordId) => _playerRaid.ContainsKey(discordId);

    public bool TryRegisterParticipant(ulong discordId, Guid raidId) => _playerRaid.TryAdd(discordId, raidId);

    public void UnregisterParticipant(ulong discordId) => _playerRaid.TryRemove(discordId, out _);

    public bool TryAdd(RaidSession session) => _raids.TryAdd(session.RaidId, session);

    public RaidSession? Peek(Guid raidId) => _raids.TryGetValue(raidId, out var session) ? session : null;

    public void Remove(Guid raidId)
    {
        if (!_raids.TryRemove(raidId, out var session))
        {
            return;
        }

        session.TimeoutCts?.Cancel();
        session.TimeoutCts?.Dispose();

        // Copia de la lista: UnregisterParticipant no toca Participants, pero por las dudas no
        // queremos enumerar sobre una colección que otro hilo pudiera estar tocando bajo el lock.
        List<ulong> participantIds;
        lock (session.Lock)
        {
            participantIds = session.Participants.Select(p => p.DiscordId).ToList();
        }

        foreach (ulong discordId in participantIds)
        {
            UnregisterParticipant(discordId);
        }
    }
}
