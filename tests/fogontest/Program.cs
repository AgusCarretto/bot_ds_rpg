using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.11.0: El Fogón Eterno (zona 0). Reglas puras, base de datos (usuario descartable), combate, victoria transaccional, recetas, ayuda.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
Embed CallE(Type t, string method, params object?[] args) => (Embed)t.GetMethod(method, AnyS)!.Invoke(null, args)!;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var monstersRepo = new MonsterRepository(factory);
var recipesRepo = new RecipeRepository(factory);
var adventure = new AdventureRepository(factory);
var buffRepo = new BuffRepository(factory);
var pets = new PetRepository(factory);
var combat = new CombatSessionService(users);
var evts = new RecordingEvents();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000011000UL;
async Task Reset(int level = 30, int highest = 0)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET level = @level, max_hp = 600, current_hp = 600, highest_zone_cleared = @highest, gold = 100000 WHERE discord_id = @id", new { id = (long)U, level, highest });
}
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;
async Task Equip(string weapon, string amulet) =>
    await db.ExecuteAsync("UPDATE users SET weapon_id = (SELECT item_id FROM items WHERE name = @w), amulet_id = (SELECT item_id FROM items WHERE name = @a) WHERE discord_id = @id", new { id = (long)U, w = weapon, a = amulet });

var normal = (await zonesRepo.GetAllAsync()).ToList();
var ordered = ZoneRanking.OrderByDifficulty(normal);
int lastZoneId = ordered[^1].ZoneId, z4 = ordered[3].ZoneId;
var gate = (await zonesRepo.GetGateAsync())!;

Console.WriteLine("== 1. La puerta en la base y la escalera intacta ==");
Check(normal.Count == 5 && normal.All(z => z.Kind == "normal" && z.ZoneId > 0), "GetAllAsync devuelve solo las 5 zonas de la escalera (la puerta no está)");
Check(gate is { ZoneId: 0, Kind: "gate", Name: "El Fogón Eterno", MinLevel: 25 } && (await zonesRepo.GetByIdAsync(0))?.Kind == "gate", "la puerta existe aparte: zona 0, «El Fogón Eterno», nivel 25");
Check((await monstersRepo.GetAllAsync()).Count == 20 && (await monstersRepo.GetAllAsync()).All(m => m.ZoneId > 0), "GetAllAsync de monstruos sigue en 20 (el Asador no entra en /drops, cajas, trueque)");
var boss = await monstersRepo.GetBossByZoneAsync(FogonRules.GateZoneId);
Check(boss is { Name: "El Asador Eterno", MinHp: 2503, MaxHp: 3001, MinDamage: 233, MaxDamage: 289, GoldBonus: 3000, XpBonus: 300 } && boss.DropItemNames.SequenceEqual(["Brasero del Fogón"]), "El Asador Eterno: HP 2503-3001 (×1,25 en la v0.17.1), daño 233-289 (v0.15.1), oro 3000 / XP 300 (v0.14.0) y suelta el Brasero del Fogón");
Check(await monstersRepo.GetTravelMonsterByZoneAsync(0) is null && (await monstersRepo.GetMonstersByZoneAsync(0)).Count == 0, "la puerta no tiene pool de /hunt ni monstruo de /travel");
var recipes = await recipesRepo.GetAllAsync();
var gateRecipes = recipes.Where(r => r.ZoneId == 0).ToList();
Check(gateRecipes.Count == 2 && gateRecipes.All(r => !r.Affinity && r.GoldCost == 25000 && r.Ingredients.Count == 8) && gateRecipes.Select(r => r.ResultItem.Name).OrderBy(n => n).SequenceEqual([FogonRules.AmuletName, FogonRules.WeaponName]), "dos recetas del Fogón: el arma y el amuleto, 25.000 de oro y 8 ingredientes cada una");
var wRec = gateRecipes.Single(r => r.ResultItem.Name == FogonRules.WeaponName); var aRec = gateRecipes.Single(r => r.ResultItem.Name == FogonRules.AmuletName);
Check(wRec.ResultItem is { Type: "Weapon", StatValue: 128, WeaponFamily: null, ClassRequirement: null, Rarity: "Mítico" } && aRec.ResultItem is { Type: "Amulet", StatValue: 165, ClassRequirement: null }, "Trinche +128 ATQ sin familia ni clase; Brasa +165 DEF (v0.15.1); los dos Míticos");
string[] huntDrops = ["Colmillo de Jabalí", "Garra de Puma Cenizo", "Yunque Fragmentado", "Núcleo de Magma", "Corazón de Titán", "Pluma de Ñandú", "Esencia Espectral", "Gema en Bruto", "Escama Ígnea", "Fragmento de Alma"];
var allMonsters = await monstersRepo.GetAllAsync();
Check(huntDrops.All(d => allMonsters.Any(m => m.Kind == MonsterKind.Hunt && m.Monster.DropItemNames.Contains(d))) && gateRecipes.SelectMany(r => r.Ingredients).Count(i => huntDrops.Contains(i.ItemName)) == 10 && gateRecipes.SelectMany(r => r.Ingredients).Where(i => huntDrops.Contains(i.ItemName)).All(i => i.Quantity == 5), "los 10 drops de CACERÍA de las 5 zonas, 5 de cada uno, repartidos entre el arma y el amuleto");
Check(gateRecipes.SelectMany(r => r.Ingredients).Count(i => i.ItemName is "Corteza del Árbol de Vida" or "Fragmento de Meteorito") == 2 && wRec.Ingredients.Any(i => i.ItemName == "Corteza del Árbol de Vida" && i.Quantity == 1) && aRec.Ingredients.Any(i => i.ItemName == "Fragmento de Meteorito" && i.Quantity == 1), "un Mítico de recolección por pieza (v0.15.1): Corteza x1 en el Trinche y Meteorito x1 en la Brasa");

