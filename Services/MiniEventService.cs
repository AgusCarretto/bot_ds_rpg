using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;

namespace BotDsRpg.Services;

public enum JoinStatus { Joined, AlreadyJoined, Finished, NoAccount, Gone }

public sealed record JoinOutcome(JoinStatus Status, int Participants = 0);

// El mensaje del minievento en el canal, que se edita al terminar (para poder probarlo sin Discord).
public interface IEventAnnouncement
{
    Task UpdateAsync(Embed embed, MessageComponent? components);
}

// Cada cuánto aparece un minievento y cuánto dura. Se puede cambiar con variables de entorno (útil para probarlo: MiniEvent__ChancePercent=100
// y MiniEvent__ChannelCooldownMinutes=0 hacen que aparezca en cada comando).
public sealed record MiniEventSettings(int ChancePercent, TimeSpan ChannelCooldown, TimeSpan JoinWindow)
{
    public static MiniEventSettings Default => new(3, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15));

    public static MiniEventSettings FromEnvironment()
    {
        static int Read(string key, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(key), out int value) && value >= 0 ? value : fallback;

        var d = Default;
        return new MiniEventSettings(
            Math.Min(100, Read("MiniEvent__ChancePercent", d.ChancePercent)),
            TimeSpan.FromMinutes(Read("MiniEvent__ChannelCooldownMinutes", (int)d.ChannelCooldown.TotalMinutes)),
            TimeSpan.FromSeconds(Math.Max(3, Read("MiniEvent__JoinSeconds", (int)d.JoinWindow.TotalSeconds))));
    }
}

public interface IMiniEventService
{
    // Se llama después de un comando en un canal: con poca probabilidad (y no más de uno cada tanto por canal) aparece un minievento ahí.
    // "publish" manda el mensaje al canal y devuelve cómo editarlo después. Nunca tira excepción: un minievento es un extra.
    Task MaybeSpawnAsync(ulong channelId, Func<Embed, MessageComponent, Task<IEventAnnouncement>> publish);

    Task<JoinOutcome> JoinAsync(Guid eventId, ulong userId);
}

