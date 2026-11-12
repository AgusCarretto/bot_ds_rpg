using System.Diagnostics;
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

// v0.48.0: la PESCA (el dueño, 2026-10-29: «arma 3 tipos de pescado… me gusta que se cocine con carbón y que con eso puedas ir teniendo bufos»; la cocina es la etapa 2). Lo que se prueba: las reglas puras (la tabla
// de tres rarezas, el tipo de recolección, el cooldown, el oficio Pescador y la caña), el catálogo de la base (3 peces, 2 cañas con 4 materiales y 0 de oro), que la migración sea re-ejecutable y admita 'rod',
// la pesca REAL de punta a punta (cooldown, inventario, contadores sin tocar gathered_units, la avanzada al nivel 100), la caña (se arma, multiplica ×2 y NO toca /chop ni /mine, la borra el Fuego Nuevo),
// el inventario, el logro y los títulos, los recordatorios, /cd, la ayuda y el registro con Discord.Net de verdad. Contra la base LOCAL.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));

const ulong UFish = 999000000000042101UL, URod = 999000000000042102UL, UAdv = 999000000000042103UL, UBase = 999000000000042104UL, UCraft = 999000000000042105UL, UFn = 999000000000042106UL, UGhost = 999000000000042199UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [UFish, URod, UAdv, UBase, UCraft, UFn];

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
var cooldownRepo = sp.GetRequiredService<ICooldownRepository>();
var gatheringRepo = sp.GetRequiredService<IGatheringRepository>();
var itemRepo = sp.GetRequiredService<IItemRepository>();
var bonusSvc = sp.GetRequiredService<IPlayerBonusService>();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

async Task Clean()
{
    foreach (var id in all.Append(UGhost))
    {
        await db.ExecuteAsync("DELETE FROM player_tools WHERE discord_id = @id; DELETE FROM inventory WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id;", new { id = (long)id });
    }
}

async Task Player(ulong id, int level, int highestCleared)
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET class = 'Guerrero', level = @l, highest_zone_cleared = @h, current_hp = max_hp WHERE discord_id = @id", new { id = (long)id, l = level, h = highestCleared });
}

async Task Give(ulong id, string item, int q) =>
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + @q", new { id = (long)id, n = item, q });

async Task<long> Stat(ulong id, string key) =>
    await db.ExecuteScalarAsync<long?>("SELECT value FROM player_stats WHERE discord_id = @id AND stat_key = @k", new { id = (long)id, k = key }) ?? 0;

async Task SetStat(ulong id, string key, long value) =>
    await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, @k, @v) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = @v", new { id = (long)id, k = key, v = value });

async Task<int> Fish(ulong id) =>
    await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory i JOIN items t ON t.item_id = i.item_id WHERE i.discord_id = @id AND t.type = 'Pescado'", new { id = (long)id });

// Una pesca (o varias) de verdad: limpia el cooldown antes de cada una y devuelve cuántos peces entraron en cada una.
async Task<List<int>> Cast(ulong id, int times, bool advanced = false)
{
    var deltas = new List<int>();
    for (int i = 0; i < times; i++)
    {
        await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
        int before = await Fish(id);
        await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, id, CooldownCatalog.Fish, "Pescado", advanced);
        deltas.Add(await Fish(id) - before);
        await SetStat(id, "fish_xp", 0);   // el nivel del Pescador queda en 0: si subiera, la cantidad dejaría de ser un múltiplo exacto de la caña
    }

    return deltas;
}

await Clean();
var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
int Z2 = zones[1].ZoneId, Z4 = zones[3].ZoneId;
const string RodZ3 = "Caña de Hierro Forjado", RodZ5 = "Caña de Escoria Templada";

