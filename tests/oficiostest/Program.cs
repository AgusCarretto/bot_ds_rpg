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

// v0.13.0: Oficios (Leñador, Minero, Encantador). Reglas puras, XP sacada de los contadores (con la base real y un usuario descartable), efectos en /chop, /mine y /enchant, las versiones
// avanzadas (nivel 100) con su cooldown propio y la concurrencia, pantallas, comandos con el InteractionService real y la ayuda.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
string Foot(Embed? e) => e?.Footer?.Text ?? string.Empty;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var buffRepo = new BuffRepository(factory);
var pets = new PetRepository(factory);
var blessings = new BlessingRepository(factory);
var eventRepo = new GameEventRepository(factory);
var bonusSvc = new PlayerBonusService(pets, blessings, eventRepo);
var cooldowns = new CooldownRepository(factory);
var gathering = new GatheringRepository(factory);
var dustRepo = new DustRepository(factory);
var events = new GameEventService(eventRepo);   // el de verdad (sin avisos): escribe game_events y player_stats
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000013000UL;
async Task Reset()
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
}
async Task SetStat(string kind, long value) =>
    await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, @k, @v) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = @v", new { id = (long)U, k = kind, v = value });
async Task<long> Stat(string kind) => await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT value FROM player_stats WHERE discord_id = @id AND stat_key = @k), 0)", new { id = (long)U, k = kind });
async Task ClearCooldowns() => await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
async Task<long> Bag() => await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity), 0) FROM inventory WHERE discord_id = @id", new { id = (long)U });
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;
// Con tantos usos de /chop el Leñador está justo en el nivel dado.
long UsesFor(int level, int xpPerUse = 10) => (ProfessionRules.ProgressFor(0).Level == 0 ? (long)Math.Ceiling(CumXp(level) / (double)xpPerUse) : 0);
long CumXp(int level) { long sum = 0; for (int l = 1; l <= level; l++) sum += ProfessionRules.XpForLevel(l); return sum; }
Task<GatheringModule.GatherResult> Chop(bool advanced = false, Random? rng = null) =>
    GatheringModule.ExecuteGatherAsync(cooldowns, gathering, users, itemsRepo, bonusSvc, events, U, CooldownCatalog.Chop, "Madera", advanced, rng);
Task<GatheringModule.GatherResult> Mine(bool advanced = false) =>
    GatheringModule.ExecuteGatherAsync(cooldowns, gathering, users, itemsRepo, bonusSvc, events, U, CooldownCatalog.Mine, "Mineral", advanced);

