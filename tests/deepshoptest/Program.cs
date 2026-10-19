using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.17.0: el Tabernero del Inframundo (/deepshop, aa deepshop). Reglas puras (qué vende, a cuánto, cuándo se conoce), la compra atómica con Polvo contra la base real (usuario descartable),
// la escena, los diálogos, los comandos con el InteractionService real, la ayuda y la DI.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
IEnumerable<IMessageComponent> Flat(MessageComponent c) => c.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components);
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var dust = new DustRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000040000UL, U2 = 999000000000040001UL;
var eventsRepo = new GameEventRepository(factory);
var rec = new GameEventService(eventsRepo);

async Task Reset(ulong id = U, int dustAmount = 0, int highest = 0, int fn = 0)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET dust = @d, highest_zone_cleared = @h, fuego_nuevo = @f WHERE discord_id = @id", new { id = (long)id, d = dustAmount, h = highest, f = fn });
}
async Task<int> Have(string name, ulong id = U) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<int> DustOf(ulong id = U) => await db.ExecuteScalarAsync<int>("SELECT dust FROM users WHERE discord_id = @id", new { id = (long)id });
async Task<long> Stat(string kind, ulong id = U) => (await eventsRepo.GetStatsAsync(id)).GetValueOrDefault(kind);

var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
int lastZoneId = zones[^1].ZoneId;
Task<DeepShopResult> Buy(string text, int qty = 1, ulong id = U) => DeepShopModule.ExecuteBuyAsync(users, zonesRepo, items, dust, rec, id, text, qty, new Random(1));

Console.WriteLine("== 1. Reglas puras ==");
Check(DeepShop.PricePerUnit == 3000 && DeepShop.Cost(1) == 3000 && DeepShop.Cost(4) == 12000 && DeepShop.MaxPerPurchase == 10, "cada uno cuesta 3.000 de Polvo (el número del dueño) y se compran de 1 a 10 por vez");
Check(DeepShop.Stock.Select(s => s.ItemName).SequenceEqual(["Corteza del Árbol de Vida", "Fragmento de Meteorito"]) && DeepShop.Stock.All(s => s.UseLine.Contains("/forge-special")), "vende los dos Míticos de recolección, y cada uno dice para qué sirve");
Check(DeepShop.PricePerUnit > Dismantling.DustPerUnit("Mítico") && Dismantling.DustPerUnit("Mítico") == 500, "desmantelar un Mítico da 500 y comprarlo cuesta 3.000: no se puede desmantelar-comprar en círculo");
var allNames = new[] { "corteza", "Corteza", "CORTEZA", "árbol", "arbol", "Arbol de Vida", "madera", "Corteza del Árbol de Vida", "corteza del arbol de vida" };
Check(allNames.All(n => DeepShop.Find(n)?.Key == "corteza"), "la Corteza se nombra de varias formas (clave, árbol, nombre completo; sin importar mayúsculas ni tildes)");
var allMet = new[] { "meteorito", "Meteoro", "METEORITO", "fragmento", "Fragmento de Meteorito", "fragmento de meteorito" };
Check(allMet.All(n => DeepShop.Find(n)?.Key == "meteorito"), "el Meteorito también");
Check(DeepShop.Find("espada") is null && DeepShop.Find("") is null && DeepShop.Find("   ") is null && DeepShop.Find(null) is null, "algo que no vende (o nada) no se entiende");

Console.WriteLine("\n== 2. Cuándo se conoce el vendedor ==");
var fresh = new User { HighestZoneCleared = 0, FuegoNuevo = 0 };
Check(!DeepShop.IsOpen(zones, fresh) && !DeepShop.IsOpen(zones, new User { HighestZoneCleared = zones[0].ZoneId }) && !DeepShop.IsOpen(zones, new User { HighestZoneCleared = zones[^2].ZoneId }), "antes de vencer al jefe de la última zona no se conoce (ni con la anteúltima vencida)");
Check(DeepShop.IsOpen(zones, new User { HighestZoneCleared = lastZoneId }), "al vencer al jefe de la última zona se conoce");
Check(DeepShop.IsOpen(zones, new User { HighestZoneCleared = 0, FuegoNuevo = 1 }) && DeepShop.IsOpen(zones, new User { HighestZoneCleared = 0, FuegoNuevo = 7 }), "con un Fuego Nuevo hecho ya lo conocés para siempre (el Fuego Nuevo vuelve a cerrar la puerta, no el recuerdo)");
Check(DeepShop.IsOpen(zones, new User { HighestZoneCleared = lastZoneId }) == FogonRules.IsGateOpen(zones, lastZoneId), "es la misma regla que abre El Fogón (/zona 0)");

