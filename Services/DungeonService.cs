using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.GameData.Dungeons;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Una corrida de mazmorra en curso (en memoria, como una pelea: un reinicio del bot la corta; la Reliquia se gasta recién al terminar, así que un reinicio no la quema).
// La vida está en las unidades que VE el jugador (HpDisplay), igual que en una pelea; al terminar se pasa a vida real.
public sealed class DungeonSession
{
    public required Guid RunId { get; init; }
    public required ulong OwnerId { get; init; }
    public required int ZoneId { get; init; }
    public required string ZoneName { get; init; }
    public required int ZoneRank { get; init; }
    // La zona tope del jugador: ganar lo manda directo contra el Asador (si no, abre la zona siguiente).
    public required bool IsTop { get; init; }
    public required IDungeonPuzzle Puzzle { get; init; }
    public required int MaxHp { get; init; }
    public required int StartHp { get; init; }
    public required double HpMultiplier { get; init; }
    public required RelicInfo Relic { get; init; }
    public ulong ChannelId { get; init; }

    public int Hp { get; set; }
    public int Mistakes { get; set; }
    public bool Ended { get; set; }
    public long Version { get; set; }
    public DateTime LastActionUtc { get; set; } = DateTime.UtcNow;

    // Lo que hay que hacer con el mensaje si la corrida vence por tiempo (lo pone el módulo de Discord). Null en las pruebas.
    public Func<DungeonActResult, Task>? OnTimeout { get; set; }

    public object Lock { get; } = new();

    // Las primeras 8 letras del id: lo llevan los botones, para que uno de una corrida vieja no juegue en la nueva.
    public string Token => RunId.ToString("N")[..8];
}

public enum DungeonStartStatus { Started, NoAccount, NotDungeonZone, AlreadyBusy, OnCooldown, NoHp, NotLeveled, NoRelic, RaceLost }

public sealed record DungeonStartResult(DungeonStartStatus Status, DungeonSession? Session = null, TimeSpan? Wait = null, int? RequiredLevel = null, RelicInfo? Relic = null);

public enum DungeonEnd { None, Won, Lost, TimedOut }

// Step: qué hizo la acción. End: si la corrida terminó y cómo. Finish: lo que se guardó al terminar. Asador: la pelea que arranca al ganar la mazmorra de la zona tope (el módulo la registra y la muestra).
public sealed record DungeonActResult(DungeonStep Step, DungeonSession? Session, DungeonEnd End, DungeonFinishOutcome? Finish = null, CombatState? Asador = null, int Damage = 0, int Gold = 0, string? BoxName = null);

public interface IDungeonService
{
    Task<DungeonStartResult> StartAsync(ulong discordId, ulong channelId, CancellationToken cancellationToken = default);

    // token: el de los botones (null = la acción escrita, que juega en la corrida que haya).
    Task<DungeonActResult> ActAsync(ulong discordId, string? token, string action, CancellationToken cancellationToken = default);

    DungeonSession? Peek(ulong discordId);

    bool IsInDungeon(ulong discordId);
}