Console.WriteLine("== 1. La curva y los niveles ==");
Check(ProfessionRules.MaxLevel == 100 && ProfessionRules.XpForLevel(1) == 10 && ProfessionRules.XpForLevel(100) == 186, $"pasar al nivel 1 pide 10 de XP (un uso) y al 100 pide 186 (~19 usos) ({ProfessionRules.XpForLevel(1)} y {ProfessionRules.XpForLevel(100)})");
Check(Enumerable.Range(2, 99).All(l => ProfessionRules.XpForLevel(l) >= ProfessionRules.XpForLevel(l - 1)), "cada nivel pide lo mismo o más que el anterior");
long total = ProfessionRules.TotalXpToMax;
Check(total is > 7800 and < 8400 && Enumerable.Range(1, 100).Sum(l => ProfessionRules.XpForLevel(l)) == total, $"llegar al 100 son {total} de XP (≈ {total / 10} usos de /chop o /mine, ≈ {total / 50} intentos de encantar)");
Check(ProfessionRules.LevelFor(0) == 0 && ProfessionRules.LevelFor(9) == 0 && ProfessionRules.LevelFor(10) == 1 && ProfessionRules.LevelFor(-5) == 0 && ProfessionRules.LevelFor(total) == 100 && ProfessionRules.LevelFor(total - 1) == 99 && ProfessionRules.LevelFor(total * 50) == 100, "nivel 0 sin XP, nivel 1 a los 10, 99 a uno del tope y 100 de ahí en adelante (sin pasarse)");
var pr = ProfessionRules.ProgressFor(CumXp(2) + 5);
Check(pr is { Level: 2, TotalXp: 25 } && pr.IntoLevel == 5 && pr.ToNext == ProfessionRules.XpForLevel(3) && !pr.IsMax, $"el progreso dentro del nivel: nivel {pr.Level}, {pr.IntoLevel}/{pr.ToNext}");
var prMax = ProfessionRules.ProgressFor(total + 77);
Check(prMax.IsMax && prMax.Level == 100 && prMax.ToNext == 0, "al máximo ya no hay «próximo nivel»");
Check(ProfessionCatalog.All.Count == 3 && ProfessionCatalog.All.Select(p => p.Key).Distinct().Count() == 3 && ProfessionCatalog.All.Select(p => p.StatKind).Distinct().Count() == 3, "tres oficios con claves y contadores distintos");
Check(ProfessionCatalog.Get(ProfessionCatalog.WoodcutterKey) is { StatKind: "chop", XpPerAction: 10 } && ProfessionCatalog.Get(ProfessionCatalog.MinerKey) is { StatKind: "mine", XpPerAction: 10 } && ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey) is { StatKind: "enchant", XpPerAction: 50 }, "Leñador = /chop (10 XP), Minero = /mine (10 XP), Encantador = /enchant (50 XP)");
Check(new[] { "Leñador", "lenador", "LEÑADOR", "woodcutter", "talar", "chop" }.All(t => ProfessionCatalog.Find(t)?.Key == ProfessionCatalog.WoodcutterKey) && ProfessionCatalog.Find("minero")?.Key == ProfessionCatalog.MinerKey && ProfessionCatalog.Find("Encantador")?.Key == ProfessionCatalog.EnchanterKey && ProfessionCatalog.Find("nada") is null && ProfessionCatalog.Find(" ") is null, "se encuentran por nombre, sin importar tildes ni mayúsculas, y por alias");
var statsSample = new Dictionary<string, long> { ["chop"] = 30, ["enchant"] = 4, ["mine"] = -9 };
Check(ProfessionRules.XpFrom(ProfessionCatalog.Get("woodcutter"), statsSample) == 300 && ProfessionRules.XpFrom(ProfessionCatalog.Get("enchanter"), statsSample) == 200 && ProfessionRules.XpFrom(ProfessionCatalog.Get("miner"), statsSample) == 0 && ProfessionRules.XpFrom(ProfessionCatalog.Get("miner"), null) == 0, "la XP es el contador por la XP de cada uso (un contador negativo o ausente es 0)");

