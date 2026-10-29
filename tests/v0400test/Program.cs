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

// v0.40.0: los TIERS de las mascotas y los huevos de repuesto (el dueño, 2026-10-29): cada primera victoria de la vuelta sobre un jefe da su huevo AUNQUE ya tengas la mascota (se guarda sin abrir), las horas de
// expedición se cuentan POR mascota, y al nivel máximo de su tier (10 → 20 → 30) una mascota evoluciona gastando horas de expedición (100 / 250) y huevos sobrantes de su especie (5 / 10) — sin condición de
// Fuego Nuevo. Cada tier bonifica el doble del anterior (como mínimo) y el III es «una locura»; subir de nivel después del 10 cuesta mucho más comida. Contra la base LOCAL con un usuario descartable.
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
var adventure = new AdventureRepository(factory);
var eventRepo = new GameEventRepository(factory);
var events = new GameEventService(eventRepo);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000040000UL;
const int Level = 20;

var ladder = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
int zone1 = ladder[0].ZoneId, zone3 = ladder[2].ZoneId;

async Task Reset()
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET level = @l, xp = 0, gold = 0, dust = 0, current_zone_id = @z, highest_zone_cleared = 0, gate_cleared = false WHERE discord_id = @id", new { id = (long)U, l = Level, z = zone3 });
}
async Task<int> SpeciesId(string bonusKind) => await db.ExecuteScalarAsync<int>("SELECT species_id FROM pet_species WHERE bonus_kind = @k ORDER BY zone_id LIMIT 1", new { k = bonusKind });
async Task<string> EggName(string bonusKind) => await db.ExecuteScalarAsync<string>("SELECT i.name FROM pet_species s JOIN items i ON i.item_id = s.egg_item_id WHERE s.bonus_kind = @k ORDER BY s.zone_id LIMIT 1", new { k = bonusKind }) ?? "?";
// Deja a la mascota de ese tipo con ese nivel, tier y horas de expedición (sin esperar la hora de la última comida).
async Task<int> SetPet(string bonusKind, int level, int tier = 1, int hours = 0)
{
    int id = await SpeciesId(bonusKind);
    await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points, tier, expedition_hours) VALUES (@u, @s, @p, @t, @h) ON CONFLICT (discord_id, species_id) DO UPDATE SET feed_points = @p, tier = @t, expedition_hours = @h, last_fed_at = NULL",
        new { u = (long)U, s = id, p = PetRules.PointsToReach(level), t = tier, h = hours });
    return id;
}
async Task SetItem(string name, int n) => await db.ExecuteAsync(
    "INSERT INTO inventory (discord_id, item_id, quantity) SELECT @u, item_id, @n FROM items WHERE name = @name ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @n", new { u = (long)U, n, name });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.name = @n", new { u = (long)U, n = name });
