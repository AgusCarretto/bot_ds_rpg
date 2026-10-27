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

// v0.29.0: las herramientas (hacha y pico) y el Carpintero (/tools, aa herramientas|equipamiento). Lo que se prueba: las reglas puras (solo Común, Raro y Épico se multiplican), el catálogo de la base (4 ítems, 4 recetas
// solo con materiales y 0 de oro, en las zonas 3 y 5), que el herrero no las vea ni las forje, la transacción de crafteo (una sola por tipo, la mejor reemplaza a la peor, nunca al revés, 12 pedidos a la vez arman una),
// el Carpintero (zona cerrada, material que falta, ya la tenés, éxito), la escena, la recolección REAL con y sin hacha, que el Fuego Nuevo las borre y el registro con Discord.Net de verdad. Contra la base LOCAL.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U1 = 999000000000098101UL, U2 = 999000000000098102UL, U3 = 999000000000098103UL, UBase = 999000000000098104UL, UTool = 999000000000098105UL, UFn = 999000000000098106UL, UGhost = 999000000000098199UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [U1, U2, U3, UBase, UTool, UFn];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(ToolsModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var recipesRepo = sp.GetRequiredService<IRecipeRepository>();
var zonesRepo = sp.GetRequiredService<IZoneRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var crafting = sp.GetRequiredService<ICraftingRepository>();
var toolRepo = sp.GetRequiredService<IToolRepository>();
var events = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

async Task Clean()
{
    foreach (var id in all.Append(UGhost))
    {
        await db.ExecuteAsync("DELETE FROM player_tools WHERE discord_id = @id; DELETE FROM inventory WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id;", new { id = (long)id });
    }
}

async Task Player(ulong id, int level, int highestCleared, string cls = "Guerrero")
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET class = @c, level = @l, highest_zone_cleared = @h, current_hp = max_hp WHERE discord_id = @id", new { id = (long)id, c = cls, l = level, h = highestCleared });
}

async Task Give(ulong id, string item, int q) =>
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + @q", new { id = (long)id, n = item, q });

async Task<int> Qty(ulong id, string item) =>
    await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory i JOIN items t ON t.item_id = i.item_id WHERE i.discord_id = @id AND t.name = @n", new { id = (long)id, n = item });

// Todo lo que pide una receta de herramienta, una vez (o las veces que se pida).
async Task GiveRecipe(ulong id, string tool, int times = 1)
{
    var recipe = (await recipesRepo.GetByResultItemNameAsync(tool))!;
    foreach (var i in recipe.Ingredients) { await Give(id, i.ItemName, i.Quantity * times); }
}

await Clean();
var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
int Z2 = zones[1].ZoneId, Z4 = zones[3].ZoneId;   // haber vencido al jefe de la Zona 2 abre la 3; al de la 4, la 5
const string AxeZ3 = "Hacha de Hierro Forjado", PickZ3 = "Pico de Hierro Forjado", AxeZ5 = "Hacha de Escoria Templada", PickZ5 = "Pico de Escoria Templada";

Console.WriteLine("== 1. Las reglas puras ==");
Check(ToolRules.KindOfItemType("Hacha") == "axe" && ToolRules.KindOfItemType("Pico") == "pickaxe" && ToolRules.KindOfItemType("Weapon") is null && ToolRules.IsToolType("Pico") && !ToolRules.IsToolType(null), "«Hacha» es el hacha, «Pico» el pico y nada más es una herramienta");
Check(new[] { "Común", "Raro", "Épico" }.All(r => ToolRules.QuantityFactor(r, 3) == 3 && ToolRules.QuantityFactor(r, 2) == 2) && new[] { "Legendario", "Mítico", "xyz" }.All(r => ToolRules.QuantityFactor(r, 3) == 1) && ToolRules.QuantityFactor("Común", 1) == 1 && ToolRules.QuantityFactor("Común", 0) == 1,
    "la herramienta multiplica Común, Raro y Épico; Legendario y Mítico siguen en 1; sin herramienta (×1) no cambia nada");