Console.WriteLine("\n== 2. Lo que da cada nivel ==");
Check(Near(ProfessionRules.QuantityMultiplier(100), 1.2, 1e-12) && Near(ProfessionRules.RarityUpgradeChance(100), 0.1, 1e-12) && Near(ProfessionRules.DustMultiplier(100), 0.75, 1e-12), "al 100: +20 % de cantidad, +10 % de chance de mejorar la rareza y −25 % de Polvo");
Check(ProfessionRules.QuantityMultiplier(0) == 1 && ProfessionRules.RarityUpgradeChance(0) == 0 && ProfessionRules.DustMultiplier(0) == 1 && ProfessionRules.QuantityMultiplier(500) == ProfessionRules.QuantityMultiplier(100) && ProfessionRules.DustMultiplier(-4) == 1, "sin nivel no cambia nada, y un nivel fuera de rango se acota");
Check(Near(ProfessionRules.QuantityMultiplier(50), 1.1, 1e-12) && Near(ProfessionRules.DustMultiplier(40), 0.9, 1e-12), "es lineal: el 50 da la mitad del 100 (se nota el 100 contra el 10)");
var rng0 = new Random(2024);
Check(ProfessionRules.UpgradeRarity("Común", 1.0, rng0) == "Raro" && ProfessionRules.UpgradeRarity("Raro", 1.0, rng0) == "Épico" && ProfessionRules.UpgradeRarity("Épico", 1.0, rng0) == "Legendario", "con chance 100 % la rareza sube un escalón (Común → Raro → Épico → Legendario)");
Check(ProfessionRules.UpgradeRarity("Legendario", 1.0, rng0) == "Legendario" && ProfessionRules.UpgradeRarity("Mítico", 1.0, rng0) == "Mítico" && ProfessionRules.UpgradeRarity("Común", 0, rng0) == "Común" && ProfessionRules.UpgradeRarity("Común", -1, rng0) == "Común" && ProfessionRules.UpgradeRarity("???", 1.0, rng0) == "???", "el Legendario no pasa a Mítico, el Mítico nunca se mejora y sin chance no pasa nada");
int upgraded = Enumerable.Range(0, 200000).Count(_ => ProfessionRules.UpgradeRarity("Común", 0.1, rng0) == "Raro");
Check(Near(upgraded / 200000.0, 0.1, 0.004), $"con chance 10 %, mejoran {upgraded / 2000.0:0.0} % de 200.000 (esperado 10)");
var cost1 = ProfessionRules.EnchantCost((112, 10), 0, false); var cost100 = ProfessionRules.EnchantCost((112, 10), 100, false);
Check(cost1 == (112, 10) && cost100 == (112, 8), $"el Polvo de un intento: 10 sin oficio y {cost100.Dust} con el Encantador al 100 (el oro no cambia)");
Check(ProfessionRules.EnchantCost((112, 10), 100, true) == (168, 11) && ProfessionRules.EnchantCost((112, 10), 0, true) == (168, 15) && ProfessionRules.EnchantCost((1000, 1), 100, false).Dust == 1, "el avanzado cuesta 1,5× (oro y Polvo, con el descuento) y el Polvo nunca baja de 1");
Check(ProfessionRules.AdvancedUnlocked(100) && !ProfessionRules.AdvancedUnlocked(99) && ProfessionRules.AdvancedGatherRolls == 4 && ProfessionRules.AdvancedGatherCooldown == TimeSpan.FromHours(1) && ProfessionRules.AdvancedEnchantRolls == 2, "la avanzada se desbloquea justo al 100: 4 sorteos cada 1 h, o 2 tiradas de encantamiento");
Check(CooldownCatalog.ChopAdvanced.CommandName == "chop_adv" && CooldownCatalog.MineAdvanced.CommandName == "mine_adv" && CooldownCatalog.ChopAdvanced.Duration == TimeSpan.FromHours(1) && !CooldownCatalog.All.Contains(CooldownCatalog.ChopAdvanced) && CooldownCatalog.AdvancedOf(CooldownCatalog.Chop) == CooldownCatalog.ChopAdvanced && CooldownCatalog.AdvancedOf(CooldownCatalog.Mine) == CooldownCatalog.MineAdvanced && CooldownCatalog.AdvancedOf(CooldownCatalog.Hunt) is null, "cada recolección avanzada tiene su cooldown propio (no pisa al normal) y no figura en la lista general");
Check(GatheringModule.ParseAdvanced("") == false && GatheringModule.ParseAdvanced("normal") == false && GatheringModule.ParseAdvanced(" Avanzada ") == true && GatheringModule.ParseAdvanced("adv") == true && GatheringModule.ParseAdvanced("advanced") == true && GatheringModule.ParseAdvanced("avanzado") == true && GatheringModule.ParseAdvanced("xyz") is null, "el modo se lee de «avanzada/avanzado/adv/advanced» y lo demás se rechaza");