Console.WriteLine("\n== 2. Reglas puras ==");
Item Gear(string name, string type) => new() { Name = name, Type = type };
var kw = Gear(FogonRules.WeaponName, "Weapon"); var ka = Gear(FogonRules.AmuletName, "Amulet");
Check(FogonRules.IsGateOpen(ordered, lastZoneId) && !FogonRules.IsGateOpen(ordered, z4) && !FogonRules.IsGateOpen(ordered, 0) && !FogonRules.IsGateOpen([], 3), "la puerta se abre solo al vencer al jefe de la ÚLTIMA zona (por posición, no por id)");
Check(FogonRules.CheckEntry(ordered, gate, 30, z4, kw, ka).Status == FogonRules.EntryStatus.NotOpen && FogonRules.CheckEntry(ordered, gate, 24, lastZoneId, kw, ka).Status == FogonRules.EntryStatus.LevelTooLow, "sin abrir → NotOpen; con 24 de nivel → LevelTooLow");
var noGear = FogonRules.CheckEntry(ordered, gate, 30, lastZoneId, null, ka); var noAmu = FogonRules.CheckEntry(ordered, gate, 30, lastZoneId, kw, null); var none = FogonRules.CheckEntry(ordered, gate, 30, lastZoneId, Gear("Espada", "Weapon"), Gear("Amuleto", "Amulet"));
Check(noGear is { Status: FogonRules.EntryStatus.MissingGear, MissingWeapon: true, MissingAmulet: false } && noAmu is { MissingWeapon: false, MissingAmulet: true } && none is { MissingWeapon: true, MissingAmulet: true } && FogonRules.CheckEntry(ordered, gate, 30, lastZoneId, kw, ka).Status == FogonRules.EntryStatus.Ok, "falta el arma / el amuleto / los dos / nada");
Check(FogonRules.MissingText(none).Contains("Trinche") && FogonRules.MissingText(none).Contains("Brasa") && FogonRules.MissingText(noAmu).Contains("Brasa") && !FogonRules.MissingText(noAmu).Contains("Trinche"), "el texto de lo que falta nombra la pieza justa");
Check(FogonRules.HasKeyGear(kw, ka) && !FogonRules.HasKeyGear(kw, Gear("Otro", "Amulet")) && !FogonRules.HasKeyGear(null, ka), "el equipo se reconoce por el nombre");

