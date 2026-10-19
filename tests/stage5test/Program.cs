using System.Globalization;
using System.Reflection;
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

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var transfers = sp.GetRequiredService<ITransferRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const ulong A = 999000000000006001UL, B = 999000000000006002UL, C = 999000000000006003UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id IN (@a, @b, @c)", new { a = (long)A, b = (long)B, c = (long)C });
async Task Set(ulong id, string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)id, n = name, q = qty });
async Task<int> Have(ulong id, string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<int> Id(string name) => (await items.GetByNameAsync(name))!.ItemId;
async Task Reset() { await Cleanup(); foreach (var u in new[] { A, B }) await users.GetOrCreateUserAsync(u); }
object Fake(Type t, Func<string, object?[], object?> h) => FakeProxy.Create(t, h);
IUser FakeUser(ulong id, bool bot = false) => (IUser)Fake(typeof(IUser), (n, a) => n switch { "get_Id" => id, "get_IsBot" => bot, "get_Mention" => $"<@{id}>", "get_Username" => "tester", _ => throw new NotSupportedException(n) });

Console.WriteLine("== 1. Comandos en inglés registrados ==");
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var slash = interactions.SlashCommands.Select(c => c.Name).ToHashSet();
Check(new[] { "open", "missions", "achievements", "trade" }.All(slash.Contains) && !new[] { "abrir", "misiones", "logros" }.Any(slash.Contains), $"/open /missions /achievements /trade registrados; /abrir /misiones /logros ya no ({slash.Count} comandos)");
Check(interactions.ComponentCommands.Any(c => c.Name.StartsWith("trade_accept")) && interactions.ComponentCommands.Any(c => c.Name.StartsWith("trade_decline")), "botones Aceptar / Rechazar del tradeo registrados");
var tcmds = texts.Commands.ToList();
Check(new[] { "open", "missions", "achievements", "trade" }.All(n => tcmds.Any(c => c.Name == n)) && tcmds.Any(c => c.Name == "open" && c.Aliases.Contains("abrir")) && tcmds.Any(c => c.Name == "missions" && c.Aliases.Contains("misiones")) && tcmds.Any(c => c.Name == "achievements" && c.Aliases.Contains("logros")), "aa open / missions / achievements / trade (y los nombres viejos siguen como alias)");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
Check(MissionCatalog.WeeklyPool.Any(m => m.Key == "w_gather300" && m.Target == 300) && !MissionCatalog.WeeklyPool.Any(m => m.Key == "w_gather60") && MissionCatalog.WeeklyPool.Where(m => m.Kind == GameEventKinds.GatheredUnits).All(m => m.Target >= 300), "la misión semanal de materiales ahora pide 300 (y la otra 500)");
Check(MissionCatalog.DailyPool.Concat(MissionCatalog.WeeklyPool).All(m => !m.Title.Contains("/abrir")) && AchievementCatalog.All.All(a => !a.Description.Contains("/abrir")), "los textos de misiones y logros nombran /open");

Console.WriteLine("\n== 2. Reglas del tradeo (ítems reales) ==");
async Task<string?> Rule(string give, string get) => TradeRules.Validate((await items.GetByNameAsync(give))!, (await items.GetByNameAsync(get))!);
Check(await Rule("Madera de Roble", "Hierro") is null && await Rule("Hierro", "Carbón") is null && await Rule("Madera de Pino", "Piedra") is null && await Rule("Madera de Nogal", "Oro Puro") is null && await Rule("Madera de Ébano", "Gema de Zafiro") is null && await Rule("Corteza del Árbol de Vida", "Fragmento de Meteorito") is null, "se puede cambiar dentro de cada rareza: Roble-Hierro-Carbón, Pino-Piedra, Nogal-Oro Puro, Ébano-Zafiro, Corteza-Meteorito");
Check(await Rule("Madera de Roble", "Madera de Pino") is { } m1 && m1.Contains("misma rareza") && await Rule("Hierro", "Oro Puro") is not null, "rarezas distintas: no (Raro por Común, Raro por Épico)");
Check(await Rule("Hierro", "Hierro") is not null && await Rule("Mate Amargo", "Hierro") is not null && await Rule("Cajón de Pino", "Cajón de Pino") is not null && await Rule("Garra de Puma Cenizo", "Hierro") is not null, "lo mismo por lo mismo, comida, cajas y drops de monstruo: no (solo recolección)");

Console.WriteLine("\n== 3. Intercambio atómico en la base ==");
await Reset();
await Set(A, "Madera de Roble", 2); await Set(B, "Hierro", 1);
var rid = await Id("Madera de Roble"); var hid = await Id("Hierro");
Check(await transfers.SwapItemsAsync(A, rid, B, hid) == SwapStatus.Ok && await Have(A, "Madera de Roble") == 1 && await Have(A, "Hierro") == 1 && await Have(B, "Madera de Roble") == 1 && await Have(B, "Hierro") == 0, "A da 1 Roble y B da 1 Hierro: cada uno queda con lo suyo cambiado (y a B se le borra la fila en 0)");
var second = await transfers.SwapItemsAsync(A, rid, B, hid);
Check(second == SwapStatus.SecondMissing && await Have(A, "Madera de Roble") == 1 && await Have(A, "Hierro") == 1 && await Have(B, "Madera de Roble") == 1, "B ya no tiene Hierro: SecondMissing y no se mueve NADA");
await Set(A, "Madera de Roble", 0);
Check(await transfers.SwapItemsAsync(A, rid, B, await Id("Carbón")) == SwapStatus.FirstMissing && await Have(B, "Madera de Roble") == 1, "A sin Roble: FirstMissing y no se mueve nada");
await Reset(); await Set(A, "Madera de Roble", 1); await Set(B, "Hierro", 20);
var race = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
{
    try { return await transfers.SwapItemsAsync(A, rid, B, hid); }
    catch (Exception ex) { Console.WriteLine("    excepción: " + ex.Message.Split((char)10)[0]); return (SwapStatus)99; }
})));
Check(race.Count(s => s == SwapStatus.Ok) == 1 && race.Count(s => s == SwapStatus.FirstMissing) == 19 && await Have(A, "Hierro") == 1 && await Have(B, "Hierro") == 19 && await Have(B, "Madera de Roble") == 1, "20 cambios a la vez con UN solo Roble: se hace uno, y los materiales se conservan (A: 1 Hierro, B: 19 Hierro + 1 Roble)");
await Reset(); await Set(A, "Madera de Roble", 5); await Set(A, "Hierro", 5); await Set(B, "Madera de Roble", 5); await Set(B, "Hierro", 5);
var cross = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
{
    try { return i % 2 == 0 ? await transfers.SwapItemsAsync(A, rid, B, hid) : await transfers.SwapItemsAsync(B, rid, A, hid); }
    catch (Exception ex) { Console.WriteLine("    excepción: " + ex.Message.Split((char)10)[0]); return (SwapStatus)99; }
})));
int total = await Have(A, "Madera de Roble") + await Have(B, "Madera de Roble"), totalH = await Have(A, "Hierro") + await Have(B, "Hierro");
Check(cross.All(s => s != (SwapStatus)99) && total == 10 && totalH == 10, $"40 cambios CRUZADOS a la vez (A a B y B a A): sin excepciones ni trabas y se conserva todo (Roble {total}/10, Hierro {totalH}/10; {cross.Count(s => s == SwapStatus.Ok)} hechos)");