Console.WriteLine("\n== 3. PlayerBonuses con oficios ==");
var xp100 = new Dictionary<string, long> { ["woodcutter"] = total, ["miner"] = 0, ["enchanter"] = total };
var withProf = new PlayerBonuses(PetBonuses.None, 0, null, xp100);
Check(withProf.ProfessionLevel("woodcutter") == 100 && withProf.ProfessionLevel("miner") == 0 && withProf.ProfessionLevel("nada") == 0 && !withProf.IsNone && PlayerBonuses.None.IsNone, "el nivel sale de la XP, un oficio sin XP es 0 y con algún oficio ya no es «sin nada»");
Check(Near(withProf.ChopMultiplier, 1.2, 1e-12) && withProf.MineMultiplier == 1.0 && Near(withProf.ChopRarityUpgrade, 0.1, 1e-12) && withProf.MineRarityUpgrade == 0 && Near(withProf.EnchantDustMultiplier, 0.75, 1e-12), "cada oficio mejora SU comando: Leñador a /chop, Minero a /mine (sin XP no cambia), Encantador al Polvo");
var combo = new PlayerBonuses(PetBonuses.None, 10, new Dictionary<string, int> { [BlessingCatalog.ChopKey] = 5 }, xp100);
Check(Near(combo.ChopMultiplier, 3.0 * 1.25 * 1.2, 1e-9), "multiplica con Fuego Nuevo (×3 en el FN 10) y con la bendición Mano de Leñador V (×1,25): ×4,5 en total");

Console.WriteLine("\n== 4. La XP sale de los contadores (base real) ==");
await Reset();
var b0 = await bonusSvc.GetAsync(U, 0);
Check(b0.ProfessionLevel("woodcutter") == 0 && b0.ProfessionXpOf("woodcutter") == 0 && b0.ChopMultiplier == 1.0, "un jugador nuevo: todos los oficios en 0 y nada cambia");
await SetStat("chop", 100); await SetStat("mine", 31); await SetStat("enchant", 10);
var b1 = await bonusSvc.GetAsync(U, 0);
Check(b1.ProfessionXpOf("woodcutter") == 1000 && b1.ProfessionXpOf("miner") == 310 && b1.ProfessionXpOf("enchanter") == 500 && b1.ProfessionLevel("woodcutter") == ProfessionRules.LevelFor(1000) && b1.ProfessionLevel("enchanter") == ProfessionRules.LevelFor(500), $"100 talas = 1.000 XP (nivel {b1.ProfessionLevel("woodcutter")}), 31 minadas = 310 y 10 encantamientos = 500 (nivel {b1.ProfessionLevel("enchanter")}): lo que se hizo antes cuenta");
var brokenEvents = new PlayerBonusService(pets, blessings, new ThrowingEvents());
var b2 = await brokenEvents.GetAsync(U, 3);
Check(b2.FuegoNuevo == 3 && b2.ProfessionXp is null && b2.ProfessionLevel("woodcutter") == 0, "si falla la lectura de los contadores NO se cae: sigue sin oficios (el servicio nunca tira)");

Console.WriteLine("\n== 5. /chop sube el oficio ==");
await Reset(); await ClearCooldowns();
var c1 = await Chop();
Check(c1.Embed is not null && !c1.Ephemeral && Foot(c1.Embed)?.Contains("Leñador subió al nivel 1") == true && await Stat("chop") == 1 && await Bag() >= 1, $"el primer /chop da XP, sube al nivel 1 y lo festeja al pie («{c1.Embed?.Footer?.Text}»)");
await ClearCooldowns();
var c2 = await Chop();
Check(Foot(c2.Embed).Contains("Leñador subió al nivel 2") && await Stat("chop") == 2, $"el segundo uso (20 XP) sube al nivel 2 («{Foot(c2.Embed)}»)");
await ClearCooldowns();
var c3 = await Chop();
Check(Foot(c3.Embed).StartsWith("🪓 Leñador nivel 2 ·") && Foot(c3.Embed).Contains("/") && await Stat("chop") == 3, $"el siguiente muestra dónde está («{Foot(c3.Embed)}»)");
var cCool = await Chop();
Check(cCool.Embed is not null && cCool.Ephemeral && cCool.Embed.Title.Contains("todavía no podés") && await Stat("chop") == 3, "con el cooldown vigente no suma XP ni da nada");
await ClearCooldowns();
var m1 = await Mine();
Check(m1.Embed is not null && Foot(m1.Embed).Contains("Minero") && await Stat("mine") == 1 && await Stat("chop") == 3, "/mine sube el Minero y no toca al Leñador");
var lockedAdv = await Chop(advanced: true);
Check(lockedAdv.PlainMessage is not null && lockedAdv.PlainMessage.Contains("se desbloquea") && lockedAdv.PlainMessage.Contains("nivel **100**") && lockedAdv.Ephemeral && lockedAdv.Embed is null && await Stat("chop") == 3, "la avanzada sin el nivel 100 se rechaza explicando qué falta, sin gastar nada");

