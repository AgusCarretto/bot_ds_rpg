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

// v0.39.0: las EXPEDICIONES de las mascotas (el dueño, 2026-10-29): una mascota de nivel 10 sale por 1, 4 u 8 horas (cuesta 2 / 10 / 25 Comidas para Mascotas), de a una por jugador, y al volver el mismo comando
// recoge el premio —IGUAL PARA TODOS, sin importar la zona: un oro del rango de esa duración, pensado para la Zona 5, y 5 / 20 / 50 % de un nivel de EXP— más, con chance mayor mientras más dura, un hallazgo de su
// especie. Reglas puras, el repositorio contra la base LOCAL (usuario descartable, concurrencia, premios exactos), las pantallas, el aviso de que volvió, la línea de /cd, el comando y el menú registrados
// con el InteractionService real, DI y ayuda.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var petRepo = new PetRepository(factory);
var expRepo = new PetExpeditionRepository(factory);
var invRepo = new InventoryRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var eventRepo = new GameEventRepository(factory);
var events = new GameEventService(eventRepo);
var cooldowns = new CooldownRepository(factory);
var reminderRepo = new ReminderRepository(factory);
var bonusSvc = new PlayerBonusService(petRepo, new BlessingRepository(factory), eventRepo);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000039000UL;

// La zona en la 3.ª posición de la escalera (la de los jugadores del test) y la 1.ª y la 5.ª para probar que el premio NO depende de la zona.
var ladder = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
int zone1 = ladder[0].ZoneId, zone3 = ladder[2].ZoneId, zone5 = ladder[4].ZoneId;
const int Level = 20;

async Task Reset(int zone = 0)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET level = @l, xp = 0, gold = 0, dust = 0, current_zone_id = @z WHERE discord_id = @id", new { id = (long)U, l = Level, z = zone == 0 ? zone3 : zone });
}
async Task<int> SpeciesId(string bonusKind) => await db.ExecuteScalarAsync<int>("SELECT species_id FROM pet_species WHERE bonus_kind = @k ORDER BY zone_id LIMIT 1", new { k = bonusKind });
async Task<string> SpeciesName(string bonusKind) => await db.ExecuteScalarAsync<string>("SELECT name FROM pet_species WHERE bonus_kind = @k ORDER BY zone_id LIMIT 1", new { k = bonusKind }) ?? "?";
async Task<int> GivePet(string bonusKind, int level)
{
    int id = await SpeciesId(bonusKind);
    await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points) VALUES (@u, @s, @p) ON CONFLICT (discord_id, species_id) DO UPDATE SET feed_points = @p", new { u = (long)U, s = id, p = PetRules.PointsToReach(level) });
    return id;
}
async Task SetFood(int n) => await db.ExecuteAsync(
    "INSERT INTO inventory (discord_id, item_id, quantity) SELECT @u, item_id, @n FROM items WHERE name = @name ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @n", new { u = (long)U, n, name = PetRules.FoodItemName });
async Task<int> Food() => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.name = @name", new { u = (long)U, name = PetRules.FoodItemName });
async Task<int> Rows() => await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM pet_expeditions WHERE discord_id = @u", new { u = (long)U });
async Task Finish() => await db.ExecuteAsync("UPDATE pet_expeditions SET ends_at = now() - interval '1 minute' WHERE discord_id = @u", new { u = (long)U });
async Task PutExpedition(int speciesId, int hours) => await db.ExecuteAsync(
    "INSERT INTO pet_expeditions (discord_id, species_id, hours, ends_at) VALUES (@u, @s, @h, now() - interval '1 minute') ON CONFLICT (discord_id) DO UPDATE SET species_id = @s, hours = @h, ends_at = now() - interval '1 minute'", new { u = (long)U, s = speciesId, h = hours });
async Task<(int Gold, int Xp, int Dust, int Lvl)> Me() => await db.QuerySingleAsync<(int, int, int, int)>("SELECT gold, xp, dust, level FROM users WHERE discord_id = @u", new { u = (long)U });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.name = @n", new { u = (long)U, n = name });
// La EXP que paga una expedición de esas horas a un jugador de nivel 20: el % de lo que pide el nivel.
int ExpectedXp(int hours, bool xpFind) => Math.Max(1, (int)(LevelingCalculator.RequiredXpForLevel(Level) * PetExpeditions.XpPercentFor(PetExpeditions.OptionFor(hours)!, xpFind) / 100.0));

Console.WriteLine("== 1. Las reglas (puras) ==");
var o1 = PetExpeditions.OptionFor(1)!; var o4 = PetExpeditions.OptionFor(4)!; var o8 = PetExpeditions.OptionFor(8)!;
Check(PetExpeditions.Options.Select(o => o.Hours).SequenceEqual([1, 4, 8]) && PetExpeditions.OptionFor(2) is null && PetExpeditions.OptionFor(0) is null && PetExpeditions.RequiredLevel == 10 && PetExpeditions.CheapestFoodCost == 2, "tres duraciones (1, 4, 8) y pide nivel 10");
Check(o1.FoodCost == 2 && o4.FoodCost == 10 && o8.FoodCost == 25, "la de 1 hora cuesta 2 comidas, la de 4 horas 10 y la de 8 horas 25 (lo que pidió el dueño)");
Check(o1.XpPercent == 5 && o4.XpPercent == 20 && o8.XpPercent == 50, "la EXP es 5 % / 20 % / 50 % del nivel (lo que pidió el dueño)");
Check(o1.GoldMax > o1.GoldMin && o4.GoldMax > o4.GoldMin && o8.GoldMax > o8.GoldMin && new[] { o1, o4, o8 }.All(o => o.GoldMin % 100 == 0 && o.GoldMax % 100 == 0), "el oro es un RANGO (de a 100)");
Check(o1.GoldMin < o4.GoldMin && o4.GoldMin < o8.GoldMin && o1.GoldMax < o4.GoldMax && o4.GoldMax < o8.GoldMax && o1.XpPercent < o4.XpPercent && o4.XpPercent < o8.XpPercent && o1.FindChancePercent < o4.FindChancePercent && o4.FindChancePercent < o8.FindChancePercent,
    "mientras más dura, más oro, más EXP y más chance de hallazgo (lo que pidió el dueño)");
