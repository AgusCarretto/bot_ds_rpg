using System.Globalization;
using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Etapa 1: eventos + contadores + give. Contra la base REAL con usuarios descartables (se borran al final).
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var transfers = new TransferRepository(factory);
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong A = 999000000000001001UL, B = 999000000000001002UL, C = 999000000000001003UL, GHOST = 999000000000001999UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids); DELETE FROM game_events WHERE discord_id = ANY(@ids);", new { ids = new long[] { (long)A, (long)B, (long)C, (long)GHOST } });
async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
async Task SetGold(ulong id, int g) => await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)id, g });

Console.WriteLine("== 0. Registro de módulos y dependencias (como arranca el bot) ==");
{
    var client = new DiscordSocketClient();
    var interactions = new InteractionService(client.Rest);
    var texts = new CommandService();
    texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
    var sp = (ServiceProvider)typeof(GiveModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [client, interactions, cfg])!;
    try
    {
        var m = await interactions.AddModulesAsync(typeof(GiveModule).Assembly, sp);
        var t = await texts.AddModulesAsync(typeof(GiveModule).Assembly, sp);
        Check(m.Any(x => x.SlashCommands.Any(c => c.Name == "give")), $"/give registrado ({interactions.SlashCommands.Count} comandos de barra en {m.Count()} módulos)");
        Check(t.SelectMany(x => x.Commands).Count(c => c.Name == "give") == 2, "aa give registrado con sus dos órdenes (jugador cantidad / cantidad jugador)");
        Check(t.SelectMany(x => x.Commands).Any(c => c.Aliases.Contains("dar")), "alias 'aa dar'");
        // Todos los módulos se pueden construir con la inyección de dependencias real.
        var broken = new List<string>();
        var moduleTypes = typeof(GiveModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))).ToList();
        foreach (var type in moduleTypes)
        {
            try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
        }

        Check(broken.Count == 0, $"los {moduleTypes.Count} módulos se construyen con el DI real ({(broken.Count == 0 ? "todos" : string.Join(" | ", broken))})");
    }
    catch (Exception ex) { Check(false, "registro de módulos: " + ex.Message); }
}

Console.WriteLine("\n== 1. Eventos y contadores ==");
await Cleanup();
await users.GetOrCreateUserAsync(A); await users.GetOrCreateUserAsync(B); await users.GetOrCreateUserAsync(C);
long v1 = await eventsRepo.RecordAsync(A, GameEventKinds.HuntWin, 1, 1, null);
long v2 = await eventsRepo.RecordAsync(A, GameEventKinds.HuntWin, 1, 1, null);
long v3 = await eventsRepo.RecordAsync(A, GameEventKinds.GatheredUnits, null, 4, "Madera de Pino");
Check(v1 == 1 && v2 == 2 && v3 == 4, $"el contador sube y devuelve el valor nuevo ({v1}, {v2}, {v3})");
var stats = await eventsRepo.GetStatsAsync(A);
Check(stats["hunt_win"] == 2 && stats["gathered_units"] == 4 && stats.Count == 2, $"GetStatsAsync: {string.Join(", ", stats.Select(k => $"{k.Key}={k.Value}"))}");
long rows = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM game_events WHERE discord_id = @id", new { id = (long)A });
Check(rows == 3, $"cada evento queda en game_events ({rows} filas)");
var service = new GameEventService(eventsRepo);
await service.RecordAsync(GHOST, GameEventKinds.HuntWin); // jugador que no existe: la FK falla
long ghostRows = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM game_events WHERE discord_id = @id", new { id = (long)GHOST });
Check(ghostRows == 0, "registrar para un jugador inexistente NO tira excepción y no deja el evento a medias (transacción)");
await service.RecordAsync(A, GameEventKinds.HuntWin);
Check((await eventsRepo.GetStatsAsync(A))["hunt_win"] == 3, "el servicio registra normal");
await service.RecordVictoryAsync(B, GameEventKinds.TravelWin, new LevelUpOutcome(new User { DiscordId = (long)B, CurrentZoneId = 1, Level = 6 }, 2));
var sb = await eventsRepo.GetStatsAsync(B);
Check(sb["travel_win"] == 1 && sb["level_up"] == 2, "RecordVictoryAsync registra la victoria y el level_up (2 niveles)");
string? lvlDetail = await db.ExecuteScalarAsync<string?>("SELECT detail FROM game_events WHERE discord_id = @id AND kind = 'level_up'", new { id = (long)B });
Check(lvlDetail == "6", $"el level_up guarda el nivel nuevo ({lvlDetail})");
await GatheringEvents.RecordAsync(service, C, CooldownCatalog.Mine, new Item { Name = "Hierro" }, 3);
var sc = await eventsRepo.GetStatsAsync(C);
Check(sc["mine"] == 1 && sc["gathered_units"] == 3, "recolección: 1 /mine y 3 unidades");
Check(service.TakeNotices(A).Count == 0, "sin misiones ni logros todavía: no hay avisos");

