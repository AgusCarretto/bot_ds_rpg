using System.Reflection;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.10.0: mascotas. Reglas puras, repositorio contra la base (usuarios descartables) y concurrencia.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

PetSpecies Sp(string kind, double max) => new(1, 1, "Z", "X", null, kind, max, 1, "Huevo");

Console.WriteLine("== 1. Reglas puras ==");
Check(PetRules.MaxLevel == 10 && PetRules.TotalFeedsToMax == 25 && PetRules.FeedCooldown == TimeSpan.FromHours(1) && PetRules.FoodItemName == "Comida para Mascotas", "10 niveles, 25 comidas en total, una vez por hora");
Check(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }.Select(PetRules.LevelFor).SequenceEqual([1, 2, 3, 3, 4, 4, 5, 5, 5, 6, 6, 6, 7]), "nivel según comidas: 0→1, 1→2, 2→3, 4→4, 6→5, 9→6, 12→7");
Check(PetRules.LevelFor(15) == 7 && PetRules.LevelFor(16) == 8 && PetRules.LevelFor(19) == 8 && PetRules.LevelFor(20) == 9 && PetRules.LevelFor(24) == 9 && PetRules.LevelFor(25) == 10 && PetRules.LevelFor(9999) == 10 && PetRules.LevelFor(-3) == 1, "16→8, 20→9, 25→10 y nunca pasa de 10 (ni baja de 1)");
Check(PetRules.PointsToReach(1) == 0 && PetRules.PointsToReach(2) == 1 && PetRules.PointsToReach(3) == 2 && PetRules.PointsToReach(4) == 4 && PetRules.PointsToReach(10) == 25 && PetRules.PointsToReach(99) == 25 && PetRules.PointsToReach(0) == 0, "comidas para llegar a cada nivel");
Check(Enumerable.Range(1, 10).All(l => PetRules.LevelFor(PetRules.PointsToReach(l)) == l && (l == 1 || PetRules.LevelFor(PetRules.PointsToReach(l) - 1) == l - 1)), "PointsToReach y LevelFor son inversas (justo en el borde de cada nivel)");
Check(PetRules.ProgressToNextLevel(0) == (0, 1) && PetRules.ProgressToNextLevel(3) == (1, 2) && PetRules.ProgressToNextLevel(24) == (4, 5) && PetRules.ProgressToNextLevel(25) is null, "progreso al próximo nivel: (0,1) (1,2) (4,5) y null al máximo");
Check(PetRules.BonusPercent(Sp("gold", 5), 1) == 0.5 && PetRules.BonusPercent(Sp("gold", 5), 5) == 2.5 && PetRules.BonusPercent(Sp("gold", 5), 10) == 5 && PetRules.BonusPercent(Sp("xp", 10), 10) == 10 && PetRules.BonusPercent(Sp("gold", 5), 99) == 5, "bonus = tope × nivel / 10 (nivel 1 = 10 % del tope, nivel 10 = el tope, sin pasarse)");
var allMax = new[] { new OwnedPet(Sp("gold", 5), 25, null), new OwnedPet(Sp("xp", 5), 25, null), new OwnedPet(Sp("defense", 6), 25, null), new OwnedPet(Sp("drop", 6), 25, null), new OwnedPet(Sp("xp", 10), 25, null) };
var total = PetRules.Total(allMax);
Check(total == new PetBonuses(5, 15, 6, 6) && !total.IsNone, "las cinco al máximo suman todas a la vez: oro 5, EXP 15 (5 + 10), defensa 6, drop 6");
Check(PetRules.Total([]) == PetBonuses.None && PetBonuses.None.IsNone, "sin mascotas no hay bonus");
Check(PetRules.Total([new OwnedPet(Sp("gold", 5), 0, null), new OwnedPet(Sp("gold", 5), 4, null)]).GoldPercent == 0.5 + 2.0, "dos del mismo tipo se suman");
Check(PetRules.Boost(38, 5) == 40 && PetRules.Boost(100, 0) == 100 && PetRules.Boost(100, -5) == 100 && PetRules.Boost(10, 0.5) == 10 && PetRules.Boost(1000, 12.5) == 1125 && PetRules.Boost(0, 50) == 0, "Boost: 38 con +5 % = 40, 0 % no toca nada, redondea al más cercano");
Check(PetRules.PercentText(2.5) == "+2,5 %" && PetRules.PercentText(5) == "+5 %" && PetRules.PercentText(0.6) == "+0,6 %" && PetRules.PercentText(15) == "+15 %", "texto del porcentaje con coma decimal");
Check(PetRules.KindName("gold") == "oro" && PetRules.KindName("xp") == "EXP" && PetRules.KindName("defense") == "defensa" && PetRules.KindName("drop").StartsWith("drop"), "nombres de cada bonus");