Console.WriteLine("\n== 4. Propuestas ==");
var offers = new TradeOfferService();
var roble = (await items.GetByNameAsync("Madera de Roble"))!; var hierro = (await items.GetByNameAsync("Hierro"))!;
var o1 = offers.Create(A, B, roble, hierro);
var taken = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => offers.TryTake(o1.Id))));
Check(taken.Count(t => t is not null) == 1 && offers.Peek(o1.Id) is null, "20 clicks a la vez en Aceptar: la propuesta la recibe UNO solo");
var o2 = offers.Create(A, B, roble, hierro); var o3 = offers.Create(A, C, roble, hierro);
Check(offers.Peek(o2.Id) is null && offers.Peek(o3.Id) is not null, "una propuesta nueva del mismo jugador reemplaza a la anterior");
await Reset(); await Set(A, "Madera de Roble", 3); await Set(B, "Hierro", 3);
async Task<TradeOfferResult> Offer(IUser to, string give, string get, ulong from = A) => await TradeModule.ExecuteOfferAsync(users, items, inventory, new TradeOfferService(), combat, from, to, give, get);
var ok = await Offer(FakeUser(B), "madera de roble", "HIERRO");
var btns = ok.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(ok.PlainMessage is null && ok.Content == $"<@{B}>" && ok.Embed!.Description.Contains("1×") && ok.Embed.Description.Contains("Raro") && btns!.Count == 2 && btns[0].CustomId!.StartsWith("trade_accept:") && btns[1].CustomId!.StartsWith("trade_decline:"), $"propuesta válida (escrita en minúsculas/MAYÚSCULAS): menciona al otro y trae Aceptar y Rechazar. Texto: «{ok.Embed?.Description}»");
Check((await Offer(FakeUser(A), "Madera de Roble", "Hierro")).PlainMessage!.Contains("vos mismo") && (await Offer(FakeUser(B, bot: true), "Madera de Roble", "Hierro")).PlainMessage!.Contains("bots") && (await Offer(FakeUser(C), "Madera de Roble", "Hierro")).PlainMessage!.Contains("todavía no empezó"), "contra uno mismo, un bot o alguien sin cuenta: se rechaza");
Check((await Offer(FakeUser(B), "Madera de Roble", "Madera de Pino")).PlainMessage!.Contains("misma rareza") && (await Offer(FakeUser(B), "Madera de Roble", "Mate Amargo")).PlainMessage!.Contains("materiales") && (await Offer(FakeUser(B), "Roble inventado", "Hierro")).PlainMessage!.Contains("No encontré"), "otra rareza, algo que no es material o un nombre inexistente: se rechaza con el motivo");
Check((await Offer(FakeUser(B), "Piedra", "Madera de Pino")).PlainMessage!.Contains("No tenés") && (await Offer(FakeUser(B), "Madera de Roble", "Carbón")).PlainMessage!.Contains("no tiene"), "si vos no tenés lo que das, o el otro no tiene lo que pedís: se rechaza antes de molestarlo");
var list = TradeChoices.ForGive(await inventory.GetByDiscordIdAsync(A), "");
Check(list.Count == 1 && list[0].Value.ToString() == "Madera de Roble" && list[0].Name.Contains("tenés 3") && TradeChoices.ForGet(await items.GetAllByTypeAsync("Mineral"), "hier").Count == 1, "las listas desplegables de give (lo que tenés) y get (todos los materiales con su rareza) funcionan");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

public class FakeProxy : DispatchProxy
{
    private Func<string, object?[], object?> _h = null!;
    public static object Create(Type iface, Func<string, object?[], object?> h)
    {
        var create = typeof(DispatchProxy).GetMethod("Create", 2, [])!.MakeGenericMethod(iface, typeof(FakeProxy));
        var proxy = create.Invoke(null, null)!;
        ((FakeProxy)proxy)._h = h;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? a) => _h(m!.Name, a ?? []);
}