double Mean(ExpeditionOption o) => (o.GoldMin + o.GoldMax) / 2.0;
Check(Mean(o1) / 1 < Mean(o4) / 4 && Mean(o4) / 4 < Mean(o8) / 8 && Mean(o8) is >= 30_000 and <= 60_000, $"y por hora rinde más la larga ({Mean(o1) / 1:0} / {Mean(o4) / 4:0} / {Mean(o8) / 8:0} de oro por hora); la de 8 h paga ~{Mean(o8):0} de media (la Zona 5: una sesión activa de 2 h son ~90.000)");
Check(Mean(o1) > o1.FoodCost * 100 * 5 && Mean(o4) > o4.FoodCost * 100 * 5 && Mean(o8) > o8.FoodCost * 100 * 5, "el oro paga la comida (a 100 de oro cada una) con holgura: más de 5 veces");
var goldRng = new Random(3); var golds = Enumerable.Range(0, 20000).Select(_ => PetExpeditions.RollGold(o8, false, goldRng)).ToList();
Check(golds.All(g => g >= o8.GoldMin && g <= o8.GoldMax && g % 100 == 0) && golds.Min() < o8.GoldMin + 1500 && golds.Max() > o8.GoldMax - 1500 && Near(golds.Average(), Mean(o8), 400), $"el oro sale del rango, de a 100, y promedia el medio ({golds.Average():0})");
Check(PetExpeditions.RollGold(o8, true, new ScriptedRandom(99)) == 45_000 && PetExpeditions.RollGold(o8, false, new ScriptedRandom(99)) == 30_000 && PetExpeditions.RollGold(o1, true, new ScriptedRandom(99)) == 3_000, "con el hallazgo de oro el oro sube un 50 % (30.000 → 45.000)");
Check(PetExpeditions.XpPercentFor(o8, false) == 50 && PetExpeditions.XpPercentFor(o8, true) == 75 && PetExpeditions.XpPercentFor(o4, true) == 30 && PetExpeditions.XpPercentFor(o1, true) == 8, "con el hallazgo de EXP la EXP sube un 50 % (50 → 75 %, y 5 → 8 redondeando hacia arriba)");
Check(new[] { PetBonusKind.Gold, PetBonusKind.Xp, PetBonusKind.Defense, PetBonusKind.Drop, PetBonusKind.Gather }.Select(PetExpeditions.FindKindOf).SequenceEqual(
    [ExpeditionFindKind.GoldBag, ExpeditionFindKind.XpBoost, ExpeditionFindKind.Dust, ExpeditionFindKind.HuntDrops, ExpeditionFindKind.Gather]), "cada tipo de bonus tiene su hallazgo");
var rngFind = new Random(7); int hit1 = 0, hit4 = 0, hit8 = 0; const int Samples = 200000;
for (int i = 0; i < Samples; i++) { if (PetExpeditions.RollFind(o1, rngFind)) hit1++; if (PetExpeditions.RollFind(o4, rngFind)) hit4++; if (PetExpeditions.RollFind(o8, rngFind)) hit8++; }
Check(Near(hit1 * 100.0 / Samples, 10, 0.4) && Near(hit4 * 100.0 / Samples, 35, 0.6) && Near(hit8 * 100.0 / Samples, 70, 0.6), $"la chance de hallazgo sale 10 / 35 / 70 % ({hit1 * 100.0 / Samples:0.0} / {hit4 * 100.0 / Samples:0.0} / {hit8 * 100.0 / Samples:0.0})");
var pool = await db.QueryAsync<GatherCandidate>("SELECT item_id AS \"ItemId\", name AS \"Name\", rarity AS \"Rarity\", emoji AS \"Emoji\", type AS \"Type\" FROM items WHERE type IN ('Madera', 'Mineral')");
var poolList = pool.ToList();
var rngG = new Random(11); bool mythic = false, onlyKnown = true; int drawn = 0;
for (int i = 0; i < 20000; i++)
{
    foreach (var (item, qty) in PetExpeditions.RollGather(poolList, 8, rngG)) { if (item.Rarity == "Mítico") mythic = true; if (qty is < 1 or > 5 * 8) onlyKnown = false; drawn++; }
}
Check(!mythic && onlyKnown && drawn > 20000, $"el hallazgo de la Chispa: 20.000 veces 8 tiradas, nunca un Mítico (la Corteza y el Meteorito siguen siendo de /chop, /mine y el Brasero) y nunca más de 5 unidades por tirada ({drawn} stacks)");
var scripted = PetExpeditions.RollGather(poolList, 8, new ScriptedRandom(0));
Check(scripted.Count == 2 && scripted.Single(s => s.Item.Name == "Madera de Pino").Quantity == 4 && scripted.Single(s => s.Item.Name == "Piedra").Quantity == 4, "las tiradas alternan madera y mineral (8 tiradas = 4 y 4) y juntan los repetidos");
Check(PetExpeditions.HoursText(1) == "1 hora" && PetExpeditions.HoursText(8) == "8 horas", "«1 hora» / «8 horas»");
var (n1, h1) = PetModule.ParseExpeditionRequest("Cachorro de Puma 4h"); var (n2, h2) = PetModule.ParseExpeditionRequest("8 Ñandusito"); var (n3, h3) = PetModule.ParseExpeditionRequest("Ñandusito"); var (n4, h4) = PetModule.ParseExpeditionRequest("  ");
var (n5, h5) = PetModule.ParseExpeditionRequest("Quimerita 8 horas"); var (n6, h6) = PetModule.ParseExpeditionRequest("8");
Check(n1 == "Cachorro de Puma" && h1 == 4 && n2 == "Ñandusito" && h2 == 8 && n3 == "Ñandusito" && h3 is null && n4 is null && h4 is null && n5 == "Quimerita" && h5 == 8 && n6 is null && h6 == 8,
    "el pedido de texto separa la mascota de las horas («Cachorro de Puma 4h», «8 Ñandusito», «Quimerita 8 horas»)");