Console.WriteLine("\n== 2. Repositorio contra la base ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var pets = new PetRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000010000UL;
async Task Reset()
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });   // el CASCADE borra mascotas e inventario
    await users.GetOrCreateUserAsync(U);
}
async Task Give(string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task<int> Rows(string name) => await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task<int> PetCount() => await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM player_pets WHERE discord_id = @id", new { id = (long)U });
async Task<int> Points(int speciesId) => await db.ExecuteScalarAsync<int>("SELECT feed_points FROM player_pets WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = speciesId });
async Task Ready(int speciesId) => await db.ExecuteAsync("UPDATE player_pets SET last_fed_at = now() - interval '61 minutes' WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = speciesId });

var allSpecies = await pets.GetSpeciesAsync();
Check(allSpecies.Count == 6 && allSpecies[^1].ZoneId == 0, $"hay 6 especies, la última la del Fogón ({allSpecies.Count}); este arnés prueba las 5 de la escalera (la sexta: cofrestest)");
var species = allSpecies.Where(s => s.ZoneId > 0).ToList();
Check(species.Count == 5, $"5 especies en la escalera ({species.Count})");
Check(species.Select(s => s.Name).SequenceEqual(["Ñandusito", "Cachorro de Puma", "Gólem de bolsillo", "Salamandrita", "Quimerita"]), "en orden de zona: Ñandusito, Cachorro de Puma, Gólem de bolsillo, Salamandrita, Quimerita");
Check(species.Select(s => s.BonusKind).SequenceEqual(["gold", "xp", "defense", "drop", "xp"]) && species.Select(s => s.MaxBonusPercent).SequenceEqual([5.0, 5.0, 6.0, 6.0, 10.0]), "bonus: oro 5, EXP 5, defensa 6, drop 6, EXP 10");
Check(species.All(s => s.EggName.StartsWith("Huevo") && s.EggItemId > 0 && !string.IsNullOrEmpty(s.ZoneName)) && species.Select(s => s.EggItemId).Distinct().Count() == 5, "cada una con su huevo (ítem distinto) y el nombre de su zona");
var nandu = species[0]; var puma = species[1]; var quimera = species[4];
int foodId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE name = @n", new { n = PetRules.FoodItemName });
Check(foodId > 0, "la Comida para Mascotas existe en el catálogo");

await Reset();
Check((await pets.GetOwnedAsync(U)).Count == 0 && (await pets.GetBonusesAsync(U)).IsNone, "un jugador nuevo no tiene mascotas ni bonus");

// -- abrir el huevo
Check((await pets.HatchAsync(999000000000019999UL, nandu.EggItemId)).Status == HatchStatus.NoAccount, "abrir un huevo sin cuenta: NoAccount");
Check((await pets.HatchAsync(U, foodId)).Status == HatchStatus.NotAnEgg, "un ítem que no es un huevo: NotAnEgg");
var h0 = await pets.HatchAsync(U, nandu.EggItemId);
Check(h0.Status == HatchStatus.NoEgg && await PetCount() == 0, "sin el huevo en la mochila: NoEgg y no nace nada");
await Give("Huevo de la Pradera", 1);
var h1 = await pets.HatchAsync(U, nandu.EggItemId);
var owned1 = await pets.GetOwnedAsync(U);
Check(h1.Status == HatchStatus.Ok && h1.Species?.Name == "Ñandusito" && await Have("Huevo de la Pradera") == 0 && await Rows("Huevo de la Pradera") == 0, "abrir el huevo: nace el Ñandusito y el huevo se gasta (la fila de la mochila desaparece)");
Check(owned1.Count == 1 && owned1[0].FeedPoints == 0 && owned1[0].LastFedAtUtc is null && PetRules.LevelFor(owned1[0].FeedPoints) == 1 && owned1[0].Species.ZoneName == nandu.ZoneName, "nace nivel 1, sin comidas y listo para comer");
var b1 = await pets.GetBonusesAsync(U);
Check(b1 == new PetBonuses(0.5, 0, 0, 0), $"a nivel 1 da un 10 % de su tope: +0,5 % de oro ({b1})");
await Give("Huevo de la Pradera", 1);
var h2 = await pets.HatchAsync(U, nandu.EggItemId);
Check(h2.Status == HatchStatus.AlreadyOwned && await Have("Huevo de la Pradera") == 1 && await PetCount() == 1, "abrir otro huevo de la misma especie: AlreadyOwned, el huevo NO se gasta y sigue habiendo una sola mascota");

// -- alimentar
Check((await pets.FeedAsync(U, puma.SpeciesId)).Status == FeedStatus.NotOwned, "alimentar una que no tiene: NotOwned");
Check((await pets.FeedAsync(999000000000019999UL, nandu.SpeciesId)).Status == FeedStatus.NoAccount, "alimentar sin cuenta: NoAccount");
var f0 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(f0.Status == FeedStatus.NoFood && await Points(nandu.SpeciesId) == 0 && (await pets.GetOwnedAsync(U))[0].LastFedAtUtc is null, "sin comida: NoFood, no suma nada y no arranca el cooldown");
await Give(PetRules.FoodItemName, 3);
var f1 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(f1.Status == FeedStatus.Ok && f1.Pet!.FeedPoints == 1 && f1.LevelBefore == 1 && PetRules.LevelFor(f1.Pet.FeedPoints) == 2 && f1.FoodLeft == 2 && await Have(PetRules.FoodItemName) == 2 && f1.Remaining == TimeSpan.FromHours(1), "alimentar: +1 punto, sube de nivel 1 a 2, gasta UNA comida (quedan 2)");
Check(f1.Pet!.LastFedAtUtc is { } fedAt && Math.Abs((DateTime.UtcNow - fedAt).TotalSeconds) < 30 && fedAt.Kind == DateTimeKind.Utc, "last_fed_at queda en la hora de la base, en UTC");
var f2 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(f2.Status == FeedStatus.OnCooldown && f2.Remaining > TimeSpan.FromMinutes(59) && f2.Remaining <= TimeSpan.FromHours(1) && await Have(PetRules.FoodItemName) == 2 && await Points(nandu.SpeciesId) == 1, $"enseguida otra vez: OnCooldown (faltan {f2.Remaining:hh\\:mm\\:ss}), no se gasta comida ni se suma punto");
await Ready(nandu.SpeciesId);
var f3 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(f3.Status == FeedStatus.Ok && await Points(nandu.SpeciesId) == 2 && f3.FoodLeft == 1, "pasada la hora vuelve a comer (2 puntos, queda 1 comida)");
await Ready(nandu.SpeciesId);
var f4 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(f4.Status == FeedStatus.Ok && f4.FoodLeft == 0 && await Rows(PetRules.FoodItemName) == 0, "la última comida: la fila de la mochila desaparece (nada de cantidad 0)");
await Ready(nandu.SpeciesId);
var f5 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(f5.Status == FeedStatus.NoFood && await Points(nandu.SpeciesId) == 3, "sin comida otra vez: NoFood, queda en 3 puntos");

// -- el cooldown es POR mascota
await Give("Huevo del Bosque de Cenizas", 1);
await pets.HatchAsync(U, puma.EggItemId);
await Give(PetRules.FoodItemName, 5);
var g1 = await pets.FeedAsync(U, puma.SpeciesId);
var g2 = await pets.FeedAsync(U, nandu.SpeciesId);
Check(g1.Status == FeedStatus.Ok && g2.Status == FeedStatus.Ok && await Have(PetRules.FoodItemName) == 3, "alimentar a la primera no frena a la segunda: el cooldown es de cada mascota");
await Ready(nandu.SpeciesId);

// -- bonus de varias a la vez
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 25 WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = puma.SpeciesId });
await Give("Huevo del Cráter", 1);
await pets.HatchAsync(U, quimera.EggItemId);
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 25 WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = quimera.SpeciesId });
var bAll = await pets.GetBonusesAsync(U);
var ownedAll = await pets.GetOwnedAsync(U);
Check(ownedAll.Select(o => o.Species.Name).SequenceEqual(["Ñandusito", "Cachorro de Puma", "Quimerita"]), "GetOwned en orden de zona");
Check(Math.Abs(bAll.GoldPercent - PetRules.BonusPercent(nandu, PetRules.LevelFor(4))) < 1e-9 && bAll.XpPercent == 15 && bAll.DefensePercent == 0 && bAll.DropPercent == 0, $"bonus de las tres a la vez: oro del Ñandusito nivel 4 ({bAll.GoldPercent:0.##} %) + EXP 5 + 10 = 15 ({bAll})");