Console.WriteLine("\n== 3. /zona 0 ==");
await Reset(level: 30, highest: z4);
var e1 = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, 0, itemsRepo);
Check(e1.Message!.Contains("se abre cuando vencés") && !(await Me()).InGate, "sin vencer al jefe de la última zona: «se abre cuando vencés al jefe de ...»");
await Reset(level: 24, highest: lastZoneId);
Check((await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, 0, itemsRepo)).Message!.Contains("requiere nivel **25**"), "con la puerta abierta pero nivel 24: pide nivel 25");
await Reset(level: 30, highest: lastZoneId);
var e3 = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, 0, itemsRepo);
Check(e3.Message!.Contains("PUESTOS") && e3.Message.Contains("Trinche") && e3.Message.Contains("Brasa") && !(await Me()).InGate, "sin el equipo puesto: avisa qué pieza falta");
await Equip(FogonRules.WeaponName, FogonRules.AmuletName);
int zoneBefore = (await Me()).CurrentZoneId;
var e4 = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, 0, itemsRepo);
var me4 = await Me();
Check(e4.Embed is { } eb && eb.Title!.Contains("Entraste") && me4.InGate && me4.CurrentZoneId == zoneBefore, "con el equipo: entra, queda in_gate y su zona actual NO cambia");
Check((await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, 0, itemsRepo)).Message!.Contains("Ya estás"), "entrar de nuevo: «Ya estás»");
var list = await ZoneModule.BuildZoneListEmbedAsync(users, zonesRepo, U);
Check(list.Fields.Length == 6 && list.Fields[5].Name.Contains("Fogón") && list.Fields[5].Name.Contains("acá estás") && !list.Fields[4].Name.Contains("acá estás"), "/zonas muestra el Fogón como la 6.ª (con 📍) y la zona normal deja de decir «acá estás»");
await Reset(level: 30, highest: z4);
Check((await ZoneModule.BuildZoneListEmbedAsync(users, zonesRepo, U)).Fields.Length == 5, "antes de abrir la puerta, /zonas no la muestra (sorpresa)");
await Reset(level: 30, highest: lastZoneId); await Equip(FogonRules.WeaponName, FogonRules.AmuletName); await users.SetInGateAsync(U, true);
var choices = ZoneChoices.For(normal, await Me(), new Dictionary<int, string>(), "", gate);
Check(choices.Count == 6 && choices[5].Value.ToString() == "0" && choices[5].Name.Contains("estás acá") && !choices[4].Name.Contains("estás acá"), "el autocompletado de /zona ofrece la 0 solo cuando la puerta está abierta");
Check(ZoneChoices.For(normal, new User { Level = 30, HighestZoneCleared = z4, CurrentZoneId = z4 }, new Dictionary<int, string>(), "", gate).Count == 5, "y no la ofrece antes de abrirse");
var leave = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, ordered[0].ZoneId, itemsRepo);
Check(leave.Embed is not null && !(await Me()).InGate && (await Me()).CurrentZoneId == ordered[0].ZoneId, "viajar a una zona normal saca del Fogón");
await users.ChangeZoneAsync(U, lastZoneId); await users.SetInGateAsync(U, true);
var leaveSame = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, lastZoneId, itemsRepo);
Check(leaveSame.Embed is not null && !(await Me()).InGate, "volver a TU misma zona estando en el Fogón también te saca (no dice «ya estás»)");
Check((await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monstersRepo, U, 99, itemsRepo)).Message!.Contains("No existe ninguna zona"), "una zona que no existe sigue avisando");

