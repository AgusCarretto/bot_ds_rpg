using System.Collections.Concurrent;
using System.Reflection;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

// El aviso de "¡SUBISTE DE NIVEL!" (y de misiones/logros) llegaba recién con el comando SIGUIENTE. Este arnés usa el InteractionService REAL de
// Discord.Net con interacciones falsas: un comando lento que, al final, deja un aviso en la cola (como cualquier victoria que sube de nivel).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

var events = new FakeEvents();
var sp = new ServiceCollection().AddSingleton(events).BuildServiceProvider();
var client = new DiscordSocketClient();
var commands = new InteractionService(client.Rest);
await commands.AddModuleAsync(typeof(SlowModule), sp);

const ulong U = 111, OTHER = 222;
FakeContext Ctx(FakeInteraction i) => new((IDiscordInteraction)(object)i);

Console.WriteLine("== A. La causa: ExecuteCommandAsync vuelve ANTES de que el comando termine (modo asíncrono por defecto) ==");
Check(new InteractionServiceConfig().DefaultRunMode == RunMode.Async, $"el modo por defecto de Discord.Net es {new InteractionServiceConfig().DefaultRunMode}");
{
    events.Reset(); SlowModule.Reset();
    var interaction = FakeInteraction.Slash("slow", U);
    var ctx = Ctx(interaction);
    var result = await commands.ExecuteCommandAsync(ctx, sp);   // lo que hacía Program.cs...
    bool finishedWhenItReturned = SlowModule.Finished.Task.IsCompleted;
    var takenRightAfter = events.TakeNotices(U);                // ...y entregaba los avisos acá, justo después
    Check(result.IsSuccess && !finishedWhenItReturned, $"ExecuteCommandAsync devolvió éxito con el comando TODAVÍA corriendo (terminado: {finishedWhenItReturned})");
    Check(takenRightAfter.Count == 0, "y en ese momento no había ningún aviso para entregar: el viejo flujo no entrega nada");
    await SlowModule.Finished.Task;
    Check(events.TakeNotices(U).Count == 1, "el aviso aparece recién cuando el comando termina: quedaba esperando en la cola hasta el comando siguiente");
}

#if FIX
Console.WriteLine("\n== B. El arreglo: se entrega cuando el comando TERMINA (InteractionExecuted) ==");
NoticeDelivery.Attach(commands, events);

{
    events.Reset(); SlowModule.Reset();
    var interaction = FakeInteraction.Slash("slow", U);
    await commands.ExecuteCommandAsync(Ctx(interaction), sp);
    await SlowModule.Finished.Task;
    var delivered = await interaction.WaitForFollowupsAsync(1, TimeSpan.FromSeconds(3));
    Check(delivered == 1, $"slash: el aviso llega solo, apenas termina el comando (seguimientos: {delivered})");
    Check(interaction.Followups.Count == 1 && interaction.Followups[0].Embed is not null && interaction.Followups[0].Embed!.Title == "🎉 ¡SUBISTE DE NIVEL! 🎉" && interaction.Followups[0].Ephemeral == false,
        "con el embed del festejo y público (no efímero)");
    Check(events.TakeNotices(U).Count == 0, "y se sacó de la cola: no se entrega dos veces");
}

{
    events.Reset(); SlowModule.Reset();
    var interaction = FakeInteraction.Component("btn_slow", U);
    await commands.ExecuteCommandAsync(Ctx(interaction), sp);
    await SlowModule.Finished.Task;
    var delivered = await interaction.WaitForFollowupsAsync(1, TimeSpan.FromSeconds(3));
    Check(delivered == 1, $"botón (así se pelea: Atacar): también llega solo (seguimientos: {delivered})");
}

{
    events.Reset(); SlowModule.Reset();
    var interaction = FakeInteraction.Slash("quiet", U);
    await commands.ExecuteCommandAsync(Ctx(interaction), sp);
    await Task.Delay(500);
    Check(interaction.Followups.Count == 0, "un comando sin avisos no manda nada");
}

{
    events.Reset(); SlowModule.Reset();
    events.Add(OTHER, new GameNotice("de otro", true));
    var interaction = FakeInteraction.Slash("quiet", U);
    await commands.ExecuteCommandAsync(Ctx(interaction), sp);
    await Task.Delay(500);
    Check(interaction.Followups.Count == 0 && events.TakeNotices(OTHER).Count == 1, "los avisos de otro jugador no se entregan en tu comando (siguen en SU cola)");
}

{
    events.Reset(); SlowModule.Reset();
    var interaction = FakeInteraction.Slash("boom", U);
    await commands.ExecuteCommandAsync(Ctx(interaction), sp);
    await Task.Delay(800);
    Check(interaction.Followups.Count == 1, $"si el comando falla DESPUÉS de dejar el aviso, el aviso igual sale (el progreso ya estaba guardado): seguimientos {interaction.Followups.Count}");
}

{
    events.Reset(); SlowModule.Reset();
    var interaction = FakeInteraction.Slash("slow", U, failFollowup: true);
    await commands.ExecuteCommandAsync(Ctx(interaction), sp);
    await SlowModule.Finished.Task;
    await Task.Delay(500);
    Check(true, "si Discord rechaza el seguimiento (token vencido) no se cae nada: se registra y se descarta");
    var next = FakeInteraction.Slash("quiet", U);
    await commands.ExecuteCommandAsync(Ctx(next), sp);
    await Task.Delay(300);
    Check(next.Followups.Count == 0, "y el aviso fallido no queda dando vueltas para el comando siguiente");
}

#endif

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
public sealed class SlowModule(FakeEvents events) : InteractionModuleBase<IInteractionContext>
{
    public static TaskCompletionSource Finished = new();
    public static void Reset() => Finished = new TaskCompletionSource();