Console.WriteLine("\n== 3. Diálogos ==");
foreach (var line in Enum.GetValues<DeepShopkeeperLine>())
{
    var seen = new HashSet<string>();
    for (int i = 0; i < 60; i++) seen.Add(NpcDialogue.DeepShopkeeper(line, new Random(i)));
    Check(seen.Count >= 2 && seen.All(t => t.StartsWith("🕯️ **El Tabernero del Inframundo:** «") && t.EndsWith("»")), $"{line}: al menos 2 frases distintas, todas con el formato de siempre ({seen.Count})");
}

Console.WriteLine("\n== 4. La compra, contra la base ==");
await Reset(dustAmount: 10000, highest: 0);
var closed = await Buy("corteza");
Check(closed.PlainMessage == DeepShopModule.NotOpenMessage && closed.Embed is null && await DustOf() == 10000 && await Have("Corteza del Árbol de Vida") == 0, "sin conocerlo: dice que no sabés de qué hablan y NO cobra");
await Reset(dustAmount: 10000, highest: lastZoneId);
var noAcc = await Buy("corteza", 1, 999000000000049999UL);
Check(noAcc.PlainMessage!.Contains("/start"), "sin cuenta: pide /start");
var unknown = await Buy("espada");
Check(unknown.PlainMessage is not null && unknown.PlainMessage.Contains("Corteza") && await DustOf() == 10000, "algo que no vende: lo dice y no cobra");
var q0 = await Buy("corteza", 0); var q11 = await Buy("corteza", 11);
Check(q0.PlainMessage!.Contains("1 a 10") && q11.PlainMessage!.Contains("1 a 10") && await DustOf() == 10000, "0 u 11 se rechazan sin cobrar");
var b1 = await Buy("corteza");
Check(b1.Embed is not null && await DustOf() == 7000 && await Have("Corteza del Árbol de Vida") == 1 && AllText(b1.Embed).Contains("7.000") && AllText(b1.Embed).Contains("Comprás **1×**") && AllText(b1.Embed).Contains("3.000"),
    $"comprar 1 Corteza: -3.000 de Polvo, +1 en la mochila y la respuesta dice cuánto te queda (7.000)");
var b2 = await Buy("Meteoro", 2);
Check(b2.Embed is not null && await DustOf() == 1000 && await Have("Fragmento de Meteorito") == 2, "comprar 2 Meteoritos (por una palabra suelta): -6.000 y +2");
Check(b2.Embed!.Thumbnail is null || b2.Embed.Thumbnail?.Url?.Contains("cdn.discordapp.com") == true, "lleva la miniatura del ítem si tiene emoji");
var poor = await Buy("meteorito");
Check(poor.PlainMessage is not null && poor.PlainMessage.Contains("3.000") && poor.PlainMessage.Contains("1.000") && await DustOf() == 1000 && await Have("Fragmento de Meteorito") == 2, "con 1.000 de Polvo no alcanza: dice cuánto cuesta y cuánto tenés, y no toca nada");
await db.ExecuteAsync("UPDATE users SET dust = 3000 WHERE discord_id = @id", new { id = (long)U });
var exact = await Buy("corteza");
Check(exact.Embed is not null && await DustOf() == 0 && await Have("Corteza del Árbol de Vida") == 2, "con el Polvo justo (3.000) alcanza y queda en 0");
Check(await Stat("deepshop_buy") == 4, $"los eventos suman las unidades compradas (1 + 2 + 1 = 4): {await Stat("deepshop_buy")}");
// concurrencia
await db.ExecuteAsync("UPDATE users SET dust = 3000 WHERE discord_id = @id", new { id = (long)U });
int before = await Have("Corteza del Árbol de Vida");
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Buy("corteza")));
Check(race.Count(r => r.Embed is not null) == 1 && race.Count(r => r.PlainMessage is not null) == 9 && await DustOf() == 0 && await Have("Corteza del Árbol de Vida") == before + 1, "10 compras a la vez con Polvo para UNA: se paga una sola, el Polvo nunca queda negativo");
// el Fuego Nuevo cuenta
await Reset(dustAmount: 3000, highest: 0, fn: 1);
Check((await Buy("meteorito")).Embed is not null && await Have("Fragmento de Meteorito") == 1, "con un Fuego Nuevo hecho se compra aunque la puerta esté cerrada de nuevo");

