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
var recipes = sp.GetRequiredService<IRecipeRepository>();
var zones = sp.GetRequiredService<IZoneRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var crafting = sp.GetRequiredService<ICraftingRepository>();
var shop = sp.GetRequiredService<IShopRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
var events = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const ulong U = 999000000000007001UL, GHOST = 999000000000007999UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id;", new { id = (long)U });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task Reset(int gold = 0) { await Cleanup(); await users.GetOrCreateUserAsync(U); await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)U, g = gold }); }
bool InTable(string text, string key) => NpcDialogue.AllTables().First(t => t.Key == key).Lines.Any(l => text.Contains(l));
List<SelectMenuOption> Options(BlacksmithScene s) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Where(m => m.CustomId.StartsWith("blacksmith_pick")).SelectMany(m => m.Options).ToList() ?? [];

Console.WriteLine("== 1. Registro de comandos ==");
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
Check(interactions.SlashCommands.Any(c => c.Name == "forge") && interactions.ComponentCommands.Any(c => c.Name.StartsWith("blacksmith_pick")) && texts.Commands.Any(c => c.Name == "blacksmith" && c.Aliases.Contains("herrero")), "/blacksmith, su desplegable y 'aa herrero' registrados; /forge sigue (" + (interactions.SlashCommands.Any(c => c.Name == "make") ? "make" : "FALTA") + ")");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");

Console.WriteLine("\n== 2. Catálogo de diálogos ==");
var tables = NpcDialogue.AllTables().ToList();
Check(tables.All(t => t.Lines.Count >= 2 && t.Lines.All(l => l.Length is > 5 and < 150) && t.Lines.Distinct().Count() == t.Lines.Count), $"las {tables.Count} situaciones tienen 2 o más frases distintas (para que no se lea siempre lo mismo)");
Check(Enum.GetValues<BlacksmithLine>().All(l => NpcDialogue.Blacksmith(l).Contains("El Herrero")) && Enum.GetValues<ShopkeeperLine>().All(l => NpcDialogue.Shopkeeper(l).Contains("El Tabernero")) && Enum.GetValues<InnkeeperLine>().All(l => NpcDialogue.Innkeeper(l).Contains("El Tabernero")), "toda situación de cada personaje tiene su tabla (ninguna se rompe al pedirla)");
Check(InTable("x " + string.Join("|", NpcDialogue.AllTables().First(t => t.Key == "herrero:Success").Lines), "herrero:Success") && NpcDialogue.AllTables().First(t => t.Key == "herrero:Success").Lines.Contains("¡En camino, loco! Queda pronta.") && NpcDialogue.AllTables().First(t => t.Key == "herrero:NotEnough").Lines.Contains("No tenés lo suficiente, crack. Andá a farmear y después hablamo."), "las dos frases del herrero que pediste están");
var seeded = Enumerable.Range(0, 20).Select(_ => NpcDialogue.Blacksmith(BlacksmithLine.Success, new Random(5))).Distinct().Count();
var varied = Enumerable.Range(0, 200).Select(_ => NpcDialogue.Blacksmith(BlacksmithLine.Success)).Distinct().Count();
Check(seeded == 1 && varied == 3, $"con el mismo Random sale la misma frase (determinista), y con el azar real se ven las {varied} variantes");
var bossesDb = (await db.QueryAsync<string>("SELECT name FROM monsters WHERE is_boss")).ToHashSet();
Check(bossesDb.SetEquals(NpcDialogue.KnownBosses), $"los {bossesDb.Count} jefes de la base tienen sus frases propias ({string.Join(", ", bossesDb)})");
Check(NpcDialogue.Boss("Jefe Nuevo", "👑", NpcDialogue.BossLine.Intro).Contains("Jefe Nuevo") && NpcDialogue.Boss("Rey Jabalí", "👑🐗", NpcDialogue.BossLine.Defeated).StartsWith("👑🐗 **Rey Jabalí:** «"), "un jefe sin frases propias usa unas genéricas (nunca queda mudo) y el formato es '👑 **Nombre:** «frase»'");