// -- tope de nivel
await Give(PetRules.FoodItemName, 4);
var m1 = await pets.FeedAsync(U, quimera.SpeciesId);
Check(m1.Status == FeedStatus.MaxLevel && await Have(PetRules.FoodItemName) == 4 && await Points(quimera.SpeciesId) == 25, "una mascota al nivel 10: MaxLevel, no se gasta comida");

// -- concurrencia
await Reset();
await Give("Huevo de la Pradera", 1);
var hatches = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => pets.HatchAsync(U, nandu.EggItemId)));
Check(hatches.Count(h => h.Status == HatchStatus.Ok) == 1 && await PetCount() == 1 && await Have("Huevo de la Pradera") == 0, $"12 aperturas a la vez con UN huevo: nace una sola mascota ({string.Join(",", hatches.GroupBy(h => h.Status).Select(g => $"{g.Key}×{g.Count()}"))})");
await Reset();
await Give("Huevo de la Pradera", 5);
var hatches2 = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => pets.HatchAsync(U, nandu.EggItemId)));
Check(hatches2.Count(h => h.Status == HatchStatus.Ok) == 1 && await PetCount() == 1 && await Have("Huevo de la Pradera") == 4, $"12 aperturas a la vez con 5 huevos de la misma especie: solo uno se gasta y quedan 4 ({string.Join(",", hatches2.GroupBy(h => h.Status).Select(g => $"{g.Key}×{g.Count()}"))})");
await Give(PetRules.FoodItemName, 10);
var feeds = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => pets.FeedAsync(U, nandu.SpeciesId)));
Check(feeds.Count(f => f.Status == FeedStatus.Ok) == 1 && await Points(nandu.SpeciesId) == 1 && await Have(PetRules.FoodItemName) == 9, $"12 alimentaciones a la vez: solo una pasa (1 punto, 9 comidas) ({string.Join(",", feeds.GroupBy(f => f.Status).Select(g => $"{g.Key}×{g.Count()}"))})");
await Ready(nandu.SpeciesId);
await Give(PetRules.FoodItemName, 1);
var feeds2 = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => pets.FeedAsync(U, nandu.SpeciesId)));
Check(feeds2.Count(f => f.Status == FeedStatus.Ok) == 1 && await Have(PetRules.FoodItemName) == 0 && await Points(nandu.SpeciesId) == 2, "con UNA comida y la mascota lista, 12 a la vez: se gasta una sola y la comida nunca queda negativa");


// ================= v0.10.0, parte 2: las mascotas dentro del juego =================
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
Embed CallE(Type t, string method, params object?[] args) => (Embed)t.GetMethod(method, AnyS)!.Invoke(null, args)!;
var itemsRepo = new ItemRepository(factory);
var inventoryRepo = new InventoryRepository(factory);
var adventure = new AdventureRepository(factory);
var buffRepo = new BuffRepository(factory);
var zoneRepo = new ZoneRepository(factory);
var shopRepo = new ShopRepository(factory);
var combat = new CombatSessionService(users);
var evts = new RecordingEvents();
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;