Console.WriteLine("== 1. Las reglas puras ==");
Check(RarityCatalog.FishChances.Select(c => (c.Rarity, c.Percent)).SequenceEqual([("Común", 78.0), ("Raro", 19.0), ("Épico", 3.0)]) && Math.Abs(RarityCatalog.FishChances.Sum(c => c.Percent) - 100) < 1e-9, "la pesca tiene su propia tabla de tres rarezas: Común 78 % / Raro 19 % / Épico 3 %, que suma 100");
var rolled = Enumerable.Range(0, 20000).Select(_ => RarityCatalog.RollFishRarity()).ToList();
double commonShare = rolled.Count(r => r == "Común") / 20000.0;
Check(rolled.All(r => r is "Común" or "Raro" or "Épico") && commonShare is > 0.76 and < 0.80, $"20.000 sorteos: nunca sale Legendario ni Mítico y el Común es ~78 % ({commonShare:P1})");
Check(GatherKinds.Of(CooldownCatalog.Chop) == "chop" && GatherKinds.Of("chop_adv") == "chop" && GatherKinds.Of(CooldownCatalog.Mine) == "mine" && GatherKinds.Of("mine_adv") == "mine" && GatherKinds.Of(CooldownCatalog.Fish) == "fish" && GatherKinds.Of("fish_adv") == "fish",
    "GatherKinds reconoce los tres comandos de recolección y sus versiones avanzadas");
Check(GatherKinds.ProfessionKey("fish") == ProfessionCatalog.FisherKey && GatherKinds.UseEvent("fish") == "fish" && GatherKinds.XpEvent("fish") == "fish_xp" && GatherKinds.UnitsEvent("fish") == "fish_caught"
    && GatherKinds.UnitsEvent("chop") == "gathered_units" && GatherKinds.UnitsEvent("mine") == "gathered_units" && GatherKinds.XpEvent("chop") == "chop_xp" && GatherKinds.XpEvent("mine") == "mine_xp",
    "la pesca lleva sus propios contadores (fish, fish_xp, fish_caught); chop y mine siguen con los de siempre");
Check(CooldownCatalog.Fish.CommandName == "fish" && CooldownCatalog.Fish.Duration == TimeSpan.FromMinutes(5) && CooldownCatalog.All.Contains(CooldownCatalog.Fish) && !CooldownCatalog.All.Contains(CooldownCatalog.FishAdvanced)
    && CooldownCatalog.AdvancedOf(CooldownCatalog.Fish) == CooldownCatalog.FishAdvanced && CooldownCatalog.FishAdvanced.Duration == TimeSpan.FromHours(1) && CooldownCatalog.AdvancedOf(CooldownCatalog.Chop) == CooldownCatalog.ChopAdvanced,
    "el cooldown de /fish es de 5 minutos y está en /cd; su versión avanzada es de 1 hora y no figura hasta desbloquearla");
var fisher = ProfessionCatalog.Get(ProfessionCatalog.FisherKey);
Check(ProfessionCatalog.All.Count == 4 && fisher.StatKind == "fish_xp" && fisher.XpPerAction == 1 && fisher.Command == "/fish" && new[] { "pescador", "pescar", "fish", "Pesca", "fisher" }.All(a => ProfessionCatalog.Find(a)?.Key == "fisher"),
    "el oficio Pescador existe (4 oficios), cuenta fish_xp con 1 de XP por punto y se encuentra por pescador / pescar / fish / pesca / fisher");
string xpText = ProfessionRules.DescribeXpSource(fisher);
Check(xpText.Contains("Común 2") && xpText.Contains("Raro 7") && xpText.Contains("Épico 15") && !xpText.Contains("Legendario") && !xpText.Contains("Mítico"), $"la XP por tirada del Pescador cuenta solo sus tres rarezas: «{xpText}»");
Check(ToolRules.KindOfItemType("Caña") == "rod" && ToolRules.IsToolType("Caña") && ToolRules.Kinds.SequenceEqual(["axe", "pickaxe", "rod"]) && ToolRules.KindLabel("rod") == "Caña" && ToolRules.KindCommand("rod") == "/fish" && ToolRules.KindEmoji("rod") == "🎣"
    && ToolRules.KindLabel("axe") == "Hacha" && ToolRules.KindLabel("pickaxe") == "Pico", "la caña es la tercera herramienta (rod): su tipo, su nombre, su comando y su emoji");
