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

// La referencia al bot copia Assets/npc a la carpeta de salida; este arnés prueba primero el caso SIN foto y después copia las fotos a mano, así que parte de una carpeta limpia.
{ var cleanAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "npc"); if (Directory.Exists(cleanAssets)) Directory.Delete(cleanAssets, true); }

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
var shop = sp.GetRequiredService<IShopRepository>();
var buffs = sp.GetRequiredService<IBuffRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
var events = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const ulong U = 999000000000010001UL, GHOST = 999000000000010999UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id;", new { id = (long)U });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task Give(string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q });
async Task Reset(int gold) { await Cleanup(); await users.GetOrCreateUserAsync(U); await db.ExecuteAsync("UPDATE users SET gold = @g, current_hp = max_hp, class = 'Hechicero' WHERE discord_id = @id", new { id = (long)U, g = gold }); /* Hechicero a propósito (v0.25.3): factor de vida 1,0, la comida cura lo que dice */ await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U }); }
List<SelectMenuComponent> Menus(TabernaScene s) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().ToList() ?? [];
var boxCtx = sp.GetRequiredService<IBoxContextService>();
Task<TabernaScene> Scene(string? talk = null, ulong? id = null) => TabernaModule.BuildSceneAsync(users, items, inventory, buffs, id ?? U, talk, boxCtx);
Task<string> Act(string action, string name) => TabernaModule.ExecuteActionAsync(action, users, items, inventory, shop, buffs, combat, events, U, name, boxCtx);

Console.WriteLine("== 1. Comandos ==");
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var slash = interactions.SlashCommands.Select(c => c.Name).ToList();
Check(slash.Contains("forge") && slash.Contains("taberna") && !slash.Contains("blacksmith") && !slash.Contains("recipes") && !slash.Contains("make"), $"/forge (solo, sin subcomandos) y /taberna existen; /blacksmith, /forge make y /forge recipes ya no ({slash.Count} comandos)");
var forgeCmd = interactions.SlashCommands.First(c => c.Name == "forge");
Check(forgeCmd.Parameters.Count == 0 && forgeCmd.Module.Name.Contains("Blacksmith"), "/forge no pide nada: abre la herrería con su lista");
Check(new[] { "taberna_eat", "taberna_buy", "taberna_box", "taberna_sell", "blacksmith_pick" }.All(p => interactions.ComponentCommands.Any(c => c.Name.StartsWith(p))), "las 4 listas de la taberna y la de la herrería están registradas");
Check(texts.Commands.Any(c => c.Name == "taberna" && c.Aliases.Contains("tienda")) && texts.Commands.Any(c => c.Name == "blacksmith" && c.Aliases.Contains("forge") && c.Aliases.Contains("herrero")), "aa taberna (alias tienda) y aa forge / aa herrero");
// Discord.Net no ejecuta el primer candidato de Search: prueba el PARSEO de cada uno y se queda con el mejor. Se prueba igual.
var fakeCtx = (ICommandContext)FakeProxy.Create(typeof(ICommandContext), (n, a) => null);
async Task<List<string>> ParsesOk(string input)
{
    var found = texts.Search(input);
    var ok = new List<string>();
    foreach (var m in found.Commands)
    {
        var pr = await m.ParseAsync(fakeCtx, found, Discord.Commands.PreconditionResult.FromSuccess(), sp);
        if (pr.IsSuccess) ok.Add(m.Command.Name);
    }

    return ok;
}

var pRecipes = await ParsesOk("forge recipes");
var pMake = await ParsesOk("forge make Espada de Madera");
var pAlone = await ParsesOk("forge");
Check(pRecipes.SequenceEqual(["forge recipes"]) && pMake.Contains("forge make") && !pMake.Contains("blacksmith") && pAlone.SequenceEqual(["blacksmith"]),
    "en texto: 'aa forge recipes' solo parsea como recetas (" + string.Join(",", pRecipes) + "), 'aa forge make ...' como forjar (" + string.Join(",", pMake) + ") y 'aa forge' solo como la herrería (" + string.Join(",", pAlone) + ")");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && !x.IsSealed && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");