Console.WriteLine("\n== 4. Combate en la puerta ==");
var starter = new AdventureCombatStarter(new CooldownRepository(factory), adventure, users, itemsRepo, monstersRepo, zonesRepo, buffRepo, new PlayerBonusService(pets, new BlessingRepository(factory), new GameEventRepository(factory)), combat, new RaidSessionService());
await Reset(level: 30, highest: lastZoneId); await Equip(FogonRules.WeaponName, FogonRules.AmuletName); await users.SetInGateAsync(U, true);
Check((await starter.PrepareHuntAsync(U)).Status == CombatStartStatus.InGate && (await starter.PrepareTravelAsync(U)).Status == CombatStartStatus.InGate, "en la puerta no andan /hunt ni /travel");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM cooldowns WHERE discord_id = @id", new { id = (long)U }) == 0, "y no gastan ningún cooldown");
var raidReject = await RaidModule.ValidateStartAsync(users, itemsRepo, monstersRepo, zonesRepo, combat, new RaidSessionService(), U);
Check(raidReject is { Embed: not null } && raidReject.Embed.Title!.Contains("Fogón"), "ni /raid");
await db.ExecuteAsync("UPDATE users SET weapon_id = NULL WHERE discord_id = @id", new { id = (long)U });
Check((await starter.PrepareBossAsync(U)).Status == CombatStartStatus.GateGearMissing && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM cooldowns WHERE discord_id = @id", new { id = (long)U }) == 0, "/boss sin el equipo puesto (lo vendió): GateGearMissing y sin gastar el cooldown");
await Equip(FogonRules.WeaponName, FogonRules.AmuletName);
var started = await starter.PrepareBossAsync(U);
Check(started is { Status: CombatStartStatus.Started, State: { MonsterName: "El Asador Eterno", BossZoneId: 0, CommandName: "boss" } }, "/boss en la puerta enfrenta al Asador Eterno (BossZoneId = 0)");
int expectedDef = CombatStats.TotalDefense(30, 165);
Check(started.State!.PlayerDefense == expectedDef && started.State.MonsterMaxHp is >= 2503 and <= 3001, $"con la defensa del amuleto del Fogón ({started.State.PlayerDefense}) y la vida del jefe ({started.State.MonsterMaxHp})");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });

Console.WriteLine("\n== 5. Ganarle al Asador ==");
await Reset(level: 30, highest: lastZoneId); await Equip(FogonRules.WeaponName, FogonRules.AmuletName); await users.SetInGateAsync(U, true);
await db.ExecuteAsync("UPDATE users SET gold = 0 WHERE discord_id = @id", new { id = (long)U });
var win = await adventure.ApplyBossVictoryAsync(U, 22000, 22000, 0, null, 1, FogonRules.GateZoneId);
var after = await Me();
Check(win.Gate == GateEvent.Cleared && after is { GateCleared: true, InGate: false } && after.CurrentZoneId == lastZoneId && after.HighestZoneCleared == lastZoneId, "ganar: gate_cleared, sale de la puerta, vuelve a la última zona y highest_zone_cleared no se toca");
Check(after.Gold == 22000 && win.EggGranted is { Name: "Chispa del Asador" } && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM inventory WHERE discord_id = @id", new { id = (long)U }) == 1, "cobra el premio de siempre y, desde la v0.14.0, el huevo de la Chispa del Asador (sin drop: el Brasero lo pone el módulo)");
await users.SetInGateAsync(U, true);
var win2 = await adventure.ApplyBossVictoryAsync(U, 10, 10, 0, null, 1, FogonRules.GateZoneId);
Check(win2.Gate == GateEvent.Cleared && (await Me()).GateCleared, "ganarle otra vez sigue siendo válido (cooldown de 5 h aparte)");
var raceUser = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, FogonRules.GateZoneId)));
Check(raceUser.All(r => r.Gate == GateEvent.Cleared) && (await Me()).Gold == 22000 + 10 + 6 && (await Me()).CurrentZoneId == lastZoneId, "6 victorias a la vez: cada una cobra una vez y el jugador queda coherente");