var lvl100 = new Dictionary<string, long> { ["fisher"] = ProfessionRules.XpToReach(100) };
var b100 = new PlayerBonuses(PetBonuses.None, 0, null, lvl100);
Check(Math.Abs(b100.FishMultiplier - 1.2) < 1e-9 && Math.Abs(b100.FishRarityUpgrade - 0.1) < 1e-9 && b100.ChopMultiplier == 1.0 && PlayerBonuses.None.FishMultiplier == 1.0 && PlayerBonuses.None.FishRarityUpgrade == 0
    && !b100.IsNone && !new PlayerBonuses(PetBonuses.None, FishToolFactor: 2).IsNone && new PlayerBonuses(PetBonuses.None, FishToolFactor: 1).IsNone,
    "el Pescador al 100 da +20 % de cantidad y +10 % de rareza (solo a la pesca); la caña también cuenta para IsNone");

Console.WriteLine("\n== 2. El catálogo de la base ==");
var fishItems = new Dictionary<string, Item>();
foreach (var rarity in new[] { "Común", "Raro", "Épico" })
{
    var it = await itemRepo.GetRandomByTypeAndRarityAsync("Pescado", rarity);
    if (it is not null) { fishItems[rarity] = it; }
}

Check(fishItems.Count == 3 && fishItems["Común"].Name == "Mojarra" && fishItems["Raro"].Name == "Dorado" && fishItems["Épico"].Name == "Surubí" && (await itemRepo.GetRandomByTypeAndRarityAsync("Pescado", "Legendario")) is null,
    "hay exactamente 3 peces: Mojarra (Común), Dorado (Raro) y Surubí (Épico), y ninguno Legendario");
Check(fishItems["Común"].SellPrice == 2 && fishItems["Raro"].SellPrice == 10 && fishItems["Épico"].SellPrice == 30 && fishItems.Values.All(i => !string.IsNullOrEmpty(i.Emoji)),
    "se venden a 2 / 10 / 30 (como Pino, Roble y Nogal) y nunca salen sin emoji");
var rod3 = await recipesRepo.GetByResultItemNameAsync(RodZ3);
var rod5 = await recipesRepo.GetByResultItemNameAsync(RodZ5);
Check(rod3 is not null && rod5 is not null && rod3.GoldCost == 0 && rod5.GoldCost == 0 && rod3.Ingredients.Count == 4 && rod5.Ingredients.Count == 4 && rod3.ResultItem.StatValue == 2 && rod5.ResultItem.StatValue == 3
    && rod3.ZoneId is not null && rod5.ZoneId is not null && rod3.ResultItem.Type == "Caña" && rod5.ResultItem.Type == "Caña", "las dos cañas: ×2 y ×3, 0 de oro, 4 materiales cada una y la zona que las desbloquea");
Check(rod3!.Ingredients.Concat(rod5!.Ingredients).All(i => i.ItemName is "Madera de Roble" or "Madera de Nogal" or "Hierro" or "Piedra" or "Gema de Zafiro") && ZoneRanking.RankOf(zones, rod3.ZoneId!.Value) == 3 && ZoneRanking.RankOf(zones, rod5.ZoneId!.Value) == 5,
    "piden solo materiales de /chop y /mine, y se abren en la Zona 3 y la Zona 5");
var allRecipes = await recipesRepo.GetAllAsync();
Check(allRecipes.Count(r => ToolRules.IsToolType(r.ResultItem.Type)) == 6 && allRecipes.Where(r => ToolRules.IsToolType(r.ResultItem.Type)).All(r => !RecipeCatalog.IsVisibleTo(r, "Guerrero")),
    "ahora hay 6 recetas de herramientas (2 por tipo) y el herrero no ve ninguna");