Console.WriteLine("\n== 6. Los efectos en la recolección (nivel 100) ==");
await Reset(); await ClearCooldowns();
await SetStat("chop", total / 10 + 5);
var topBonuses = await bonusSvc.GetAsync(U, 0);
Check(topBonuses.ProfessionLevel("woodcutter") == 100 && Near(topBonuses.ChopMultiplier, 1.2, 1e-9), "con ~810 usos el Leñador está al 100");
var min = new MinRandom();
int notCommon = 0, runs = 40; var quantities = new List<int>();
for (int i = 0; i < runs; i++)
{
    await ClearCooldowns();
    long before = await Bag();
    var r = await Chop(rng: min);
    quantities.Add((int)(await Bag() - before));
    if (r.Embed is not null && !r.Embed.Fields.Any(f => f.Name == "Rareza" && f.Value == "Común")) notCommon++;
}
Check(notCommon == runs && r_allUpgraded(), $"con el azar forzado a «mejorar»: las {runs} talas salieron de Raro para arriba (nunca Común)");
bool r_allUpgraded() => true;
Check(quantities.All(q => q == 2), $"y con el azar al mínimo la cantidad es 1 × 1,2 redondeada hacia arriba = 2 ({string.Join(",", quantities.Distinct())})");
await ClearCooldowns();
var capped = await Chop();
Check(Foot(capped.Embed).Contains("(máximo)") || Foot(capped.Embed).Contains("nivel 100"), $"al 100 el pie dice que está al máximo («{Foot(capped.Embed)}»)");

Console.WriteLine("\n== 7. La versión avanzada ==");
await ClearCooldowns();
long bagBefore = await Bag(); long chopsBefore = await Stat("chop"); long unitsBefore = await Stat("gathered_units");
var adv = await Chop(advanced: true);
long gained = await Bag() - bagBefore;
Check(adv.Embed is not null && !adv.Ephemeral && adv.Embed.Title.Contains("Tala avanzada exitosa") && gained >= 4 && await Stat("chop") == chopsBefore + 4 && await Stat("gathered_units") == unitsBefore + gained, $"la tala avanzada da 4 sorteos juntos ({gained} unidades), cuenta 4 usos y registra las unidades");
var lines = adv.Embed!.Description.Split('\n');
Check(lines.Length is >= 1 and <= 4 && lines.All(l => l.Contains('×') && (l.Contains("Común") || l.Contains("Raro") || l.Contains("Épico") || l.Contains("Legendario") || l.Contains("Mítico"))) && Foot(adv.Embed).Contains("Leñador"), $"la pantalla lista cada material en su renglón con su rareza ({lines.Length} renglones) y trae el pie del oficio");
var advAgain = await Chop(advanced: true);
Check(advAgain.Embed is not null && advAgain.Ephemeral && advAgain.Embed.Title.Contains("Tala avanzada") && advAgain.Embed.Title.Contains("todavía no podés"), "la avanzada tiene su propio cooldown de 1 hora: una segunda enseguida se rechaza");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id AND command_name = 'chop'", new { id = (long)U });
var normalAfter = await Chop();
Check(normalAfter.Embed is not null && !normalAfter.Ephemeral && normalAfter.Embed.Title.Contains("Talar exitoso"), "y el /chop normal sigue andando aparte (cooldowns independientes)");
Check((await db.QueryAsync<string>("SELECT command_name FROM cooldowns WHERE discord_id = @id ORDER BY command_name", new { id = (long)U })).SequenceEqual(["chop", "chop_adv"]), "quedan los dos cooldowns, el normal y el avanzado");
await ClearCooldowns();
long chopsBeforeRace = await Stat("chop"); long bagBeforeRace = await Bag();
var race = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Chop(advanced: true))));
long raceGain = await Bag() - bagBeforeRace;
int wins = race.Count(r => r.Embed is not null && !r.Ephemeral);
Check(wins == 1 && await Stat("chop") == chopsBeforeRace + 4 && raceGain >= 4 && raceGain <= 4 * 8, $"12 talas avanzadas a la vez: UNA sola se paga ({wins}), suma 4 usos y {raceGain} unidades, sin duplicar");
await Reset(); await SetStat("mine", total / 10 + 5); await ClearCooldowns();
var advMine = await Mine(advanced: true);
Check(advMine.Embed is not null && advMine.Embed.Title.Contains("Minería avanzada") && await Stat("mine") >= total / 10 + 5 + 4, "la minería avanzada funciona igual con el Minero al 100");

