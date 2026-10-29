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
// avanzadas (nivel 100) con su cooldown propio y la concurrencia, pantallas, comandos con el InteractionService real y la ayuda. v0.38.1: el nivel sigue subiendo pasado el 100 (costo exponencial, efectos al tope).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
string Foot(Embed? e) => e?.Footer?.Text ?? string.Empty;
string RarityOf(Embed? e) => e?.Fields.Where(f => f.Name == "Rareza").Select(f => f.Value).FirstOrDefault() ?? string.Empty;

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
// Con tanta XP de tala el Leñador está justo en el nivel dado (v0.20.0: la XP sale de chop_xp, no de los usos).
long XpFor(int level) => CumXp(level);
long CumXp(int level) { long sum = 0; for (int l = 1; l <= level; l++) sum += ProfessionRules.XpForLevel(l); return sum; }
Task<GatheringModule.GatherResult> Chop(bool advanced = false, Random? rng = null) =>
    GatheringModule.ExecuteGatherAsync(cooldowns, gathering, users, itemsRepo, bonusSvc, events, U, CooldownCatalog.Chop, "Madera", advanced, rng);
Task<GatheringModule.GatherResult> Mine(bool advanced = false) =>
    GatheringModule.ExecuteGatherAsync(cooldowns, gathering, users, itemsRepo, bonusSvc, events, U, CooldownCatalog.Mine, "Mineral", advanced);