Console.WriteLine("\n== 3. La migración add_fishing.sql ==");
string script = File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database", "add_fishing.sql"));
await using (var tx = await db.BeginTransactionAsync())
{
    bool ranTwice = true;
    try { await db.ExecuteAsync(script, transaction: tx); await db.ExecuteAsync(script, transaction: tx); }
    catch (Exception ex) { ranTwice = false; Console.WriteLine("   " + ex.Message); }

    int fishCount = ranTwice ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE type = 'Pescado'", transaction: tx) : -1;
    int rodRecipes = ranTwice ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.type = 'Caña'", transaction: tx) : -1;
    int rodIngredients = ranTwice ? await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM recipe_ingredients ri JOIN recipes r ON r.recipe_id = ri.recipe_id JOIN items i ON i.item_id = r.result_item_id WHERE i.type = 'Caña'", transaction: tx) : -1;
    Check(ranTwice && fishCount == 3 && rodRecipes == 2 && rodIngredients == 8, $"el script corre dos veces seguidas sin duplicar nada ({fishCount} peces, {rodRecipes} recetas, {rodIngredients} ingredientes)");

    await db.ExecuteAsync("INSERT INTO users (discord_id) VALUES (999000000000042198) ON CONFLICT DO NOTHING", transaction: tx);
    bool rodOk = true;
    try { await db.ExecuteAsync("INSERT INTO player_tools (discord_id, kind, item_id) SELECT 999000000000042198, 'rod', item_id FROM items WHERE name = @n", new { n = RodZ3 }, tx); }
    catch (Exception ex) { rodOk = false; Console.WriteLine("   " + ex.Message); }

    Check(rodOk, "player_tools admite el tipo 'rod'");
    await tx.RollbackAsync();
}

await using (var tx = await db.BeginTransactionAsync())
{
    await db.ExecuteAsync("INSERT INTO users (discord_id) VALUES (999000000000042198) ON CONFLICT DO NOTHING", transaction: tx);
    bool rejected = false;
    try { await db.ExecuteAsync("INSERT INTO player_tools (discord_id, kind, item_id) SELECT 999000000000042198, 'bogus', item_id FROM items WHERE name = @n", new { n = RodZ3 }, tx); }
    catch (PostgresException) { rejected = true; }

    Check(rejected, "y sigue rechazando un tipo que no existe (el CHECK no se aflojó de más)");
    await tx.RollbackAsync();
}

Console.WriteLine("\n== 4. La pesca de verdad ==");
await Player(UFish, 10, 0);
var first = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UFish, CooldownCatalog.Fish, "Pescado");
int caught = await Fish(UFish);
Check(first.Embed is not null && !first.Ephemeral && first.Embed.Title.Contains("Pescar") && first.Embed.Title.Contains("🎣") && caught is >= 1 and <= 5 && (first.Embed.Footer?.Text ?? "").Contains("Pescador"),
    $"/fish pesca: «{first.Embed?.Title}», {caught} unidad(es) en la mochila y el pie dice el nivel del Pescador");
Check(await Stat(UFish, "fish") == 1 && await Stat(UFish, "fish_caught") == caught && await Stat(UFish, "fish_xp") is 2 or 7 or 15 && await Stat(UFish, "gathered_units") == 0 && await Stat(UFish, "chop") == 0 && await Stat(UFish, "mine") == 0,
    "los contadores: fish = 1, fish_caught = las unidades y fish_xp = la XP de la rareza; gathered_units, chop y mine NO se tocan");
int cd = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cooldowns WHERE discord_id = @id AND command_name = 'fish'", new { id = (long)UFish });
Check(cd == 1 && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cooldowns WHERE discord_id = @id", new { id = (long)UFish }) == 1, "se guarda UN cooldown, el de fish (no pisa el de chop ni el de mine)");
var again = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UFish, CooldownCatalog.Fish, "Pescado");
Check(again.Embed is not null && again.Ephemeral && again.Embed.Title.Contains("⏳") && await Fish(UFish) == caught, "pescar de nuevo enseguida contesta el cooldown (solo para vos) y no suma nada");
var chopNow = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UFish, CooldownCatalog.Chop, "Madera");
Check(chopNow.Embed is not null && !chopNow.Ephemeral, "y con /fish en cooldown, /chop sigue andando (cada uno tiene el suyo)");
var cdEmbed = await CooldownModule.BuildStatusEmbedAsync(cooldownRepo, users, UFish, bonusService: bonusSvc);
Check(AllText(cdEmbed).Contains("Pescar"), "/cd muestra la línea de Pescar");

await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)UFish });
var deltas = await Cast(UFish, 80);
var held = (await inventory.GetByDiscordIdAsync(UFish)).Where(e => e.Quantity > 0).ToList();
Check(held.All(e => e.Type == "Pescado" && e.ItemName is "Mojarra" or "Dorado" or "Surubí") && held.Any(e => e.ItemName == "Mojarra") && held.Any(e => e.ItemName == "Dorado"),
    "80 pescas: en la mochila solo hay peces (Mojarra y Dorado seguro; el Surubí es del 3 %): " + string.Join(", ", held.Select(e => $"{e.ItemName} x{e.Quantity}")));