Console.WriteLine("\n== 2. Salir de expedición (repositorio) ==");
await Reset();
int gold = await GivePet(PetBonusKind.Gold, 10);
await SetFood(100);
var noAcc = await expRepo.StartAsync(U + 777, gold, 8);
Check(noAcc.Status == ExpeditionStartStatus.NoAccount, "sin cuenta: se rechaza");
Check((await expRepo.StartAsync(U, gold, 3)).Status == ExpeditionStartStatus.BadDuration && (await expRepo.StartAsync(U, gold, 0)).Status == ExpeditionStartStatus.BadDuration && await Rows() == 0 && await Food() == 100, "una duración que no es 1, 4 u 8 se rechaza sin gastar nada");
int puma = await SpeciesId(PetBonusKind.Xp);
var notOwned = await expRepo.StartAsync(U, puma, 8);
Check(notOwned.Status == ExpeditionStartStatus.NotOwned && await Food() == 100, "una mascota que no tenés: se rechaza");
await GivePet(PetBonusKind.Xp, 9);
var low = await expRepo.StartAsync(U, puma, 8);
Check(low.Status == ExpeditionStartStatus.NotMaxLevel && low.Species?.SpeciesId == puma && await Food() == 100 && await Rows() == 0, "una mascota de nivel 9 no sale (solo el nivel 10) y no gasta comida");
await SetFood(24);
var poor = await expRepo.StartAsync(U, gold, 8);
Check(poor.Status == ExpeditionStartStatus.NoFood && poor.FoodHave == 24 && await Food() == 24 && await Rows() == 0, "con 24 comidas no alcanza para la de 8 horas (cuesta 25): se dice cuántas hay y no se gasta nada");
await SetFood(1);
Check((await expRepo.StartAsync(U, gold, 1)).Status == ExpeditionStartStatus.NoFood && await Food() == 1 && await Rows() == 0, "ni con 1 comida alcanza para la de 1 hora (cuesta 2)");
await SetFood(24);
var go4 = await expRepo.StartAsync(U, gold, 4);
Check(go4.Status == ExpeditionStartStatus.Ok && go4.FoodHave == 14 && await Food() == 14 && Near(go4.Expedition!.Remaining.TotalHours, 4, 0.01), "con las mismas 24 comidas sí sale la de 4 horas: gasta exactamente 10 (24 → 14)");
await db.ExecuteAsync("DELETE FROM pet_expeditions WHERE discord_id = @u", new { u = (long)U });
await SetFood(2);
var go1 = await expRepo.StartAsync(U, gold, 1);
Check(go1.Status == ExpeditionStartStatus.Ok && go1.FoodHave == 0 && await Food() == 0 && Near(go1.Expedition!.Remaining.TotalHours, 1, 0.01), "y la de 1 hora sale con 2 comidas justas (se gastan las 2 y la fila de la mochila se borra)");
await db.ExecuteAsync("DELETE FROM pet_expeditions WHERE discord_id = @u", new { u = (long)U });
await SetFood(40);
var go = await expRepo.StartAsync(U, gold, 8);
Check(go.Status == ExpeditionStartStatus.Ok && go.FoodHave == 15 && await Food() == 15 && await Rows() == 1 && go.Expedition is { Hours: 8 } e8 && e8.Species.SpeciesId == gold && !e8.IsReady && Near(e8.Remaining.TotalHours, 8, 0.01), $"sale por 8 horas: gasta exactamente 25 comidas (40 → 15) y vuelve en {go.Expedition?.Remaining.TotalHours:0.00} h");
Check(Near((go.Expedition!.EndsUtc - go.Expedition.StartedUtc).TotalHours, 8, 0.001), "el fin es el comienzo + 8 horas (reloj de la base)");
var again = await expRepo.StartAsync(U, puma, 4);
Check(again.Status == ExpeditionStartStatus.AlreadyOut && again.Expedition?.Species.SpeciesId == gold && await Food() == 15 && await Rows() == 1, "de a UNA por vez: la segunda se rechaza (con la que ya está afuera) y no gasta comida");
Check((await expRepo.GetAsync(U))?.Species.SpeciesId == gold && await expRepo.GetAsync(U + 777) is null, "GetAsync lee la expedición en curso (y nada para quien no tiene)");