Console.WriteLine("== 1. La curva y los niveles ==");
Check(ProfessionRules.MaxLevel == 100 && ProfessionRules.XpForLevel(1) == 5 && ProfessionRules.XpForLevel(100) == 93, $"pasar al nivel 1 pide 5 de XP y al 100 pide 93 (la unidad bajó de 10 a 5 en la v0.20.0) ({ProfessionRules.XpForLevel(1)} y {ProfessionRules.XpForLevel(100)})");
Check(Enumerable.Range(2, 99).All(l => ProfessionRules.XpForLevel(l) >= ProfessionRules.XpForLevel(l - 1)), "cada nivel pide lo mismo o más que el anterior");
long total = ProfessionRules.TotalXpToMax;
Check(total is > 3900 and < 4200 && Enumerable.Range(1, 100).Sum(l => ProfessionRules.XpForLevel(l)) == total, $"llegar al 100 son {total} de XP (≈ {total / ProfessionRules.AverageGatherXp():0} tiradas de /chop o /mine con la XP por rareza, ≈ {total / 25} intentos de encantar)");
Check(ProfessionRules.LevelFor(0) == 0 && ProfessionRules.LevelFor(4) == 0 && ProfessionRules.LevelFor(5) == 1 && ProfessionRules.LevelFor(-5) == 0 && ProfessionRules.LevelFor(total) == 100 && ProfessionRules.LevelFor(total - 1) == 99 && ProfessionRules.LevelFor(total + 97) == 100 && ProfessionRules.LevelFor(total + 98) == 101, "nivel 0 sin XP, nivel 1 a los 5, 99 a uno del 100 y el 100 dura hasta juntar los 98 del siguiente (v0.38.1: el 100 ya no es el tope)");
var pr = ProfessionRules.ProgressFor(CumXp(2) + 2);
Check(pr.Level == 2 && pr.TotalXp == CumXp(2) + 2 && pr.IntoLevel == 2 && pr.ToNext == ProfessionRules.XpForLevel(3) && !pr.IsMax, $"el progreso dentro del nivel: nivel {pr.Level}, {pr.IntoLevel}/{pr.ToNext}");
var prMax = ProfessionRules.ProgressFor(total + 77);
Check(prMax.AtEffectCap && !prMax.IsMax && prMax.Level == 100 && prMax.IntoLevel == 77 && prMax.ToNext == 98, "en el 100 los efectos están al tope pero SIGUE habiendo próximo nivel (77/98 XP)");
Check(ProfessionCatalog.All.Count == 3 && ProfessionCatalog.All.Select(p => p.Key).Distinct().Count() == 3 && ProfessionCatalog.All.Select(p => p.StatKind).Distinct().Count() == 3, "tres oficios con claves y contadores distintos");
Check(ProfessionCatalog.Get(ProfessionCatalog.WoodcutterKey) is { StatKind: "chop_xp", XpPerAction: 1 } && ProfessionCatalog.Get(ProfessionCatalog.MinerKey) is { StatKind: "mine_xp", XpPerAction: 1 } && ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey) is { StatKind: "enchant", XpPerAction: 25 }, "Leñador = chop_xp y Minero = mine_xp (el contador ya es XP: la suma cada tirada según la rareza), Encantador = /enchant (25 XP por intento)");
Check(new[] { "Leñador", "lenador", "LEÑADOR", "woodcutter", "talar", "chop" }.All(t => ProfessionCatalog.Find(t)?.Key == ProfessionCatalog.WoodcutterKey) && ProfessionCatalog.Find("minero")?.Key == ProfessionCatalog.MinerKey && ProfessionCatalog.Find("Encantador")?.Key == ProfessionCatalog.EnchanterKey && ProfessionCatalog.Find("nada") is null && ProfessionCatalog.Find(" ") is null, "se encuentran por nombre, sin importar tildes ni mayúsculas, y por alias");
var statsSample = new Dictionary<string, long> { ["chop_xp"] = 300, ["enchant"] = 4, ["mine_xp"] = -9, ["chop"] = 999 };
Check(ProfessionRules.XpFrom(ProfessionCatalog.Get("woodcutter"), statsSample) == 300 && ProfessionRules.XpFrom(ProfessionCatalog.Get("enchanter"), statsSample) == 100 && ProfessionRules.XpFrom(ProfessionCatalog.Get("miner"), statsSample) == 0 && ProfessionRules.XpFrom(ProfessionCatalog.Get("miner"), null) == 0, "la XP es el contador por la XP de cada unidad (un contador negativo o ausente es 0) y los usos viejos de /chop ya no suman por sí solos");
Check(new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Select(ProfessionRules.GatherXp).SequenceEqual([2, 7, 15, 30, 100]) && ProfessionRules.GatherXp("???") == 2 && ProfessionRules.GatherXp(null) == 2, "la XP de una tirada por rareza: Común 2, Raro 7, Épico 15, Legendario 30, Mítico 100 (una rareza desconocida cuenta como Común)");
Check(Near(ProfessionRules.AverageGatherXp(), 0.68 * 2 + 0.21 * 7 + 0.07 * 15 + 0.035 * 30 + 0.005 * 100, 1e-9) && Near(ProfessionRules.AverageGatherXp(), 5.43, 0.01) && ProfessionRules.DescribeGatherXp() == "Común 2 · Raro 7 · Épico 15 · Legendario 30 · Mítico 100", $"el promedio con las chances reales es {ProfessionRules.AverageGatherXp():0.00} de XP por tirada: con la unidad de 5 el ritmo es el de siempre ({total / ProfessionRules.AverageGatherXp():0} tiradas al 100, eran ~810)");

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
await SetStat("chop_xp", 1000); await SetStat("mine_xp", 310); await SetStat("enchant", 10); await SetStat("chop", 5000);
var b1 = await bonusSvc.GetAsync(U, 0);
Check(b1.ProfessionXpOf("woodcutter") == 1000 && b1.ProfessionXpOf("miner") == 310 && b1.ProfessionXpOf("enchanter") == 250 && b1.ProfessionLevel("woodcutter") == ProfessionRules.LevelFor(1000) && b1.ProfessionLevel("enchanter") == ProfessionRules.LevelFor(250), $"1.000 de XP de tala (nivel {b1.ProfessionLevel("woodcutter")}), 310 de minería y 10 encantamientos = 250 (nivel {b1.ProfessionLevel("enchanter")}); y los 5.000 usos viejos de /chop NO suman XP por sí solos (de eso se ocupa la migración)");
var brokenEvents = new PlayerBonusService(pets, blessings, new ThrowingEvents());
var b2 = await brokenEvents.GetAsync(U, 3);
Check(b2.FuegoNuevo == 3 && b2.ProfessionXp is null && b2.ProfessionLevel("woodcutter") == 0, "si falla la lectura de los contadores NO se cae: sigue sin oficios (el servicio nunca tira)");

