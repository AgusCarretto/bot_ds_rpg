using System.Collections.Concurrent;
using BotDsRpg.GameData;
using Discord;

namespace BotDsRpg.Services;

// Un desafío pendiente: ChallengerId le propone un duelo amistoso a TargetId (con los nombres con los que se van a mostrar).
public sealed record DuelChallenge(Guid Id, ulong ChallengerId, string ChallengerName, ulong TargetId, string TargetName, DateTime ExpiresAtUtc);

public enum DuelEnd
{
    None,      // sigue en curso
    Knockout,  // uno se quedó sin vida
    Forfeit,   // uno se rindió (EndedBy)
    TimedOut,  // al que le tocaba no jugó a tiempo y perdió (EndedBy)
    TimeCap,   // se acabó el tiempo de combate (DuelEngine.MaxActions): gana el que tiene más vida
}

// Cómo se dibuja el mensaje del duelo (embed + botones). Lo pone quien arranca el duelo (el módulo): el servicio no sabe de Discord más
// que lo justo, pero el timeout tiene que poder editar el mensaje por su cuenta. El renderer toma el Lock de la sesión (reentrante) para leer
// un estado consistente, así que se puede llamar dentro o fuera de él.
public delegate (Embed Embed, MessageComponent Components) DuelRenderer(DuelSession session);

// Un duelo en curso. Mutable y protegido por Lock — plain lock, así que NUNCA un await adentro (se hace el I/O después de soltarlo). Es el mismo
// modelo que el raid (RaidSession): acá dos personas distintas pueden apretar a la vez, y cada mutación pasa de a una.
public sealed class DuelSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public object Lock { get; } = new();

    public required DuelState State { get; set; }
    public required ICombatReplyTarget ReplyTarget { get; set; }
    public required DuelRenderer Renderer { get; init; }

    public DuelEnd End { get; set; }
    public ulong? EndedBy { get; set; }

    // Las últimas líneas de lo que pasó (lo que se muestra arriba del mensaje).
    public List<string> Log { get; } = [];
    public CancellationTokenSource? TurnTimer { get; set; }

    public ulong AId => State.A.Fighter.DiscordId;
    public ulong BId => State.B.Fighter.DiscordId;

    // Quién ganó (null si sigue en curso). Para Forfeit/TimedOut es el rival de quien se rindió o se durmió.
    public DuelSide? Winner => End switch
    {
        DuelEnd.None => null,
        DuelEnd.Forfeit or DuelEnd.TimedOut => EndedBy == AId ? State.B : State.A,
        _ => DuelEngine.Conclude(State).Winner,
    };

    public DuelSide? Loser => Winner is { } w ? (ReferenceEquals(w, State.A) ? State.B : State.A) : null;
}

public enum DuelActStatus
{
    Ok,
    NotFound,           // ese duelo no existe (o ya terminó y se limpió)
    AlreadyOver,        // justo terminó
    NotParticipant,     // no es uno de los dos
    NotYourTurn,
    AbilityUnavailable, // la habilidad está en enfriamiento (o la clase no tiene)
}

public sealed record DuelActResult(DuelActStatus Status, DuelSession? Session);

public interface IDuelService
{
    // Crea el desafío (vence a los 2 minutos). Cada jugador tiene UN desafío abierto: uno nuevo reemplaza al anterior.
    DuelChallenge CreateChallenge(ulong challengerId, string challengerName, ulong targetId, string targetName);

    // Mirar sin sacarlo (null si no existe o ya venció).
    DuelChallenge? PeekChallenge(Guid id);

    // Lo saca de forma atómica: de varios clicks a la vez en "Aceptar" solo UNO lo recibe. Null también si ya venció.
    DuelChallenge? TryTakeChallenge(Guid id);

    bool IsInDuel(ulong discordId);

    DuelSession? Find(Guid id);

    // Arranca el duelo (el que sale en la moneda empieza). Null si alguno de los dos ya está en otro duelo.
    DuelSession? TryStart(DuelFighter a, DuelFighter b, ICombatReplyTarget replyTarget, DuelRenderer renderer);

    // La acción del jugador al que le toca. replyTarget: la interacción de este click (la anterior ya puede estar vencida para editar).
    DuelActResult TryAct(Guid duelId, ulong actorId, PlayerAction action, ICombatReplyTarget replyTarget);

