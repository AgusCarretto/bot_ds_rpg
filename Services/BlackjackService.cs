using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

public enum BlackjackAction { Hit, Stand, Double }

// Lo que se ve de una mano: la mano, el oro que tiene el jugador ahora y, si hay, una nota (por ejemplo «te plantó la banca por tardar»).
public sealed record BlackjackView(Guid GameId, BlackjackState State, int Gold, string? Note = null)
{
    // Doblar pide las dos primeras cartas Y oro para igualar la apuesta.
    public bool CanDouble => State.CanDouble && Gold >= State.Stake;
}

// Message = un rechazo (con el motivo); View = la mano para mostrar. Una acción sobre una mano que ya terminó devuelve esa mano ya terminada (con su nota) y sin mensaje de error.
public sealed record BlackjackResult(string? Message, BlackjackView? View);

public interface IBlackjackService
{
    // Reparte una mano apostando betText (un número, 10k, 1m o all). Cobra la apuesta al empezar.
    Task<BlackjackResult> StartAsync(ulong discordId, string betText);

    // Pide, planta o dobla. gameId = la mano de los botones (null en los comandos de texto: la mano actual del jugador).
    Task<BlackjackResult> ActAsync(ulong discordId, Guid? gameId, BlackjackAction action);

    // La mano en juego del jugador, o null.
    BlackjackView? Peek(ulong discordId);
}