Console.WriteLine("\n== 5. /chop sube el oficio ==");
await Reset(); await ClearCooldowns();
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });   // game_events no tiene clave foránea al jugador: quedan filas de corridas anteriores
long xpRun = 0;
var chopSteps = new List<GatheringModule.GatherResult>();
for (int i = 0; i < 3; i++)
{
    await ClearCooldowns();
    var step = await Chop();
    chopSteps.Add(step);
    int stepXp = ProfessionRules.GatherXp(RarityOf(step.Embed));
    xpRun += stepXp;
    Check(step.Embed is not null && !step.Ephemeral && Foot(step.Embed).Contains($"(+{stepXp})") && await Stat("chop") == i + 1 && await Stat("chop_xp") == xpRun && await Bag() >= i + 1,
        $"el /chop {i + 1} (salió {RarityOf(step.Embed)}) suma {stepXp} de XP por la rareza, lo dice al pie y el contador queda en {xpRun} («{Foot(step.Embed)}»)");
}
var lastFoot = Foot(chopSteps[^1].Embed);
var levelNow = ProfessionRules.LevelFor(xpRun);
Check(lastFoot.Contains(levelNow > ProfessionRules.LevelFor(xpRun - ProfessionRules.GatherXp(RarityOf(chopSteps[^1].Embed))) ? $"subió al nivel {levelNow}" : $"nivel {levelNow}"), $"el pie dice el nivel que corresponde a {xpRun} de XP (nivel {levelNow}): «{lastFoot}»");
Check(await db.ExecuteScalarAsync<long>("SELECT count(*) FROM game_events WHERE discord_id = @id AND kind = 'chop_xp'", new { id = (long)U }) == 3 && await db.ExecuteScalarAsync<string>("SELECT detail FROM game_events WHERE discord_id = @id AND kind = 'chop_xp' ORDER BY event_id LIMIT 1", new { id = (long)U }) == RarityOf(chopSteps[0].Embed), "cada tirada registra su evento chop_xp con la rareza como detalle");
var cCool = await Chop();
Check(cCool.Embed is not null && cCool.Ephemeral && cCool.Embed.Title.Contains("todavía no podés") && await Stat("chop") == 3 && await Stat("chop_xp") == xpRun, "con el cooldown vigente no suma XP ni da nada");
await ClearCooldowns();
var m1 = await Mine();
Check(m1.Embed is not null && Foot(m1.Embed).Contains("Minero") && await Stat("mine") == 1 && await Stat("mine_xp") == ProfessionRules.GatherXp(RarityOf(m1.Embed)) && await Stat("chop") == 3 && await Stat("chop_xp") == xpRun, "/mine sube el Minero (con la XP de su rareza) y no toca al Leñador");
var lockedAdv = await Chop(advanced: true);
Check(lockedAdv.PlainMessage is not null && lockedAdv.PlainMessage.Contains("se desbloquea") && lockedAdv.PlainMessage.Contains("nivel **100**") && lockedAdv.Ephemeral && lockedAdv.Embed is null && await Stat("chop") == 3, "la avanzada sin el nivel 100 se rechaza explicando qué falta, sin gastar nada");

Console.WriteLine("\n== 6. Los efectos en la recolección (nivel 100) ==");
await Reset(); await ClearCooldowns();
await SetStat("chop_xp", total);
var topBonuses = await bonusSvc.GetAsync(U, 0);
Check(topBonuses.ProfessionLevel("woodcutter") == 100 && Near(topBonuses.ChopMultiplier, 1.2, 1e-9), "con la XP del nivel 100 el Leñador está al 100");
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
int footLevel = int.Parse(System.Text.RegularExpressions.Regex.Match(Foot(capped.Embed), @"nivel (\d+)").Groups[1].Value);
Check(footLevel >= 100 && !Foot(capped.Embed).Contains("(máximo)") && Foot(capped.Embed).Contains("XP"), $"pasado el 100 el pie sigue mostrando el avance, ya no dice «máximo» («{Foot(capped.Embed)}»)");