Console.WriteLine("\n== 8. /enchant sube el Encantador ==");
await Reset();
var weaponId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = 'Weapon' AND rarity = 'Común' ORDER BY item_id LIMIT 1");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, gold = 1000000, dust = 5000 WHERE discord_id = @id", new { id = (long)U, w = weaponId });
var gearItem = (await itemsRepo.GetByIdAsync(weaponId))!;
var baseCost = Enchantments.Cost(Enchantments.GearRank(gearItem.Rarity));
var e1 = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", bonusService: bonusSvc);
var u1 = await Me();
Check(e1.Embed is not null && u1.Gold == 1000000 - baseCost.Gold && u1.Dust == 5000 - baseCost.Dust && await Stat("enchant") == 1 && Foot(e1.Embed).Contains("Encantador subió al nivel"), $"un intento cuesta {baseCost.Gold} oro y {baseCost.Dust} Polvo sin oficio, suma 1 uso (50 XP ⇒ nivel 1) y lo festeja al pie («{e1.Embed?.Footer?.Text}»)");
var eLocked = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", bonusService: bonusSvc, advanced: true);
Check(eLocked.PlainMessage is not null && eLocked.PlainMessage.Contains("se desbloquea") && eLocked.Embed is null && await Stat("enchant") == 1 && (await Me()).Dust == u1.Dust, "el avanzado sin el nivel 100 se rechaza sin gastar nada");
await SetStat("enchant", total / 50 + 3);
long dustBefore = (await Me()).Dust, goldBefore = (await Me()).Gold;
var e2 = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", bonusService: bonusSvc);
var u2 = await Me();
var disc = ProfessionRules.EnchantCost(baseCost, 100, false);
Check(e2.Embed is not null && goldBefore - u2.Gold == disc.Gold && dustBefore - u2.Dust == disc.Dust && disc.Dust < baseCost.Dust, $"con el Encantador al 100 el intento cuesta {disc.Dust} de Polvo en vez de {baseCost.Dust} (oro igual: {disc.Gold})");
long dustBefore3 = (await Me()).Dust, goldBefore3 = (await Me()).Gold; long enchBefore3 = await Stat("enchant");
var e3 = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", rng: min, bonusService: bonusSvc, advanced: true);
var u3 = await Me();
var advCost = ProfessionRules.EnchantCost(baseCost, 100, true);
Check(e3.Embed is not null && goldBefore3 - u3.Gold == advCost.Gold && dustBefore3 - u3.Dust == advCost.Dust && await Stat("enchant") == enchBefore3 + 2 && AllText(e3.Embed).Contains("Tiraste 2 veces"), $"el avanzado cuesta {advCost.Gold} oro y {advCost.Dust} Polvo (1,5×), cuenta 2 usos y muestra las dos tiradas");
double single = Enumerable.Range(0, 20000).Average(_ => Enchantments.Roll()); double best2 = Enumerable.Range(0, 20000).Average(_ => Math.Max(Enchantments.Roll(), Enchantments.Roll()));
Check(best2 > single + 0.3, $"quedarse con la mejor de dos sube el tier promedio ({single:0.00} → {best2:0.00})");
var info = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, null, bonusService: bonusSvc);
Check(info.Embed is not null && info.Embed.Fields.Any(f => f.Name.Contains("Encantador · nivel 100")) && AllText(info.Embed).Contains($"{disc.Dust} Polvo") && info.Embed.Length <= 6000, "/enchant sin pieza muestra el oficio y ya el Polvo con el descuento");
Check(AllText(DustModule.BuildOptionsEmbed()).Contains("El oficio Encantador") && DustModule.BuildOptionsEmbed().Length <= 6000, "/enchant info explica el oficio y entra en los límites");

