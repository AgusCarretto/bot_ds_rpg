using System.Collections.Concurrent;

namespace BotDsRpg.Services;

public sealed class RaidSessionService : IRaidSessionService
{
    private readonly ConcurrentDictionary<Guid, RaidSession> _raids = new();
    private readonly ConcurrentDictionary<ulong, Guid> _playerRaid = new();

    public bool IsInAnyRaid(ulong discordId) => _playerRaid.ContainsKey(discordId);

    public Guid? RaidIdOf(ulong discordId) => _playerRaid.TryGetValue(discordId, out var raidId) ? raidId : null;

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

        // Se libera por el ÍNDICE (jugador -> raid), no por session.Participants: si algún día un
        // jugador queda registrado sin figurar en la lista (le pasaba a quien arrancaba el lobby),
        // recorrer la lista lo dejaría "en un raid" para siempre — sin poder cazar ni viajar hasta
        // reiniciar el bot. Remove(KeyValuePair) solo saca la entrada si TODAVÍA apunta a este raid,
        // así que no pisa el registro de alguien que ya se metió en otro.
        var index = (ICollection<KeyValuePair<ulong, Guid>>)_playerRaid;
        foreach (var entry in _playerRaid)
        {
            if (entry.Value == raidId)
            {
                index.Remove(entry);
            }
        }
    }
}