Console.WriteLine("\n== 3. Diez salidas a la vez ==");
await Reset();
await GivePet(PetBonusKind.Gold, 10); await GivePet(PetBonusKind.Xp, 10); await SetFood(200);
int gId = await SpeciesId(PetBonusKind.Gold), xId = await SpeciesId(PetBonusKind.Xp);
var starts = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => expRepo.StartAsync(U, i % 2 == 0 ? gId : xId, 8)));
Check(starts.Count(s => s.Status == ExpeditionStartStatus.Ok) == 1 && starts.Count(s => s.Status == ExpeditionStartStatus.AlreadyOut) == 9 && await Rows() == 1 && await Food() == 175, $"10 salidas simultáneas: sale UNA, las otras 9 se rechazan y se gastan 25 comidas, no más (quedan {await Food()})");

Console.WriteLine("\n== 4. Recoger el premio (repositorio) ==");
await Reset();
Check((await expRepo.ClaimAsync(U + 777)).Status == ExpeditionClaimStatus.NoAccount, "sin cuenta: se rechaza");
Check((await expRepo.ClaimAsync(U)).Status == ExpeditionClaimStatus.NoExpedition, "sin expedición: no hay nada que recoger");
gold = await GivePet(PetBonusKind.Gold, 10); await SetFood(25);
await expRepo.StartAsync(U, gold, 8);
var tooSoon = await expRepo.ClaimAsync(U);
Check(tooSoon.Status == ExpeditionClaimStatus.NotReady && tooSoon.Remaining > TimeSpan.FromHours(7.9) && tooSoon.Receipt is null && (await Me()).Gold == 0 && await Rows() == 1, "antes de tiempo: «todavía no volvió» (dice cuánto falta) y no paga ni borra nada");
await Finish();
var noFind = await expRepo.ClaimAsync(U, new ScriptedRandom(99));
var me = await Me();
Check(noFind.Status == ExpeditionClaimStatus.Ok && noFind.Find is null && noFind.Receipt!.Reward.Gold == 30_000 && me.Gold == 30_000 && me.Xp == ExpectedXp(8, false) && ExpectedXp(8, false) == 8_000 && await Rows() == 0,
    $"vuelve y paga lo exacto (con el azar al mínimo): 30.000 de oro y el 50 % del nivel 20 = {me.Xp} de EXP, sin hallazgo, y la fila se borra");
Check((await expRepo.ClaimAsync(U)).Status == ExpeditionClaimStatus.NoExpedition && (await Me()).Gold == 30_000, "recogerla otra vez no paga de nuevo");

// El premio NO depende de la zona: la misma expedición paga lo mismo en la Zona 1, la 3 y la 5.
var paid = new List<(int Gold, int Xp)>();
foreach (int z in new[] { zone1, zone3, zone5 })
{
    await Reset(z); int g = await GivePet(PetBonusKind.Gold, 10); await PutExpedition(g, 8);
    await expRepo.ClaimAsync(U, new ScriptedRandom(99));
    var m = await Me(); paid.Add((m.Gold, m.Xp));
}
Check(paid.All(p => p == paid[0]) && paid[0].Gold == 30_000, $"el premio es igual en la Zona 1, la 3 y la 5 (oro {paid[0].Gold}, EXP {paid[0].Xp}): no depende de la zona");
// ...ni de la especie: todas las mascotas pagan lo mismo (sin hallazgo).
var perSpecies = new List<(int Gold, int Xp)>();
foreach (string kind in new[] { PetBonusKind.Gold, PetBonusKind.Xp, PetBonusKind.Defense, PetBonusKind.Drop, PetBonusKind.Gather })
{
    await Reset(); int sid = await GivePet(kind, 10); await PutExpedition(sid, 8);
    await expRepo.ClaimAsync(U, new ScriptedRandom(99));
    var m = await Me(); perSpecies.Add((m.Gold, m.Xp));
}
Check(perSpecies.All(p => p == perSpecies[0]) && perSpecies[0].Gold == 30_000, "ni de la especie: las cinco mascotas pagan lo mismo; la especie solo decide el hallazgo");
// Por duración.
foreach (var o in PetExpeditions.Options)
{
    await Reset(); int sid = await GivePet(PetBonusKind.Defense, 10); await PutExpedition(sid, o.Hours);
    await expRepo.ClaimAsync(U, new ScriptedRandom(99));
    var m = await Me();
    Check(m.Gold == o.GoldMin && m.Xp == ExpectedXp(o.Hours, false), $"{PetExpeditions.HoursText(o.Hours)}: {m.Gold} de oro (el mínimo del rango) y {m.Xp} de EXP ({o.XpPercent} % del nivel 20)");
}
// Con el azar real el oro cae dentro del rango.
await Reset(); gold = await GivePet(PetBonusKind.Defense, 10);
var seen = new List<int>();
for (int i = 0; i < 25; i++)
{
    await PutExpedition(gold, 4);
    await db.ExecuteAsync("UPDATE users SET gold = 0 WHERE discord_id = @u", new { u = (long)U });
    await expRepo.ClaimAsync(U, new Random(i));
    seen.Add((await Me()).Gold);
}
Check(seen.All(g => g >= o4.GoldMin && g <= o4.GoldMax && g % 100 == 0) && seen.Distinct().Count() > 5, $"con el azar real el oro de la de 4 horas cae dentro de {o4.GoldMin}–{o4.GoldMax} ({seen.Min()}–{seen.Max()}, {seen.Distinct().Count()} valores distintos)");