Console.WriteLine("\n== 9. Fuego Nuevo no toca los oficios ==");
await Reset();
await SetStat("chop", 500); await SetStat("mine", 120); await SetStat("enchant", 9);
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
var fnRepo = new FuegoNuevoRepository(factory);
var renewed = await fnRepo.RenewAsync(U, 0, "Ninja");
Check(renewed.Status == RenewStatus.Ok && await Stat("chop") == 500 && await Stat("mine") == 120 && await Stat("enchant") == 9 && (await bonusSvc.GetAsync(U, 1)).ProfessionXpOf("woodcutter") == 5000, "después de un Fuego Nuevo los contadores (y los niveles) siguen igual");

Console.WriteLine("\n== 10. Pantallas ==");
await Reset();
Check((await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U + 1)).PlainMessage!.Contains("/start"), "sin cuenta, /professions manda a /start");
var pe0 = (await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U)).Embed!;
Check(pe0.Fields.Length == 3 && pe0.Fields.All(f => f.Name.Contains("nivel 0/100") && f.Value.Contains("🔒") && f.Value.Contains("Cada nivel da")) && pe0.Length <= 6000 && pe0.Fields.All(f => f.Value.Length <= 1024), "un jugador nuevo ve los 3 oficios en 0 con la avanzada bloqueada y lo que da cada nivel");
await SetStat("chop", 100); await SetStat("mine", total / 10 + 1); await SetStat("enchant", 5);
var pe1 = (await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U)).Embed!;
string chopField = pe1.Fields.Single(f => f.Name.Contains("Leñador")).Value, mineField = pe1.Fields.Single(f => f.Name.Contains("Minero")).Value;
Check(chopField.Contains("█") && chopField.Contains("XP") && chopField.Contains("Hoy da +") && chopField.Contains("🔒") && mineField.Contains("¡Nivel máximo!") && mineField.Contains("🔓") && pe1.Fields.Single(f => f.Name.Contains("Minero")).Name.Contains("nivel 100/100"), "con progreso: barra y XP del Leñador, lo que da hoy, y el Minero al máximo con la avanzada desbloqueada");
Check(AllText(pe1).Contains("/chop") && AllText(pe1).Contains("/enchant") && AllText(pe1).Contains("Fuego Nuevo") && Foot(pe1).Contains("también cuenta"), "explica con qué comando sube cada uno, que se quedan con el Fuego Nuevo y que lo anterior cuenta");
var prof = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets, bonusSvc);
var profPlain = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets);
Check(!prof.Fields.Any(f => f.Name.Contains("Oficios")) && !profPlain.Fields.Any(f => f.Name.Contains("Oficios")) && prof.Length <= 6000, "el perfil ya NO muestra los oficios (v0.14.3: se ven con /professions)");
await Reset();
Check(!(await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets, bonusSvc)).Fields.Any(f => f.Name.Contains("Oficios")), "y un jugador nuevo no ve la línea");
var cdNew = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, bonusSvc);
await SetStat("chop", total / 10 + 5);
var cdTop = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, bonusSvc);
Check(!cdNew.Description.Contains("avanzada") && cdTop.Description.Contains("Tala avanzada") && !cdTop.Description.Contains("Minería avanzada") && !(await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U)).Description.Contains("avanzada"), "/cd muestra la tala avanzada solo con el Leñador al 100 (y la minería no, hasta que llegue)");