Console.WriteLine("\n== 7. La versión avanzada ==");
await ClearCooldowns();
long bagBefore = await Bag(); long chopsBefore = await Stat("chop"); long unitsBefore = await Stat("gathered_units");
long xpBeforeAdv = await Stat("chop_xp");
var adv = await Chop(advanced: true);
long gained = await Bag() - bagBefore;
Check(adv.Embed is not null && !adv.Ephemeral && adv.Embed.Title.Contains("Tala avanzada exitosa") && gained >= 4 && await Stat("chop") == chopsBefore + 4 && await Stat("gathered_units") == unitsBefore + gained, $"la tala avanzada da 4 sorteos juntos ({gained} unidades), cuenta 4 usos y registra las unidades");
long advXp = await Stat("chop_xp") - xpBeforeAdv;
var advDetail = (await db.ExecuteScalarAsync<string>("SELECT detail FROM game_events WHERE discord_id = @id AND kind = 'chop_xp' ORDER BY event_id DESC LIMIT 1", new { id = (long)U }))!.Split(',');
Check(advXp >= 4 * 2 && advXp <= 4 * 100 && advDetail.Length == 4 && advDetail.Sum(r => ProfessionRules.GatherXp(r)) == advXp, $"la tala avanzada suma la XP de la rareza de CADA una de sus 4 tiradas ({advXp} de XP: {string.Join('+', advDetail)})");
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
await Reset(); await SetStat("mine_xp", total); await ClearCooldowns();
var advMine = await Mine(advanced: true);
Check(advMine.Embed is not null && advMine.Embed.Title.Contains("Minería avanzada") && await Stat("mine") == 4 && await Stat("mine_xp") > total, "la minería avanzada funciona igual con el Minero al 100");