Check(Enumerable.Range(0, 500).All(_ => GatheringYield.RollScaled("Legendario", 1.2 * ToolRules.QuantityFactor("Legendario", 3), new Random()) is 1 or 2), "con el hacha ×3 un Legendario sigue saliendo de a uno (solo lo escala el Fuego Nuevo)");
double avgBase = Enumerable.Range(0, 4000).Average(_ => GatheringYield.RollScaled("Común", 1.0)), avgTool = Enumerable.Range(0, 4000).Average(_ => GatheringYield.RollScaled("Común", 1.0 * ToolRules.QuantityFactor("Común", 3)));
Check(avgBase is > 2.8 and < 3.2 && avgTool is > 8.5 and < 9.5, $"un Común da en promedio 3 sin herramienta y 9 con el ×3 ({avgBase:0.0} / {avgTool:0.0})");
bool dialogueOk = Enum.GetValues<CarpenterLine>().All(l => Enumerable.Range(0, 60).Select(i => NpcDialogue.Carpenter(l, new Random(i))).Distinct().Count() >= 2 && NpcDialogue.Carpenter(l, new Random(1)).StartsWith("🪚 **El Carpintero:** «"));
Check(dialogueOk, "el Carpintero tiene al menos 2 frases distintas en cada situación y habla con el formato de siempre");

Console.WriteLine("\n== 2. El catálogo de la base ==");
var tools = (await recipesRepo.GetAllAsync()).Where(r => ToolRules.IsToolType(r.ResultItem.Type)).ToList();
int Q(string tool, string item) => tools.Single(t => t.ResultItem.Name == tool).Ingredients.Single(i => i.ItemName == item).Quantity;
int AxeHierro = Q(AxeZ3, "Hierro");   // cuánto Hierro pide el hacha de la Zona 3 (las pruebas de abajo lo usan para no repetir el número)
Check(tools.Count == 4&& new[] { AxeZ3, PickZ3, AxeZ5, PickZ5 }.All(n => tools.Any(t => t.ResultItem.Name == n)), "4 herramientas con receta: un hacha y un pico de la Zona 3 y de la Zona 5");
var ingredientTypes = (await db.QueryAsync<string>("SELECT DISTINCT type FROM items WHERE item_id = ANY(@ids)", new { ids = tools.SelectMany(t => t.Ingredients).Select(i => i.ItemId).Distinct().ToArray() })).ToList();
Check(tools.All(t => t.GoldCost == 0 && t.Ingredients.Count == 4 && t.Ingredients.All(i => i.Quantity > 0)) && ingredientTypes.Count > 0 && ingredientTypes.All(t => t is "Madera" or "Mineral"), "todas sin oro, con 4 materiales y SOLO de recolección (madera y mineral)");
Check(Q(AxeZ3, "Madera de Roble") == 12 && Q(AxeZ3, "Madera de Nogal") == 3 && AxeHierro == 9 && Q(AxeZ3, "Piedra") == 18 && Q(PickZ3, "Hierro") == 6 && Q(PickZ3, "Carbón") == 6 && Q(PickZ3, "Madera de Roble") == 9 && Q(PickZ3, "Piedra") == 18
    && Q(AxeZ5, "Madera de Ébano") == 3 && Q(AxeZ5, "Madera de Nogal") == 6 && Q(AxeZ5, "Hierro") == 9 && Q(AxeZ5, "Carbón") == 6 && Q(PickZ5, "Gema de Zafiro") == 3 && Q(PickZ5, "Oro Puro") == 3 && Q(PickZ5, "Hierro") == 9 && Q(PickZ5, "Madera de Roble") == 12,
    "v0.30.2 («salen muy baratos para como multiplican las cosas, pedí más»): las 4 herramientas piden el TRIPLE de materiales (la Zona 3: unas 4 a 5 horas de recolección sin herramienta; la Zona 5: unas 6 a 13)");
Check(tools.Where(t => t.ResultItem.Name.Contains("Hierro")).All(t => t.ResultItem.StatValue == 2 && t.ZoneId == zones[2].ZoneId) && tools.Where(t => t.ResultItem.Name.Contains("Escoria")).All(t => t.ResultItem.StatValue == 3 && t.ZoneId == zones[4].ZoneId),
    "las de la Zona 3 multiplican ×2 y se desbloquean en la 3; las de la Zona 5, ×3 y en la 5");
var shown = RecipeCatalog.BookFor(await recipesRepo.GetAllAsync(), await zonesRepo.GetAllAsync(), "Guerrero", true).SelectMany(s => s.Recipes);
var viewed = RecipeCatalog.ViewFor(await recipesRepo.GetAllAsync(), await zonesRepo.GetAllAsync(), "Guerrero", zones[2].ZoneId, true).Recipes;
Check(!shown.Concat(viewed).Any(r => ToolRules.IsToolType(r.ResultItem.Type)), "el herrero no las ve: ni el recetario ni la lista para forjar traen herramientas");
await Player(U1, 30, Z4);
await GiveRecipe(U1, AxeZ3);
var viaForge = await ForgeModule.ExecuteMakeAsync(users, recipesRepo, crafting, events, U1, AxeZ3);
Check(viaForge.PlainMessage!.Contains("Carpintero") && await Qty(U1, "Hierro") == AxeHierro && (await toolRepo.GetAsync(U1)).Count == 0, "tampoco las forja el herrero escribiendo el nombre: lo manda al Carpintero y no gasta nada");