Check(deltas.All(d => d is >= 1 and <= 5) && deltas.Any(d => d % 2 == 1) && deltas.Average() is > 1.8 and < 3.2, $"sin caña cada pesca da de 1 a 5 y en promedio ~2,4 ({deltas.Average():0.0})");

Console.WriteLine("\n== 5. La pesca avanzada (oficio al 100) ==");
await Player(UAdv, 10, 0);
var locked = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UAdv, CooldownCatalog.Fish, "Pescado", advanced: true);
Check(locked.Embed is null && locked.Ephemeral && locked.PlainMessage!.Contains("pesca avanzada") && locked.PlainMessage.Contains("Pescador") && await Fish(UAdv) == 0, "con el oficio en 0 la avanzada se rechaza y dice qué oficio hace falta");
await SetStat(UAdv, "fish_xp", ProfessionRules.XpToReach(100));
var adv = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UAdv, CooldownCatalog.Fish, "Pescado", advanced: true);
Check(adv.Embed is not null && !adv.Ephemeral && adv.Embed.Title.Contains("avanzada") && await Fish(UAdv) >= 4 && await Stat(UAdv, "fish") == 4, $"al nivel 100 la avanzada hace 4 sorteos juntos ({await Fish(UAdv)} peces, el contador de usos sube 4)");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cooldowns WHERE discord_id = @id AND command_name = 'fish_adv'", new { id = (long)UAdv }) == 1 && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cooldowns WHERE discord_id = @id AND command_name = 'fish'", new { id = (long)UAdv }) == 0,
    "usa SU cooldown (fish_adv) y no toca el de la pesca normal");
var advAgain = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UAdv, CooldownCatalog.Fish, "Pescado", advanced: true);
var normalAfter = await GatheringModule.ExecuteGatherAsync(cooldownRepo, gatheringRepo, users, itemRepo, bonusSvc, events, UAdv, CooldownCatalog.Fish, "Pescado");
Check(advAgain.Embed is not null && advAgain.Ephemeral && normalAfter.Embed is not null && !normalAfter.Ephemeral, "la avanzada repetida espera su hora y mientras tanto la pesca normal sigue disponible");
var cdAdv = await CooldownModule.BuildStatusEmbedAsync(cooldownRepo, users, UAdv, bonusService: bonusSvc);
Check(AllText(cdAdv).Contains("Pesca avanzada"), "/cd muestra la pesca avanzada una vez desbloqueada");
Check(AllText(cdEmbed).Contains("Pesca avanzada") == false, "…y no la muestra antes de desbloquearla");