Console.WriteLine("\n== 3. Las mascotas en las recompensas y en la defensa ==");
double Avg(Func<CombatReward> roll, Func<CombatReward, double> pick, int n = 40000) { double s = 0; for (int i = 0; i < n; i++) s += pick(roll()); return s / n; }
double Rate(Func<CombatReward> roll, int n = 300000) { int k = 0; for (int i = 0; i < n; i++) if (roll().DroppedSomething) k++; return 100.0 * k / n; }
var gx = new PetBonuses(10, 20, 0, 0);
double goldA = Avg(() => CombatRewardCalculator.RollHuntReward(10), r => r.Gold), goldB = Avg(() => CombatRewardCalculator.RollHuntReward(10, 0, 0, gx), r => r.Gold);
double x0 = Avg(() => CombatRewardCalculator.RollHuntReward(10), r => r.Xp), x1 = Avg(() => CombatRewardCalculator.RollHuntReward(10, 0, 0, gx), r => r.Xp);
Check(Math.Abs(goldB / goldA - 1.10) < 0.01 && Math.Abs(x1 / x0 - 1.20) < 0.01, $"cacería con +10 % de oro y +20 % de EXP: {goldA:0.0}→{goldB:0.0} oro, {x0:0.0}→{x1:0.0} EXP");
double tg0 = Avg(() => CombatRewardCalculator.RollTravelReward(10, 3, 4), r => r.Gold, 8000), tg1 = Avg(() => CombatRewardCalculator.RollTravelReward(10, 3, 4, gx), r => r.Gold, 8000);
double bx0 = Avg(() => CombatRewardCalculator.RollBossReward(10, 3, 4, true), r => r.Xp, 8000), bx1 = Avg(() => CombatRewardCalculator.RollBossReward(10, 3, 4, true, gx), r => r.Xp, 8000);
Check(Math.Abs(tg1 / tg0 - 1.10) < 0.01 && Math.Abs(bx1 / bx0 - 1.20) < 0.01, "el viaje y el jefe también cobran el bonus (sobre la recompensa entera, con su multiplicador)");
Check(Enumerable.Range(0, 3000).All(_ => CombatRewardCalculator.RollHuntReward(10, 0, 0, null).Gold is >= 25 and <= 35 && CombatRewardCalculator.RollHuntReward(10, 0, 0, PetBonuses.None).Xp is >= 9 and <= 15), "sin mascotas (null o sin bonus) las cuentas son exactamente las de siempre");
var d6 = new PetBonuses(0, 0, 0, 6);
Check(CombatRewardCalculator.HuntDropChance(null) == 6 && CombatRewardCalculator.TravelBoxChance(40, null) == 40 && Math.Abs(CombatRewardCalculator.HuntDropChance(d6) - 6.36) < 1e-9 && Math.Abs(CombatRewardCalculator.TravelBoxChance(40, d6) - 42.4) < 1e-9 && CombatRewardCalculator.TravelBoxChance(40, new PetBonuses(0, 0, 0, 100)) == 80 && CombatRewardCalculator.TravelBoxChance(40, new PetBonuses(0, 0, 0, 200)) == 100, "la chance de drop es RELATIVA: +6 % sobre 6 % = 6,36 % y sobre 40 % = 42,4 % (con tope en 100)");
double hunt0 = Rate(() => CombatRewardCalculator.RollHuntReward(10)), hunt1 = Rate(() => CombatRewardCalculator.RollHuntReward(10, 0, 0, new PetBonuses(0, 0, 0, 50)));
Check(Math.Abs(hunt0 - 6) < 0.3 && Math.Abs(hunt1 - 9) < 0.35, $"sorteando: 6 % → {hunt0:0.00} % sin mascotas, y con +50 % de drop {hunt1:0.00} % (esperado 9)");
double chestRepeat = Rate(() => CombatRewardCalculator.RollBossReward(10, 0, 0, false, new PetBonuses(0, 0, 0, 100), 40));
Check(Math.Abs(chestRepeat - 40) < 0.8 && Rate(() => CombatRewardCalculator.RollBossReward(10, 0, 0, true, new PetBonuses(0, 0, 0, 100)), 2000) == 100, $"el cofre del jefe NO lo toca el bonus de drop (40 % de repetición → {chestRepeat:0.0} % aun con +100 %)");

await Reset();
await db.ExecuteAsync("UPDATE users SET level = 30 WHERE discord_id = @id", new { id = (long)U });
var me30 = await Me();
var base30 = PlayerCombatProfileCalculator.Resolve(me30, null, null);
var withPet = PlayerCombatProfileCalculator.Resolve(me30, null, null, 0, new PetBonuses(0, 0, 6, 0));
Check(withPet.Defense == PetRules.Boost(base30.Defense, 6) && withPet.Defense > base30.Defense && withPet.Damage == base30.Damage && withPet.CombatMaxHp == base30.CombatMaxHp && withPet.Bonuses!.Pets.DefensePercent == 6, $"la mascota de defensa sube SOLO la defensa: {base30.Defense}→{withPet.Defense} (ataque y vida igual)");
// duelos y Arena: el perfil se arma con Resolve SIN mascotas, aunque el jugador tenga una de defensa
var golem = species[2];
await Give("Huevo de las Minas", 1);
await pets.HatchAsync(U, golem.EggItemId);
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 25 WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = golem.SpeciesId });
var fighter = await new DuelFighterFactory(users, itemsRepo, buffRepo).BuildAsync(U, "T");
Check(fighter is not null && fighter.Profile.Defense == base30.Defense, $"en duelos y Arena las mascotas NO pelean: defensa {fighter?.Profile.Defense} = {base30.Defense}");
// la pelea solitaria sí las lleva (defensa dentro del perfil; oro/EXP/drop viajan en el estado)
var starter = new AdventureCombatStarter(new CooldownRepository(factory), adventure, users, itemsRepo, new MonsterRepository(factory), zoneRepo, buffRepo, new PlayerBonusService(pets, new BlessingRepository(factory), new GameEventRepository(factory)), combat, new RaidSessionService());
await db.ExecuteAsync("UPDATE users SET current_zone_id = 1, level = 30, max_hp = 500, current_hp = 500 WHERE discord_id = @id", new { id = (long)U });
var started = await starter.PrepareHuntAsync(U);
Check(started.State is { Bonuses: { Pets: { DefensePercent: 6 } } } && started.State.PlayerDefense == PetRules.Boost(CombatStats.TotalDefense(30, 0), 6), $"/hunt arranca con la defensa de la mascota ({started.State?.PlayerDefense}) y los bonus en el estado ({started.Status})");