Console.WriteLine("\n== 11. Comandos registrados y DI (InteractionService real) ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var profCmd = interactions.SlashCommands.Single(c => c.Name == "professions");
Check(profCmd.Module.SlashGroupName is null && validName(profCmd.Name) && profCmd.Description.Length is >= 1 and <= 100 && profCmd.Parameters.Count == 0, "/professions existe, sin grupo ni parámetros y dentro de los límites de Discord");
foreach (var name in new[] { "chop", "mine", "enchant" })
{
    var cmd = interactions.SlashCommands.Single(c => c.Name == name && c.Module.SlashGroupName is null);
    var modo = cmd.Parameters.SingleOrDefault(p => p.Name == "modo");
    Check(modo is { IsRequired: false, Choices.Count: 2 } && modo.Description.Length is >= 1 and <= 100 && cmd.Description.Length <= 100 && cmd.Parameters.All(p => validName(p.Name) && p.Description.Length is >= 1 and <= 100), $"/{name} tiene el parámetro opcional «modo» con 2 opciones y respeta los límites");
}
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "professions", "oficios", "oficio", "profesiones", "profesion", "prof" }.All(textNames.Contains) && textNames.Count(n => n == "prof") == 1 && textNames.Count(n => n == "oficios") == 1, "aa professions / oficios / profesiones / prof están registrados, sin repetirse");
Check(interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "siguen entrando en el tope de 100 comandos de nivel superior");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");

Console.WriteLine("\n== 12. Ayuda ==");
var topic = HelpTopics.Find("oficios");
Check(topic?.Key == "professions" && new[] { "oficio", "profesiones", "prof", "leñador", "lenador", "professions" }.All(a => HelpTopics.Find(a)?.Key == "professions") && HelpTopics.Find("enchant")?.Key == "enchant" && HelpTopics.Find("gather")?.Key == "gather" && HelpTopics.Find("fuego")?.Key == "fuego", "«info oficios / prof / leñador» abren el tema nuevo y los demás siguen en su lugar");
var te = topic!.Build(); string tt = AllText(te);
Check(te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024) && tt.Contains("/professions") && tt.Contains("+0,2 %") && tt.Contains("0,1 %") && tt.Contains("0,25 %") && tt.Contains("1,5×") && tt.Contains("se quedan"), $"el tema explica cómo suben, lo que da cada nivel (leído de las constantes), la avanzada y el Fuego Nuevo, dentro de los límites ({te.Length})");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/professions") && AllText(infoE).Contains("professions") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024), $"/info lista /professions y el tema, dentro de los límites ({infoE.Length})");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un Random que siempre da lo mínimo y NextDouble() = 0: fuerza «sí mejora la rareza» y la cantidad en su piso (redondeada hacia arriba).
sealed class MinRandom : Random
{
    public override double NextDouble() => 0.0;
    public override int Next(int minValue, int maxValue) => minValue;
    public override int Next(int maxValue) => 0;
    public override int Next() => 0;
}

sealed class ThrowingEvents : IGameEventRepository
{
    public Task<long> RecordAsync(ulong discordId, string kind, int? zoneId, long amount, string? detail, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
    public Task<IReadOnlyDictionary<string, long>> GetStatsAsync(ulong discordId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
    public Task<IReadOnlyList<EventTotal>> GetBreakdownAsync(ulong discordId, IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
    public Task<IReadOnlyList<RecentEvent>> GetRecentAsync(ulong discordId, IReadOnlyCollection<string> kinds, int limit, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
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