Console.WriteLine("\n== 6. La caña ==");
await Player(UCraft, 20, Z2);
foreach (var i in rod3.Ingredients) { await Give(UCraft, i.ItemName, i.Quantity); }
var closed = await ToolsModule.ExecuteCraftAsync(users, recipesRepo, zonesRepo, crafting, toolRepo, events, UCraft, RodZ5, new Random(1));
Check(closed.Embed is null && closed.PlainMessage!.Contains("Zona 5") && (await toolRepo.GetAsync(UCraft)).Count == 0, "la caña de la Zona 5 está cerrada hasta desbloquear esa zona y no gasta nada");
var made = await ToolsModule.ExecuteCraftAsync(users, recipesRepo, zonesRepo, crafting, toolRepo, events, UCraft, RodZ3, new Random(1));
var worn = await toolRepo.GetAsync(UCraft);
Check(made.Embed is not null && made.Embed.Title.Contains("lista") && made.Embed.Description.Contains("caña") && worn.Count == 1 && worn[0].Kind == "rod" && worn[0].Multiplier == 2, "el Carpintero arma la Caña de Hierro Forjado y queda puesta como tu caña (×2)");
bool spent = true; foreach (var i in rod3.Ingredients) { spent &= await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items t ON t.item_id = inv.item_id WHERE inv.discord_id = @id AND t.name = @n", new { id = (long)UCraft, n = i.ItemName }) == 0; }
Check(spent, "gastó exactamente los materiales (sin oro)");
var sameAgain = await ToolsModule.ExecuteCraftAsync(users, recipesRepo, zonesRepo, crafting, toolRepo, events, UCraft, RodZ3, new Random(1));
Check(sameAgain.Embed is null && sameAgain.PlainMessage!.Contains("caña"), "una igual no se vuelve a armar (y dice «caña», no «hacha»)");
var scene = await ToolsModule.BuildSceneAsync(users, recipesRepo, zonesRepo, inventory, toolRepo, UCraft, new Random(1));
var menu = scene.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single();
Check(scene.Embed.Description.Contains("**Caña:**") && scene.Embed.Description.Contains(RodZ3) && scene.Embed.Description.Contains("**Hacha:**") && scene.Embed.Description.Contains("**Pico:**") && menu.Options.Count == 5
    && menu.Options.Any(o => o.Label.Contains(RodZ5) && o.Label.StartsWith("🔒")) && !menu.Options.Any(o => o.Value == RodZ3),
    $"la escena del Carpintero muestra las tres herramientas y ofrece lo que falta ({menu.Options.Count} opciones, la caña de la Zona 5 con candado)");
await Player(URod, 30, Z4); await Player(UBase, 30, Z4);
await db.ExecuteAsync("INSERT INTO player_tools (discord_id, kind, item_id) SELECT @id, 'rod', item_id FROM items WHERE name = @n", new { id = (long)URod, n = RodZ3 });
var bRod = await bonusSvc.GetAsync(URod, 0); var bNone = await bonusSvc.GetAsync(UBase, 0);
Check(bRod.FishToolFactor == 2 && bRod.ChopToolFactor == 1 && bRod.MineToolFactor == 1 && !bRod.IsNone && bNone.FishToolFactor == 1 && bNone.IsNone, "el servicio de bonus lee la caña: ×2 solo para /fish, el hacha y el pico siguen ×1");
var withRod = await Cast(URod, 60); var noRod = await Cast(UBase, 60);
Check(withRod.All(d => d >= 2 && d % 2 == 0) && noRod.Any(d => d % 2 == 1), "con la caña ×2 cada pesca da siempre una cantidad PAR de al menos 2 (la base multiplicada por 2); sin ella salen impares");
Check(withRod.Sum() > 1.6 * noRod.Sum() && withRod.Sum() < 2.6 * noRod.Sum(), $"60 pescas: con la caña sale ~2× lo de sin ella ({withRod.Sum()} contra {noRod.Sum()})");
int wood = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory i JOIN items t ON t.item_id = i.item_id WHERE i.discord_id = @id AND t.type IN ('Madera', 'Mineral')", new { id = (long)URod });
Check(wood == 0, "la pesca no entrega madera ni minerales");

Console.WriteLine("\n== 7. El Fuego Nuevo la borra ==");
await Player(UFn, 30, Z4);
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)UFn });
await db.ExecuteAsync("INSERT INTO player_tools (discord_id, kind, item_id) SELECT @id, 'rod', item_id FROM items WHERE name = @r UNION ALL SELECT @id, 'axe', item_id FROM items WHERE name = 'Hacha de Hierro Forjado'", new { id = (long)UFn, r = RodZ5 });
await Give(UFn, "Mojarra", 7); await Give(UFn, "Surubí", 2);
var renewed = await sp.GetRequiredService<IFuegoNuevoRepository>().RenewAsync(UFn, 0, "Ninja");
Check(renewed.Status == RenewStatus.Ok && (await toolRepo.GetAsync(UFn)).Count == 0, $"renacer deja la caña (y el hacha) en ×1: hay que armarlas de nuevo en cada vuelta ({renewed.Status})");
Check(await Fish(UFn) == 0, "y el pescado se va con la mochila (los materiales no sobreviven al Fuego Nuevo)");