async Task<(int Tier, int Hours, int Points)> PetRow(int speciesId) => await db.QuerySingleAsync<(int, int, int)>("SELECT tier::int, expedition_hours, feed_points FROM player_pets WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = speciesId });
async Task PutExpedition(int speciesId, int hours) => await db.ExecuteAsync(
    "INSERT INTO pet_expeditions (discord_id, species_id, hours, ends_at) VALUES (@u, @s, @h, now() - interval '1 minute') ON CONFLICT (discord_id) DO UPDATE SET species_id = @s, hours = @h, ends_at = now() - interval '1 minute'", new { u = (long)U, s = speciesId, h = hours });
async Task<(int Gold, int Xp, int Dust, int Lvl)> Me() => await db.QuerySingleAsync<(int, int, int, int)>("SELECT gold, xp, dust, level FROM users WHERE discord_id = @u", new { u = (long)U });
var o8 = PetExpeditions.OptionFor(8)!; var o1 = PetExpeditions.OptionFor(1)!;
PetSpecies Sp(string kind, double max = 5) => new(1, 1, "Z", "Bicho", "🐾", kind, max, 1, "Huevo");

Console.WriteLine("== 1. Las reglas de los tiers (puras) ==");
Check(PetRules.MaxTier == 3 && PetRules.LevelCap(1) == 10 && PetRules.LevelCap(2) == 20 && PetRules.LevelCap(3) == 30 && PetRules.LevelCap(0) == 10 && PetRules.LevelCap(9) == 30, "tres tiers con tope de nivel 10 / 20 / 30 (y los valores raros se acotan)");
Check(PetRules.PointsToReach(10) == 25 && PetRules.PointsToReach(20) == 105 && PetRules.PointsToReach(30) == 235 && PetRules.TotalFeedsToMax == 25 && PetRules.PointsToReach(20) - PetRules.PointsToReach(10) == 80 && PetRules.PointsToReach(30) - PetRules.PointsToReach(20) == 130,
    "el tier I sigue siendo 25 alimentadas, el II son 80 más (hasta 105) y el III 130 más (hasta 235)");
Check(Enumerable.Range(1, 30).All(l => PetRules.LevelFor(PetRules.PointsToReach(l)) == l && (l == 1 || PetRules.LevelFor(PetRules.PointsToReach(l) - 1) == l - 1)) && PetRules.LevelFor(235) == 30 && PetRules.LevelFor(99999) == 30, "PointsToReach y LevelFor son inversas en los 30 niveles, y no pasan del 30");
Check(PetRules.FoodPerFeed(1) == 1 && PetRules.FoodPerFeed(9) == 1 && PetRules.FoodPerFeed(10) == 3 && PetRules.FoodPerFeed(19) == 3 && PetRules.FoodPerFeed(20) == 6 && PetRules.FoodPerFeed(29) == 6, "cada alimentada gasta 1 comida hasta el 10, 3 del 10 al 20 y 6 del 20 al 30");
int foodTier2 = 0, foodTier3 = 0;
for (int l = 10; l < 20; l++) foodTier2 += (PetRules.PointsToReach(l + 1) - PetRules.PointsToReach(l)) * PetRules.FoodPerFeed(l);
for (int l = 20; l < 30; l++) foodTier3 += (PetRules.PointsToReach(l + 1) - PetRules.PointsToReach(l)) * PetRules.FoodPerFeed(l);
Check(foodTier2 == 240 && foodTier3 == 780, $"llegar al 20 cuesta 240 comidas (24.000 de oro) y del 20 al 30 son 780 ({foodTier2} y {foodTier3}): «que cueste las mascotas»");
Check(Near(PetRules.BonusMultiplier(1), 0.1, 1e-9) && Near(PetRules.BonusMultiplier(10), 1.0, 1e-9) && Near(PetRules.BonusMultiplier(11), 1.1, 1e-9) && Near(PetRules.BonusMultiplier(20), 2.0, 1e-9) && Near(PetRules.BonusMultiplier(21), 2.2, 1e-9) && Near(PetRules.BonusMultiplier(30), 4.0, 1e-9) && Near(PetRules.BonusMultiplier(99), 4.0, 1e-9),
    "el bonus es ×0,1 al nivel 1, ×1 al 10, ×2 al 20 y ×4 al 30, lineal por nivel (y no pasa de ×4)");
Check(PetRules.BonusMultiplier(20) >= 2 * PetRules.BonusMultiplier(10) && PetRules.BonusMultiplier(30) >= 2 * PetRules.BonusMultiplier(20) && PetRules.ExpeditionRewardMultiplier(2) >= 2 * PetRules.ExpeditionRewardMultiplier(1) && PetRules.ExpeditionRewardMultiplier(3) >= 2 * PetRules.ExpeditionRewardMultiplier(2),
    "cada tier bonifica el DOBLE o más que el anterior, en el bono y en el premio de las expediciones (lo que pidió el dueño)");
Check(Near(PetRules.BonusPercent(Sp("gold", 5), 10), 5, 1e-9) && Near(PetRules.BonusPercent(Sp("gold", 5), 20), 10, 1e-9) && Near(PetRules.BonusPercent(Sp("gold", 5), 30), 20, 1e-9) && Near(PetRules.BonusPercent(Sp("xp", 10), 30), 40, 1e-9), "Ñandusito: +5 % al 10, +10 % al 20 y +20 % al 30; Quimerita llega a +40 % de EXP");
Check(PetRules.EvolveHours(2) == 100 && PetRules.EvolveHours(3) == 250 && PetRules.EvolveEggs(2) == 5 && PetRules.EvolveEggs(3) == 10 && PetRules.EvolveHours(4) == int.MaxValue, "evolucionar pide 100 h y 5 huevos (tier II) y 250 h y 10 huevos más (tier III)");
Check(PetRules.EvolveEggs(2) + PetRules.EvolveEggs(3) == 15, "con un huevo sobrante por vuelta: 5 huevos = FN 6 para el tier II y 15 en total = FN 16 para el III (sin condición de FN)");
var owned1 = new OwnedPet(Sp("gold"), PetRules.PointsToReach(10), null);
var owned2 = new OwnedPet(Sp("gold"), PetRules.PointsToReach(10), null, 2, 0);
var owned3 = new OwnedPet(Sp("gold"), PetRules.PointsToReach(20), null, 3, 0);
Check(PetRules.TierName(1) == "I" && PetRules.TierName(2) == "II" && PetRules.TierName(3) == "III" && PetRules.DisplayName(owned1) == "Bicho" && PetRules.DisplayName(owned2) == "Bicho II" && PetRules.DisplayName(owned3) == "Bicho III", "el nombre lleva el número romano desde el tier II");
Check(PetRules.IsMaxLevel(owned1) && !PetRules.IsMaxLevel(owned2) && !PetRules.CanEat(owned1, DateTime.UtcNow) && PetRules.CanEat(owned2, DateTime.UtcNow) && PetRules.ProgressToNextLevel(PetRules.PointsToReach(10), 1) is null && PetRules.ProgressToNextLevel(PetRules.PointsToReach(10), 2) == (0, 6),
    "el tope es el de SU tier: un tier I en el 10 ya no come, un tier II en el 10 sí (necesita 6 alimentadas para el 11)");
Check(PetRules.Total([owned1, new OwnedPet(Sp("xp", 10), PetRules.PointsToReach(30), null, 3, 0)]) is { GoldPercent: 5, XpPercent: 40 }, "la suma de todas usa el bono del tier (5 % de oro + 40 % de EXP)");
Check(PetExpeditions.RollGold(o8, false, new ScriptedRandom(99), 1) == 30_000 && PetExpeditions.RollGold(o8, false, new ScriptedRandom(99), 2) == 60_000 && PetExpeditions.RollGold(o8, false, new ScriptedRandom(99), 3) == 120_000 && PetExpeditions.RollGold(o8, true, new ScriptedRandom(99), 3) == 180_000,
    "el oro de la expedición es ×1 / ×2 / ×4 por tier (30.000 → 60.000 → 120.000) y el hallazgo de oro sigue sumando 50 %");
Check(PetExpeditions.XpPercentFor(o8, false, 1) == 50 && PetExpeditions.XpPercentFor(o8, false, 2) == 100 && PetExpeditions.XpPercentFor(o8, false, 3) == 200 && PetExpeditions.XpPercentFor(o8, true, 2) == 150 && PetExpeditions.DustFor(o8, 3) == 960 && PetExpeditions.DropQuantityFor(o8, 3) == 8 && PetExpeditions.GatherRollsFor(o8, 2) == 16 && PetExpeditions.DustFor(o8) == 240,
    "la EXP (50 → 100 → 200 %) y el hallazgo (Polvo 240 → 960, drops 2 → 8, tiradas 8 → 16 → 32) también");

Console.WriteLine("\n== 2. Alimentar con tiers (repositorio) ==");
await Reset();
int gold = await SetPet(PetBonusKind.Gold, 10);
await SetItem(PetRules.FoodItemName, 50);
var capped = await petRepo.FeedAsync(U, gold);
Check(capped.Status == FeedStatus.MaxLevel && await Have(PetRules.FoodItemName) == 50 && (await PetRow(gold)).Points == 25, "un tier I en el nivel 10: ya no come (nivel máximo de su tier) y no gasta comida");
await db.ExecuteAsync("UPDATE player_pets SET tier = 2 WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = gold });
await SetItem(PetRules.FoodItemName, 2);
var poor = await petRepo.FeedAsync(U, gold);
Check(poor.Status == FeedStatus.NoFood && poor.FoodCost == 3 && await Have(PetRules.FoodItemName) == 2 && (await PetRow(gold)).Points == 25, "evolucionada (tier II) en el nivel 10, la alimentada gasta 3 comidas: con 2 no alcanza y no se toca nada");
await SetItem(PetRules.FoodItemName, 10);
var fed = await petRepo.FeedAsync(U, gold);
Check(fed.Status == FeedStatus.Ok && fed.FoodCost == 3 && fed.FoodLeft == 7 && await Have(PetRules.FoodItemName) == 7 && (await PetRow(gold)).Points == 26 && fed.Pet!.Tier == 2, "con 10 comidas come: gasta exactamente 3 (10 → 7) y suma un punto");
Check((await petRepo.FeedAsync(U, gold)).Status == FeedStatus.OnCooldown, "y sigue esperando su hora entre alimentadas");
await db.ExecuteAsync("UPDATE player_pets SET feed_points = @p, last_fed_at = NULL WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = gold, p = PetRules.PointsToReach(11) - 1 });
await SetItem(PetRules.FoodItemName, 9);
var up = await petRepo.FeedAsync(U, gold);
Check(up.Status == FeedStatus.Ok && up.LevelBefore == 10 && PetRules.LevelFor(up.Pet!.FeedPoints) == 11 && await Have(PetRules.FoodItemName) == 6, "la alimentada que cruza del 10 al 11 gasta 3 comidas (el costo es el del nivel que tiene) y sube de nivel");
await db.ExecuteAsync("UPDATE player_pets SET feed_points = @p, last_fed_at = NULL WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = gold, p = PetRules.PointsToReach(20) });
Check((await petRepo.FeedAsync(U, gold)).Status == FeedStatus.MaxLevel, "un tier II en el nivel 20 también topa: hay que evolucionarlo");
await db.ExecuteAsync("UPDATE player_pets SET tier = 3, last_fed_at = NULL WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = gold });
await SetItem(PetRules.FoodItemName, 5);
var poor3 = await petRepo.FeedAsync(U, gold);
Check(poor3.Status == FeedStatus.NoFood && poor3.FoodCost == 6 && await Have(PetRules.FoodItemName) == 5, "del nivel 20 al 30 cada alimentada gasta 6 comidas: con 5 no alcanza");
await SetItem(PetRules.FoodItemName, 6);
var fed3 = await petRepo.FeedAsync(U, gold);
Check(fed3.Status == FeedStatus.Ok && fed3.FoodCost == 6 && await Have(PetRules.FoodItemName) == 0, "con 6 come (y la fila de comida de la mochila se borra)");
await db.ExecuteAsync("UPDATE player_pets SET feed_points = @p, last_fed_at = NULL WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = gold, p = PetRules.PointsToReach(30) });
await SetItem(PetRules.FoodItemName, 50);
Check((await petRepo.FeedAsync(U, gold)).Status == FeedStatus.MaxLevel, "el tier III en el 30 es el máximo de verdad");

Console.WriteLine("\n== 3. Evolucionar (repositorio) ==");
await Reset();
string eggGold = await EggName(PetBonusKind.Gold);
Check((await petRepo.EvolveAsync(U + 777, gold)).Status == EvolveStatus.NoAccount, "sin cuenta: se rechaza");
Check((await petRepo.EvolveAsync(U, gold)).Status == EvolveStatus.NotOwned, "una mascota que no tenés: se rechaza");
gold = await SetPet(PetBonusKind.Gold, 9, 1, 500); await SetItem(eggGold, 20);
var notCap = await petRepo.EvolveAsync(U, gold);
Check(notCap.Status == EvolveStatus.NotAtCap && notCap.Have == 9 && notCap.Need == 10 && await Have(eggGold) == 20 && (await PetRow(gold)).Tier == 1, "solo al nivel máximo del tier: en el 9 se rechaza (9 de 10) aunque sobren horas y huevos");
gold = await SetPet(PetBonusKind.Gold, 10, 1, 99); await SetItem(eggGold, 5);
var noHours = await petRepo.EvolveAsync(U, gold);
Check(noHours.Status == EvolveStatus.NotEnoughHours && noHours.Have == 99 && noHours.Need == 100 && await Have(eggGold) == 5 && (await PetRow(gold)).Tier == 1, "con 99 horas de expedición no alcanza (99 de 100) y no gasta los huevos");
gold = await SetPet(PetBonusKind.Gold, 10, 1, 100); await SetItem(eggGold, 4);
var noEggs = await petRepo.EvolveAsync(U, gold);
Check(noEggs.Status == EvolveStatus.NotEnoughEggs && noEggs.Have == 4 && noEggs.Need == 5 && await Have(eggGold) == 4 && (await PetRow(gold)).Tier == 1, "con 4 huevos no alcanza (4 de 5): no se gasta ninguno");
await SetItem(eggGold, 7);
var evolved = await petRepo.EvolveAsync(U, gold);
Check(evolved.Status == EvolveStatus.Ok && evolved.Pet!.Tier == 2 && (await PetRow(gold)).Tier == 2 && await Have(eggGold) == 2 && (await PetRow(gold)).Hours == 100, "con nivel 10, 100 horas y 7 huevos evoluciona al tier II: gasta exactamente 5 (quedan 2) y las horas se conservan");
var again = await petRepo.EvolveAsync(U, gold);
Check(again.Status == EvolveStatus.NotAtCap && again.Have == 10 && again.Need == 20 && (await PetRow(gold)).Tier == 2, "ya evolucionada, en el nivel 10 del tier II ya no puede otra vez (hace falta el 20)");
gold = await SetPet(PetBonusKind.Gold, 20, 2, 249); await SetItem(eggGold, 10);
Check((await petRepo.EvolveAsync(U, gold)) is { Status: EvolveStatus.NotEnoughHours, Have: 249, Need: 250 }, "al tier III: 249 horas no alcanzan (el total es 250, no se reinician)");
gold = await SetPet(PetBonusKind.Gold, 20, 2, 250); await SetItem(eggGold, 9);
Check((await petRepo.EvolveAsync(U, gold)) is { Status: EvolveStatus.NotEnoughEggs, Have: 9, Need: 10 }, "y 9 huevos no alcanzan (hacen falta 10)");
await SetItem(eggGold, 10);
var evolved3 = await petRepo.EvolveAsync(U, gold);
Check(evolved3.Status == EvolveStatus.Ok && (await PetRow(gold)).Tier == 3 && await Have(eggGold) == 0 && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.name = @n", new { u = (long)U, n = eggGold }) == 0,
    "con nivel 20, 250 horas y 10 huevos pasa al tier III y la fila de huevos de la mochila se borra");
await db.ExecuteAsync("UPDATE player_pets SET feed_points = @p WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = gold, p = PetRules.PointsToReach(30) });
Check((await petRepo.EvolveAsync(U, gold)).Status == EvolveStatus.MaxTier, "el tier III no evoluciona más");
// Ocho evoluciones a la vez con huevos para UNA.
gold = await SetPet(PetBonusKind.Gold, 10, 1, 100); await SetItem(eggGold, 5);
var races = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => petRepo.EvolveAsync(U, gold)));
Check(races.Count(r => r.Status == EvolveStatus.Ok) == 1 && races.Count(r => r.Status == EvolveStatus.NotAtCap) == 7 && (await PetRow(gold)).Tier == 2 && await Have(eggGold) == 0, "8 evoluciones simultáneas: evoluciona UNA vez y se gastan 5 huevos, no más");

Console.WriteLine("\n== 4. Las horas por mascota y el premio de las expediciones por tier ==");
await Reset();
int def = await SetPet(PetBonusKind.Defense, 10); int xpP = await SetPet(PetBonusKind.Xp, 10);
await PutExpedition(def, 8); await expRepo.ClaimAsync(U, new ScriptedRandom(99));
await PutExpedition(def, 4); await expRepo.ClaimAsync(U, new ScriptedRandom(99));
await PutExpedition(xpP, 1); await expRepo.ClaimAsync(U, new ScriptedRandom(99));
Check((await PetRow(def)).Hours == 12 && (await PetRow(xpP)).Hours == 1, "recoger una expedición suma sus horas a ESA mascota (8 + 4 = 12; la otra lleva 1)");
await PutExpedition(def, 8);
await expRepo.ClaimAsync(U);   // (sin azar fijo) también suma
Check((await PetRow(def)).Hours == 20, "con cualquier resultado del hallazgo, las horas suman igual");
Check((await expRepo.ClaimAsync(U)).Status == ExpeditionClaimStatus.NoExpedition && (await PetRow(def)).Hours == 20, "y recogerla de nuevo no suma otra vez");
foreach (int tier in new[] { 1, 2, 3 })
{
    await Reset(); int d = await SetPet(PetBonusKind.Defense, 10 * tier, tier); await PutExpedition(d, 8);
    var claim = await expRepo.ClaimAsync(U, new ScriptedRandom(99));
    int mult = new[] { 1, 2, 4 }[tier - 1];
    int expectedXp = (int)(LevelingCalculator.RequiredXpForLevel(Level) * (50 * mult) / 100.0);
    Check(claim.Receipt!.Reward.Gold == 30_000 * mult && claim.Receipt.Reward.Xp == expectedXp && (await Me()).Gold == 30_000 * mult, $"tier {PetRules.TierName(tier)}: la expedición de 8 horas paga {30_000 * mult} de oro y el {50 * mult} % de un nivel ({claim.Receipt.Reward.Xp} de EXP)");
}
await Reset(); int golem3 = await SetPet(PetBonusKind.Defense, 30, 3); await PutExpedition(golem3, 8);
Check((await expRepo.ClaimAsync(U, new ScriptedRandom(0))).Find is { Kind: ExpeditionFindKind.Dust, Amount: 960 } && (await Me()).Dust == 960, "el hallazgo también se multiplica: el Gólem tier III trae 960 de Polvo (240 × 4)");
await Reset(); int sala2 = await SetPet(PetBonusKind.Drop, 20, 2); await PutExpedition(sala2, 8);
var dropClaim = await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(dropClaim.Find is { Kind: ExpeditionFindKind.HuntDrops } && dropClaim.Find.Items.Sum(i => i.Quantity) == 4 && await Have(dropClaim.Find.Items[0].ItemName) == 4, "la Salamandrita tier II trae 4 drops de cacería (2 × 2)");
await Reset(); int chispa3 = await SetPet(PetBonusKind.Gather, 30, 3); await PutExpedition(chispa3, 8);
await expRepo.ClaimAsync(U, new ScriptedRandom(0));
Check(await Have("Madera de Pino") == 16 && await Have("Piedra") == 16, "la Chispa tier III hace 32 tiradas (con el azar fijo, 16 Pinos y 16 Piedras)");

Console.WriteLine("\n== 5. Huevos de repuesto en cada vuelta ==");
await Reset();
var species = await petRepo.GetSpeciesAsync();
var z1 = species.First(s => s.ZoneId == zone1);
string eggZ1 = z1.EggName;
var first = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(first.EggGranted?.SpeciesId == z1.SpeciesId && !first.EggIsSpare && await Have(eggZ1) == 1, "la primera vez que vencés al jefe llega el huevo de siempre (no es de repuesto)");
var sameLap = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(sameLap.EggGranted is null && await Have(eggZ1) == 1, "volver a vencerlo en la MISMA vuelta no da otro huevo");
await petRepo.HatchAsync(U, z1.EggItemId);
Check(await Have(eggZ1) == 0 && (await petRepo.GetOwnedAsync(U)).Any(p => p.Species.SpeciesId == z1.SpeciesId), "se abre el huevo y nace la mascota");
await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = 0 WHERE discord_id = @u", new { u = (long)U });   // un Fuego Nuevo: la vuelta nueva
var lap2 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(lap2.EggGranted?.SpeciesId == z1.SpeciesId && lap2.EggIsSpare && await Have(eggZ1) == 1, "en la vuelta siguiente, con la mascota ya nacida, el huevo llega IGUAL, de repuesto (antes no llegaba)");
await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = 0 WHERE discord_id = @u", new { u = (long)U });
var lap3 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(lap3.EggIsSpare && await Have(eggZ1) == 2, "y en la siguiente se juntan: 2 huevos sobrantes");
var openSpare = await petRepo.HatchAsync(U, z1.EggItemId);
Check(openSpare.Status == HatchStatus.AlreadyOwned && await Have(eggZ1) == 2, "abrir uno de repuesto no hace nada y no lo gasta");
// El Fogón: el huevo de la Chispa también llega cada vuelta.
await db.ExecuteAsync("UPDATE users SET gate_cleared = false WHERE discord_id = @u", new { u = (long)U });
int gateZone = await db.ExecuteScalarAsync<int>("SELECT zone_id FROM zones WHERE kind = 'gate'");
var gateSpecies = species.FirstOrDefault(s => s.ZoneId == gateZone);
if (gateSpecies is not null)
{
    var g1 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, gateZone);
    await petRepo.HatchAsync(U, gateSpecies.EggItemId);
    await db.ExecuteAsync("UPDATE users SET gate_cleared = false WHERE discord_id = @u", new { u = (long)U });
    var g2 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, gateZone);
    Check(g1.EggGranted is not null && !g1.EggIsSpare && g2.EggGranted is not null && g2.EggIsSpare && await Have(gateSpecies.EggName) == 1, "El Fogón: el Asador da el huevo de la Chispa la primera vez y de repuesto en cada vuelta siguiente");
}

// El regalo retroactivo: un huevo sobrante por cada Fuego Nuevo y por especie que ya tiene el jugador, una sola vez.
await Reset();
await SetPet(PetBonusKind.Gold, 10); await SetPet(PetBonusKind.Xp, 5);
await db.ExecuteAsync("UPDATE users SET fuego_nuevo = 3 WHERE discord_id = @u", new { u = (long)U });
string eggXp = await EggName(PetBonusKind.Xp);
string scriptText = File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database", "grant_retroactive_spare_eggs.sql")).Replace("\r\n", "\n").Replace("\nBEGIN;\n", "\n").Replace("\nCOMMIT;\n", "\n");
await using (var tx = await db.BeginTransactionAsync())
{
    await db.ExecuteAsync(scriptText, transaction: tx);
    int Eggs(string name) => db.ExecuteScalar<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.name = @n", new { u = (long)U, n = name }, tx);
    int other = db.ExecuteScalar<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @u AND i.type = 'Huevo' AND i.name NOT IN (@a, @b)", new { u = (long)U, a = eggGold, b = eggXp }, tx);
    Check(Eggs(eggGold) == 3 && Eggs(eggXp) == 3 && other == 0, "el regalo retroactivo da 3 huevos sobrantes (el jugador hizo 3 Fuegos Nuevos) de CADA mascota que tiene, y de las que no, ninguno");
    await db.ExecuteAsync(scriptText, transaction: tx);
    Check(Eggs(eggGold) == 3 && Eggs(eggXp) == 3, "y correrlo de nuevo NO regala otra tanda (la marca de data_migrations lo frena)");
    await tx.RollbackAsync();
}
Check(await Have(eggGold) == 0, "(la prueba se deshizo: no quedó ningún huevo regalado ni la marca en la base local)");

Console.WriteLine("\n== 6. Las pantallas y los comandos ==");
await Reset();
var gSpecies = species.First(s => s.BonusKind == PetBonusKind.Gold);
OwnedPet P(PetSpecies sp, int level, int tier, int hours) => new(sp, PetRules.PointsToReach(level), null, tier, hours);
PetModule.PetViewData View(OwnedPet pet, int eggs, int food = 100) => new(species, [pet], food, new HashSet<string>(), DateTime.UtcNow, null, 1.0, new Dictionary<string, int> { [gSpecies.EggName] = eggs });
var v1 = AllText(PetModule.BuildViewEmbed(View(P(gSpecies, 10, 1, 62), 3)));
Check(v1.Contains("nivel 10/10") && v1.Contains("¡al máximo de su tier!") && v1.Contains("🧬 Para evolucionar al tier II") && v1.Contains("62/100 h") && v1.Contains("3/5") && !v1.Contains("¡Lista para evolucionar") && !v1.Contains("Puede evolucionar"), "un tier I en el 10 muestra qué le falta para evolucionar (62/100 h, 3/5 huevos)");
var vReady = AllText(PetModule.BuildViewEmbed(View(P(gSpecies, 10, 1, 150), 6)));
Check(vReady.Contains("¡Lista para evolucionar al tier II!") && vReady.Contains("/pet evolve") && vReady.Contains("100/100 h") && vReady.Contains("5/5"), "con 100 horas y 5 huevos o más dice que está lista y usa /pet evolve (los números no pasan del tope)");
var vT2 = AllText(PetModule.BuildViewEmbed(View(P(gSpecies, 14, 2, 120), 0)));
Check(vT2.Contains($"{gSpecies.Name} II") && vT2.Contains("nivel 14/20") && vT2.Contains("cada una gasta 3 comidas") && !vT2.Contains("Para evolucionar") && vT2.Contains("alimentadas"), "un tier II en el 14 se llama «… II», muestra 14/20 y que cada alimentada gasta 3 comidas, sin hablar de evolución todavía");
var vT3 = AllText(PetModule.BuildViewEmbed(View(P(gSpecies, 30, 3, 400), 0)));
Check(vT3.Contains($"{gSpecies.Name} III") && vT3.Contains("nivel 30/30") && vT3.Contains("🏆 Nivel máximo") && !vT3.Contains("evolucionar"), "el tier III en el 30 es «Nivel máximo»");
Check(AllText(PetModule.BuildViewEmbed(View(P(gSpecies, 10, 1, 0), 0))).Length < 6000 && PetModule.EvolutionText(P(gSpecies, 10, 1, 0), null).Contains("0/5"), "sin información de huevos (null) la pantalla no se rompe");

Task<PetModule.PetResult> Evolve(string? name) => PetModule.ExecuteEvolveAsync(users, petRepo, events, U, name);
Check((await PetModule.ExecuteEvolveAsync(users, petRepo, events, U + 777, null)).PlainMessage!.Contains("/start"), "evolve sin cuenta: manda a /start");
Check((await Evolve(null)).PlainMessage!.Contains("mascota"), "evolve sin mascotas: lo dice");
await SetPet(PetBonusKind.Gold, 7, 1, 500);
Check((await Evolve(null)).PlainMessage!.Contains("nivel máximo de su tier"), "evolve sin nombre y ninguna en el máximo: lo explica");
Check((await Evolve("Fantasma")).PlainMessage!.Contains("No tenés una mascota"), "una mascota que no existe: se dice");
gold = await SetPet(PetBonusKind.Gold, 10, 1, 99); await SetItem(eggGold, 9);
Check((await Evolve(null)).PlainMessage!.Contains("99") && (await Evolve(null)).PlainMessage!.Contains("100"), "con la única lista que no alcanza, sin nombre usa esa y dice las horas (99 de 100)");
gold = await SetPet(PetBonusKind.Gold, 10, 1, 100); await SetItem(eggGold, 3);
var noEggsMsg = await Evolve(gSpecies.Name);
Check(noEggsMsg.PlainMessage!.Contains("huevos sobrantes") && noEggsMsg.PlainMessage.Contains("3") && noEggsMsg.PlainMessage.Contains("5"), "y con pocos huevos dice cuántos tenés y cuántos hacen falta");
await SetPet(PetBonusKind.Xp, 10, 1, 0);
Check((await Evolve(null)).PlainMessage!.Contains("Hay más de una lista") && (await Evolve(null)).PlainMessage!.Contains("/pet evolve mascota"), "con dos en el máximo y sin nombre pide que elijas cuál");
await SetItem(eggGold, 6);
var done = await Evolve(gSpecies.Name);
Check(done.Embed is not null && done.Embed.Title.Contains("evolucionó") && AllText(done.Embed).Contains("tier II") && AllText(done.Embed).Contains("nivel 20") && AllText(done.Embed).Contains("5") && await Have(eggGold) == 1 && (await PetRow(gold)).Tier == 2
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game_events WHERE discord_id = @u AND kind = 'pet_evolved'", new { u = (long)U }) == 1, "evolucionar con el nombre: pantalla «evolucionó», tier II, nivel 20, gasta 5 huevos y registra el evento «pet_evolved»");
var choices = EvolveChoices.ForEvolve([P(gSpecies, 10, 1, 0), P(gSpecies, 9, 1, 0), P(gSpecies, 20, 3, 0), P(gSpecies, 20, 2, 0)], "");
Check(choices.Count == 2, "el autocompletado de /pet evolve lista solo las que están en el máximo de su tier y pueden evolucionar (no las del 9 ni el tier III)");

var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var slashEvolve = interactions.SlashCommands.Single(c => c.Name == "evolve" && c.Module.SlashGroupName == "pet");
Check(slashEvolve.Parameters.Count == 1 && slashEvolve.Parameters[0].Name == "mascota" && !slashEvolve.Parameters[0].IsRequired && slashEvolve.Parameters[0].AutocompleteHandler is not null && slashEvolve.Description.Length is >= 1 and <= 100
    && slashEvolve.Parameters.All(p => validName(p.Name) && p.Description.Length is >= 1 and <= 100) && interactions.SlashCommands.Count(c => c.Module.SlashGroupName == "pet") == 4, $"/pet evolve existe con «mascota» opcional y autocompletado, descripciones ≤ 100 ({slashEvolve.Description.Length}) y el grupo /pet tiene 4 comandos");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
Check(textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Count(a => a.Equals("pet", StringComparison.OrdinalIgnoreCase)) == 1, "«aa pet» sigue registrado una sola vez (evolve es una acción suya)");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");
var helpEmbed = HelpTopics.Find("pets")!.Build(); string helpText = AllText(helpEmbed);
Check(helpEmbed.Length <= 6000 && helpEmbed.Fields.All(f => f.Value.Length <= 1024) && helpText.Contains("evolucionar") && helpText.Contains("/pet evolve") && !helpText.Contains("100 h") && !helpText.Contains("huevos sobrantes"), "la ayuda de las mascotas menciona que se puede evolucionar pero no cuenta qué hace falta (se descubre)");

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