Console.WriteLine("\n== 4. El huevo del jefe ==");
await Reset();
var z1 = species[0]; var z2 = species[1];
var e1 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(e1.EggGranted?.Name == "Ñandusito" && await Have("Huevo de la Pradera") == 1 && (await Me()).HighestZoneCleared == z1.ZoneId, "la primera vez que vence al jefe de la Zona 1: llega el huevo del Ñandusito (junto con el avance de zona)");
var e2 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(e2.EggGranted is null && await Have("Huevo de la Pradera") == 1, "la segunda vez no hay otro huevo");
var e3 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z2.ZoneId);
Check(e3.EggGranted?.Name == "Cachorro de Puma" && await Have("Huevo del Bosque de Cenizas") == 1, "el jefe de la Zona 2 da SU huevo");
var e4 = await adventure.ApplyVictoryAsync(U, 10, 5, 0, null, 1);
Check(e4.EggGranted is null && await Have("Huevo de la Pradera") == 1, "una victoria común (no jefe) nunca da huevo");
await Reset();
await Give("Huevo de la Pradera", 1);
await pets.HatchAsync(U, z1.EggItemId);
var e5 = await adventure.ApplyBossVictoryAsync(U, 10, 5, 0, null, 1, z1.ZoneId);
Check(e5.EggGranted is null && await Have("Huevo de la Pradera") == 0, "si ya tiene esa mascota no se le da otro huevo");
await Reset();
var race = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, z1.ZoneId)));
Check(race.Count(r => r.EggGranted is not null) == 1 && await Have("Huevo de la Pradera") == 1, "8 victorias simultáneas sobre el jefe: el huevo llega UNA sola vez");

// los mensajes de victoria
CombatState Boss() => new("boss", "Rey Jabalí", "👑", 100, 60, 10, [], 0, 0, z1.ZoneId, 100, 80, 100, 10, 2, 5, "Guerrero", ClassPassives.For("Guerrero"));
var turn = new TurnResult(null, false, 12, 0, 0, 60, 100, new MonsterHitOutcome(false, 7), 93, default);
var playerU = new User { MaxHp = 100, CurrentHp = 80, Level = 5, Class = "Guerrero" };
var rw = new CombatReward(10, 10, false);
var withEgg = CallE(typeof(AdventureModule), "BuildVictoryEmbed", Boss(), turn, rw, null, new LevelUpOutcome(playerU, 0, z1), true);
var noEgg = CallE(typeof(AdventureModule), "BuildVictoryEmbed", Boss(), turn, rw, null, new LevelUpOutcome(playerU, 0), false);
Check(AllText(withEgg).Contains("🥚") && AllText(withEgg).Contains("Huevo de la Pradera") && AllText(withEgg).Contains("/open") && !AllText(noEgg).Contains("🥚") && withEgg.Length <= 6000, "el mensaje de victoria del jefe anuncia el huevo solo cuando llegó");
var session = new RaidSession { RaidId = Guid.NewGuid(), BossName = "Rey Jabalí", BossEmoji = "👑", BossBaseHp = 100, ZoneRank = 1, BossMaxHp = 200, BossDamage = 20, BossDropItemNames = [], BossGoldBonus = 0, BossXpBonus = 0, ZoneId = 1, ZoneName = "Praderas", StarterId = 1 };
var rp = new RaidParticipant { DiscordId = 1, DisplayName = "Tester", Damage = 20, Defense = 5, Level = 5, PlayerClass = "Guerrero", Passives = ClassPassives.For("Guerrero"), MaxHp = 100, StartingHp = 100, CurrentHp = 100 };
var raidEmbed = CallE(typeof(RaidModule), "BuildVictoryEmbed", session, "log", new List<(RaidParticipant, CombatReward, Item?, LevelUpOutcome)> { (rp, rw, null, new LevelUpOutcome(playerU, 0, z1)) });
Check(AllText(raidEmbed).Contains("🥚 Huevo de la Pradera"), "el raid también lo anuncia en el campo de quien lo recibió");

Console.WriteLine("\n== 5. /pet, /open, la tienda y el inventario ==");
await Reset();
var v0 = await PetModule.ExecuteViewAsync(users, pets, inventoryRepo, U);
Check(v0.Embed is not null && AllText(v0.Embed).Contains("Todavía no tenés ninguna") && v0.Embed.Fields.Length == 0 && !allSpecies.Any(s => AllText(v0.Embed).Contains(s.Name) || AllText(v0.Embed).Contains(s.ZoneName + ":")) && !AllText(v0.Embed).Contains("Fogón") && !AllText(v0.Embed).Contains("descubrir"), "sin mascotas: explica cómo conseguir la primera y NO lista las que faltan (sin spoilers: ni nombres, ni bonus, ni el Fogón)");
await Give("Huevo de la Pradera", 1);
var v0egg = await PetModule.ExecuteViewAsync(users, pets, inventoryRepo, U);
Check(v0egg.Embed!.Fields.Length == 1 && v0egg.Embed.Fields[0].Name == "🥚 Huevos sin abrir" && v0egg.Embed.Fields[0].Value.Contains("Huevo de la Pradera") && v0egg.Embed.Fields[0].Value.Contains("/open"), "si guardás un huevo sin abrir, la pantalla lo avisa (eso ya lo conocés: está en tu mochila)");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
Check((await PetModule.ExecuteViewAsync(users, pets, inventoryRepo, 999000000000019999UL)).PlainMessage!.Contains("/start") && (await PetModule.ExecuteFeedAsync(users, pets, evts, 999000000000019999UL, null)).PlainMessage!.Contains("/start"), "sin cuenta: manda a /start");
Check(v0.Components is not null && !v0.Components.Components.Any(), "y sin mascotas no hay botón de alimentar");
await Give("Huevo de la Pradera", 1);
var vEgg = await PetModule.ExecuteViewAsync(users, pets, inventoryRepo, U);
Check(vEgg.Embed!.Fields.Length == 1 && vEgg.Embed.Fields[0].Name == "🥚 Huevos sin abrir" && vEgg.Embed.Fields[0].Value.Contains("/open"), "con un huevo en la mochila, la pantalla avisa «Huevos sin abrir ... abrilo con /open» (y nada más de lo que falta)");