Console.WriteLine("\n== 2. La escena de la taberna ==");
await Reset(gold: 5000);
await Give("Mate Amargo", 3); await Give("Hierro", 4); await Give("Madera de Pino", 2);
await db.ExecuteAsync("UPDATE users SET current_hp = 10 WHERE discord_id = @id", new { id = (long)U });
var scene = await Scene();
var menus = Menus(scene);
var ids = menus.Select(m => m.CustomId).ToList();
Check(menus.Count == 4 && ids[0] == $"taberna_eat:{U}" && ids[1] == $"taberna_buy:{U}" && ids[2] == $"taberna_box:{U}" && ids[3] == $"taberna_sell:{U}", $"4 listas, una por fila: comer, comprar comida, comprar caja, vender ({string.Join(", ", menus.Select(m => m.Placeholder))})");
Check(menus.All(m => m.Options.Count is >= 1 and <= 25 && m.Options.All(o => o.Label.Length <= 100 && o.Value.Length <= 100 && (o.Description?.Length ?? 0) <= 100 && o.Emote is null)) && menus.All(m => m.Options.Select(o => o.Value).Distinct().Count() == m.Options.Count), "opciones dentro de los límites de Discord, sin emojis personalizados y sin repetidos");
var buyOpts = menus[1].Options.ToList(); var boxOpts = menus[2].Options.ToList(); var sellOpts = menus[3].Options.ToList(); var eatOpts = menus[0].Options.ToList();
Check(buyOpts.Count == 7 && buyOpts.Any(o => o.Value == "Comida para Mascotas" && o.Description == "Para tus mascotas · 100 oro") && buyOpts.Any(o => o.Value == "Mate Amargo" && o.Description == "Cura 15 HP · 8 oro") && buyOpts.Any(o => o.Description.Contains("+15% ATQ 30 min")) && boxOpts.Count == 4 && boxOpts[0].Value == "Cajón de Pino" && boxOpts[0].Description == "entre 1 y 5 ítems · 2.300 oro" && boxOpts.Skip(1).All(o => o.Description.StartsWith("🔒 Zona ") && o.Description.Contains(" ítems · ")) && !boxOpts.Any(o => o.Value == "Arca del Soberano"), "comprar comida: las 6 con su cura y precio (el banquete con su buff); comprar caja: las 4 de la tienda (no la Mítica), con su rango y las de zonas cerradas con 🔒");
Check(eatOpts.Count == 2 && eatOpts[0].Label == "🍖 Curarme del todo" && eatOpts[1].Label == "Mate Amargo ×3" && sellOpts.Any(o => o.Label == "Hierro ×4") && sellOpts.Any(o => o.Value == "Madera de Pino"), "comer: 'Curarme del todo' (estás herido) y lo que tenés (Mate Amargo ×3); vender: lo que tenés y se puede vender");
Check(scene.Embed.Title == "🍺 La Taberna" && scene.Embed.Description.Contains("El Tabernero") && scene.Embed.Description.Contains("Tu oro: **5000**") && scene.Embed.Fields.Length == 3 && scene.Embed.Fields.All(f => f.Inline), "el embed: tabernero que saluda, tu oro y la carta en dos columnas");
Console.WriteLine("   " + scene.Embed.Description.Replace("\n", "\n   "));