Console.WriteLine("\n== 8. El inventario ==");
InventoryEntry E(string name, string type, int qty, string rarity = "Común", string? emoji = null) => new() { ItemName = name, Type = type, Quantity = qty, Rarity = rarity, Emoji = emoji };
var sample = new List<InventoryEntry> { E("Madera de Pino", "Madera", 5), E("Hierro", "Mineral", 2, "Raro"), E("Mojarra", "Pescado", 8, "Común", "🐟"), E("Surubí", "Pescado", 1, "Épico", "👑"), E("Cuero Curtido de Pradera", "Material", 7), E("Mate Amargo", "Consumable", 4) };
var (tabGather, menuGather) = GameModule.BuildInventoryView(sample, "Agus", 1, 2, null);
Check(tabGather.Fields.Any(f => f.Name == "Pescado" && f.Value.Contains("Mojarra") && f.Value.Contains("Surubí")) && tabGather.Fields.Any(f => f.Name == "Madera") && tabGather.Fields.Any(f => f.Name == "Mineral") && !AllText(tabGather).Contains("Cuero Curtido"),
    "la pestaña Recolección muestra Madera, Mineral y Pescado (y nada de los drops)");
Check(tabGather.Description.StartsWith("**🪵⛏️ 16**"), $"y su cuenta de unidades suma el pescado (5 + 2 + 8 + 1 = 16): «{tabGather.Description}»");
var (tabAll, _) = GameModule.BuildInventoryView(sample, "Agus", 1, 2, GameModule.InvAll);
Check(tabAll.Fields.Any(f => f.Name == "Pescado" && f.Value.Contains("Mojarra")), "la pestaña Todo también lo muestra");
var onlyFish = GameModule.BuildInventoryView([E("Mojarra", "Pescado", 3)], "Agus", 1, 2, null);
Check(onlyFish.Embed.Fields.Any(f => f.Value.Contains("Mojarra")) && onlyFish.Components is null, "con solo pescado abre la Recolección (una sola categoría: sin desplegable)");
var enLoc = Loc.Use("en");
var tabEn = GameModule.BuildInventoryView(sample, "Agus", 1, 2, null).Embed;
enLoc.Dispose();
Check(tabEn.Fields.Any(f => f.Name == "Fish"), "en inglés el campo se llama «Fish»");

Console.WriteLine("\n== 9. Vender y desmantelar ==");
Check(Dismantling.CanDismantle("Pescado") && !Dismantling.CanDismantle("Caña") && Dismantling.DustPerUnit("Común") == 1 && Dismantling.DustPerUnit("Raro") == 6 && Dismantling.DustPerUnit("Épico") == 24, "el pescado se puede desmantelar (Polvo por rareza: 1 / 6 / 24) y las herramientas no");
Check(!TradeRules.IsTradeable(new Item { Name = "Mojarra", Type = "Pescado", Rarity = "Común" }), "el pescado no se comercia con otros jugadores (/trade es de madera y minerales)");

Console.WriteLine("\n== 10. El logro y los títulos ==");
var fishAch = AchievementCatalog.Find("pescador");
Check(fishAch is not null && fishAch.StatKind == "fish" && fishAch.Tiers.Select(t => t.Threshold).SequenceEqual([25L, 150, 600]) && fishAch.Tiers.All(t => t.Reward.Box == BoxGrant.None) && AchievementCatalog.Categories.Single(c => c.Name == "Oficios").Keys.Contains("pescador"),
    "el logro Pescador cuenta usos de /fish (25 / 150 / 600), paga solo oro y EXP y está en la página Oficios");
Check(AchievementCatalog.All.Count == 33 && AchievementCatalog.Categories.SelectMany(c => c.Keys).Distinct().Count() == 33, "33 logros, cada uno en una sola página");
var fishTitles = TitleCatalog.All.Where(t => t.AchievementKey == "pescador").ToList();
var masterTitles = TitleCatalog.All.Where(t => t.Key.StartsWith("fisher_")).ToList();
Check(fishTitles.Count == 3 && fishTitles.Select(t => t.Name).SequenceEqual(["Cañero", "Lobo de Río", "Señor del Surubí"]) && masterTitles.Count == 3 && masterTitles.Select(t => t.Name).SequenceEqual(["Pescador de Orilla", "Capitán del Río", "Rey del Dorado"]),
    "3 títulos del logro y 3 de maestro del oficio (nivel 25, 50 y 100)");