Console.WriteLine("\n== 3. La escena del herrero ==");
Environment.SetEnvironmentVariable("Images__Blacksmith", null);
await Reset(gold: 0);
var scene = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U);
var opts = Options(scene);
var menu = scene.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId.StartsWith("blacksmith_pick"));
Check(opts.Count >= 3 && menu.CustomId == $"blacksmith_pick:{U}" && opts.All(o => o.Label.Length <= 100 && o.Value.Length <= 100) && scene.Embed.Description.Contains("El Herrero") && scene.Embed.Thumbnail is null, $"escena inicial: el herrero saluda, y trae la lista con {opts.Count} recetas de tu zona (sin imagen si no hay una configurada)");
Check(InTable(scene.Embed.Description, "herrero:Greeting") && opts[0].Label.Contains("❌"), $"saludo de la tabla y primer renglón de la lista: «{opts[0].Label}»");
Environment.SetEnvironmentVariable("Images__Blacksmith", "https://example.com/herrero.png");
var withImg = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U);
Environment.SetEnvironmentVariable("Images__Blacksmith", "no es una url");
var badImg = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U);
Environment.SetEnvironmentVariable("Images__Blacksmith", null);
Check(withImg.Embed.Thumbnail?.Url == "https://example.com/herrero.png" && withImg.AttachmentPath is null && badImg.Embed.Thumbnail is null, "con Images__Blacksmith la foto del herrero va de miniatura (sin adjunto); con algo que no es una URL se ignora");
// La foto que viene con el bot (Assets/npc): se adjunta al mensaje y el embed la referencia como attachment://.
var assetDir = Path.Combine(AppContext.BaseDirectory, "Assets", "npc");
Directory.CreateDirectory(assetDir);
File.Copy(Path.Combine(TestEnv.RepoRoot, "Assets/npc/blacksmith.jpg"), Path.Combine(assetDir, "blacksmith.jpg"), true);
File.Copy(Path.Combine(TestEnv.RepoRoot, "Assets/npc/innkeeper.jpg"), Path.Combine(assetDir, "innkeeper.jpg"), true);
var local = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U);
Check(local.Embed.Thumbnail?.Url == "attachment://blacksmith.jpg" && local.AttachmentPath is { } lp && File.Exists(lp) && new FileInfo(lp).Length < 100_000, $"con la foto del bot en Assets/npc: miniatura attachment://blacksmith.jpg y el archivo a adjuntar ({new FileInfo(local.AttachmentPath!).Length / 1024} KB)");
var afterLocal = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U, true);
Check(afterLocal.Embed.Thumbnail?.Url == "attachment://blacksmith.jpg" && afterLocal.AttachmentPath is not null, "el recetario completo conserva la miniatura (el mensaje mantiene el adjunto al editarse)");
var healEmbed = await TavernModule.ExecuteHealAsync(users, sp.GetRequiredService<IInventoryRepository>(), combat, sp.GetRequiredService<IBuffRepository>(), U);
Check(healEmbed.Thumbnail?.Url == "attachment://innkeeper.jpg" && NpcImages.AttachmentPathFor(healEmbed) is not null && NpcImages.AttachmentPathFor(new EmbedBuilder().WithDescription("z").Build()) is null, "/heal lleva la foto del tabernero adjunta; un embed común no adjunta nada");
Environment.SetEnvironmentVariable("Images__Innkeeper", "https://example.com/tabernero.png");
Check((await TavernModule.ExecuteHealAsync(users, sp.GetRequiredService<IInventoryRepository>(), combat, sp.GetRequiredService<IBuffRepository>(), U)).Thumbnail?.Url == "https://example.com/tabernero.png", "una URL en el .env gana sobre la foto local");
Environment.SetEnvironmentVariable("Images__Innkeeper", null);
Directory.Delete(assetDir, true);
var ghost = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, GHOST);
Check(ghost.Components is null && ghost.Embed.Description.Contains("/start"), "alguien sin cuenta: el herrero lo manda a /start (y no se le crea cuenta)");