Console.WriteLine("\n== 5. La escena ==");
await Reset(dustAmount: 12345, highest: lastZoneId);
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 3 FROM items WHERE name = 'Corteza del Árbol de Vida' ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = 3", new { id = (long)U });
var scene = await DeepShopModule.BuildSceneAsync(users, zonesRepo, dust, items, U, null, new Random(2));
string st = AllText(scene.Embed!);
Check(scene.Embed is not null && scene.PlainMessage is null && st.Contains("El Tabernero del Inframundo") && st.Contains("12.345") && st.Contains("3.000 de Polvo") && st.Contains("Corteza del Árbol de Vida") && st.Contains("Fragmento de Meteorito") && st.Contains("Tenés **3**") && st.Contains("/forge-special"),
    "la carta: el saludo, tu Polvo (12.345), los dos con su precio, cuántos tenés y para qué sirven");
var menu = Flat(scene.Components!).OfType<SelectMenuComponent>().Single();
Check(menu.CustomId == $"deepshop_buy:{U}" && menu.MinValues == 1 && menu.MaxValues == 1 && menu.Options.Select(o => o.Value).SequenceEqual(["corteza", "meteorito"]) && menu.Options.All(o => o.Emote is null && o.Label.Length <= 100 && o.Description.Length <= 100), "la lista lleva al dueño en el id, compra de a una y sus opciones no llevan emoji");
Check(scene.Embed!.Length <= 6000 && scene.AttachmentPath is null, $"entra en Discord ({scene.Embed.Length}) y sin foto no adjunta nada");
var scene2 = await DeepShopModule.BuildSceneAsync(users, zonesRepo, dust, items, U, "🕯️ frase mía", new Random(2));
Check(AllText(scene2.Embed!).Contains("frase mía"), "se le puede pasar la frase (la respuesta del tabernero)");
await Reset(dustAmount: 5, highest: 0);
var closedScene = await DeepShopModule.BuildSceneAsync(users, zonesRepo, dust, items, U, null);
Check(closedScene.Embed is null && closedScene.Components is null && closedScene.PlainMessage == DeepShopModule.NotOpenMessage, "sin conocerlo: solo el mensaje de «no sabés de qué hablan»");
var noAccScene = await DeepShopModule.BuildSceneAsync(users, zonesRepo, dust, items, 999000000000049999UL, null);
Check(noAccScene.Embed is null && noAccScene.PlainMessage!.Contains("/start"), "sin cuenta: pide /start");

Console.WriteLine("\n== 6. Comandos, ayuda y DI con el InteractionService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var cmd = interactions.SlashCommands.Single(c => c.Name == "deepshop");
var itemParam = cmd.Parameters.Single(p => p.Name == "item"); var qtyParam = cmd.Parameters.Single(p => p.Name == "cantidad");
Check(cmd.Module.SlashGroupName is null && validName(cmd.Name) && cmd.Description.Length is >= 1 and <= 100 && cmd.Parameters.Count == 2 && !itemParam.IsRequired && itemParam.Choices.Select(c => c.Value.ToString()).SequenceEqual(["corteza", "meteorito"]) && qtyParam.MinValue == 1 && qtyParam.MaxValue == DeepShop.MaxPerPurchase,
    "/deepshop existe, sin grupo, con item (Corteza o Meteorito, opcional) y cantidad de 1 a 10, dentro de los límites de Discord");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"deepshop_buy:{U}"));
Check(routed.IsSuccess && routed.Command.Module.Name == "DeepShopModule" && routed.Command.MethodName == "HandleBuyAsync" && routed.RegexCaptureGroups is { Length: 1 } g && g[0] == U.ToString(), $"la lista «deepshop_buy:{{dueño}}» llega a su método ({(routed.IsSuccess ? routed.Command.MethodName : "(ninguno)")})");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "deepshop", "inframundo", "deep" }.All(a => textNames.Count(n => n == a) == 1), "aa deepshop / inframundo / deep están registrados, sin repetirse");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");
Check(interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "siguen entrando en el tope de 100 comandos de nivel superior");

var fogonTopic = HelpTopics.Find("fogon")!.Build(); string ft = AllText(fogonTopic);
Check(ft.Contains("/deepshop") && ft.Contains("3.000") && ft.Contains("0,5 %") && ft.Contains("500") && fogonTopic.Length <= 6000 && fogonTopic.Fields.All(f => f.Value.Length <= 1024), $"/info tema:fogon explica el vendedor con los números reales ({fogonTopic.Length})");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(ft, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/deepshop") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024), $"/info lista /deepshop, dentro de los límites ({infoE.Length})");
Check(HelpTopics.All.Count == 26 && HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256); }), "los 26 temas siguen entrando en Discord");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)U, (long)U2 } });
Check(true, "(limpieza) usuarios descartables borrados");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
public class FakeProxy : DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var v)) return v;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public static T Make<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)proxy).Values = values;
        return proxy;
    }
}