// abrir con /open
var opened = await BoxModule.ExecuteOpenAsync(itemsRepo, null!, null!, combat, evts, U, "Huevo de la Pradera", 3, petRepository: pets);
Check(opened.Embed is { } oe && oe.Title.Contains("Nació") && oe.Title.Contains("Ñandusito") && AllText(oe).Contains("/pet feed") && evts.Kinds.Contains("pet_hatched") && await Have("Huevo de la Pradera") == 0 && await PetCount() == 1, "/open con un huevo: nace la mascota (aunque se pidan 3), se anota el evento y el huevo se gasta");
await Give("Huevo de la Pradera", 1);
var dup = await BoxModule.ExecuteOpenAsync(itemsRepo, null!, null!, combat, evts, U, "Huevo de la Pradera", 1, petRepository: pets);
Check(dup.PlainMessage!.Contains("Ya tenés a") && await Have("Huevo de la Pradera") == 1, "otro huevo de la misma especie: avisa y no lo gasta");
await Give("Huevo del Bosque de Cenizas", 1);
var legacy = await BoxModule.ExecuteOpenAsync(itemsRepo, null!, null!, combat, evts, U, "Huevo del Bosque de Cenizas", 1);
Check(legacy.PlainMessage!.Contains("no es una caja") && await Have("Huevo del Bosque de Cenizas") == 1, "un llamador sin repositorio de mascotas no abre huevos (no rompe nada)");
var boxList = ItemChoices.ForOwnedBoxes(await inventoryRepo.GetOwnedByTypeAsync(U, "Huevo"), "");
Check(boxList.Count == 2 && boxList.All(c => c.Name.StartsWith("🥚") && c.Name.Contains("nace tu mascota")) && boxList.Select(c => c.Value.ToString()).OrderBy(n => n).SequenceEqual(["Huevo de la Pradera", "Huevo del Bosque de Cenizas"]), "la lista de /open ofrece los huevos que tenés");

// alimentar
await Give(PetRules.FoodItemName, 5);
var fd1 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, null);
Check(fd1.Embed is { } fe && fe.Title.Contains("Ñandusito comió") && AllText(fe).Contains("Subió al **nivel 2**") && fe.Footer?.Text?.Contains("Te quedan 4") == true && evts.Kinds.Contains("pet_fed") && await Have(PetRules.FoodItemName) == 4, "/pet feed sin nombre alimenta a las que pueden: sube de nivel, avisa y anota el evento");
var fd2 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, null);
Check(fd2.PlainMessage!.Contains("Ninguna mascota puede comer todavía") && fd2.PlainMessage.Contains("Ñandusito") && await Have(PetRules.FoodItemName) == 4, "enseguida otra vez: dice cuánto falta y no gasta comida");
var fd3 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, "ÑANDUSITO");
Check(fd3.Embed is { } p3 && p3.Title == "🍖 Tus mascotas" && AllText(p3).Contains("todavía no tiene hambre") && await Have(PetRules.FoodItemName) == 4, "eligiéndola por nombre (sin importar mayúsculas): «todavía no tiene hambre» y el título no miente");
Check((await PetModule.ExecuteFeedAsync(users, pets, evts, U, "Quimerita")).PlainMessage!.Contains("No tenés una mascota que se llame **Quimerita**"), "una mascota que no tenés: lo dice");
// varias a la vez, con comida justa
await db.ExecuteAsync("DELETE FROM player_pets WHERE discord_id = @id", new { id = (long)U });
await Give("Huevo de la Pradera", 1); await Give("Huevo del Bosque de Cenizas", 1); await Give("Huevo de las Minas", 1);
foreach (var s in species.Take(3)) await pets.HatchAsync(U, s.EggItemId);
await Give(PetRules.FoodItemName, 2);
var fd4 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, null);
Check(fd4.Embed is { } p4 && p4.Title == "🍖 Alimentaste a tus mascotas" && AllText(p4).Contains("Ñandusito") && AllText(p4).Contains("Cachorro de Puma") && !AllText(p4).Contains("Gólem") && AllText(p4).Contains("Te quedaste sin") && await Have(PetRules.FoodItemName) == 0, "con 2 comidas y 3 mascotas listas comen las 2 primeras (por zona) y avisa que se acabó la comida");
var fd5 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, "Gólem de bolsillo");
Check(fd5.Embed is { } p5 && AllText(p5).Contains("Te quedaste sin") && p5.Title == "🍖 Tus mascotas", "sin comida: avisa dónde comprarla");
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 25 WHERE discord_id = @id", new { id = (long)U });
var fd6 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, null);
var fd7 = await PetModule.ExecuteFeedAsync(users, pets, evts, U, "Ñandusito");
Check(fd6.PlainMessage!.Contains("nivel máximo") && fd7.Embed is not null && AllText(fd7.Embed).Contains("nivel máximo"), "todas al máximo: no hay nada que alimentar");