Console.WriteLine("\n== 8. /enchant sube el Encantador ==");
await Reset();
var weaponId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = 'Weapon' AND rarity = 'Común' ORDER BY item_id LIMIT 1");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, gold = 1000000, dust = 5000 WHERE discord_id = @id", new { id = (long)U, w = weaponId });
var gearItem = (await itemsRepo.GetByIdAsync(weaponId))!;
var baseCost = Enchantments.Cost(Enchantments.GearRank(gearItem.Rarity));
var e1 = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", bonusService: bonusSvc);
var u1 = await Me();
Check(e1.Embed is not null && u1.Gold == 1000000 - baseCost.Gold && u1.Dust == 5000 - baseCost.Dust && await Stat("enchant") == 1 && Foot(e1.Embed).Contains("Encantador subió al nivel"), $"un intento cuesta {baseCost.Gold} oro y {baseCost.Dust} Polvo sin oficio, suma 1 uso (25 XP ⇒ nivel 4) y lo festeja al pie («{e1.Embed?.Footer?.Text}»)");
var eLocked = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", bonusService: bonusSvc, advanced: true);
Check(eLocked.PlainMessage is not null && eLocked.PlainMessage.Contains("se desbloquea") && eLocked.Embed is null && await Stat("enchant") == 1 && (await Me()).Dust == u1.Dust, "el avanzado sin el nivel 100 se rechaza sin gastar nada");
await SetStat("enchant", total / 25 + 3);
long dustBefore = (await Me()).Dust, goldBefore = (await Me()).Gold;
var e2 = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", bonusService: bonusSvc);
var u2 = await Me();
var disc = ProfessionRules.EnchantCost(baseCost, 100, false);
Check(e2.Embed is not null && goldBefore - u2.Gold == disc.Gold && dustBefore - u2.Dust == disc.Dust && disc.Dust < baseCost.Dust, $"con el Encantador al 100 el intento cuesta {disc.Dust} de Polvo en vez de {baseCost.Dust} (oro igual: {disc.Gold})");
// El intento de arriba (e2) usa el azar de verdad: si sacó un tier alto, el texto del siguiente lo nombra (por ejemplo «Incandescente») y esta prueba fallaba una de cada diez veces. Se deja la pieza en tier I para que lo forzado de abajo sea determinista.
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 1, weapon_enchant_kind = 'filo' WHERE discord_id = @id", new { id = (long)U });
long dustBefore3 = (await Me()).Dust, goldBefore3 = (await Me()).Gold; long enchBefore3 = await Stat("enchant");
var e3 = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, "arma", rng: min, bonusService: bonusSvc, advanced: true);
var u3 = await Me();
var advCost = ProfessionRules.EnchantCost(baseCost, 100, true);
Check(e3.Embed is not null && goldBefore3 - u3.Gold == advCost.Gold && dustBefore3 - u3.Dust == advCost.Dust && await Stat("enchant") == enchBefore3 + 2 && AllText(e3.Embed).Contains("Tiraste 2 veces"), $"el avanzado cuesta {advCost.Gold} oro y {advCost.Dust} Polvo (1,5×), cuenta 2 usos y muestra las dos tiradas");
Check(Foot(e3.Embed).Contains("tu oficio mejoró el tier") && AllText(e3.Embed).Contains("Incandescente") == false, "con el Encantador al 100 el pie avisa cuando el oficio subió el tier sorteado (azar forzado: salió Tibio, subió a Al Rojo)");
Check(ProfessionRules.TierUpgradeChance(0) == 0 && Near(ProfessionRules.TierUpgradeChance(100), 0.1, 1e-12) && ProfessionRules.TierUpgradeChance(500) == ProfessionRules.TierUpgradeChance(100) && ProfessionRules.TierUpgradeChance(-3) == 0 && ProfessionRules.DescribePerLevel(ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey)).Contains("mejorar el tier"),
    "el Encantador suma +0,1 % de chance de mejorar el tier por nivel (+10 % al 100), acotado, y la pantalla lo dice");
var upRng = new Random(5); int upgradedCount = 0, v5 = 0, over = 0; const int UpN = 300000;
for (int i = 0; i < UpN; i++) { var (tier, up) = Enchantments.RollWithBonus(upRng, 0.1); if (up) upgradedCount++; if (tier == 5) v5++; if (tier > 5 || tier < 1) over++; }
Check(over == 0 && Near(v5 / (double)UpN, 0.01 + (0.09 * 0.1), 0.0015) && Near(upgradedCount / (double)UpN, 0.1 * (1 - 0.01), 0.005), $"con +10 % de ayuda el tier V sale {100.0 * v5 / UpN:0.00} % (1 % de base + 9 % del IV × 10 %), nunca pasa del V y {100.0 * upgradedCount / UpN:0.0} % de los tiros suben uno");
Check(Enchantments.RollWithBonus(new Random(1), 1.0).Upgraded && Enchantments.RollWithBonus(new Random(1), 0.0) is { Upgraded: false } && Enumerable.Range(0, 50).All(_ => Enchantments.RollWithBonus(null, 1.0).Tier is >= 2 and <= 5), "con chance 100 % el tier siempre sube uno (salvo el V) y sin chance nunca");
double single = Enumerable.Range(0, 20000).Average(_ => Enchantments.Roll()); double best2 = Enumerable.Range(0, 20000).Average(_ => Math.Max(Enchantments.Roll(), Enchantments.Roll()));
Check(best2 > single + 0.3, $"quedarse con la mejor de dos sube el tier promedio ({single:0.00} → {best2:0.00})");
var info = await DustModule.ExecuteEnchantAsync(users, itemsRepo, dustRepo, events, U, null, bonusService: bonusSvc);
Check(info.Embed is not null && info.Embed.Fields.Any(f => f.Name.Contains("Encantador · nivel ") && int.Parse(f.Name.Split("nivel ")[1].Split(' ')[0]) >= 100) && AllText(info.Embed).Contains($"{disc.Dust} Polvo") && info.Embed.Length <= 6000, "/enchant sin pieza muestra el oficio y ya el Polvo con el descuento");
Check(AllText(DustModule.BuildOptionsEmbed()).Contains("El oficio Encantador") && DustModule.BuildOptionsEmbed().Length <= 6000, "/enchant info explica el oficio y entra en los límites");