await Reset();
gold = await GivePet(PetBonusKind.Gold, 10);
await PutExpedition(gold, 4);
var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => expRepo.ClaimAsync(U, new ScriptedRandom(99))));
Check(claims.Count(c => c.Status == ExpeditionClaimStatus.Ok) == 1 && claims.Count(c => c.Status == ExpeditionClaimStatus.NoExpedition) == 7 && (await Me()).Gold == o4.GoldMin && await Rows() == 0, $"8 cobros simultáneos: paga UNA vez ({o4.GoldMin} de oro) y nada más");
await Reset(); gold = await GivePet(PetBonusKind.Gold, 10); await PutExpedition(gold, 8);
await db.ExecuteAsync("UPDATE users SET gold = 2147483000 WHERE discord_id = @u", new { u = (long)U });
await expRepo.ClaimAsync(U, new ScriptedRandom(99));
Check((await Me()).Gold == int.MaxValue, "el oro se frena en int.MaxValue y no rompe");

Console.WriteLine("\n== 5. El hallazgo de cada especie (chance forzada) ==");
// Oro: +50 % de oro.
await Reset(); gold = await GivePet(PetBonusKind.Gold, 10); await PutExpedition(gold, 8);
var goldFind = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(goldFind.Find?.Kind == ExpeditionFindKind.GoldBag && goldFind.Receipt!.Reward.Gold == 45_000 && (await Me()).Gold == 45_000 && (await Me()).Xp == ExpectedXp(8, false), "Ñandusito con hallazgo: el oro sube un 50 % (30.000 → 45.000) y la EXP no cambia");
// EXP: +50 % de EXP.
await Reset(); int xpSpecies = await GivePet(PetBonusKind.Xp, 10); await PutExpedition(xpSpecies, 8);
var xpFind = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(xpFind.Find?.Kind == ExpeditionFindKind.XpBoost && xpFind.Receipt!.Reward.Xp == ExpectedXp(8, true) && ExpectedXp(8, true) == 12_000 && (await Me()).Xp == 12_000 && (await Me()).Gold == 30_000, "Cachorro de Puma con hallazgo: la EXP sube un 50 % (8.000 → 12.000) y el oro no cambia");
// Polvo.
await Reset(); int golem = await GivePet(PetBonusKind.Defense, 10); await PutExpedition(golem, 8);
var dustFind = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(dustFind.Find is { Kind: ExpeditionFindKind.Dust, Amount: 240 } && (await Me()).Dust == 240 && dustFind.Receipt!.Reward.Gold == 30_000, "Gólem con hallazgo: +240 de Polvo en la de 8 horas, y el oro es el normal");
await Reset(); golem = await GivePet(PetBonusKind.Defense, 10); await PutExpedition(golem, 1);
var dust1 = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(dust1.Find?.Amount == 30 && (await Me()).Dust == 30, "y +30 en la de 1 hora (el Polvo también crece con el tiempo)");
await db.ExecuteAsync("UPDATE users SET dust = 2147483600 WHERE discord_id = @u", new { u = (long)U });
await PutExpedition(golem, 8);
await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check((await Me()).Dust == int.MaxValue, "el Polvo se frena en int.MaxValue y no rompe");
// Drops de cacería de la zona donde está el jugador.
await Reset(); int sala = await GivePet(PetBonusKind.Drop, 10); await PutExpedition(sala, 8);
var zoneDrops = (await db.QueryAsync<string>("SELECT DISTINCT i.name FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id JOIN items i ON i.item_id = md.item_id WHERE m.zone_id = @z AND NOT m.is_boss AND NOT m.is_travel", new { z = zone3 })).ToList();
var dropFind = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
string dropName = dropFind.Find?.Items.FirstOrDefault().ItemName ?? "";
Check(dropFind.Find is { Kind: ExpeditionFindKind.HuntDrops } df && df.Items.Count == 1 && df.Items[0].Quantity == 2 && zoneDrops.Count == 2 && zoneDrops.Contains(dropName) && await Have(dropName) == 2, $"Salamandrita con hallazgo: 2 drops de cacería de la zona del jugador (la 3: {dropName})");
await Reset(); sala = await GivePet(PetBonusKind.Drop, 10); await PutExpedition(sala, 1);
var dropFind1 = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(dropFind1.Find?.Items.Sum(i => i.Quantity) == 1, "y 1 drop en la de 1 hora");
// Recolección.
await Reset(); int chispa = await GivePet(PetBonusKind.Gather, 10); await PutExpedition(chispa, 8);
var gatherFind = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(gatherFind.Find is { Kind: ExpeditionFindKind.Gather } && await Have("Madera de Pino") == 4 && await Have("Piedra") == 4, "Chispa con hallazgo: madera y mineral a la mochila (con el azar fijo, 4 Pinos y 4 Piedras)");
await Reset(); chispa = await GivePet(PetBonusKind.Gather, 10); await PutExpedition(chispa, 8);
await expRepo.ClaimAsync(U, new Random(5));
Check(await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.rarity = 'Mítico'", new { u = (long)U }) == 0, "y nunca un Mítico en la mochila");
// Sin hallazgo: nada extra.
await Reset(); golem = await GivePet(PetBonusKind.Defense, 10); await PutExpedition(golem, 8);
var none = await expRepo.ClaimAsync(U, new ScriptedRandom(99));
Check(none.Find is null && (await Me()).Dust == 0 && none.Receipt!.Reward.Gold > 0, "sin hallazgo: solo el oro y la EXP");
// Subida de nivel.
await Reset(); gold = await GivePet(PetBonusKind.Gold, 10); await PutExpedition(gold, 8);
await db.ExecuteAsync("UPDATE users SET xp = @x WHERE discord_id = @u", new { u = (long)U, x = LevelingCalculator.RequiredXpForLevel(Level) - 5 });
var lvUp = await expRepo.ClaimAsync(U, new ScriptedRandom(99));
Check(lvUp.Receipt!.LevelsGained >= 1 && (await Me()).Lvl == Level + lvUp.Receipt.LevelsGained, $"la EXP de la expedición sube de nivel ({Level} → {(await Me()).Lvl})");

Console.WriteLine("\n== 6. Las pantallas y los comandos (módulo) ==");
await Reset();
Task<PetModule.PetResult> Run(string? name, int? hours, Random? rng = null) => PetModule.ExecuteExpeditionAsync(users, petRepo, expRepo, invRepo, events, U, name, hours, rng);
var noAccount = await PetModule.ExecuteExpeditionAsync(users, petRepo, expRepo, invRepo, events, U + 777, null, null);
Check(noAccount.PlainMessage!.Contains("/start"), "sin cuenta: manda a /start");
Check((await Run(null, null)).PlainMessage!.Contains("mascota"), "sin mascotas: lo dice");
await GivePet(PetBonusKind.Gold, 6);
var notYet = await Run(null, null);
Check(notYet.PlainMessage!.Contains("nivel 10") && notYet.PlainMessage.Contains("nivel 6") && notYet.Embed is null, "con una mascota de nivel 6: «las expediciones son para el nivel 10» y dice en qué nivel está");
gold = await GivePet(PetBonusKind.Gold, 10); await SetFood(1);
var menuPoor = await Run(null, null);
var selectPoor = ((ActionRowComponent)menuPoor.Components!.Components.First()).Components.OfType<SelectMenuComponent>().Single();
Check(menuPoor.Embed is not null && selectPoor.IsDisabled && selectPoor.Options.Count == 3 && selectPoor.CustomId == $"pet_exp_go:{U}", "con una mascota nivel 10 y 1 comida (no alcanza ni para la de 1 hora): el menú sale pero DESACTIVADO (tres opciones, una por duración)");
await SetFood(2);
Check(!((ActionRowComponent)(await Run(null, null)).Components!.Components.First()).Components.OfType<SelectMenuComponent>().Single().IsDisabled, "con 2 comidas el menú se activa (alcanza para la de 1 hora)");
await GivePet(PetBonusKind.Xp, 10); await SetFood(60);
var menu = await Run(null, null);
var select = ((ActionRowComponent)menu.Components!.Components.First()).Components.OfType<SelectMenuComponent>().Single();
string text = AllText(menu.Embed!);
Check(!select.IsDisabled && select.Options.Count == 6 && select.Options.All(o => o.Value.Split(':').Length == 2 && o.Label.Length <= 100 && o.Description.Length <= 100) && select.Options.Select(o => o.Value).Distinct().Count() == 6
    && select.Options.Any(o => o.Value == $"{gold}:8"), "con comida y dos mascotas al máximo: menú activo con 6 opciones (mascota × duración) con valores «especie:horas»");
Check(menu.Embed!.Length <= 6000 && menu.Embed.Fields.All(f => f.Value.Length <= 1024) && text.Contains("1 hora") && text.Contains("4 horas") && text.Contains("8 horas") && text.Contains("2 comidas") && text.Contains("10 comidas") && text.Contains("25 comidas") && text.Contains("Descubrilo"),
    $"la pantalla muestra cuánto dura y cuánta comida cuesta cada salida y qué mascotas pueden ir, dentro de los límites de Discord ({menu.Embed.Length})");
Check(!text.Contains("oro") && !text.Contains("EXP") && !text.Contains("%") && !text.Contains("hallazgo") && !text.Contains("Polvo") && !text.Contains("2.000") && !text.Contains("60.000") && select.Options.All(o => !o.Description.Contains("oro") && !o.Description.Contains("%") && o.Description.Contains("comidas")),
    "y NO muestra el premio, los porcentajes, el hallazgo ni qué trae cada mascota (el dueño quiere que lo descubran jugando), ni en la pantalla ni en las opciones del menú");
Check((await Run("Fantasma", 8)).PlainMessage!.Contains("No tenés una mascota"), "una mascota que no existe: se dice");
var wrongHours = await Run(await SpeciesName(PetBonusKind.Gold), 3);
Check(wrongHours.PlainMessage!.Contains("1, 4, 8") && await Food() == 60, "una duración inválida se explica y no gasta");
await SetFood(24);
var noFood8 = await Run(await SpeciesName(PetBonusKind.Gold), 8);
Check(noFood8.PlainMessage!.Contains("25") && noFood8.PlainMessage.Contains("24") && await Food() == 24 && await Rows() == 0, "con 24 comidas la de 8 horas dice «cuesta 25 y tenés 24» y no gasta");
await SetFood(60);
var started = await Run(await SpeciesName(PetBonusKind.Gold), 8);
Check(started.Embed is not null && started.Embed.Title.Contains("salió de expedición") && AllText(started.Embed).Contains("<t:") && !AllText(started.Embed).Contains("oro") && !AllText(started.Embed).Contains("%") && !AllText(started.Embed).Contains("30.000") && AllText(started.Embed).Contains("35") && await Food() == 35 && await Rows() == 1,
    "con mascota y horas: sale (embed «salió de expedición» con la hora de vuelta y SIN decir qué va a traer) y gasta 25 comidas (60 → 35)");
var whileOut = await Run(null, null);
Check(whileOut.Embed!.Title.Contains("está de expedición") && AllText(whileOut.Embed).Contains("<t:") && whileOut.Components is null && await Food() == 35, "mientras está afuera, el comando dice cuándo vuelve (sin menú) y no gasta");
var tryOther = await Run(await SpeciesName(PetBonusKind.Xp), 4);
Check(tryOther.Embed!.Title.Contains("está de expedición") && await Food() == 35 && await Rows() == 1, "pedir otra mientras una está afuera: muestra la que ya salió, de a una por vez");
await Finish();
int goldBefore = (await Me()).Gold;
var back = await Run(null, null, new ScriptedRandom(99));
Check(back.Embed!.Title.Contains("volvió de la expedición") && AllText(back.Embed).Contains("oro") && AllText(back.Embed).Contains("XP") && AllText(back.Embed).Contains("No encontró nada") && (await Me()).Gold == goldBefore + 30_000 && await Rows() == 0,
    "cuando ya volvió, el mismo comando recoge el premio: oro, XP y «no encontró nada especial» sin hallazgo");
var foundScreen = await PetModule.ClaimExpeditionAsync(expRepo, events, (await users.GetByDiscordIdAsync(U))!, new ScriptedRandom(0));
Check(foundScreen.PlainMessage!.Contains("No tenés ninguna"), "recoger sin expedición: lo dice");
await PutExpedition(gold, 8);
var foundBack = await PetModule.ClaimExpeditionAsync(expRepo, events, (await users.GetByDiscordIdAsync(U))!, new ScriptedRandom(0));
Check(foundBack.Embed!.Color == Color.Gold && AllText(foundBack.Embed).Contains("bolsa extra") && AllText(foundBack.Embed).Contains("50 %"), "con hallazgo la pantalla es dorada y lo cuenta («bolsa extra», +50 %)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game_events WHERE discord_id = @u AND kind = 'pet_expedition_start'", new { u = (long)U }) == 1
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game_events WHERE discord_id = @u AND kind = 'pet_expedition_claim'", new { u = (long)U }) == 2
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game_events WHERE discord_id = @u AND kind = 'pet_expedition_claim' AND detail LIKE '%:hallazgo'", new { u = (long)U }) == 1, "se registran los eventos de salir (1) y de recoger (2, uno con hallazgo)");
await Reset(); gold = await GivePet(PetBonusKind.Gold, 10); await PutExpedition(gold, 8);
await db.ExecuteAsync("UPDATE users SET xp = @x WHERE discord_id = @u", new { u = (long)U, x = LevelingCalculator.RequiredXpForLevel(Level) - 5 });
await PetModule.ClaimExpeditionAsync(expRepo, events, (await users.GetByDiscordIdAsync(U))!, new ScriptedRandom(99));
Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game_events WHERE discord_id = @u AND kind = 'level_up'", new { u = (long)U }) == 1, "y si la EXP sube de nivel se registra «level_up» (sale el cartel de nivel)");

Console.WriteLine("\n== 7. El aviso de que volvió y la línea de /cd ==");
Check(ReminderCatalog.All.Count == 13 && ReminderCatalog.Find("expedition") is { Command: "/pet expedition", AutoDelete: null } && ReminderCatalog.Keys.Distinct().Count() == 13 && ReminderCatalog.WaitOf("expedition") is null, "hay 13 avisos y el nuevo («expedition») no tiene una espera fija");
Check(ReminderCatalog.MayChangeExpedition("pet expedition") && ReminderCatalog.MayChangeExpedition("pet_exp_go") && ReminderCatalog.MayChangeExpedition("aa pet expedición") && !ReminderCatalog.MayChangeExpedition("hunt") && !ReminderCatalog.MayChangeExpedition("pet feed"), "el aviso se recalcula con /pet expedition y el menú, y no con el resto");
var nowU = DateTime.UtcNow;
Check(ReminderCatalog.ExpeditionDue(null, nowU) is null && ReminderCatalog.ExpeditionDue(TimeSpan.Zero, nowU) is null && ReminderCatalog.ExpeditionDue(TimeSpan.FromHours(3), nowU) is { Kind: "expedition" } d3 && Near((d3.DueUtc - nowU).TotalHours, 3, 0.001), "sin expedición o ya vuelta no hay aviso; en curso, avisa al terminar");
await Reset(); gold = await GivePet(PetBonusKind.Gold, 10); await SetFood(10);
await expRepo.StartAsync(U, gold, 4);
var service = new ReminderService(reminderRepo, cooldowns, users, petRepo, bonusSvc, null, expRepo);
await service.SyncAsync(U, 123456UL, "pet expedition");
var due = await db.ExecuteScalarAsync<DateTime?>("SELECT due_at FROM reminders WHERE discord_id = @u AND kind = 'expedition'", new { u = (long)U });
var ends = (await expRepo.GetAsync(U))!.EndsUtc;
Check(due is { } dd && Near((DateTime.SpecifyKind(dd, DateTimeKind.Utc) - ends).TotalSeconds, 0, 5), $"al mandarla de expedición se guarda el aviso para cuando vuelve ({due:HH:mm} UTC, la vuelta es {ends:HH:mm})");
Check(ReminderCatalog.Compose(U, ["expedition"]).Contains("volvió") && ReminderCatalog.Compose(U, ["expedition"]).Contains("/pet expedition"), "el mensaje del aviso dice que volvió y qué comando usar");
Check(CooldownModule.ExpeditionLine(null) is null, "/cd: sin expedición no hay línea");
var active = (await expRepo.GetAsync(U))!;
Check(CooldownModule.ExpeditionLine(active) is { } line && line.Contains("restantes") && line.Contains(active.Species.Name), "/cd: en curso, dice la mascota y cuánto falta");
Check(CooldownModule.ExpeditionLine(active with { Remaining = TimeSpan.Zero }) is { } ready && ready.Contains("/pet expedition"), "/cd: ya vuelta, manda a recogerla");
var cdEmbed = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, null, false, petRepo, null, expRepo);
Check(cdEmbed.Description.Contains("Expedición") && cdEmbed.Length <= 6000, "/cd muestra la línea de la expedición en curso");
await db.ExecuteAsync("DELETE FROM pet_expeditions WHERE discord_id = @u", new { u = (long)U });
Check(!(await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, null, false, petRepo, null, expRepo)).Description.Contains("Expedición"), "y sin expedición no sale");
using (Loc.Use("en"))
{
    Check(ReminderCatalog.Compose(U, ["expedition"]).Contains("is back from its expedition") && CooldownModule.ExpeditionLine(active)!.Contains("Expedition") && CooldownModule.ExpeditionLine(active)!.Contains("left"), "en inglés: el aviso y la línea de /cd");
}