Console.WriteLine("\n== 4. Pedirle algo al herrero ==");
string pick = opts[0].Value;
var poor = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, pick);
Check(poor.Embed is not null && InTable(poor.Embed.Description, "herrero:NotEnough") && poor.Embed.Description.Contains("No pudiste forjar"), $"sin materiales: «{poor.Embed?.Description.Split((char)10)[0]}»");
var afterPoor = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U);
var poorAnswer = BlacksmithModule.BuildAnswerEmbed(poor);
Check(InTable(poorAnswer.Description, "herrero:NotEnough") && !afterPoor.Embed.Description.Contains("No pudiste forjar"), "la respuesta del herrero sale en un mensaje aparte y la escena vuelve a ofrecer la lista");
Check(Options(afterPoor).Count == opts.Count && afterPoor.Components is not null, "y el desplegable sigue ahí para pedir otra cosa");
var unknown = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Cosa inventada");
Check(unknown.PlainMessage is not null && InTable(unknown.PlainMessage, "herrero:UnknownRecipe"), "una receta que no existe: el herrero contesta con su frase");
var recipe = (await recipes.GetByResultItemNameAsync(pick))!;
foreach (var ing in recipe.Ingredients) await Give(ing.ItemName, ing.Quantity + 1);
await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)U, g = recipe.GoldCost + 100 });
async Task<bool> WornNow(string name) => await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM users u JOIN items i ON i.item_id IN (u.weapon_id, u.amulet_id) WHERE u.discord_id = @id AND i.name = @n)", new { id = (long)U, n = name });
var good = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, pick);
Check(good.Embed is not null && InTable(good.Embed.Description, "herrero:Success") && await WornNow(pick) && await Have(pick) == 0, $"con todo: el herrero lo forja y lo dice, y la pieza queda EQUIPADA (no pasa por el inventario) ({pick})");
var afterGood = await BlacksmithModule.BuildSceneAsync(users, recipes, zones, inventory, U);
Check(Options(afterGood).Count > 0 && afterGood.Embed.Description.Contains("El Herrero"), "la escena sigue con la lista");
await db.ExecuteAsync("UPDATE users SET class = 'Hechicero' WHERE discord_id = @id", new { id = (long)U });
var other = (await recipes.GetAllAsync()).FirstOrDefault(r => r.ResultItem.ClassRequirement is { } c && c != "Hechicero");
if (other is not null)
{
    var wrong = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, other.ResultItem.Name);
    Check(wrong.PlainMessage is not null && InTable(wrong.PlainMessage, "herrero:WrongClass"), $"un ítem de otra clase ({other.ResultItem.Name}): el herrero lo rechaza con su frase");
}

