using System.Collections.Concurrent;
using BotDsRpg.GameData;
using Discord;

namespace BotDsRpg.Services;

public enum RpsPhase { Offered, Picking, Finished }

// Una partida de piedra, papel o tijera. Se muta SOLO bajo Lock y sin await adentro (el mismo modelo que el jefe en equipo):
// dos clicks a la vez del último que elige deciden la partida UNA sola vez.
public sealed class RpsGame
{
    public required Guid Id { get; init; }
    public required ulong ChallengerId { get; init; }
    public required ulong OpponentId { get; init; }
    public required string ChallengerName { get; init; }
    public required string OpponentName { get; init; }
    public required int Bet { get; init; }

    public object Lock { get; } = new();
    public RpsPhase Phase { get; set; } = RpsPhase.Offered;
    public RpsChoice? ChallengerPick { get; set; }
    public RpsChoice? OpponentPick { get; set; }
    public int Ties { get; set; }

    // Sube con cada mano nueva: un reloj viejo (de una mano que ya se empató o se decidió) se descarta solo.
    public int Round { get; set; }
    public DateTime OfferExpiresUtc { get; set; }

    // El mensaje público de la partida, para que el reloj lo pueda editar cuando alguien no elige a tiempo.
    public IUserMessage? Message { get; set; }

    public bool IsPlayer(ulong id) => id == ChallengerId || id == OpponentId;
}

public enum RpsAcceptStatus { Ok, NotFound, NotYours, Expired, AlreadyStarted, Busy }

public enum RpsPickStatus { NotFound, NotPlayer, NotPicking, AlreadyPicked, Waiting, Tie, Cancelled, Decided }

// Lo que pasó al elegir (o al vencer el reloj). En Tie y Decided van las dos elecciones de la mano que se reveló.
public sealed record RpsPickResult(
    RpsPickStatus Status, RpsChoice? ChallengerChoice = null, RpsChoice? OpponentChoice = null, ulong WinnerId = 0, ulong LoserId = 0, bool ByTimeout = false);

public interface IRpsService
{
    // Crea la propuesta. Null si alguno de los dos ya está jugando otra partida (una ya aceptada). Una propuesta nueva del mismo
    // retador reemplaza a la que tenía abierta.
    RpsGame? Create(ulong challengerId, string challengerName, ulong opponentId, string opponentName, int bet);

    RpsGame? Get(Guid id);

    RpsAcceptStatus Accept(Guid id, ulong userId);

    // Rechazar (el invitado) o retirar (el retador) una propuesta que todavía no se aceptó.
    bool Decline(Guid id, ulong userId);

    RpsPickResult Pick(Guid id, ulong userId, RpsChoice choice);

    // El reloj de la mano: si sigue siendo la misma mano y alguien no eligió, gana el que eligió (o se cancela si ninguno).
    RpsPickResult Timeout(Guid id, int round);
}

// En memoria, como las propuestas de /trade y los duelos: una partida es cosa de minutos. Si el bot se reinicia en medio, se pierde
// la partida y NO se pierde oro, porque el oro se mueve recién al final (ver RpsModule.SettleAsync).
public sealed class RpsService : IRpsService
{
    private readonly ConcurrentDictionary<Guid, RpsGame> _games = new();

    public RpsGame? Create(ulong challengerId, string challengerName, ulong opponentId, string opponentName, int bet)
    {
        Purge();

        if (_games.Values.Any(g => g.Phase == RpsPhase.Picking && (g.IsPlayer(challengerId) || g.IsPlayer(opponentId))))
        {
            return null;
        }

        foreach (var old in _games.Values.Where(g => g.Phase == RpsPhase.Offered && g.ChallengerId == challengerId))
        {
            _games.TryRemove(old.Id, out _);
        }

        var game = new RpsGame
        {
            Id = Guid.NewGuid(),
            ChallengerId = challengerId,
            ChallengerName = challengerName,
            OpponentId = opponentId,
            OpponentName = opponentName,
            Bet = bet,
            OfferExpiresUtc = DateTime.UtcNow + RpsRules.OfferLifetime,
        };
        _games[game.Id] = game;
        return game;
    }

    public RpsGame? Get(Guid id) => _games.TryGetValue(id, out var game) ? game : null;