Console.WriteLine("\n== 2. Transferencias de oro ==");
await SetGold(A, 100); await SetGold(B, 50);
var ok = await transfers.TransferGoldAsync(A, B, 30);
Check(ok.Status == TransferStatus.Ok && ok.SenderGold == 70 && ok.RecipientGold == 80 && await Gold(A) == 70 && await Gold(B) == 80, $"A->B 30: {ok.Status}, A={ok.SenderGold} B={ok.RecipientGold}");
Check((await transfers.TransferGoldAsync(A, B, 71)).Status == TransferStatus.InsufficientGold && await Gold(A) == 70, "más de lo que tiene: rechazado y no cambia nada");
Check((await transfers.TransferGoldAsync(A, A, 5)).Status == TransferStatus.SameUser, "a uno mismo: rechazado");
Check((await transfers.TransferGoldAsync(A, B, 0)).Status == TransferStatus.InvalidAmount && (await transfers.TransferGoldAsync(A, B, -5)).Status == TransferStatus.InvalidAmount, "cantidad 0 o negativa: rechazada");
Check((await transfers.TransferGoldAsync(A, GHOST, 5)).Status == TransferStatus.RecipientMissing && await Gold(A) == 70, "destinatario sin cuenta: rechazado y no se pierde oro");
Check((await transfers.TransferGoldAsync(GHOST, A, 5)).Status == TransferStatus.SenderMissing, "remitente sin cuenta: rechazado");
Check((await transfers.TransferGoldAsync(A, B, 70)).Status == TransferStatus.Ok && await Gold(A) == 0, "puede dar TODO su oro (queda en 0)");

// Concurrencia 1: 40 transferencias simultáneas de 10 con saldo 100 -> exactamente 10 pasan.
await SetGold(A, 100); await SetGold(B, 0);
var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => transfers.TransferGoldAsync(A, B, 10))));
int accepted = results.Count(r => r.Status == TransferStatus.Ok);
Check(accepted == 10 && await Gold(A) == 0 && await Gold(B) == 100, $"40 intentos simultáneos con 100 de oro: pasaron {accepted} (esperado 10), A={await Gold(A)}, B={await Gold(B)}");

// Concurrencia 2: cruzadas A<->B 200 veces: sin deadlock y el oro total se conserva.
await SetGold(A, 500); await SetGold(B, 500);
var sw = System.Diagnostics.Stopwatch.StartNew();
var cross = Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => i % 2 == 0 ? transfers.TransferGoldAsync(A, B, 7) : transfers.TransferGoldAsync(B, A, 5))));
bool finished = await Task.WhenAny(cross, Task.Delay(TimeSpan.FromSeconds(60))) == cross;
Check(finished && await Gold(A) + await Gold(B) == 1000, $"200 transferencias cruzadas A<->B: {(finished ? "sin deadlock" : "TRABADO")} en {sw.ElapsedMilliseconds} ms, oro total {await Gold(A) + await Gold(B)} (esperado 1000)");

Console.WriteLine("\n== 3. El comando give ==");
await SetGold(A, 200); await SetGold(B, 10);
var give = new GiveRecorder();
GiveModule.GiveResult r1 = await GiveModule.ExecuteGiveAsync(users, transfers, service, A, FakeUser.Create(B, false), 50);
Check(r1.Embed is not null && r1.Embed.Description.Contains("50") && await Gold(A) == 150 && await Gold(B) == 60, $"/give 50: {r1.Embed?.Title}; A={await Gold(A)} B={await Gold(B)}");
var sa = await eventsRepo.GetStatsAsync(A); var sb2 = await eventsRepo.GetStatsAsync(B);
Check(sa["gold_given"] == 50 && sb2["gold_received"] == 50, "queda registrado: A dio 50, B recibió 50 (gold_given / gold_received)");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, service, A, FakeUser.Create(A, false), 5)).PlainMessage!.Contains("vos mismo"), "no podés darte a vos mismo");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, service, A, FakeUser.Create(B, true), 5)).PlainMessage!.Contains("bots"), "no se le da a un bot");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, service, A, FakeUser.Create(GHOST, false), 5)).PlainMessage!.Contains("/start"), "a alguien sin cuenta: le dice que use /start");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, service, A, FakeUser.Create(B, false), 100000)).PlainMessage!.Contains("No te alcanza"), "sin saldo: 'No te alcanza' con tu saldo");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, service, A, FakeUser.Create(B, false), 0)).PlainMessage!.Contains("al menos 1"), "cantidad 0: rechazada");
Check(await Gold(A) == 150 && await Gold(B) == 60, "los rechazos no movieron oro");

await Cleanup();
Console.WriteLine($"\nLimpieza: usuarios de prueba restantes = {await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)A, (long)B, (long)C } })}");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class GiveRecorder { }

// IUser falso: solo lo que usa GiveModule (Id, IsBot, Mention).
class FakeUser : DispatchProxy
{
    public ulong UserId; public bool Bot;
    public static IUser Create(ulong id, bool isBot) { var p = Create<IUser, FakeUser>(); var f = (FakeUser)(object)p; f.UserId = id; f.Bot = isBot; return p; }
    protected override object? Invoke(MethodInfo? m, object?[]? args) => m!.Name switch
    {
        "get_Id" => UserId,
        "get_IsBot" => Bot,
        "get_Mention" => $"<@{UserId}>",
        "get_Username" => $"user{UserId}",
        _ => m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null,
    };
}