Console.WriteLine("\n== 5. Tendero y tabernero ==");
await Reset(gold: 100);
var viewEmbed = ShopModule.BuildViewEmbed(await ShopModule.LoadShopItemsAsync(items));
Check(InTable(viewEmbed.Description, "tendero:Greeting") && viewEmbed.Description.Contains("cajas"), "/shop view abre con el saludo del tendero");
var buy = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Mate Amargo", 1);
Check(buy.Embed is not null && InTable(buy.Embed.Description, "tendero:BuySuccess"), "comprar: el tendero lo celebra");
var noGold = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cofre de Oro", 1);
Check(noGold.PlainMessage is not null && InTable(noGold.PlainMessage, "tendero:NoGold") && noGold.PlainMessage.Contains("No te alcanza"), "sin oro: el tendero se queja y se sigue diciendo cuánto cuesta");
await db.ExecuteAsync("UPDATE users SET gold = 100000 WHERE discord_id = @id", new { id = (long)U });
await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1);
var wait = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1);
Check(wait.Embed is not null && InTable(wait.Embed.Description, "tendero:BoxWait") && wait.Embed.Description.Contains("una vez cada 2 horas"), "otra caja enseguida: el tendero pide que esperes (y se sigue diciendo cuánto falta)");
Check(InTable((await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Hierro", 1)).PlainMessage!, "tendero:NotForSale"), "algo que no vende: lo dice");
await Give("Hierro", 2); await Give("Arca del Soberano", 1);
var sold = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Hierro", 1);
Check(sold.Embed is not null && InTable(sold.Embed.Description, "tendero:SellSuccess"), "vender: el tendero cierra el trato");
Check(InTable((await ShopModule.ExecuteSellAsync(items, shop, events, U, "Arca del Soberano", 1)).PlainMessage!, "tendero:Unsellable") && InTable((await ShopModule.ExecuteSellAsync(items, shop, events, U, "Piedra", 1)).PlainMessage!, "tendero:NotOwned"), "un premio no se compra y lo que no tenés no se vende: cada caso con su frase");
var all = await ShopModule.ExecuteSellAllAsync(shop, events, U);
Check(all.Embed is not null && InTable(all.Embed.Description, "tendero:SellAll"), "vender todo: 'qué cargamento'");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
Check(InTable((await ShopModule.ExecuteSellAllAsync(shop, events, U)).PlainMessage!, "tendero:NothingToSell"), "vender todo con la mochila vacía: el tendero lo nota");
var inv = sp.GetRequiredService<IInventoryRepository>();
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
var full = await TavernModule.ExecuteHealAsync(users, inv, combat, sp.GetRequiredService<IBuffRepository>(), U);
Check(InTable(full.Description, "tabernero:FullHp"), "/heal con la vida llena: el tabernero lo nota");
await db.ExecuteAsync("UPDATE users SET current_hp = 1 WHERE discord_id = @id", new { id = (long)U });
var none = await TavernModule.ExecuteHealAsync(users, inv, combat, sp.GetRequiredService<IBuffRepository>(), U);
await Give("Mate Amargo", 2);
var ate = await TavernModule.ExecuteHealAsync(users, inv, combat, sp.GetRequiredService<IBuffRepository>(), U);
Check(InTable(none.Description, "tabernero:NothingToEat") && InTable(ate.Description, "tabernero:Healed") && ate.Description.Contains("Comiste"), "sin comida: 'comprá algo'; con comida: el tabernero te sirve y se sigue mostrando la vida");

Console.WriteLine("\n== 6. Los jefes hablan ==");
CombatState Boss(string cmd, string name) => new(cmd, name, "👑", 100, 100, 10, [], 0, 0, cmd == "boss" ? 1 : null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
var enc = AdventureModule.BuildEncounterEmbed(Boss("boss", "Rey Jabalí"));
Check(InTable(enc.Description, "Rey Jabalí:Intro") && enc.Description.Contains("salvaje aparece") && InTable(AdventureModule.BuildEncounterEmbed(Boss("hunt", "Jabalí Rabioso")).Description, "Jabalí Rabioso:Intro"), "al aparecer el jefe dice su frase, y desde la v0.8.3 un monstruo común también (las suyas)");
var winTurn = CombatTurnResolver.ResolveTurn(new CombatantProfile(100, 0, "Guerrero", ClassPassives.For("Guerrero")), default, PlayerAction.Attack, 100, 100, 1, 5);
var loseTurn = CombatTurnResolver.ResolveTurn(new CombatantProfile(1, 0, "Guerrero", ClassPassives.For("Guerrero")), default, PlayerAction.Attack, 1, 100, 100000, 500);
var willUser = new User { DiscordId = (long)U, Level = 1, CurrentHp = 50, MaxHp = 100, Class = "Guerrero" };
var victory = typeof(AdventureModule).GetMethod("BuildVictoryEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
var defeat = typeof(AdventureModule).GetMethod("BuildDefeatEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
Embed Win(string cmd, bool first) => (Embed)victory.Invoke(null, [Boss(cmd, "Lobisón Alfa"), winTurn, new CombatReward(10, 10, false), null, new LevelUpOutcome(willUser, 0), first])!;
string BossField(Embed e) => e.Fields.First(f => f.Name.Contains("Jefe de Zona")).Value;
var first = Win("boss", true); var again = Win("boss", false);
Check(InTable(BossField(first), "Lobisón Alfa:Defeated") && BossField(first).Contains("/zona") && BossField(first).Contains("Se abrió el camino"), $"primera vez que cae: el jefe se despide y se anuncia que se abre la próxima zona");
Check(InTable(BossField(again), "Lobisón Alfa:Defeated") && BossField(again).Contains("Volviste a ganarle") && !BossField(again).Contains("/zona") && !BossField(again).Contains("avanzar"), $"las siguientes veces: '¡Volviste a ganarle!' y NO dice que pasás de zona: «{BossField(again).Split((char)10).Last()}»");
Check(!Win("hunt", true).Fields.Any(f => f.Name.Contains("Jefe de Zona")), "una cacería común no muestra nada de jefes");
var lost = (Embed)defeat.Invoke(null, [Boss("boss", "Capataz de Hierro"), loseTurn, null])!;
Check(InTable(lost.Description, "Capataz de Hierro:Victory") && lost.Description.Contains("/heal") && InTable(((Embed)defeat.Invoke(null, [Boss("hunt", "Jabalí Rabioso"), loseTurn, null])!).Description, "Jabalí Rabioso:Victory"), "si el jefe te gana, se burla, y desde la v0.8.3 un monstruo común también");

Console.WriteLine("\n== 7. Chance de drop de /hunt ==");
Check(CombatRewardCalculator.HuntDropChancePercent == 6 && CombatRewardCalculator.BossChestFirstClearPercent == 100, "hunt 6% (antes 10%); el cofre del jefe 100% la primera vez (el viaje ya no suelta material desde la v0.36.0, ver v0360test)");
int hits = Enumerable.Range(0, 400000).Count(_ => CombatRewardCalculator.RollHuntReward(5).DroppedSomething);
Check(Math.Abs(hits / 4000.0 - 6.0) < 0.25, $"400.000 cacerías simuladas: {hits / 4000.0:0.00}% sueltan material (esperado 6%)");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