Check(masterTitles.All(t => t.PageKey == "maestros" && t.Source == "fish_xp") && TitleCatalog.All.Count == 143 && TitleCatalog.All.Select(t => t.Key).Distinct().Count() == 143 && TitleCatalog.All.Select(t => t.Name).Distinct().Count() == 143
    && TitleCatalog.Pages.All(p => TitleCatalog.OfPage(p.Key).Count <= 25), "143 títulos con claves y nombres únicos y ninguna página pasa de los 25 que admite el desplegable");

Console.WriteLine("\n== 11. Recordatorios, oficios y ayuda ==");
Check(ReminderCatalog.All.Count == 15 && ReminderCatalog.Find("fish")?.Command == "/fish" && ReminderCatalog.Find("fish_adv") is not null && ReminderCatalog.Find("fish")!.AutoDelete == ReminderCatalog.ShortLived && ReminderCatalog.Find("fish_adv")!.AutoDelete is null,
    "hay 15 tipos de aviso; el de pescar se borra solo a los 2 minutos como el de talar y minar, y el de la avanzada no");
var due = ReminderCatalog.FromCooldowns([("fish", DateTime.UtcNow), ("fish_adv", DateTime.UtcNow)], DateTime.UtcNow);
Check(due.Select(d => d.Kind).OrderBy(k => k).SequenceEqual(["fish", "fish_adv"]) && due.Single(d => d.Kind == "fish").DueUtc - DateTime.UtcNow < TimeSpan.FromMinutes(5.1) && due.Single(d => d.Kind == "fish_adv").DueUtc - DateTime.UtcNow > TimeSpan.FromMinutes(59),
    "los avisos salen de los cooldowns reales: la pesca a los 5 minutos y la avanzada a la hora");
var profEmbed = ProfessionModule.BuildEmbed(b100);
Check(profEmbed.Fields.Count(f => f.Name.Contains("Pescador")) == 1 && AllText(profEmbed).Contains("/fish") && profEmbed.Length < 6000, "/professions lista al Pescador con su comando y la pantalla entra en Discord");
var gatherTopic = HelpTopics.Find("fish");
string gatherText = gatherTopic is null ? string.Empty : AllText(HelpTopics.Render(gatherTopic));
Check(gatherTopic?.Key == "gather" && HelpTopics.Find("pescar")?.Key == "gather" && gatherText.Contains("/fish") && gatherText.Contains("78") && gatherText.Contains("Surubí") == false && gatherText.Contains("caña"), "/info tema:fish abre el tema de recolección, que cuenta la pesca con sus chances reales");
using (Loc.Use("en"))
{
    string gatherEn = AllText(HelpTopics.Render(gatherTopic!));
    Check(gatherEn.Contains("Fishing") && gatherEn.Contains("78") && LeakCheck.NoSpanish(gatherEn, []).Ok, "y en inglés el tema sale sin español colado");
}

Check(HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("`/fish`")), "/info nombra /fish");
Check(HelpTopics.Find("pescador")?.Key == "professions", "y /info tema:pescador abre el de los oficios");

Console.WriteLine("\n== 12. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(ToolsModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "fish");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Count == 1 && slash.Parameters[0].Name == "modo" && slash.Parameters[0].Choices.Select(c => c.Value.ToString()).SequenceEqual(["normal", "avanzada"]), $"/fish existe con su parámetro modo (normal / avanzada) y {slash.Description.Length} caracteres de descripción (≤ 100)");
Check(interactions.SlashCommands.Single(c => c.Name == "tools").Description.Length <= 100 && interactions.SlashCommands.Single(c => c.Name == "professions").Description.Length <= 100, "las descripciones nuevas de /tools y /professions entran en los 100 caracteres de Discord");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "fish", "pescar", "pesca", "f" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "fish"), "aa fish / pescar / pesca / f son el mismo comando y ningún otro lo pisa");
Check(texts.Search("f avanzada") is { IsSuccess: true } fAdv && fAdv.Commands[0].Command.Name == "fish", "aa f avanzada llega al mismo comando (el parámetro modo sigue ahí)");
Check(!FightLock.IsAllowed("fish"), "en plena pelea /fish queda bloqueado (como todo lo que no es de la pelea)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(GatheringModule)); ActivatorUtilities.CreateInstance(sp, typeof(ToolsModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built, "los módulos se construyen con el DI real");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