// pantalla
await db.ExecuteAsync("DELETE FROM player_pets WHERE discord_id = @id", new { id = (long)U });
foreach (var s in allSpecies) { await Give(s.EggName, 1); await pets.HatchAsync(U, s.EggItemId); }
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 25 WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = species[4].SpeciesId });
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 7, last_fed_at = now() - interval '20 minutes' WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = species[1].SpeciesId });
await Give(PetRules.FoodItemName, 3);
var vAll = await PetModule.ExecuteViewAsync(users, pets, inventoryRepo, U);
string vText = AllText(vAll.Embed!);
Check(vAll.Embed!.Fields.Length == 6 && vAll.Embed.Fields.All(f => !f.Inline) && vAll.Embed.Fields.All(f => f.Value.Length <= 1024) && vAll.Embed.Length <= 6000 && !vText.Contains("Por descubrir"), $"con las 6: un campo por mascota, apilados, dentro de los límites ({vAll.Embed.Length} / 6000)");
Check(vText.Contains("🏆 Nivel máximo") && vText.Contains("⏳ Vuelve a comer en 4") && vText.Contains("✅ Lista para comer") && vText.Contains("nivel 5/10") && vText.Contains("🍖 Comida para el próximo nivel: 1/3") && vText.Contains("Todas suman **a la vez**"), "muestra nivel, progreso, «vuelve a comer en» y «lista para comer»");
Check((vAll.Embed.Description ?? "").Contains("💰") && vAll.Embed.Description!.Contains("📊") && vAll.Embed.Description.Contains("🛡️") && vAll.Embed.Description.Contains("🎲") && vAll.Embed.Footer?.Text?.Contains("3") == true, "arriba el bonus total de todas juntas, y la comida que tenés abajo");
var btn = vAll.Components!.Components.OfType<ActionRowComponent>().Single().Components.OfType<ButtonComponent>().Single();
Check(btn.CustomId == $"pet_feed:{U}" && btn.IsDisabled == false, "el botón «Alimentar» lleva el id con el dueño y está activo (hay comida y una lista)");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id AND item_id = @f", new { id = (long)U, f = foodId });
var vNoFood = await PetModule.ExecuteViewAsync(users, pets, inventoryRepo, U);
Check(vNoFood.Components!.Components.OfType<ActionRowComponent>().Single().Components.OfType<ButtonComponent>().Single().IsDisabled == true, "sin comida el botón queda deshabilitado");

// la lista de /pet feed
var owned5 = await pets.GetOwnedAsync(U);
var choices = PetChoices.ForFeed(owned5, DateTime.UtcNow, "");
Check(choices.Count == 6 && choices[0].Value.ToString() == "Ñandusito" && choices.Any(c => c.Name.Contains("nivel máximo")) && choices.Any(c => c.Name.Contains("come en")) && choices.All(c => c.Name.Length <= 100) && PetChoices.ForFeed(owned5, DateTime.UtcNow, "pum").Count == 1 && PetChoices.ForFeed(owned5, DateTime.UtcNow, "golem").Count == 1, "la lista de /pet feed: en orden de zona, con el estado de cada una, y filtra sin tildes");

// tienda, inventario, /use y perfil
var shopItems = await ShopModule.LoadShopItemsAsync(itemsRepo);
var petFood = shopItems.Single(i => i.Name == PetRules.FoodItemName);
Check(ShopCatalog.IsForSale(petFood) && petFood.Type == "PetFood" && petFood.BuyPrice == 100 && petFood.SellPrice == 25 && ShopModule.PetFoodLine(petFood) == "Para tus mascotas · 100 oro" && shopItems.All(i => i.Type != "Huevo"), "la comida de mascotas se vende (100 de oro, vuelve a 25) y los huevos NO están a la venta");
var shopView = ShopModule.BuildViewEmbed(shopItems);
Check(shopView.Fields.Length == 3 && shopView.Fields[2].Name == "Mascotas" && !shopView.Fields[0].Value.Contains("Comida para Mascotas") && shopView.Fields[2].Value.Contains("Para tus mascotas") && shopView.Fields.All(f => f.Inline), "la tienda la lista en SU columna (la tercera), no mezclada con la comida que cura");
Check(ItemChoices.ForBuy(shopItems, 1000, "mascotas").Single().Name.Contains("para tus mascotas"), "y el autocompletado de /shop buy la describe bien (no «cura 0 HP»)");
await db.ExecuteAsync("UPDATE users SET gold = 1000 WHERE discord_id = @id", new { id = (long)U });
var buy = await ShopModule.ExecuteBuyAsync(users, itemsRepo, shopRepo, combat, evts, U, PetRules.FoodItemName, 3);
Check(buy.Embed is not null && await Have(PetRules.FoodItemName) == 3 && (await Me()).Gold == 700, "se compra con /shop buy (3 × 100) como cualquier comida");
var sell = await ShopModule.ExecuteSellAsync(itemsRepo, shopRepo, evts, U, PetRules.FoodItemName, 1);
Check(await Have(PetRules.FoodItemName) == 2 && (await Me()).Gold == 725, $"y se vende a 25 ({sell.Embed?.Title ?? sell.PlainMessage})");
var inv = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "T");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id AND item_id IN (SELECT item_id FROM items WHERE type = 'Huevo')", new { id = (long)U });
var invNoEgg = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "T");
Check(AllText(invNoEgg).Contains("Comida para Mascotas") && !AllText(invNoEgg).Contains("huevos"), "el inventario muestra la comida con los consumibles");
await Give("Huevo de la Pradera", 1);
var inv2 = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "T");
Check(AllText(inv2).Contains("Cajas y huevos") && AllText(inv2).Contains("Huevo de la Pradera") && inv2.Footer?.Text?.Contains("huevos") == true && inv2.Length <= 6000, "y el huevo, con las cajas");
var useFood = await UseModule.ExecuteUseAsync(users, itemsRepo, inventoryRepo, combat, buffRepo, U, PetRules.FoodItemName);
var useEgg = await UseModule.ExecuteUseAsync(users, itemsRepo, inventoryRepo, combat, buffRepo, U, "Huevo de la Pradera");
Check(useFood.PlainMessage!.Contains("/pet feed") && useEgg.PlainMessage!.Contains("/open") && await Have(PetRules.FoodItemName) == 2, "/use con la comida de mascotas o con un huevo explica qué hacer (y no gasta nada)");
await db.ExecuteAsync("UPDATE users SET level = 30 WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("UPDATE player_pets SET feed_points = 25 WHERE discord_id = @id AND species_id = @s", new { id = (long)U, s = golem.SpeciesId });
var prof = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zoneRepo, buffRepo, U, "T", "https://example.com/a.png", pets);
var profBase = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zoneRepo, buffRepo, U, "T", "https://example.com/a.png");
string ProfBlock(Embed e) => ProfileProbe.Stats(e);
string ProfLine(Embed e, string label) => ProfileProbe.Line(e, label);
string defField = ProfLine(prof, "DEF");
Check(!prof.Fields.Any(f => f.Name.Contains("Mascotas")) && !defField.Contains("🐾") && !ProfLine(profBase, "DEF").Contains("🐾") && prof.Length <= 6000, "el perfil ya NO lista las mascotas (v0.14.3: están en /pet) y desde la v0.25.2 la defensa tampoco lleva la marca «🐾 +6 %» (el dueño: «ya queda incluido» en el número)");
int baseDef = ProfileProbe.Number(profBase, "DEF");
int petDef = ProfileProbe.Number(prof, "DEF");
Check(petDef == PetRules.Boost(baseDef, 6) && petDef > baseDef, $"y el número del perfil ({petDef}) es el que pelea ({baseDef} + 6 %)");