await Reset(level: 30, highest: z4);
var open1 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, lastZoneId);
var open2 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, lastZoneId);
await Reset(level: 30, highest: ordered[2].ZoneId);
var notLast = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, z4);
var notBoss = await adventure.ApplyVictoryAsync(U, 1, 1, 0, null, 1);
Check(open1.Gate == GateEvent.Opened && open2.Gate == GateEvent.None && notLast.Gate == GateEvent.None && notBoss.Gate == GateEvent.None && !(await Me()).GateCleared, "la primera victoria sobre el jefe de la última zona AVISA que se abrió la puerta; las demás no");

await Reset(level: 30, highest: lastZoneId);
var rec = new RecordingEvents();
await rec.RecordVictoryAsync(U, GameEventKinds.BossWin, new LevelUpOutcome(await Me(), 0, null, GateEvent.Cleared));
await rec.RecordVictoryAsync(U, GameEventKinds.BossWin, new LevelUpOutcome(await Me(), 0));
Check(rec.Kinds.Count(k => k == "gate_win") == 1 && rec.Kinds.Count(k => k == "boss_win") == 2, "el evento gate_win sale solo cuando se le ganó al Asador");
var asador = AchievementCatalog.Find("asador");
Check(asador is { StatKind: "gate_win" } && asador.Tiers.Select(t => t.Threshold).SequenceEqual([1L, 3L, 10L]) && asador.Tiers.All(t => t.Reward.Box == BoxGrant.None) && AchievementCatalog.Categories[0].Keys.Contains("asador"), "logro Asador (1/3/10): solo oro y XP, en Combate");

Console.WriteLine("\n== 6. Recetas, herrería y consejo ==");
var all6 = ZoneRanking.OrderByDifficulty(normal);
var vOpen = RecipeCatalog.ViewFor(recipes, normal, "Guerrero", lastZoneId, includeGate: true);
var vClosed = RecipeCatalog.ViewFor(recipes, normal, "Guerrero", lastZoneId);
Check(vOpen.GateRecipes!.Count == 2 && vOpen.Recipes.Count == vClosed.Recipes.Count + 2 && vClosed.Recipes.All(r => r.ZoneId != 0) && vClosed.GateRecipes is null or { Count: 0 }, "ViewFor: con la puerta abierta suma las 2 recetas del Fogón; cerrada, ninguna");
var pOpen = new User { Class = "Guerrero", Level = 30, HighestZoneCleared = lastZoneId, CurrentZoneId = lastZoneId, Gold = 100000 };
var pClosed = new User { Class = "Guerrero", Level = 30, HighestZoneCleared = z4, CurrentZoneId = lastZoneId, Gold = 100000 };
var owned0 = new Dictionary<string, int>();
Check(ForgeChoices.For(recipes, normal, pOpen, owned0, "").Any(c => c.Value.ToString() == FogonRules.WeaponName) && !ForgeChoices.For(recipes, normal, pClosed, owned0, "").Any(c => c.Value.ToString() == FogonRules.WeaponName), "el menú y el autocompletado de /forge ofrecen el equipo del Fogón solo con la puerta abierta");
var page = ForgeModule.RenderRecipesEmbed(recipes, normal, "Guerrero", null, 30, highestZoneCleared: lastZoneId);
var pageClosed = ForgeModule.RenderRecipesEmbed(recipes, normal, "Guerrero", null, 30, highestZoneCleared: z4);
var pageZ1 = ForgeModule.RenderRecipesEmbed(recipes, normal, "Guerrero", ordered[0].ZoneId, 30, highestZoneCleared: lastZoneId);
Check(page.Fields.Any(f => f.Name == "🔥 El Fogón Eterno" && f.Value.Split('\n').Length == 2 && f.Value.Contains(FogonRules.WeaponName)) && !AllText(pageClosed).Contains("Fogón") && !AllText(pageZ1).Contains("Fogón"), "el recetario suma el bloque del Fogón (sus 2 piezas) solo con la puerta abierta (y no si pedís una sola zona)");
Check(page.Length <= 6000 && page.Fields.All(f => f.Value.Length <= 1024), $"y entra en los límites de Discord ({page.Length} / 6000)");
var scene = await BlacksmithModule.BuildSceneAsync(users, recipesRepo, zonesRepo, new InventoryRepository(factory), U);
await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = @h, current_zone_id = @z WHERE discord_id = @id", new { id = (long)U, h = lastZoneId, z = lastZoneId });
var scene2 = await BlacksmithModule.BuildSceneAsync(users, recipesRepo, zonesRepo, new InventoryRepository(factory), U);
var menu2 = scene2.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().First();
Check(menu2.Options.Any(o => o.Value == FogonRules.WeaponName) && menu2.Options.Count <= 25, "la herrería lista el equipo del Fogón en su menú");