Console.WriteLine("\n== 9. Fuego Nuevo no toca los oficios ==");
await Reset();
await SetStat("chop_xp", 2500); await SetStat("mine_xp", 600); await SetStat("enchant", 9);
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
var fnRepo = new FuegoNuevoRepository(factory);
var renewed = await fnRepo.RenewAsync(U, 0, "Ninja");
Check(renewed.Status == RenewStatus.Ok && await Stat("chop_xp") == 2500 && await Stat("mine_xp") == 600 && await Stat("enchant") == 9 && (await bonusSvc.GetAsync(U, 1)).ProfessionXpOf("woodcutter") == 2500, "después de un Fuego Nuevo los contadores (y los niveles) siguen igual");

Console.WriteLine("\n== 10. Pantallas ==");
await Reset();
Check((await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U + 1)).PlainMessage!.Contains("/start"), "sin cuenta, /professions manda a /start");
var pe0 = (await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U)).Embed!;
Check(pe0.Fields.Length == 3 && pe0.Fields.All(f => f.Name.Contains("nivel 0/100") && f.Value.Contains("🔒") && f.Value.Contains("Cada nivel da")) && pe0.Length <= 6000 && pe0.Fields.All(f => f.Value.Length <= 1024), "un jugador nuevo ve los 3 oficios en 0 con la avanzada bloqueada y lo que da cada nivel");
await SetStat("chop_xp", 100); await SetStat("mine_xp", total); await SetStat("enchant", 5);
var pe1 = (await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U)).Embed!;
string chopField = pe1.Fields.Single(f => f.Name.Contains("Leñador")).Value, mineField = pe1.Fields.Single(f => f.Name.Contains("Minero")).Value;
Check(chopField.Contains("█") && chopField.Contains("XP") && chopField.Contains("Hoy da +") && chopField.Contains("🔒") && mineField.Contains("Tope de lo que da") && mineField.Contains("0/98 XP") && !mineField.Contains("Nivel máximo") && mineField.Contains("🔓") && pe1.Fields.Single(f => f.Name.Contains("Minero")).Name.EndsWith("nivel 100", StringComparison.Ordinal), "con progreso: barra y XP del Leñador, lo que da hoy, y el Minero en el 100 (tope de lo que da, con barra hacia el 101 y la avanzada desbloqueada)");
Check(AllText(pe1).Contains("/chop") && AllText(pe1).Contains("/enchant") && AllText(pe1).Contains("Fuego Nuevo") && Foot(pe1).Contains("también cuenta"), "explica con qué comando sube cada uno, que se quedan con el Fuego Nuevo y que lo anterior cuenta");
var prof = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets, bonusSvc);
var profPlain = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets);
Check(!prof.Fields.Any(f => f.Name.Contains("Oficios")) && !profPlain.Fields.Any(f => f.Name.Contains("Oficios")) && prof.Length <= 6000, "el perfil ya NO muestra los oficios (v0.14.3: se ven con /professions)");
await Reset();
Check(!(await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets, bonusSvc)).Fields.Any(f => f.Name.Contains("Oficios")), "y un jugador nuevo no ve la línea");
var cdNew = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, bonusSvc);
await SetStat("chop_xp", total);
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
Check(te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024) && tt.Contains("/professions") && tt.Contains("+0,2 %") && tt.Contains("0,1 %") && tt.Contains("0,25 %") && tt.Contains("1,5×") && tt.Contains("se quedan") && tt.Contains("Común 2 · Raro 7 · Épico 15 · Legendario 30 · Mítico 100") && tt.Contains("tiradas"), $"el tema explica cómo suben, lo que da cada nivel (leído de las constantes), la avanzada y el Fuego Nuevo, dentro de los límites ({te.Length})");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/professions") && AllText(infoE).Contains("professions") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024), $"/info lista /professions y el tema, dentro de los límites ({infoE.Length})");