Console.WriteLine("\n== 8. Comando, menú, DI y ayuda (InteractionService real) ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var slashExp = interactions.SlashCommands.Single(c => c.Name == "expedition" && c.Module.SlashGroupName == "pet");
var pMascota = slashExp.Parameters.SingleOrDefault(p => p.Name == "mascota"); var pHoras = slashExp.Parameters.SingleOrDefault(p => p.Name == "duracion");
Check(slashExp.Parameters.Count == 2 && pMascota is { IsRequired: false } && pHoras is { IsRequired: false } && pHoras.Choices.Count == 3 && pHoras.Choices.Select(c => Convert.ToInt32(c.Value)).OrderBy(x => x).SequenceEqual([1, 4, 8])
    && slashExp.Description.Length is >= 1 and <= 100 && slashExp.Parameters.All(p => validName(p.Name) && p.Description.Length is >= 1 and <= 100) && pMascota!.AutocompleteHandler is not null,
    $"/pet expedition existe: «mascota» con autocompletado y «duracion» con 3 opciones, descripciones ≤ 100 ({slashExp.Description.Length})");
Check(interactions.SlashCommands.Count(c => c.Module.SlashGroupName == "pet") == 4 && interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "el grupo /pet tiene ver, alimentar, expedición y evolucionar (v0.40.0), y siguen entrando en el tope de 100 comandos");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var rGo = interactions.SearchComponentCommand(Click($"pet_exp_go:{U}")); var rFeed = interactions.SearchComponentCommand(Click($"pet_feed:{U}"));
Check(rGo.IsSuccess && rGo.Command.MethodName == "HandleGoAsync" && rFeed.IsSuccess && rFeed.Command.MethodName == "HandleFeedAsync", "el menú «pet_exp_go:{dueño}» llega a su handler y el botón de alimentar sigue llegando al suyo");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "pet", "mascota", "mascotas", "pets" }.All(textNames.Contains) && textNames.Count(n => n == "pet") == 1, "aa pet y sus alias siguen registrados, sin repetirse (las expediciones son una acción de «aa pet»)");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");
Check(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IPetExpeditionRepository>(sp) is PetExpeditionRepository, "el repositorio de expediciones está registrado");
var auto = ExpeditionChoices.ForExpedition(
    [new OwnedPet(new PetSpecies(1, 1, "Z1", "Ñandusito", "🐦", PetBonusKind.Gold, 5, 1, "Huevo"), PetRules.PointsToReach(10), null), new OwnedPet(new PetSpecies(2, 2, "Z2", "Cachorro de Puma", "🐆", PetBonusKind.Xp, 5, 2, "Huevo 2"), PetRules.PointsToReach(9), null)], "");
