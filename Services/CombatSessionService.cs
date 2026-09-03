using System.Collections.Concurrent;
using BotDsRpg.Repositories;
using Discord;

namespace BotDsRpg.Services;

public sealed class CombatSessionService(IUserRepository userRepository) : ICombatSessionService
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ulong, CombatSession> _sessions = new();

    public bool TryStart(ulong discordId, CombatState state, ICombatReplyTarget replyTarget)
    {
        var cts = new CancellationTokenSource();
        var session = new CombatSession { State = state, ReplyTarget = replyTarget, TimeoutCts = cts };

        if (!_sessions.TryAdd(discordId, session))
        {
            cts.Dispose();
            return false;
        }

        ScheduleTimeout(discordId, session, cts.Token);
        return true;
    }

    public CombatSession? Peek(ulong discordId) =>
        _sessions.TryGetValue(discordId, out var session) ? session : null;

    public bool TryAdvance(ulong discordId, CombatSession expectedSession, CombatState? nextState, ICombatReplyTarget? nextReplyTarget)
    {
        // Remove(KeyValuePair) solo saca la entrada si sigue siendo exactamente "expectedSession"
        // (comparación por referencia, CombatSession no sobreescribe Equals). Si otra ejecución
        // (doble click, o el timeout) ya la resolvió, esto devuelve false y no tocamos nada más.
        var collection = (ICollection<KeyValuePair<ulong, CombatSession>>)_sessions;
        if (!collection.Remove(new KeyValuePair<ulong, CombatSession>(discordId, expectedSession)))
        {
            return false;
        }

        expectedSession.TimeoutCts.Cancel();
        expectedSession.TimeoutCts.Dispose();

        if (nextState is not null && nextReplyTarget is not null)
        {
            var cts = new CancellationTokenSource();
            var nextSession = new CombatSession { State = nextState, ReplyTarget = nextReplyTarget, TimeoutCts = cts };
            _sessions[discordId] = nextSession;
            ScheduleTimeout(discordId, nextSession, cts.Token);
        }

        return true;
    }

    private void ScheduleTimeout(ulong discordId, CombatSession session, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TurnTimeout, token);
            }
            catch (TaskCanceledException)
            {
                return; // El jugador actuó a tiempo: se canceló el timer de este turno.
            }

            // Pasaron 30s sin respuesta: el combate termina como si el jugador se hubiera retirado
            // (sin recompensa, pero el HP perdido durante la pelea sigue contando).
            var collection = (ICollection<KeyValuePair<ulong, CombatSession>>)_sessions;
            if (!collection.Remove(new KeyValuePair<ulong, CombatSession>(discordId, session)))
            {
                return; // Ya se había resuelto por otra vía (ataque/huida) justo antes.
            }

            try
            {
                // Delta (no snapshot absoluto): así una curación con /use a mitad de combate no se
                // pierde si el HP en base ya reflejaba otra cosa (ver IUserRepository.ApplyCombatHpDeltaAsync).
                // ToDbHpDelta: "destraduce" el delta de HP de combate (escalado si es Guerrero) a
                // unidades reales de base — ver CombatState.ToDbHpDelta.
                int hpDelta = session.State.ToDbHpDelta(session.State.PlayerCurrentHp - session.State.PlayerStartingHp);
                await userRepository.ApplyCombatHpDeltaAsync(discordId, hpDelta);

                var embed = new EmbedBuilder()
                    .WithTitle("💨 Combate abandonado")
                    .WithDescription(
                        $"No respondiste a tiempo y te retiraste del combate contra " +
                        $"**{session.State.MonsterName}** {session.State.MonsterEmoji}.")
                    .WithColor(Color.DarkGrey)
                    .Build();

                await session.ReplyTarget.UpdateAsync(embed, new ComponentBuilder().Build());
            }
            catch
            {
                // Si ya no se puede editar el mensaje (token vencido, mensaje borrado, etc.)
                // no hay nada más que hacer; el HP ya quedó persistido arriba en cualquier caso.
            }
        }, token);
    }
}