// Minievento: aparece un aviso con un botón ("a un minero se le cayó una bolsa de piedras..."), la gente que lo toca en unos segundos se
// suma y, cuando se cierra la ventana, cada uno se lleva una recompensa CHICA que crece con la cantidad de participantes (reglas en
// GameData/MiniEvents.cs). Mismo criterio de concurrencia que el raid: el estado se muta bajo un lock SIN await adentro, y el cierre
// se decide dentro del mismo lock (después de eso nadie más se puede sumar, así que nadie cobra de más ni dos veces). El pago (que sí es
// I/O) va afuera del lock, una transacción por participante. En memoria, como los combates: si el bot se reinicia a mitad de un minievento,
// el botón queda "ya terminó" y nadie pierde nada.
public sealed class MiniEventService(
    IUserRepository userRepository, IZoneRepository zoneRepository, IItemRepository itemRepository, IMiniEventRepository payouts,
    IGameEvents gameEvents, MiniEventSettings? settings = null, Random? random = null) : IMiniEventService
{
    private readonly MiniEventSettings _settings = settings ?? MiniEventSettings.FromEnvironment();
    private readonly Random _random = random ?? Random.Shared;
    private readonly object _spawnLock = new();
    private readonly Dictionary<ulong, DateTime> _lastSpawnByChannel = new();
    private readonly HashSet<ulong> _channelsWithOpenEvent = [];
    private readonly ConcurrentDictionary<Guid, OpenEvent> _events = new();

    private sealed class OpenEvent(Guid id, ulong channelId, MiniEventKind kind)
    {
        public Guid Id { get; } = id;
        public ulong ChannelId { get; } = channelId;
        public MiniEventKind Kind { get; } = kind;
        public object Lock { get; } = new();
        public HashSet<ulong> Participants { get; } = [];
        public bool Closed { get; set; }
        public IEventAnnouncement? Announcement { get; set; }
    }

    public async Task MaybeSpawnAsync(ulong channelId, Func<Embed, MessageComponent, Task<IEventAnnouncement>> publish)
    {
        try
        {
            if (_settings.ChancePercent <= 0)
            {
                return;
            }

            MiniEventKind kind;
            lock (_spawnLock)
            {
                // Uno a la vez por canal, y no más de uno cada ChannelCooldown (si no, en un canal activo sería una molestia).
                if (_channelsWithOpenEvent.Contains(channelId)
                    || (_lastSpawnByChannel.TryGetValue(channelId, out var last) && DateTime.UtcNow - last < _settings.ChannelCooldown)
                    || _random.Next(100) >= _settings.ChancePercent)
                {
                    return;
                }

                _channelsWithOpenEvent.Add(channelId);
                _lastSpawnByChannel[channelId] = DateTime.UtcNow;
                kind = (MiniEventKind)_random.Next(Enum.GetValues<MiniEventKind>().Length);
            }

            var open = new OpenEvent(Guid.NewGuid(), channelId, kind);
            _events[open.Id] = open;

            try
            {
                var (title, story) = MiniEventRules.Announcement(kind, _random);
                var embed = new EmbedBuilder()
                    .WithTitle($"{MiniEventRules.Emoji(kind)} {title}")
                    .WithColor(Color.Orange)
                    .WithDescription(story)
                    .WithFooter($"Tenés {(int)_settings.JoinWindow.TotalSeconds} segundos · cuanta más gente se sume, más se lleva cada uno")
                    .Build();
                var buttons = new ComponentBuilder()
                    .WithButton("¡Juntar!", $"miniev_join:{open.Id}", ButtonStyle.Success, new Emoji(MiniEventRules.Emoji(kind)))
                    .Build();

                open.Announcement = await publish(embed, buttons);
            }
            catch
            {
                // No se pudo publicar (el bot no puede escribir en ese canal, por ejemplo): el evento no existió.
                _events.TryRemove(open.Id, out _);
                lock (_spawnLock)
                {
                    _channelsWithOpenEvent.Remove(channelId);
                }

                throw;
            }

            _ = Task.Run(() => CloseAfterWindowAsync(open));
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    public async Task<JoinOutcome> JoinAsync(Guid eventId, ulong userId)
    {
        if (!_events.TryGetValue(eventId, out var open))
        {
            return new JoinOutcome(JoinStatus.Gone);
        }

        if (await userRepository.GetByDiscordIdAsync(userId) is null)
        {
            return new JoinOutcome(JoinStatus.NoAccount);
        }

        lock (open.Lock)
        {
            if (open.Closed)
            {
                return new JoinOutcome(JoinStatus.Finished);
            }

            return open.Participants.Add(userId)
                ? new JoinOutcome(JoinStatus.Joined, open.Participants.Count)
                : new JoinOutcome(JoinStatus.AlreadyJoined, open.Participants.Count);
        }
    }

    private async Task CloseAfterWindowAsync(OpenEvent open)
    {
        try
        {
            await Task.Delay(_settings.JoinWindow);

            List<ulong> joined;
            lock (open.Lock)
            {
                open.Closed = true; // desde acá nadie más se suma: la lista de participantes ya es definitiva
                joined = [.. open.Participants];
            }

            _events.TryRemove(open.Id, out _);
            lock (_spawnLock)
            {
                _channelsWithOpenEvent.Remove(open.ChannelId);
            }

            var paid = await PayAllAsync(open.Kind, joined);

            if (open.Announcement is not null)
            {
                await open.Announcement.UpdateAsync(BuildResultEmbed(open.Kind, joined.Count, paid), null);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }
    }

    // Le paga a cada participante (una transacción por cada uno). Devuelve a quién y cuánto se le pagó; si uno falla, se registra y sigue.
    private async Task<List<(ulong UserId, MiniEventReward Reward)>> PayAllAsync(MiniEventKind kind, IReadOnlyList<ulong> participants)
    {
        var paid = new List<(ulong, MiniEventReward)>();
        if (participants.Count == 0)
        {
            return paid;
        }

        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());

        foreach (ulong userId in participants)
        {
            try
            {
                var user = await userRepository.GetByDiscordIdAsync(userId);
                if (user is null)
                {
                    continue;
                }

                var reward = MiniEventRules.Reward(kind, participants.Count, ZoneRanking.RankOf(zones, user.CurrentZoneId));
                int? itemId = reward.ItemName is null ? null : (await itemRepository.GetByNameAsync(reward.ItemName))?.ItemId;

                if (await payouts.PayAsync(userId, itemId, reward.Quantity, reward.Gold))
                {
                    paid.Add((userId, reward));
                    await gameEvents.RecordAsync(userId, GameEventKinds.MiniEvent, user.CurrentZoneId, detail: kind.ToString());
                }
            }
            catch (Exception ex)
            {
                BotLog.Error(ex);
            }
        }

        return paid;
    }

    // Público y puro para probarlo sin Discord.
    public static Embed BuildResultEmbed(MiniEventKind kind, int joinedCount, IReadOnlyList<(ulong UserId, MiniEventReward Reward)> paid)
    {
        if (joinedCount == 0)
        {
            return new EmbedBuilder()
                .WithTitle($"{MiniEventRules.Emoji(kind)} Se terminó")
                .WithColor(Color.DarkGrey)
                .WithDescription("Nadie llegó a tiempo: se lo llevó el viento.")
                .Build();
        }

        if (paid.Count == 0)
        {
            return new EmbedBuilder()
                .WithTitle($"{MiniEventRules.Emoji(kind)} No se pudo repartir")
                .WithColor(Color.DarkGrey)
                .WithDescription("Algo falló al repartir la recompensa esta vez. Perdón.")
                .Build();
        }

        string who = string.Join(", ", paid.Select(p => MentionUtils.MentionUser(p.UserId)));
        bool sameForAll = paid.Select(p => p.Reward).Distinct().Count() <= 1;

        // Con material todos se llevan lo mismo; con plata depende de la zona de cada uno, así que va una línea por persona.
        string reward = sameForAll && paid.Count > 0
            ? $"Cada uno se llevó **{MiniEventRules.Describe(paid[0].Reward)}**."
            : string.Join('\n', paid.Select(p => $"{MentionUtils.MentionUser(p.UserId)}: **{MiniEventRules.Describe(p.Reward)}**"));

        string tip = MiniEventRules.Counted(joinedCount) >= MiniEventRules.MaxCountedParticipants
            ? "¡Éxito total, llegaron al máximo!"
            : "Con más gente, cada uno se lleva más.";

        return new EmbedBuilder()
            .WithTitle($"{MiniEventRules.Emoji(kind)} ¡Entre {joinedCount} lo juntaron todo!")
            .WithColor(Color.Green)
            .WithDescription($"{who}\n\n{reward}\n\n_{tip}_")
            .Build();
    }
}