Console.WriteLine("\n== 3. Cada lista hace lo suyo (lógica real) ==");
string t1 = await Act("taberna_buy", "Asado de Tira");
Check(t1.Contains("El Tabernero") && t1.Contains("Compraste") && await Have("Asado de Tira") == 1 && await Gold() == 5000 - 160, $"comprar comida: el tabernero lo celebra, llega el ítem y se cobra (oro {await Gold()})");
string t2 = await Act("taberna_box", "Cajón de Pino");
Check(t2.Contains("Compraste") && await Have("Cajón de Pino") == 1 && await Gold() == 5000 - 160 - 2300, "comprar una caja: se compra");
string t3 = await Act("taberna_box", "Cajón de Pino");
Check(t3.Contains("una vez cada 2 horas") && await Have("Cajón de Pino") == 1 && await Gold() == 5000 - 160 - 2300, "otra caja enseguida: el tabernero la frena (una cada 2 horas) y no cobra");
string t3b = await Act("taberna_box", "Baúl de Roble");
Check(t3b.Contains("Zona 2") && t3b.Contains("🔒") && await Have("Baúl de Roble") == 0 && await Gold() == 5000 - 160 - 2300, "una caja de una zona que no desbloqueaste: el tabernero no la vende y no cobra");
string t4 = await Act("taberna_sell", "Hierro");
Check(t4.Contains("El Tabernero") && await Have("Hierro") == 3, "vender: se vende UNA unidad (4 -> 3)");
string t5 = await Act("taberna_eat", "Mate Amargo");
var hp = await db.ExecuteScalarAsync<int>("SELECT current_hp FROM users WHERE discord_id = @id", new { id = (long)U });
Check(t5.Contains("El Tabernero") && await Have("Mate Amargo") == 2 && hp == 25, $"comer: se gasta 1 Mate Amargo y cura (vida 10 -> {hp})");
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
string t6 = await Act("taberna_eat", "Mate Amargo");
Check(t6.Contains("sano") || t6.Contains("máximo") || t6.Contains("toro") || t6.Contains("roble"), $"comer con la vida llena: el tabernero lo nota y no gasta nada ({await Have("Mate Amargo")} mates)");
Check(await Have("Mate Amargo") == 2, "...y no se gastó el mate");
string t7 = await Act("taberna_buy", "Hierro");
Check(t7.Contains("no está disponible") && t7.Contains("El Tabernero"), "pedir algo que no vende: lo dice");
await db.ExecuteAsync("UPDATE users SET gold = 1 WHERE discord_id = @id", new { id = (long)U });
string t8 = await Act("taberna_buy", "Cordero Patagónico");
Check(t8.Contains("No te alcanza") && t8.Contains("El Tabernero"), "sin oro: el tabernero se queja y dice cuánto cuesta");
var inCombat = new CombatState("hunt", "Toro", "🐂", 100, 100, 5, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
combat.TryStart(U, inCombat, new FakeTarget());
string t9 = await Act("taberna_eat", "Mate Amargo");
Check(NpcDialogue.AllTables().First(t => t.Key == "tabernero:InCombat").Lines.Any(l => t9.Contains(l)) && await Have("Mate Amargo") == 2, "en medio de un combate el tabernero no te sirve (y no se gasta nada)");
combat.TryAdvance(U, combat.Peek(U)!, null, null);

Console.WriteLine("\n== 4. La escena se rearma con lo nuevo ==");
await db.ExecuteAsync("UPDATE users SET gold = 777 WHERE discord_id = @id", new { id = (long)U });
var after = await Scene("🍺 **El Tabernero:** «Prueba»");
Check(after.Embed.Description.StartsWith("🍺 **El Tabernero:** «Prueba»") && after.Embed.Description.Contains("Tu oro: **777**"), "después de una acción muestra lo que dijo el tabernero y tu oro nuevo");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
var poor = await Scene();
Check(Menus(poor).Count == 2 && Menus(poor).All(m => m.CustomId!.Contains("buy") || m.CustomId.Contains("box")), "con la mochila vacía no hay lista de comer ni de vender (solo las de comprar)");
var ghost = await Scene(null, GHOST);
Check(ghost.Components is null && ghost.Embed.Description.Contains("/start"), "alguien sin cuenta: lo manda a /start (y no se le crea cuenta)");

Console.WriteLine("\n== 5. La foto del tabernero ==");
var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "npc");
Directory.CreateDirectory(assetDir);
File.Copy(Path.Combine(TestEnv.RepoRoot, "Assets/npc/innkeeper.jpg"), Path.Combine(assetDir, "innkeeper.jpg"), true);
await Give("Mate Amargo", 1);
var photo = await Scene();
Check(photo.Embed.Thumbnail?.Url == "attachment://innkeeper.jpg" && photo.AttachmentPath is { } p && File.Exists(p), "con la foto del bot: miniatura attachment://innkeeper.jpg y el archivo a adjuntar");
var shopView = ShopModule.BuildViewEmbed(await ShopModule.LoadShopItemsAsync(items));
Check(shopView.Thumbnail is null && shopView.Title == "🍺 La Taberna", "/shop view (el atajo directo) no lleva foto adjunta: así nunca queda una miniatura sin archivo");
Directory.Delete(assetDir, true);

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

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
}