Console.WriteLine("\n== 13. Más allá del 100 (v0.38.1) ==");
Check(ProfessionRules.XpForLevel(101) == 98 && ProfessionRules.XpForLevel(110) == (long)Math.Round(93 * Math.Pow(1.05, 10), MidpointRounding.AwayFromZero) && ProfessionRules.XpForLevel(200) is > 12_000 and < 12_500 && ProfessionRules.XpForLevel(100) == 93, $"el 101 pide 98 de XP y de ahí cada nivel cuesta 5 % más (110: {ProfessionRules.XpForLevel(110)}, 200: {ProfessionRules.XpForLevel(200)})");
Check(Enumerable.Range(2, ProfessionRules.HardCap - 1).All(l => ProfessionRules.XpForLevel(l) >= ProfessionRules.XpForLevel(l - 1)) && Enumerable.Range(101, 199).All(l => ProfessionRules.XpForLevel(l) > ProfessionRules.XpForLevel(l - 1)), "la curva nunca baja y después del 100 sube en cada nivel (exponencial)");
Check(Enumerable.Range(101, 150).All(l => Near((double)ProfessionRules.XpForLevel(l) / ProfessionRules.XpForLevel(l - 1), 1.05, 0.01)), "cada nivel pasado el 100 cuesta ~5 % más que el anterior");
Check(ProfessionRules.XpToReach(150) - ProfessionRules.XpToReach(100) is > 15_000 and < 25_000 && ProfessionRules.XpToReach(200) - ProfessionRules.XpToReach(100) is > 200_000 and < 300_000, $"del 100 al 150 son {ProfessionRules.XpToReach(150) - ProfessionRules.XpToReach(100):N0} de XP y al 200 {ProfessionRules.XpToReach(200) - ProfessionRules.XpToReach(100):N0}");
Check(ProfessionRules.ProgressFor(ProfessionRules.XpToReach(150)).Level == 150 && ProfessionRules.ProgressFor(ProfessionRules.XpToReach(150) - 1).Level == 149 && ProfessionRules.LevelFor(ProfessionRules.XpToReach(237) + 5) == 237, "el nivel se saca de la XP también pasado el 100 (150 justo, 149 a uno, 237)");
var topProgress = ProfessionRules.ProgressFor(long.MaxValue / 4);
Check(topProgress.Level == ProfessionRules.HardCap && topProgress.IsMax && topProgress.ToNext == 0 && ProfessionRules.XpToReach(int.MaxValue) == ProfessionRules.XpToReach(ProfessionRules.HardCap) && ProfessionRules.XpToReach(ProfessionRules.HardCap) < 100_000_000, $"el tope duro ({ProfessionRules.HardCap}) no se desborda: {ProfessionRules.XpToReach(ProfessionRules.HardCap):N0} de XP en total");
// Los efectos NO crecen pasado el 100: todo se mide con el nivel del tope.
Check(Near(ProfessionRules.QuantityMultiplier(100), ProfessionRules.QuantityMultiplier(250), 1e-12) && Near(ProfessionRules.RarityUpgradeChance(100), ProfessionRules.RarityUpgradeChance(250), 1e-12)
    && Near(ProfessionRules.DustMultiplier(100), ProfessionRules.DustMultiplier(250), 1e-12) && Near(ProfessionRules.TierUpgradeChance(100), ProfessionRules.TierUpgradeChance(250), 1e-12) && ProfessionRules.AdvancedUnlocked(101) && ProfessionRules.AdvancedUnlocked(250),
    "los efectos del 250 son los del 100 y la versión avanzada sigue abierta");