Console.WriteLine("\n== 7. Textos: victoria, ayuda y perfil ==");
CombatState Boss() => new("boss", "El Asador Eterno", "🔥", 100, 60, 10, [], 0, 0, 0, 100, 80, 100, 10, 2, 5, "Guerrero", ClassPassives.For("Guerrero"));
var turn = new TurnResult(null, false, 12, 0, 0, 60, 100, new MonsterHitOutcome(false, 7), 93, default);
var rw = new CombatReward(10, 10, false);
var uu = new User { MaxHp = 100, CurrentHp = 80, Level = 5, Class = "Guerrero" };
var vCleared = CallE(typeof(AdventureModule), "BuildVictoryEmbed", Boss(), turn, rw, null, new LevelUpOutcome(uu, 0, null, GateEvent.Cleared), false);
var vOpened = CallE(typeof(AdventureModule), "BuildVictoryEmbed", Boss(), turn, rw, null, new LevelUpOutcome(uu, 0, null, GateEvent.Opened), true);
var vPlain = CallE(typeof(AdventureModule), "BuildVictoryEmbed", Boss(), turn, rw, null, new LevelUpOutcome(uu, 0), true);
Check(AllText(vCleared).Contains("El Asador Eterno cayó") && AllText(vCleared).Contains("Fuego Nuevo") && AllText(vOpened).Contains("Se abrió **El Fogón Eterno**") && AllText(vOpened).Contains("/zona 0") && !AllText(vPlain).Contains("Fogón"), "la victoria sobre el Asador y sobre el jefe de la última zona cuentan lo que pasó; una común no menciona el Fogón");
var topic = HelpTopics.Find("fogon");
Check(topic?.Key == "fogon" && HelpTopics.Find("asador")?.Key == "fogon" && HelpTopics.Find("zona 0")?.Key == "fogon" && HelpTopics.Find("puerta")?.Key == "fogon", "«info fogon / asador / zona 0 / puerta» abren el tema");
var te = topic!.Build();
Check(te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024) && AllText(te).Contains("/zona 0") && AllText(te).Contains("/boss") && AllText(te).Contains(FogonRules.WeaponName) && AllText(te).Contains("5 h"), $"el tema explica cómo entrar, el equipo y el jefe, dentro de los límites ({te.Length})");
var info = HelpModule.BuildInfoEmbed();
Check(AllText(info).Contains("/zona 0") && info.Length <= 6000 && info.Fields.All(f => f.Value.Length <= 1024), $"/info lista /zona 0 dentro de los límites ({info.Length})");
await db.ExecuteAsync("UPDATE users SET in_gate = true, gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
var prof = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets);
string gateZone = ProfileProbe.ZoneLine(prof);
Check(gateZone.StartsWith("🔥 **Zona:** 0 ") && gateZone.Contains("tu zona:") && prof.Description.Contains("Venció al Asador"), $"el perfil dice que está en la Zona 0 (El Fogón) con su zona de siempre entre paréntesis ({gateZone}) y que venció al Asador");
var lines = Enumerable.Range(0, 40).Select(_ => NpcDialogue.Monster("El Asador Eterno", "🔥", NpcDialogue.BossLine.Intro, true)).Distinct().ToList();
Check(lines.Count >= 2 && lines.All(l => l.Contains("El Asador Eterno")) && !lines.Any(l => l.Contains("Nadie pasa por aquí")), "el Asador tiene sus propias frases (no las genéricas de un jefe nuevo)");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