    private static GameNotice LevelUp() => new("", true, new EmbedBuilder().WithTitle("🎉 ¡SUBISTE DE NIVEL! 🎉").Build());

    [SlashCommand("slow", "comando lento que deja un aviso al final")]
    public async Task SlowAsync() => await WorkAsync();

    [ComponentInteraction("btn_slow")]
    public async Task ButtonAsync() => await WorkAsync();

    [SlashCommand("quiet", "no deja nada")]
    public Task QuietAsync() => Task.CompletedTask;

    [SlashCommand("boom", "deja un aviso y después falla")]
    public async Task BoomAsync()
    {
        await Task.Delay(100);
        events.Add(Context.User.Id, LevelUp());
        throw new InvalidOperationException("falla de prueba (esperada)");
    }

    private async Task WorkAsync()
    {
        await Task.Delay(300);                       // la pelea, las escrituras en la base, la respuesta a Discord...
        events.Add(Context.User.Id, LevelUp());      // ...y recién al final se registra el evento que sube de nivel
        Finished.TrySetResult();
    }
}

public sealed class FakeEvents : IGameEvents
{
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<GameNotice>> _q = new();
    public void Reset() => _q.Clear();
    public void Add(ulong id, GameNotice n) => _q.GetOrAdd(id, _ => new()).Enqueue(n);
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) => Task.CompletedTask;
    public IReadOnlyList<GameNotice> TakeNotices(ulong id)
    {
        var l = new List<GameNotice>();
        if (_q.TryGetValue(id, out var q)) while (q.TryDequeue(out var n)) l.Add(n);
        return l;
    }
}

public sealed class FakeContext(IDiscordInteraction interaction) : IInteractionContext
{
    public IDiscordClient Client => null!;
    public IGuild Guild => null!;
    public IMessageChannel Channel => null!;
    public IUser User => interaction.User;
    public IDiscordInteraction Interaction => interaction;
}

public record Followup(string? Text, Embed? Embed, bool Ephemeral);

// Una interacción falsa: un DispatchProxy que contesta lo justo para que el InteractionService la enrute y registra los seguimientos.
public class FakeInteraction : DispatchProxy
{
    public List<Followup> Followups { get; } = [];
    private Func<MethodInfo, object?[], object?> _handler = null!;
    private bool _failFollowup;

    public static FakeInteraction Slash(string name, ulong userId, bool failFollowup = false) =>
        Make<ISlashCommandInteraction>(userId, InteractionType.ApplicationCommand, name, failFollowup);

    public static FakeInteraction Component(string customId, ulong userId) =>
        Make<IComponentInteraction>(userId, InteractionType.MessageComponent, customId, false);

    private static FakeInteraction Make<TInterface>(ulong userId, InteractionType type, string key, bool failFollowup)
    {
        var proxy = (TInterface)DispatchProxy.Create<TInterface, FakeInteraction>()!;
        var self = (FakeInteraction)(object)proxy;
        self._failFollowup = failFollowup;
        var user = Fake.Of<IUser>((n, a) => n == "get_Id" ? userId : null);
        object data = typeof(TInterface) == typeof(ISlashCommandInteraction)
            ? Fake.Of<IApplicationCommandInteractionData>((n, a) => n switch
            {
                "get_Name" => key,
                "get_Options" => Array.Empty<IApplicationCommandInteractionDataOption>(),
                _ => null
            })
            : Fake.Of<IComponentInteractionData>((n, a) => n switch
            {
                "get_CustomId" => key,
                "get_Type" => ComponentType.Button,
                "get_Values" => Array.Empty<string>(),
                _ => null
            });
        self._handler = (m, a) =>
        {
            switch (m.Name)
            {
                case "get_Type": return type;
                case "get_User": return user;
                case "get_Data": return data;
                case "FollowupAsync":
                    if (self._failFollowup) throw new InvalidOperationException("token vencido (simulado)");
                    // FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, ..., MessageComponent components, Embed embed, ...)
                    string? text = a.FirstOrDefault() as string;
                    var embed = a.OfType<Embed>().FirstOrDefault();
                    var parameters = m.GetParameters();
                    bool ephemeral = false;
                    for (int i = 0; i < parameters.Length; i++) if (parameters[i].Name == "ephemeral") ephemeral = (bool)a[i]!;
                    lock (self.Followups) self.Followups.Add(new Followup(text, embed, ephemeral));
                    return null;
                default: return null;
            }
        };
        return self;
    }

    public async Task<int> WaitForFollowupsAsync(int expected, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until) { lock (Followups) if (Followups.Count >= expected) return Followups.Count; await Task.Delay(25); }
        lock (Followups) return Followups.Count;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        object? r = _handler(targetMethod!, args ?? []);
        return Fake.Shape(targetMethod!, r);
    }
}

public class Fake : DispatchProxy
{
    private Func<string, object?[], object?> _h = null!;
    public static T Of<T>(Func<string, object?[], object?> h) where T : class
    {
        var p = DispatchProxy.Create<T, Fake>();
        ((Fake)(object)p)._h = h;
        return p;
    }

    public static object? Shape(MethodInfo m, object? r)
    {
        var rt = m.ReturnType;
        if (r is not null) return rt == typeof(Task) ? Task.CompletedTask : r;
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(rt.GetGenericArguments()[0]).Invoke(null, [rt.GetGenericArguments()[0].IsValueType ? Activator.CreateInstance(rt.GetGenericArguments()[0]) : null]);
        return rt.IsValueType && rt != typeof(void) ? Activator.CreateInstance(rt) : null;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? a) => Shape(m!, _h(m!.Name, a ?? []));
}