// El blackjack con la plata de verdad. Las reglas son puras (GameData/Blackjack.cs); acá están las manos en juego (en memoria, UNA por jugador) y el dinero:
//   · La apuesta se COBRA al repartir (ICasinoRepository.PlaceBetAsync, guardado: sin oro no se juega) y el premio se paga al terminar. Doblar cobra otra apuesta igual en el momento.
//   · Mismo modelo de concurrencia que el jefe en equipo: la mano se muta bajo su lock (sin await adentro) y el dinero se mueve afuera. Diez clicks a la vez en «Pedir» o dos manos empezadas juntas no duplican nada;
//     una mano que se está doblando (cobrando) rechaza las demás acciones hasta que termine.
//   · Una mano abandonada NO pierde la apuesta por no tocar los botones: a los IdleTimeout sin jugar la banca te planta y se paga lo que salga. Un reinicio del bot sí corta las manos en juego (viven en memoria,
//     como los combates): la apuesta ya cobrada se pierde.
//   · Cada mano que termina deja su evento casino_win / casino_loss con detail = «blackjack» (un empate no deja ninguno).
public sealed class BlackjackService(
    IUserRepository userRepository, ICasinoRepository casinoRepository, IGameEvents gameEvents, Func<Card>? draw = null) : IBlackjackService
{
    // Tope de apuesta: el premio máximo (doblar y ganar = 4 veces la apuesta original) tiene que entrar en un entero del oro.
    public const int MaxBet = 250_000_000;

    // Cuánto espera sin que el jugador toque nada antes de plantarlo.
    public const int IdleSeconds = 90;

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(IdleSeconds);

    private readonly Func<Card> _draw = draw ?? (() => Blackjack.RandomCard(Random.Shared));

    private sealed class Session
    {
        public Guid Id { get; } = Guid.NewGuid();
        public BlackjackState State { get; set; } = null!;
        public int Gold { get; set; }
        public int ZoneId { get; init; }
        public bool Busy { get; set; }
        public CancellationTokenSource? Timer { get; set; }
        public object Lock { get; } = new();
    }

    private readonly ConcurrentDictionary<ulong, Session> _active = new();
    private readonly ConcurrentDictionary<Guid, (BlackjackView View, DateTime At)> _finished = new();

    private const string AlreadyPlaying = "Ya tenés una mano en juego: terminala con los botones o con **aa bj hit** / **aa bj stand** / **aa bj double**.";

    public async Task<BlackjackResult> StartAsync(ulong discordId, string betText)
    {
        // Con una mano en juego, ese es el motivo (antes que «no te alcanza el oro», que es lo que se vería si ya apostó todo).
        if (_active.ContainsKey(discordId))
        {
            return new BlackjackResult(AlreadyPlaying, null);
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var parsed = AmountParser.Parse(betText, player.Gold);
        if (!parsed.Ok)
        {
            return new BlackjackResult(parsed.Error, null);
        }

        int bet = parsed.Amount;
        if (bet < CasinoModule.MinBet)
        {
            return new BlackjackResult($"La apuesta mínima es **{CasinoModule.MinBet}** de oro.", null);
        }

        if (bet > MaxBet)
        {
            return new BlackjackResult($"La apuesta máxima en el blackjack es **{MaxBet:N0}** de oro.".Replace(',', '.'), null);
        }

        if (player.Gold < bet)
        {
            return new BlackjackResult($"No tenés suficiente oro: la apuesta es **{bet}** y tenés **{player.Gold}**.", null);
        }

        // Se reserva el lugar ANTES de cobrar: dos «aa bj 100» a la vez cobran una sola vez.
        var session = new Session { Busy = true, ZoneId = player.CurrentZoneId };
        if (!_active.TryAdd(discordId, session))
        {
            return new BlackjackResult(AlreadyPlaying, null);
        }

        User? charged;
        try
        {
            charged = await casinoRepository.PlaceBetAsync(discordId, bet, 0);
        }
        catch
        {
            _active.TryRemove(discordId, out _);
            throw;
        }

        if (charged is null)
        {
            _active.TryRemove(discordId, out _);
            return new BlackjackResult($"No te alcanza el oro: la apuesta es **{bet}** y ya no tenés suficiente.", null);
        }

        var state = Blackjack.Start(bet, _draw);
        bool finished;
        lock (session.Lock)
        {
            session.State = state;
            session.Gold = charged.Gold;
            session.Busy = false;
            finished = state.Finished;
            if (finished)
            {
                _active.TryRemove(discordId, out _);
            }
            else
            {
                ArmTimer(discordId, session);
            }
        }

        return finished ? await SettleAsync(discordId, session, null) : new BlackjackResult(null, new BlackjackView(session.Id, state, charged.Gold));
    }

    public async Task<BlackjackResult> ActAsync(ulong discordId, Guid? gameId, BlackjackAction action)
    {
        if (!_active.TryGetValue(discordId, out var session) || (gameId is { } wanted && session.Id != wanted))
        {
            if (gameId is { } old && _finished.TryGetValue(old, out var done))
            {
                return new BlackjackResult(null, done.View with { Note = "Esa mano ya había terminado." });
            }

            return new BlackjackResult(session is null ? "No tenés una mano en juego: empezá una con **aa bj <apuesta>** (por ejemplo `aa bj 10k`)." : "Esa no es tu mano actual.", null);
        }

        if (action == BlackjackAction.Double)
        {
            return await DoubleAsync(discordId, session);
        }

        BlackjackState state;
        bool finished;
        lock (session.Lock)
        {
            if (!ReferenceEquals(_active.GetValueOrDefault(discordId), session) || session.State.Finished)
            {
                return new BlackjackResult("Esa mano ya terminó.", null);
            }

            if (session.Busy)
            {
                return new BlackjackResult("Estoy repartiendo la carta de doblar: esperá un segundo.", null);
            }

            state = action == BlackjackAction.Hit ? Blackjack.Hit(session.State, _draw) : Blackjack.Stand(session.State, _draw);
            session.State = state;
            finished = state.Finished;
            if (finished)
            {
                _active.TryRemove(discordId, out _);
                session.Timer?.Cancel();
            }
            else
            {
                ArmTimer(discordId, session);
            }
        }

        return finished ? await SettleAsync(discordId, session, null) : new BlackjackResult(null, new BlackjackView(session.Id, state, session.Gold));
    }

    public BlackjackView? Peek(ulong discordId)
    {
        if (!_active.TryGetValue(discordId, out var session))
        {
            return null;
        }

        lock (session.Lock)
        {
            return session.State is null ? null : new BlackjackView(session.Id, session.State, session.Gold);
        }
    }

    // Doblar: el cobro de la segunda apuesta es plata, así que va afuera del lock (la mano queda «ocupada» mientras tanto y rechaza las demás acciones).
    private async Task<BlackjackResult> DoubleAsync(ulong discordId, Session session)
    {
        int stake;
        lock (session.Lock)
        {
            if (!ReferenceEquals(_active.GetValueOrDefault(discordId), session) || session.State.Finished)
            {
                return new BlackjackResult("Esa mano ya terminó.", null);
            }

            if (session.Busy)
            {
                return new BlackjackResult("Estoy repartiendo la carta de doblar: esperá un segundo.", null);
            }

            if (!session.State.CanDouble)
            {
                return new BlackjackResult("Solo se puede doblar con tus **dos primeras cartas**.", null);
            }

            if (session.Gold < session.State.Stake)
            {
                return new BlackjackResult($"No te alcanza el oro para doblar: necesitás **{session.State.Stake}** más y tenés **{session.Gold}**.", null);
            }

            session.Busy = true;
            session.Timer?.Cancel();
            stake = session.State.Stake;
        }

        User? charged;
        try
        {
            charged = await casinoRepository.PlaceBetAsync(discordId, stake, 0);
        }
        catch
        {
            lock (session.Lock) { session.Busy = false; ArmTimer(discordId, session); }
            throw;
        }

        if (charged is null)
        {
            lock (session.Lock) { session.Busy = false; ArmTimer(discordId, session); }
            return new BlackjackResult($"No te alcanza el oro para doblar: necesitás **{stake}** más.", null);
        }

        lock (session.Lock)
        {
            session.State = Blackjack.Double(session.State, _draw);
            session.Gold = charged.Gold;
            session.Busy = false;
            _active.TryRemove(discordId, out _);
        }

        return await SettleAsync(discordId, session, null);
    }

    // Termina la mano: paga lo que corresponde, deja el evento y recuerda el resultado (para los botones viejos). La mano ya salió de _active, así que nadie más la toca.
    private async Task<BlackjackResult> SettleAsync(ulong discordId, Session session, string? note)
    {
        var state = session.State;
        int payout = Blackjack.Payout(state);
        int gold = session.Gold;

        if (payout > 0)
        {
            var paid = await PayAsync(discordId, payout);
            if (paid is not null)
            {
                gold = paid.Gold;
            }
        }

        int net = Blackjack.Net(state);
        if (net > 0)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.CasinoWin, session.ZoneId, net, "blackjack");
        }
        else if (net < 0)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.CasinoLoss, session.ZoneId, -net, "blackjack");
        }

        var view = new BlackjackView(session.Id, state, gold, note);
        Remember(view);
        return new BlackjackResult(null, view);
    }

    // El premio es plata que el jugador ya ganó: si la base falla un instante se reintenta antes de rendirse (y se registra).
    private async Task<User?> PayAsync(ulong discordId, int payout)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await casinoRepository.PlaceBetAsync(discordId, 0, payout);
            }
            catch (Exception ex) when (attempt < 3)
            {
                BotLog.Warn(ex);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
            catch (Exception ex)
            {
                BotLog.Error(ex);
                return null;
            }
        }
    }

    private void Remember(BlackjackView view)
    {
        _finished[view.GameId] = (view, DateTime.UtcNow);
        if (_finished.Count > 500)
        {
            foreach (var old in _finished.Where(kv => DateTime.UtcNow - kv.Value.At > TimeSpan.FromMinutes(15)).Select(kv => kv.Key).ToList())
            {
                _finished.TryRemove(old, out _);
            }
        }
    }

    // Se llama con el lock de la sesión tomado. Cada acción reinicia el reloj; si pasa IdleTimeout sin jugar, la banca te planta.
    private void ArmTimer(ulong discordId, Session session)
    {
        session.Timer?.Cancel();
        var cts = new CancellationTokenSource();
        session.Timer = cts;
        _ = Task.Run(() => ExpireAsync(discordId, session, cts.Token));
    }

    private async Task ExpireAsync(ulong discordId, Session session, CancellationToken token)
    {
        try
        {
            await Task.Delay(IdleTimeout, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool settle = false;
        lock (session.Lock)
        {
            if (!token.IsCancellationRequested && !session.Busy && !session.State.Finished && ReferenceEquals(_active.GetValueOrDefault(discordId), session))
            {
                session.State = Blackjack.Stand(session.State, _draw);
                _active.TryRemove(discordId, out _);
                settle = true;
            }
        }

        if (!settle)
        {
            return;
        }

        try
        {
            await SettleAsync(discordId, session, "⏰ Pasó mucho tiempo sin jugar: la banca te plantó.");
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }
    }
}