    // Rendirse: cualquiera de los dos, en cualquier momento.
    DuelActResult TryForfeit(Guid duelId, ulong userId, ICombatReplyTarget replyTarget);
}

// En memoria, como los combates y los raids: un duelo es cosa de minutos, no se guarda. Si el bot se reinicia, los duelos en curso se cortan.
// El duelo amistoso no toca la base (ni vida, ni oro, ni nada): lo único que deja es el registro de quién ganó (game_events).
public sealed class DuelService(IGameEvents gameEvents) : IDuelService
{
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);

    // Cuánto tiene cada jugador para jugar su turno. Si no juega, pierde (como retirarse de un combate).
    public static TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(60);

    private const int LogLines = 4;

    private readonly ConcurrentDictionary<Guid, DuelChallenge> _challenges = new();
    private readonly ConcurrentDictionary<Guid, DuelSession> _sessions = new();
    private readonly ConcurrentDictionary<ulong, Guid> _byPlayer = new();

    public DuelChallenge CreateChallenge(ulong challengerId, string challengerName, ulong targetId, string targetName)
    {
        PurgeChallenges();

        foreach (var old in _challenges.Values.Where(c => c.ChallengerId == challengerId))
        {
            _challenges.TryRemove(old.Id, out _);
        }

        var challenge = new DuelChallenge(Guid.NewGuid(), challengerId, challengerName, targetId, targetName, DateTime.UtcNow + ChallengeLifetime);
        _challenges[challenge.Id] = challenge;
        return challenge;
    }

    public DuelChallenge? PeekChallenge(Guid id) =>
        _challenges.TryGetValue(id, out var challenge) && challenge.ExpiresAtUtc > DateTime.UtcNow ? challenge : null;

    public DuelChallenge? TryTakeChallenge(Guid id) =>
        _challenges.TryRemove(id, out var challenge) && challenge.ExpiresAtUtc > DateTime.UtcNow ? challenge : null;

    public bool IsInDuel(ulong discordId) => _byPlayer.ContainsKey(discordId);

    public DuelSession? Find(Guid id) => _sessions.TryGetValue(id, out var session) ? session : null;

    public DuelSession? TryStart(DuelFighter a, DuelFighter b, ICombatReplyTarget replyTarget, DuelRenderer renderer)
    {
        var session = new DuelSession
        {
            State = DuelEngine.Start(a, b, aStarts: Random.Shared.Next(2) == 0),
            ReplyTarget = replyTarget,
            Renderer = renderer,
        };

        // Los dos índices a la vez: si el segundo falla, se deshace el primero (nadie queda "en un duelo" que no existe).
        if (!_byPlayer.TryAdd(a.DiscordId, session.Id))
        {
            return null;
        }

        if (!_byPlayer.TryAdd(b.DiscordId, session.Id))
        {
            _byPlayer.TryRemove(a.DiscordId, out _);
            return null;
        }

        _sessions[session.Id] = session;
        lock (session.Lock)
        {
            ScheduleTurnTimeout(session);
        }

        return session;
    }

    public DuelActResult TryAct(Guid duelId, ulong actorId, PlayerAction action, ICombatReplyTarget replyTarget)
    {
        if (Find(duelId) is not { } session)
        {
            return new DuelActResult(DuelActStatus.NotFound, null);
        }

        bool ended;
        lock (session.Lock)
        {
            if (session.End != DuelEnd.None)
            {
                return new DuelActResult(DuelActStatus.AlreadyOver, session);
            }

            if (actorId != session.AId && actorId != session.BId)
            {
                return new DuelActResult(DuelActStatus.NotParticipant, session);
            }

            if (session.State.Actor.Fighter.DiscordId != actorId)
            {
                return new DuelActResult(DuelActStatus.NotYourTurn, session);
            }

            if (action == PlayerAction.Ability && DuelEngine.CheckAbility(session.State) != AbilityAvailability.Ready)
            {
                return new DuelActResult(DuelActStatus.AbilityUnavailable, session);
            }

            var step = DuelEngine.Step(session.State, action);
            session.State = step.State;
            session.ReplyTarget = replyTarget;
            AddLog(session, DuelNarrator.Describe(step.Event));

            if (step.State.Finished)
            {
                session.End = DuelEnd.Knockout;
            }
            else if (step.State.Actions >= DuelEngine.MaxActions)
            {
                session.End = DuelEnd.TimeCap;
            }

            ended = session.End != DuelEnd.None;
            if (ended)
            {
                CancelTimer(session);
            }
            else
            {
                ScheduleTurnTimeout(session);
            }
        }

        if (ended)
        {
            Release(session);
        }

        return new DuelActResult(DuelActStatus.Ok, session);
    }

    public DuelActResult TryForfeit(Guid duelId, ulong userId, ICombatReplyTarget replyTarget)
    {
        if (Find(duelId) is not { } session)
        {
            return new DuelActResult(DuelActStatus.NotFound, null);
        }

        lock (session.Lock)
        {
            if (session.End != DuelEnd.None)
            {
                return new DuelActResult(DuelActStatus.AlreadyOver, session);
            }

            if (userId != session.AId && userId != session.BId)
            {
                return new DuelActResult(DuelActStatus.NotParticipant, session);
            }

            session.End = DuelEnd.Forfeit;
            session.EndedBy = userId;
            session.ReplyTarget = replyTarget;
            string who = userId == session.AId ? session.State.A.Fighter.Name : session.State.B.Fighter.Name;
            AddLog(session, $"🏳️ **{who}** se rindió.");
            CancelTimer(session);
        }

        Release(session);
        return new DuelActResult(DuelActStatus.Ok, session);
    }

    // Con el lock del duelo tomado. Un temporizador por turno: si el turno que se programó sigue siendo el actual cuando vence, el que tenía
    // que jugar perdió. Una acción a tiempo cancela el reloj y programa uno nuevo para el turno siguiente.
    private void ScheduleTurnTimeout(DuelSession session)
    {
        CancelTimer(session);

        var cts = new CancellationTokenSource();
        session.TurnTimer = cts;
        int actionsAtSchedule = session.State.Actions;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TurnTimeout, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return; // Jugó a tiempo (o el duelo terminó por otra vía).
            }

            ICombatReplyTarget target;
            lock (session.Lock)
            {
                if (session.End != DuelEnd.None || session.State.Actions != actionsAtSchedule)
                {
                    return; // Se resolvió justo antes (una carrera contra el click).
                }

                session.End = DuelEnd.TimedOut;
                session.EndedBy = session.State.Actor.Fighter.DiscordId;
                AddLog(session, $"⏰ **{session.State.Actor.Fighter.Name}** no jugó a tiempo.");
                target = session.ReplyTarget;
            }

            Release(session);

            try
            {
                var (embed, components) = session.Renderer(session);
                await target.UpdateAsync(embed, components);
            }
            catch (Exception ex)
            {
                // Si ya no se puede editar el mensaje (token vencido, borrado) no hay nada más que hacer: el duelo ya quedó cerrado.
                BotLog.Warn(ex);
            }
        });
    }

    private static void CancelTimer(DuelSession session)
    {
        session.TurnTimer?.Cancel();
        session.TurnTimer = null;
    }

    private static void AddLog(DuelSession session, string line)
    {
        session.Log.Add(line);
        while (session.Log.Count > LogLines)
        {
            session.Log.RemoveAt(0);
        }
    }

    // Saca a los dos del índice y anota el resultado. Se llama una sola vez por duelo (quien lo termina lo hace dentro de su lock).
    private void Release(DuelSession session)
    {
        _byPlayer.TryRemove(session.AId, out _);
        _byPlayer.TryRemove(session.BId, out _);
        _sessions.TryRemove(session.Id, out _);

        if (session.Winner is { } winner && session.Loser is { } loser)
        {
            _ = RecordEndAsync(winner.Fighter.DiscordId, loser.Fighter.DiscordId);
        }
    }

    // RecordAsync nunca tira excepción (ver IGameEvents), así que esto no puede romper el cierre del duelo.
    private async Task RecordEndAsync(ulong winnerId, ulong loserId)
    {
        await gameEvents.RecordAsync(winnerId, GameEventKinds.DuelWin);
        await gameEvents.RecordAsync(loserId, GameEventKinds.DuelLoss);
    }

    private void PurgeChallenges()
    {
        var now = DateTime.UtcNow;
        foreach (var expired in _challenges.Values.Where(c => c.ExpiresAtUtc <= now))
        {
            _challenges.TryRemove(expired.Id, out _);
        }
    }
}