    public RpsAcceptStatus Accept(Guid id, ulong userId)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            return RpsAcceptStatus.NotFound;
        }

        if (userId != game.OpponentId)
        {
            return RpsAcceptStatus.NotYours;
        }

        // Fuera del lock de ESTA partida: mirar si alguno ya está jugando otra (no se toman dos locks a la vez).
        bool busy = _games.Values.Any(g => g.Id != id && g.Phase == RpsPhase.Picking && (g.IsPlayer(game.ChallengerId) || g.IsPlayer(game.OpponentId)));

        lock (game.Lock)
        {
            if (game.Phase != RpsPhase.Offered)
            {
                return RpsAcceptStatus.AlreadyStarted;
            }

            if (game.OfferExpiresUtc <= DateTime.UtcNow)
            {
                game.Phase = RpsPhase.Finished;
                _games.TryRemove(id, out _);
                return RpsAcceptStatus.Expired;
            }

            if (busy)
            {
                return RpsAcceptStatus.Busy;
            }

            game.Phase = RpsPhase.Picking;
            game.Round++;
            return RpsAcceptStatus.Ok;
        }
    }

    public bool Decline(Guid id, ulong userId)
    {
        if (!_games.TryGetValue(id, out var game) || !game.IsPlayer(userId))
        {
            return false;
        }

        lock (game.Lock)
        {
            if (game.Phase != RpsPhase.Offered)
            {
                return false;
            }

            game.Phase = RpsPhase.Finished;
        }

        _games.TryRemove(id, out _);
        return true;
    }

    public RpsPickResult Pick(Guid id, ulong userId, RpsChoice choice)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            return new RpsPickResult(RpsPickStatus.NotFound);
        }

        if (!game.IsPlayer(userId))
        {
            return new RpsPickResult(RpsPickStatus.NotPlayer);
        }

        RpsPickResult result;
        lock (game.Lock)
        {
            if (game.Phase != RpsPhase.Picking)
            {
                return new RpsPickResult(RpsPickStatus.NotPicking);
            }

            bool isChallenger = userId == game.ChallengerId;
            if ((isChallenger ? game.ChallengerPick : game.OpponentPick) is not null)
            {
                return new RpsPickResult(RpsPickStatus.AlreadyPicked);
            }

            if (isChallenger) game.ChallengerPick = choice;
            else game.OpponentPick = choice;

            if (game.ChallengerPick is null || game.OpponentPick is null)
            {
                return new RpsPickResult(RpsPickStatus.Waiting);
            }

            result = Reveal(game);
        }

        if (result.Status is RpsPickStatus.Decided or RpsPickStatus.Cancelled)
        {
            _games.TryRemove(id, out _);
        }

        return result;
    }

    public RpsPickResult Timeout(Guid id, int round)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            return new RpsPickResult(RpsPickStatus.NotFound);
        }

        RpsPickResult result;
        lock (game.Lock)
        {
            if (game.Phase != RpsPhase.Picking || game.Round != round)
            {
                return new RpsPickResult(RpsPickStatus.NotPicking);
            }

            game.Phase = RpsPhase.Finished;
            var c = game.ChallengerPick;
            var o = game.OpponentPick;
            result = (c, o) switch
            {
                (not null, null) => new RpsPickResult(RpsPickStatus.Decided, c, null, game.ChallengerId, game.OpponentId, ByTimeout: true),
                (null, not null) => new RpsPickResult(RpsPickStatus.Decided, null, o, game.OpponentId, game.ChallengerId, ByTimeout: true),
                _ => new RpsPickResult(RpsPickStatus.Cancelled, ByTimeout: true),
            };
        }

        _games.TryRemove(id, out _);
        return result;
    }

    // Las dos eligieron: se revela. Se llama con el lock tomado.
    private static RpsPickResult Reveal(RpsGame game)
    {
        var c = game.ChallengerPick!.Value;
        var o = game.OpponentPick!.Value;
        int cmp = RpsRules.Compare(c, o);
        if (cmp == 0)
        {
            game.Ties++;
            if (game.Ties >= RpsRules.MaxTies)
            {
                game.Phase = RpsPhase.Finished;
                return new RpsPickResult(RpsPickStatus.Cancelled, c, o);
            }

            // Otra mano: se borran las elecciones y el reloj empieza de nuevo (Round nuevo).
            game.ChallengerPick = null;
            game.OpponentPick = null;
            game.Round++;
            return new RpsPickResult(RpsPickStatus.Tie, c, o);
        }

        game.Phase = RpsPhase.Finished;
        return cmp > 0
            ? new RpsPickResult(RpsPickStatus.Decided, c, o, game.ChallengerId, game.OpponentId)
            : new RpsPickResult(RpsPickStatus.Decided, c, o, game.OpponentId, game.ChallengerId);
    }

    private void Purge()
    {
        var now = DateTime.UtcNow;
        foreach (var expired in _games.Values.Where(g => g.Phase == RpsPhase.Offered && g.OfferExpiresUtc <= now))
        {
            _games.TryRemove(expired.Id, out _);
        }
    }
}