Console.WriteLine("\n== 6. Comandos registrados, ayuda y logros ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var petCmds = interactions.SlashCommands.Where(c => c.Module.SlashGroupName == "pet").ToList();
Check(petCmds.Select(c => c.Name).OrderBy(n => n).SequenceEqual(["feed", "view"]) && petCmds.All(c => validName(c.Name) && c.Description.Length is >= 1 and <= 100 && c.Parameters.All(p => validName(p.Name) && p.Description.Length is >= 1 and <= 100)) && interactions.Modules.Single(m => m.SlashGroupName == "pet").Description!.Length <= 100, "/pet view y /pet feed cumplen los límites de Discord");
Check(petCmds.Single(c => c.Name == "feed").Parameters.Single() is { Name: "mascota", IsRequired: false, AutocompleteHandler: not null }, "y /pet feed tiene su parámetro «mascota» opcional con lista");
var openCmd = interactions.SlashCommands.Single(c => c.Name == "open" && c.Module.SlashGroupName is null);
Check(openCmd.Description.Length <= 100 && openCmd.Parameters.All(p => p.Description.Length <= 100), "/open sigue entrando en los límites (descripciones de 100)");
Discord.IComponentInteraction Click(string id) => FakeProxy.Make<Discord.IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<Discord.IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"pet_feed:{U}"));
Check(routed.IsSuccess && routed.Command.Module.Name == "PetButtonsModule" && routed.RegexCaptureGroups is { Length: 1 } g && g[0] == U.ToString(), $"el botón «pet_feed:{{dueño}}» llega a su método, sin el prefijo del grupo ({(routed.IsSuccess ? routed.Command.Module.Name + "." + routed.Command.MethodName : "(ninguno)")})");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "pet", "mascota", "mascotas" }.All(textNames.Contains), "aa pet / aa mascota / aa mascotas están registrados (y el CommandService no se queja de ambigüedad)");
int topLevel = interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count();
Check(topLevel <= 100, $"siguen entrando en el tope de 100 comandos de nivel superior ({topLevel})");

var topicPets = HelpTopics.Find("mascotas");
Check(topicPets?.Key == "pets" && HelpTopics.Find("pet")?.Key == "pets" && HelpTopics.Find("huevo")?.Key == "pets" && HelpTopics.Find("pets")?.Key == "pets", "«info mascotas / pet / huevo» abren el tema de mascotas");
var pe = topicPets!.Build();
string peText = AllText(pe);
Check(pe.Length <= 6000 && pe.Fields.All(f => f.Value.Length <= 1024) && peText.Contains("10") && peText.Contains("25 comidas") && peText.Contains("una vez por hora") && peText.Contains("6,36") && peText.Contains("/pet feed") && peText.Contains("/open"), $"el tema explica niveles, la hora, el drop relativo y los comandos, dentro de los límites ({pe.Length})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/pet") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024) && AllText(infoE).Contains("pets"), $"/info lista /pet y el tema, dentro de los límites ({infoE.Length})");
Check(AllText(HelpTopics.Find("boss")!.Build()).Contains("huevo") && AllText(HelpTopics.Find("shop")!.Build()).Contains("Comida para Mascotas"), "los temas de jefes y tienda mencionan el huevo y la comida de mascotas");
var dom = AchievementCatalog.Find("domador"); var cri = AchievementCatalog.Find("criador");
Check(dom is not null && cri is not null && dom.Tiers.Select(t => t.Threshold).SequenceEqual([1L, 3L, 6L]) && cri.Tiers.Select(t => t.Threshold).SequenceEqual([10L, 50L, 150L]) && dom.Tiers.Concat(cri.Tiers).All(t => t.Reward.Box == BoxGrant.None) && AchievementCatalog.Categories[3].Keys.Contains("domador") && dom.StatKind == GameEventKinds.PetHatched && cri.StatKind == GameEventKinds.PetFed, "logros Domador (1/3/5 mascotas) y Criador (10/50/125 comidas = las cinco al máximo): solo oro y XP, en Constancia");
Check(PetRules.TotalFeedsToMax * allSpecies.Count == 150, "150 comidas = las seis mascotas al máximo (el último tramo de Criador)");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un IGameEvents que solo anota lo que le registran (para probar qué eventos salen sin tocar la base).
sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
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