Console.WriteLine("\n== 3. La transacción de crafteo ==");
var axe3 = (await recipesRepo.GetByResultItemNameAsync(AxeZ3))!; var axe5 = (await recipesRepo.GetByResultItemNameAsync(AxeZ5))!;
var ing3 = axe3.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList();
var raceResults = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => crafting.CraftAsync(U1, 0, ing3, axe3.ResultItem.ItemId)));
var worn1 = await toolRepo.GetAsync(U1);
Check(raceResults.Count(r => r.Success) == 1 && raceResults.Where(r => r.Success).All(r => r.EquippedSlot == "tool") && worn1.Count == 1 && worn1[0].Name == AxeZ3 && worn1[0].Kind == "axe" && worn1[0].Multiplier == 2,
    $"12 pedidos a la vez con materiales para UNA hacha: se arma una sola y queda puesta ({raceResults.Count(r => r.Success)} éxitos)");
Check(await Qty(U1, "Hierro") == 0 && await Qty(U1, "Piedra") == 0 && await Qty(U1, "Madera de Roble") == 0 && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory WHERE discord_id = @id", new { id = (long)U1 }) == 0, "gastó exactamente los materiales de la receta, y la herramienta no pasa por la mochila");
await GiveRecipe(U1, AxeZ3);
var again = await crafting.CraftAsync(U1, 0, ing3, axe3.ResultItem.ItemId);
Check(!again.Success && again.SlotOccupied && await Qty(U1, "Hierro") == AxeHierro, "pedir la misma hacha de nuevo: se rechaza y NO gasta los materiales");
await GiveRecipe(U1, AxeZ5);
var ing5 = axe5.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList();
var up = await crafting.CraftAsync(U1, 0, ing5, axe5.ResultItem.ItemId);
var worn1b = await toolRepo.GetAsync(U1);
Check(up.Success && worn1b.Count == 1 && worn1b[0].Name == AxeZ5 && worn1b[0].Multiplier == 3, "la hacha ×3 REEMPLAZA a la ×2 (queda una sola por tipo)");
await GiveRecipe(U1, AxeZ3);
var down = await crafting.CraftAsync(U1, 0, ing3, axe3.ResultItem.ItemId);
Check(!down.Success && down.SlotOccupied && (await toolRepo.GetAsync(U1))[0].Name == AxeZ5 && await Qty(U1, "Hierro") >= 3, "con la ×3 puesta, armar la ×2 se rechaza: nunca se pasa a una peor y no se gasta nada");
await Clean();
await Player(U2, 30, Z4);
await Give(U2, "Hierro", 1);
var poor = await crafting.CraftAsync(U2, 0, ing3, axe3.ResultItem.ItemId);
Check(!poor.Success && poor.FailureReason!.Contains("te falta") && (await toolRepo.GetAsync(U2)).Count == 0 && await Qty(U2, "Hierro") == 1, "con un material de menos: no arma nada ni toca lo que tenías");

Console.WriteLine("\n== 4. El Carpintero (armar) ==");
Task<ForgeModule.ForgeMakeResult> Make(ulong id, string name) => ToolsModule.ExecuteCraftAsync(users, recipesRepo, zonesRepo, crafting, toolRepo, events, id, name, new Random(3));
await Clean();
await Player(U3, 1, 0);
await GiveRecipe(U3, AxeZ3);
var unknown = await Make(U3, "Hacha Inventada"); var notTool = await Make(U3, "Hoja de Acero Puro");
Check(unknown.PlainMessage!.StartsWith("🪚 **El Carpintero:**") && (await toolRepo.GetAsync(U3)).Count == 0, "un nombre que no existe: lo dice con la voz del Carpintero");
Check(notTool.PlainMessage is not null && (await toolRepo.GetAsync(U3)).Count == 0 && await Qty(U3, "Hierro") == AxeHierro, "pedirle algo que no es una herramienta (la Hoja): no se la arma ni gasta nada");
var locked = await Make(U3, AxeZ3);
Check(locked.PlainMessage!.Contains("🔒") && locked.PlainMessage.Contains("Zona 3") && (await toolRepo.GetAsync(U3)).Count == 0 && await Qty(U3, "Hierro") == AxeHierro, "con la Zona 3 sin desbloquear: avisa que se abre en la Zona 3 y no gasta nada");
await db.ExecuteAsync("UPDATE users SET level = 30, highest_zone_cleared = @h WHERE discord_id = @id", new { id = (long)U3, h = Z2 });
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id AND item_id = (SELECT item_id FROM items WHERE name = 'Piedra')", new { id = (long)U3 });
var missing = await Make(U3, AxeZ3);
Check(missing.Embed is not null && missing.Embed.Title == "🪚 El carpintero no pudo armarla" && missing.Embed.Description.Contains("Piedra") && (await toolRepo.GetAsync(U3)).Count == 0 && await Qty(U3, "Hierro") == AxeHierro, "zona abierta pero falta un material: «no pudo armarla», dice cuál y no gasta lo demás");
await Give(U3, "Piedra", Q(AxeZ3, "Piedra"));
var ok = await Make(U3, AxeZ3);
Check(ok.Embed is not null && ok.Embed.Title == "🪚 ¡Herramienta lista!" && ok.Embed.Description.Contains("×2") && ok.Embed.Description.Contains("/chop") && (await toolRepo.GetAsync(U3)).Single().Name == AxeZ3 && await Qty(U3, "Hierro") == 0, "con todo: la arma, queda puesta, dice el ×2 en /chop y descuenta los materiales");
await GiveRecipe(U3, AxeZ3);
var have = await Make(U3, AxeZ3);
Check(have.PlainMessage!.Contains("Tu hacha actual") && await Qty(U3, "Hierro") == AxeHierro, "pedirla otra vez: «ya tenés una igual o mejor» y no gasta nada");
var lockedZ5 = await Make(U3, AxeZ5);
Check(lockedZ5.PlainMessage is not null && lockedZ5.PlainMessage.Contains("Zona 5") && lockedZ5.PlainMessage.Contains("🔒"), "la de la Zona 5 sigue cerrada hasta vencer al jefe de la Zona 4");

Console.WriteLine("\n== 5. La escena ==");
await Clean();
await Player(U1, 30, Z2);
await GiveRecipe(U1, AxeZ3);
Task<ToolsScene> Scene(ulong id) => ToolsModule.BuildSceneAsync(users, recipesRepo, zonesRepo, inventory, toolRepo, id, new Random(1));
var s1 = await Scene(U1);
var menu1 = s1.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single();
Check(s1.Embed.Title == "🪚 El Carpintero" && s1.Embed.Fields.Length == 6 && s1.Embed.Fields[0].Name.Contains("Hacha") && s1.Embed.Fields[0].Value.Contains("×1") && s1.Embed.Length < 6000,
    $"sin herramientas: el hacha y el pico en ×1 y sus 4 mejoras abajo ({string.Join(" | ", s1.Embed.Fields.Select(f => f.Name))})");
Check(menu1 is not null && menu1.CustomId == $"tools_pick:{U1}" && menu1.Options.Count == 2 && menu1.Options.All(o => o.Value is AxeZ3 or PickZ3), "la lista solo ofrece lo que ya se desbloqueó (las dos de la Zona 3)");
Check(menu1!.Options.Single(o => o.Value == AxeZ3).Label.StartsWith("✅") && menu1.Options.Single(o => o.Value == PickZ3).Label.StartsWith("❌"), "marca ✅ la que podés armar ahora (tenés todo para el hacha) y ❌ la que te falta");
var axeField = s1.Embed.Fields.Single(f => f.Name.Contains(AxeZ3)); var lockedField = s1.Embed.Fields.Single(f => f.Name.Contains(AxeZ5));
Check(axeField.Value.Contains("✅") && !axeField.Value.Contains("🔒") && lockedField.Value.Contains("🔒") && lockedField.Value.Contains("Zona 5"), "cada mejora lista los materiales con ✅/❌ y «tenés/necesitás»; la cerrada dice en qué zona se abre");
await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = @h WHERE discord_id = @id", new { id = (long)U1, h = Z4 });
var s2 = await Scene(U1);
Check(s2.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single().Options.Count == 4, "al desbloquear la Zona 5 la lista ofrece las cuatro");
var made = await Make(U1, AxeZ3);
await GiveRecipe(U1, AxeZ5); await Make(U1, AxeZ5);
await GiveRecipe(U1, PickZ5); await Make(U1, PickZ5);
var s3 = await Scene(U1);
Check(made.Embed is not null && s3.Components is null && s3.Embed.Fields.Length == 2 && s3.Embed.Fields.All(f => f.Value.Contains("lo mejor que arma") && f.Value.Contains("×3")), "con las dos de la Zona 5 puestas: dice «lo mejor que arma» y no ofrece nada más");
var s4 = await Scene(UGhost);
Check(s4.Components is null && s4.Embed.Description.Contains("/start"), "sin cuenta: manda a /start");

Console.WriteLine("\n== 6. La recolección de verdad ==");
await Player(UBase, 30, Z4); await Player(UTool, 30, Z4);
await db.ExecuteAsync("INSERT INTO player_tools (discord_id, kind, item_id) SELECT @id, 'axe', item_id FROM items WHERE name = @n", new { id = (long)UTool, n = AxeZ5 });
var bonusSvc = sp.GetRequiredService<IPlayerBonusService>();
var bBase = await bonusSvc.GetAsync(UBase, 0); var bTool = await bonusSvc.GetAsync(UTool, 0);
Check(bBase.ChopToolFactor == 1 && bBase.MineToolFactor == 1 && bBase.IsNone && bTool.ChopToolFactor == 3 && bTool.MineToolFactor == 1 && !bTool.IsNone, "el servicio de bonus lee las herramientas: sin ellas ×1; con el hacha ×3 solo para /chop (el pico sigue ×1)");
int Wood(ulong id) => db.ExecuteScalar<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory i JOIN items t ON t.item_id = i.item_id WHERE i.discord_id = @id AND t.type = 'Madera'", new { id = (long)id });
int Ore(ulong id) => db.ExecuteScalar<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory i JOIN items t ON t.item_id = i.item_id WHERE i.discord_id = @id AND t.type = 'Mineral'", new { id = (long)id });
async Task Gather(ulong id, CooldownDefinition what, string type, int times)
{
    for (int i = 0; i < times; i++)
    {
        await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
        await GatheringModule.ExecuteGatherAsync(sp.GetRequiredService<ICooldownRepository>(), sp.GetRequiredService<IGatheringRepository>(), users, sp.GetRequiredService<IItemRepository>(), bonusSvc, events, id, what, type);
    }
}

await Gather(UBase, CooldownCatalog.Chop, "Madera", 40); await Gather(UTool, CooldownCatalog.Chop, "Madera", 40);
await Gather(UBase, CooldownCatalog.Mine, "Mineral", 40); await Gather(UTool, CooldownCatalog.Mine, "Mineral", 40);
Check(Wood(UBase) > 0 && Wood(UTool) > 1.8 * Wood(UBase), $"40 /chop: con el hacha ×3 sale bastante más que sin ella (esperado ~3×): {Wood(UTool)} contra {Wood(UBase)}");
Check(Ore(UBase) > 0 && Ore(UTool) > 0 && Ore(UTool) < 1.6 * Ore(UBase) && Ore(UTool) > 0.5 * Ore(UBase), $"40 /mine: el hacha NO toca la minería (el pico sigue ×1): {Ore(UTool)} contra {Ore(UBase)}");

Console.WriteLine("\n== 7. El Fuego Nuevo las borra ==");
await Player(UFn, 30, Z4);
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)UFn });
await db.ExecuteAsync("INSERT INTO player_tools (discord_id, kind, item_id) SELECT @id, 'axe', item_id FROM items WHERE name = @a UNION ALL SELECT @id, 'pickaxe', item_id FROM items WHERE name = @p", new { id = (long)UFn, a = AxeZ5, p = PickZ3 });
var renewed = await sp.GetRequiredService<IFuegoNuevoRepository>().RenewAsync(UFn, 0, "Ninja");
Check(renewed.Status == RenewStatus.Ok && (await toolRepo.GetAsync(UFn)).Count == 0, $"renacer deja el hacha y el pico en ×1 (hay que armarlos de nuevo en cada vuelta) ({renewed.Status})");

Console.WriteLine("\n== 8. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(ToolsModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "tools");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Count == 0, $"/tools existe, sin parámetros y con {slash.Description.Length} caracteres de descripción (≤ 100)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"tools_pick:{U1}"));
Check(routed.IsSuccess && routed.Command.MethodName == "HandlePickAsync" && routed.Command.Module.Name == "ToolsModule", "la lista tools_pick:{dueño} llega a su método");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "tools", "tool", "herramientas", "herramienta", "equipamiento", "equipment", "carpintero" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "tools"), "aa tools / herramientas / equipamiento / equipment / carpintero son el mismo comando y ningún otro lo pisa");
Check(!FightLock.IsAllowed("tools"), "en plena pelea /tools queda bloqueado (como todo lo que no es de la pelea)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(ToolsModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); ActivatorUtilities.CreateInstance(sp, typeof(BlacksmithModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built, "los módulos nuevos se construyen con el DI real");
Check(HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("`/tools`")), "/info nombra /tools");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

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