Check(auto.Count == 1 && auto[0].Value.ToString() == "Ñandusito", "el autocompletado de /pet expedition lista solo las mascotas de nivel 10");

var topic = HelpTopics.Find("pets");
var helpEmbed = topic!.Build(); string helpText = AllText(helpEmbed);
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(helpText, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(helpEmbed.Length <= 6000 && helpEmbed.Fields.All(f => f.Value.Length <= 1024) && helpText.Contains("Expediciones") && helpText.Contains("/pet expedition") && helpText.Contains("1, 4, 8") && helpText.Contains("2 / 10 / 25") && !helpText.Contains("30.000") && !helpText.Contains("50 %")
    && !helpText.Contains("hallazgo") && helpText.Contains("descubrilo") && mentioned.All(slashNames.Contains),
    $"el tema de ayuda «pets» explica las expediciones (duración y comida) SIN el premio, nombra comandos que existen y entra en los límites ({helpEmbed.Length}; faltan: {string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var remHelp = HelpTopics.Find("reminders")!.Build();
Check(AllText(remHelp).Contains("cuando vuelve") && remHelp.Length <= 6000 && remHelp.Fields.All(f => f.Value.Length <= 1024), "y la ayuda de los avisos nombra el nuevo («cuando vuelve») sin romperse");
var remScreen = ReminderModule.Compose([], U).Embed!;
Check(AllText(remScreen).Contains("Expedición") && AllText(remScreen).Contains("cuando vuelve") && remScreen.Length <= 6000, "la pantalla de /reminders lista la expedición sin una espera fija");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un Random con el azar fijado: Next(100) = hundred (0 = «salió el hallazgo», 99 = «no salió»), el resto en su valor más bajo (primer elemento, oro y unidades al mínimo, rareza Común).
sealed class ScriptedRandom(int hundred) : Random
{
    public override int Next(int maxValue) => maxValue == 100 ? hundred : 0;
    public override int Next(int minValue, int maxValue) => minValue;
    public override double NextDouble() => 0.0;
    public override int Next() => 0;
}

// Un proxy que implementa cualquier interfaz de Discord devolviendo los valores que se le den por nombre de propiedad (y el valor por defecto del resto).
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