public sealed class DungeonService(
    IUserRepository userRepository,
    IZoneRepository zoneRepository,
    IAdventureRepository adventureRepository,
    ICooldownRepository cooldownRepository,
    IDungeonRepository dungeonRepository,
    IZoneBoxService zoneBoxService,
    IAdventureCombatStarter combatStarter,
    ICombatSessionService combatSessions,
    IRaidSessionService raidSessions,
    IGameEvents gameEvents,
    IPlayerBonusService? bonusService = null) : IDungeonService
{
    // Premio de una mazmorra de zona intermedia (la de la zona tope no paga: la paga el Asador). En las unidades de siempre: cacerías de oro de la zona y % del nivel.
    public const int WinGoldHunts = 40;
    public const int WinXpPercent = 10;

    // Para las pruebas: el azar de los acertijos y cuánto se espera antes de dar por perdida una corrida quieta.
    public Func<Random> RandomFactory { get; set; } = () => new Random();
    public TimeSpan Timeout { get; set; } = DungeonRules.ActionTimeout;

    private readonly ConcurrentDictionary<ulong, DungeonSession> _sessions = new();
    private readonly ConcurrentDictionary<ulong, byte> _starting = new();

    public DungeonSession? Peek(ulong discordId) => _sessions.TryGetValue(discordId, out var s) && !s.Ended ? s : null;

    public bool IsInDungeon(ulong discordId) => Peek(discordId) is not null;

    public async Task<DungeonStartResult> StartAsync(ulong discordId, ulong channelId, CancellationToken cancellationToken = default)
    {
        if (IsInDungeon(discordId) || combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new DungeonStartResult(DungeonStartStatus.AlreadyBusy);
        }

        var player = await userRepository.GetByDiscordIdAsync(discordId, cancellationToken);
        if (player is null)
        {
            return new DungeonStartResult(DungeonStartStatus.NoAccount);
        }

        var ladder = ZoneRanking.ForPlayer(await zoneRepository.GetAllAsync(cancellationToken), player.FuegoNuevo);
        var zone = ladder.FirstOrDefault(z => z.ZoneId == player.CurrentZoneId);
        int rank = ZoneRanking.RankOf(ladder, player.CurrentZoneId);
        if (zone is null || player.InGate || !ZoneRanking.IsDungeonZone(zone) || DungeonRules.ForZoneRank(rank) is not { } kind)
        {
            return new DungeonStartResult(DungeonStartStatus.NotDungeonZone);
        }

        var remaining = await cooldownRepository.GetRemainingAsync(discordId, CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Duration, cancellationToken);
        if (remaining is not null)
        {
            return new DungeonStartResult(DungeonStartStatus.OnCooldown, Wait: remaining);
        }

        if (player.CurrentHp <= 0)
        {
            return new DungeonStartResult(DungeonStartStatus.NoHp);
        }

        // Como un jefe: para pasar a la zona siguiente hay que estar a su nivel. La zona tope no pide más (lleva al Asador).
        if (ZoneRanking.RequiredLevelForBoss(ladder, zone.ZoneId) is int required && player.Level < required)
        {
            return new DungeonStartResult(DungeonStartStatus.NotLeveled, RequiredLevel: required);
        }

        var relic = await dungeonRepository.GetRelicAsync(zone.ZoneId, cancellationToken);
        if (relic is null || await dungeonRepository.CountAsync(discordId, relic.ItemId, cancellationToken) < 1)
        {
            return new DungeonStartResult(DungeonStartStatus.NoRelic, Relic: relic);
        }

        if (!_starting.TryAdd(discordId, 0))
        {
            return new DungeonStartResult(DungeonStartStatus.AlreadyBusy);
        }

        try
        {
            // El cooldown del jefe se cobra al entrar con su reintento de 30 minutos; ganar lo completa (DungeonRepository.FinishAsync), perder deja el reintento.
            if (!await adventureRepository.TryClaimCooldownAsync(discordId, CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Duration, CooldownCatalog.Boss.RetryAfterFailure, cancellationToken))
            {
                return new DungeonStartResult(DungeonStartStatus.RaceLost);
            }

            double multiplier = await bonusService.HpMultiplierAsync(player);
            int maxHp = Math.Max(1, HpDisplay.Scale(player.MaxHp, multiplier));
            int hp = Math.Clamp(HpDisplay.Scale(player.CurrentHp, multiplier), 1, maxHp);
            var session = new DungeonSession
            {
                RunId = Guid.NewGuid(), OwnerId = discordId, ZoneId = zone.ZoneId, ZoneName = zone.Name, ZoneRank = rank, IsTop = ladder[^1].ZoneId == zone.ZoneId,
                Puzzle = DungeonRules.Create(kind, RandomFactory()), MaxHp = maxHp, StartHp = hp, Hp = hp, HpMultiplier = multiplier, Relic = relic, ChannelId = channelId,
            };
            _sessions[discordId] = session;
            ArmTimer(session, 0);
            return new DungeonStartResult(DungeonStartStatus.Started, session, Relic: relic);
        }
        finally
        {
            _starting.TryRemove(discordId, out _);
        }
    }

    public async Task<DungeonActResult> ActAsync(ulong discordId, string? token, string action, CancellationToken cancellationToken = default)
    {
        var session = Peek(discordId);
        if (session is null || (token is not null && token != session.Token))
        {
            return new DungeonActResult(DungeonStep.Ignored, null, DungeonEnd.None);
        }

        DungeonStep step;
        int damage = 0;
        DungeonEnd end = DungeonEnd.None;
        long version;
        lock (session.Lock)
        {
            if (session.Ended)
            {
                return new DungeonActResult(DungeonStep.Ignored, session, DungeonEnd.None);
            }

            step = session.Puzzle.Apply(action);
            if (step == DungeonStep.Ignored)
            {
                return new DungeonActResult(step, session, DungeonEnd.None);
            }

            if (step == DungeonStep.Mistake)
            {
                damage = DungeonRules.MistakeDamage(session.Puzzle.Kind, session.MaxHp);
                session.Hp = Math.Max(0, session.Hp - damage);
                session.Mistakes++;
            }

            if (step == DungeonStep.Solved)
            {
                end = DungeonEnd.Won;
            }
            else if (session.Hp <= 0 || session.Puzzle.Failed)
            {
                end = DungeonEnd.Lost;
            }

            session.LastActionUtc = DateTime.UtcNow;
            version = ++session.Version;
            if (end != DungeonEnd.None)
            {
                session.Ended = true;
            }
        }

        if (end == DungeonEnd.None)
        {
            ArmTimer(session, version);
            return new DungeonActResult(step, session, end, Damage: damage);
        }

        return await FinishAsync(session, step, end, damage, cancellationToken);
    }

    private async Task<DungeonActResult> FinishAsync(DungeonSession session, DungeonStep step, DungeonEnd end, int damage, CancellationToken cancellationToken)
    {
        _sessions.TryRemove(new KeyValuePair<ulong, DungeonSession>(session.OwnerId, session));
        bool won = end == DungeonEnd.Won;
        int shownLoss = Math.Max(0, session.StartHp - session.Hp);
        int realLoss = HpDisplay.ToReal(shownLoss, session.HpMultiplier);
        bool intermediateWin = won && !session.IsTop;
        int gold = intermediateWin ? WinGoldHunts * MissionRewards.GoldUnit(session.ZoneRank) : 0;
        string? box = intermediateWin ? (await zoneBoxService.GetAsync(cancellationToken)).ForRank(session.ZoneRank, ZoneBoxRole.Repeat)?.BoxName : null;

        var finish = await dungeonRepository.FinishAsync(
            new DungeonFinishRequest(session.OwnerId, session.Relic.ItemId, realLoss, won, intermediateWin ? session.ZoneId : null, gold, WinXpPercent, box), cancellationToken);

        if (finish.Won)
        {
            await gameEvents.RecordVictoryAsync(session.OwnerId, GameEventKinds.DungeonWin, finish.Outcome);
        }
        else
        {
            await gameEvents.RecordAsync(session.OwnerId, GameEventKinds.DungeonLoss, session.ZoneId, 1, session.ZoneId.ToString());
        }

        // La mazmorra de la zona tope: se sale de la zona y se entra directo al Fogón, contra el Asador, con la vida que quedó y sin comer.
        CombatState? asador = null;
        if (finish.Won && session.IsTop)
        {
            var start = await combatStarter.PrepareAsadorFromDungeonAsync(session.OwnerId, session.ZoneRank, session.Hp, cancellationToken);
            asador = start.State;
        }

        return new DungeonActResult(step, session, finish.Won ? end : DungeonEnd.Lost, finish, asador, damage, finish.Won ? gold : 0, finish.BoxGranted ? box : null);
    }

    // Si en Timeout no pasó nada más en esta corrida, se da por perdida (como una pelea abandonada). El temporizador lleva la versión: uno viejo se descarta solo.
    private void ArmTimer(DungeonSession session, long version)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout);
                lock (session.Lock)
                {
                    if (session.Ended || session.Version != version)
                    {
                        return;
                    }

                    session.Ended = true;
                }

                var result = await FinishAsync(session, DungeonStep.Ignored, DungeonEnd.TimedOut, 0, CancellationToken.None);
                result = result with { End = DungeonEnd.TimedOut };
                if (session.OnTimeout is { } notify)
                {
                    await notify(result);
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        });
    }
}