var woodE = ProfessionCatalog.Get(ProfessionCatalog.WoodcutterKey);
Check(ProfessionRules.DescribeEffect(woodE, 180) == ProfessionRules.DescribeEffect(woodE, 100) && ProfessionRules.DescribeEffect(woodE, 100).Contains("+20 %") && ProfessionRules.DescribePostCapGrowth() == "5 %", "la descripción del nivel 180 dice lo del 100 (+20 % de cantidad)");
// El pie: cruzar el 100 avisa de la versión avanzada; después ya solo muestra el avance.
var before99 = ProfessionRules.ProgressFor(ProfessionRules.XpToReach(100) - 3);
var after100 = ProfessionRules.ProgressFor(ProfessionRules.XpToReach(100) + 4);
var before105 = ProfessionRules.ProgressFor(ProfessionRules.XpToReach(105) + 10);
var after105 = ProfessionRules.ProgressFor(ProfessionRules.XpToReach(105) + 17);
var afterLevelUp = ProfessionRules.ProgressFor(ProfessionRules.XpToReach(106) + 2);
string footCross = GatheringModule.ProfessionFooter(woodE, before99, after100, 7), footPlain = GatheringModule.ProfessionFooter(woodE, before105, after105, 7), footUp = GatheringModule.ProfessionFooter(woodE, before105, afterLevelUp, 7);
Check(footCross.Contains("🏆") && footCross.Contains("al nivel 100") && !footPlain.Contains("🏆") && footPlain.Contains("nivel 105") && footPlain.Contains("(+7)") && !footPlain.Contains("máximo") && footUp.Contains("subió al nivel 106"), $"al cruzar el 100 avisa de la avanzada; en el 105 muestra el avance («{footPlain}») y al subir dice que subió («{footUp}»)");
string footMaxHard = GatheringModule.ProfessionFooter(woodE, topProgress, topProgress);
Check(footMaxHard.Contains("(máximo)") && footMaxHard.Contains($"nivel {ProfessionRules.HardCap}"), "solo el tope duro de la tabla dice «máximo»");
// Un jugador con XP de sobra (como quien llegó al 100 antes de la v0.38.1): conserva el nivel y /professions lo muestra sin «/100».
await Reset();
await SetStat("chop_xp", ProfessionRules.XpToReach(130) + 6); await SetStat("mine_xp", 10); await SetStat("enchant", 0);
var peHi = (await ProfessionModule.ExecuteProfessionsAsync(users, bonusSvc, U)).Embed!;
var chopHi = peHi.Fields.Single(f => f.Name.Contains("Leñador"));
Check(chopHi.Name.EndsWith("nivel 130", StringComparison.Ordinal) && !chopHi.Name.Contains("/100") && chopHi.Value.Contains("Tope de lo que da") && chopHi.Value.Contains("+20 % de cantidad") && chopHi.Value.Contains("6/") && chopHi.Value.Contains("🔓")
    && AllText(peHi).Contains("5 %") && peHi.Length <= 6000 && peHi.Fields.All(f => f.Value.Length <= 1024), $"un Leñador del 130 se ve en «nivel 130» (sin /100), con el tope de lo que da y la barra hacia el 131 («{chopHi.Name}»)");
var bonusesHi = await bonusSvc.GetAsync(U, 0);
Check(bonusesHi.ProfessionLevel("woodcutter") == 130 && Near(bonusesHi.ChopMultiplier, 1.2, 1e-9) && Near(bonusesHi.ChopRarityUpgrade, 0.1, 1e-9), "y en las tiradas el nivel 130 rinde lo del 100 (×1,2 de cantidad, +10 % de mejorar la rareza)");
await ClearCooldowns();
long bagHi = await Bag();
var advHi = await Chop(advanced: true);
Check(advHi.Embed is not null && !advHi.Ephemeral && await Bag() > bagHi && Foot(advHi.Embed).Contains("nivel 130") && !Foot(advHi.Embed).Contains("🏆"), $"la tala avanzada funciona en el 130 y su pie muestra el avance, sin avisar de nuevo la avanzada («{Foot(advHi.Embed)}»)");
Check(await Stat("chop_xp") > ProfessionRules.XpToReach(130) + 6, "y esa tala sumó XP al contador (sigue subiendo después del 100)");

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
