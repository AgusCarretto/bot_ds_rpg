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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// v0.12.0: Fuego Nuevo y bendiciones. Reglas puras, matemática de recompensas/recolección/perfil, el reinicio y la elección transaccionales (con concurrencia),
// el servicio de bonus, mascotas con Buen Pienso, /class restringido, pantallas, ruteo de botones con el InteractionService real y la ayuda.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var monstersRepo = new MonsterRepository(factory);
var adventure = new AdventureRepository(factory);
var buffRepo = new BuffRepository(factory);
var pets = new PetRepository(factory);
var blessings = new BlessingRepository(factory);
var fnRepo = new FuegoNuevoRepository(factory);
var bonusSvc = new PlayerBonusService(pets, blessings, new GameEventRepository(factory));
var combat = new CombatSessionService(users);
var raids = new RaidSessionService();
var evts = new RecordingEvents();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000012000UL;
async Task Reset()
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
}
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;
async Task<int> ItemId(string name) => await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE name = @n", new { n = name });
async Task<int> FirstOfType(string type) => await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = @t ORDER BY item_id LIMIT 1", new { t = type });
async Task Give(int itemId, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@id, @i, @q) ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + @q", new { id = (long)U, i = itemId, q = qty });
async Task<int> Qty(int itemId) => await db.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT quantity FROM inventory WHERE discord_id = @id AND item_id = @i), 0)", new { id = (long)U, i = itemId });
async Task SetBlessing(string key, int level) => await db.ExecuteAsync("INSERT INTO player_blessings (discord_id, blessing_key, level) VALUES (@id, @k, @l) ON CONFLICT (discord_id, blessing_key) DO UPDATE SET level = @l", new { id = (long)U, k = key, l = level });
async Task<int> Level(string key) => await db.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT level FROM player_blessings WHERE discord_id = @id AND blessing_key = @k), 0)", new { id = (long)U, k = key });

var ordered = ZoneRanking.OrderByDifficulty((await zonesRepo.GetAllAsync()).ToList());
int firstZone = ordered[0].ZoneId, thirdZone = ordered[2].ZoneId;

Console.WriteLine("== 1. Reglas puras: Fuego Nuevo ==");
Check(FuegoNuevoRules.HuntDropStep == 0.30 && FuegoNuevoRules.GatherStep == 0.20, "pasos acordados: /hunt ×1,30 y /chop y /mine ×1,20 por vuelta (la EXP ya no es un paso parejo desde la v0.28.0: ver v0280test)");
Check(Near(FuegoNuevoRules.HuntDropMultiplier(1), 1.3, 1e-9) && Near(FuegoNuevoRules.HuntDropMultiplier(10), 4.0, 1e-9) && FuegoNuevoRules.HuntDropMultiplier(0) == 1.0 && FuegoNuevoRules.HuntDropMultiplier(-3) == 1.0, "el drop de /hunt es LINEAL (FN 10 = ×4, no se compone) y sin Fuego Nuevo no cambia nada");
Check(Near(FuegoNuevoRules.GatherMultiplier(10), 3.0, 1e-9) && Near(FuegoNuevoRules.XpMultiplier(10), 6.45, 1e-9), "FN 10: ×3 de cantidad y ×6,45 de EXP (la fórmula de EPIC RPG, v0.28.0)");
Check(Near(FuegoNuevoRules.TravelStep(1), 0.10, 1e-9) && Near(FuegoNuevoRules.TravelStep(2), 0.095, 1e-9) && Near(FuegoNuevoRules.TravelStep(17), 0.02, 1e-9) && Near(FuegoNuevoRules.TravelStep(500), 0.02, 1e-9) && FuegoNuevoRules.TravelStep(0) == 0, "el paso de /travel arranca en +10 %, baja 0,5 puntos por vuelta y se queda en +2 %");
Check(Near(FuegoNuevoRules.TravelStepsTotal(1), 0.10, 1e-9) && Near(FuegoNuevoRules.TravelStepsTotal(2), 0.195, 1e-9) && FuegoNuevoRules.TravelStepsTotal(41) > 1.5 - 1e-9 && FuegoNuevoRules.TravelStepsTotal(0) == 0, "la suma de pasos de /travel (0,10 + 0,095 + ...) es exacta");
double m10 = FuegoNuevoRules.TravelDropMultiplier(10);
// v0.36.0: el multiplicador del FN ya no es sobre el drop del viaje sino sobre la chance de su CAJA (zone_boxes, rol "travel"); la cuenta es la misma.
Check(Near(m10, 1 + FuegoNuevoRules.TravelStepsTotal(10), 1e-9) && m10 > 1.77 && m10 < 1.78 && Near(CombatRewardCalculator.TravelBoxChance(8, null), 8, 1e-9) && CombatRewardCalculator.TravelBoxChance(100, null) == 100 && CombatRewardCalculator.TravelBoxChance(0, null) == 0 && CombatRewardCalculator.TravelBoxChance(-5, null) == 0,
    $"/travel: el FN 10 multiplica la chance de la caja ×{m10:0.##} (el spec decía 40 % → ~71 %, que es ×1,78); sin bonus la chance es la de la fila, con tope en 100 y piso en 0");
Check(FuegoNuevoRules.PercentText(1.3) == "+30 %" && FuegoNuevoRules.PercentText(1.195) == "+19,5 %" && FuegoNuevoRules.PercentText(1.0) == "+0 %", "PercentText: coma decimal y sin ceros de más");
Check(FuegoNuevoRules.CanChangeClass(1, 0) && !FuegoNuevoRules.CanChangeClass(1, 5) && !FuegoNuevoRules.CanChangeClass(2, 0), "/class solo con nivel 1 y 0 de EXP");
Check(FuegoNuevoRules.KeptItemTypes.SequenceEqual(["Huevo", "PetFood", "DragonFood"]), "al reiniciar sobreviven los huevos, la comida de mascotas y la del dragón (v0.32.0)");

Console.WriteLine("\n== 2. Bendiciones: catálogo y sorteo ==");
var all = BlessingCatalog.All;
Check(all.Count == 13 && all.Select(b => b.Key).Distinct().Count() == 13 && all.All(b => b.Key == b.Key.ToLowerInvariant() && !b.Key.Contains(':') && !b.Key.Contains(',') && b.Key.Length <= 40), "13 bendiciones con claves únicas y sin ':' ni ',' (van en los ids de los botones y en la oferta)");
Check(Enum.GetValues<BlessingKind>().All(k => all.Any(b => b.Kind == k)), "hay de cada tipo: velocidad, poder, mascotas, economía, ítems y cosmética (variado, como pidió el dueño)");
Check(BlessingCatalog.MaxLevel == 5 && BlessingCatalog.OfferSize == 3 && BlessingCatalog.RomanLevel(1) == "I" && BlessingCatalog.RomanLevel(5) == "V", "niveles I a V y oferta de 3");
var rng = new Random(12345);
var none = new Dictionary<string, int>();
var offers = Enumerable.Range(0, 400).Select(_ => BlessingCatalog.RollOffer(none, rng)).ToList();
Check(offers.All(o => o.Count == 3 && o.Distinct().Count() == 3 && o.All(k => BlessingCatalog.Find(k) is not null)), "cada oferta son 3 bendiciones DISTINTAS y existentes");
Check(all.All(b => offers.Any(o => o.Contains(b.Key))), "con el tiempo todas pueden salir (el sorteo no deja a ninguna afuera)");
var nearlyAll = all.ToDictionary(b => b.Key, _ => 5); nearlyAll.Remove(BlessingCatalog.HuntDropKey);
Check(BlessingCatalog.RollOffer(nearlyAll, rng).SequenceEqual([BlessingCatalog.HuntDropKey]) && BlessingCatalog.RollOffer(all.ToDictionary(b => b.Key, _ => 5), rng).Count == 0, "las que están al nivel máximo no se ofrecen (queda 1, o ninguna)");
var someOwned = new Dictionary<string, int> { [BlessingCatalog.AttackKey] = 5, [BlessingCatalog.GoldKey] = 4 };
Check(Enumerable.Range(0, 200).All(_ => !BlessingCatalog.RollOffer(someOwned, rng).Contains(BlessingCatalog.AttackKey)) && Enumerable.Range(0, 400).Any(_ => BlessingCatalog.RollOffer(someOwned, rng).Contains(BlessingCatalog.GoldKey)), "una al máximo no sale; una a medias sí (para subirla de nivel)");
var lines = BlessingCatalog.OwnedLines(new Dictionary<string, int> { [BlessingCatalog.GoldKey] = 2, [BlessingCatalog.ChopKey] = 5, ["inexistente"] = 3 });
Check(lines.Count == 2 && lines[0].Contains("Mano de Leñador V") && lines[0].Contains("25 %") && lines[1].Contains("Bolsillo Hondo II") && lines[1].Contains("6 %"), "OwnedLines: en el orden del catálogo, con nivel romano y el efecto de ese nivel (Mano de Leñador V = +25 %)");
Check(BlessingCatalog.OwnedLines(null).Count == 0 && BlessingCatalog.ColorTitle(1) != BlessingCatalog.ColorTitle(5) && BlessingCatalog.ColorValue(2) != BlessingCatalog.ColorValue(3) && BlessingCatalog.ColorTitle(99) == BlessingCatalog.ColorTitle(5), "Brasa de Color: título y color por nivel (y el nivel se acota)");

Console.WriteLine("\n== 3. PlayerBonuses: multiplicadores y presupuesto ==");
var noBonus = PlayerBonuses.None;
Check(noBonus.IsNone && noBonus.HuntDropMultiplier == 1 && noBonus.TravelDropMultiplier == 1 && noBonus.XpMultiplier == 1 && noBonus.GoldMultiplier == 1 && noBonus.AttackMultiplier == 1 && noBonus.DefenseMultiplier == 1 && noBonus.MaxHpMultiplier == 1 && noBonus.ChopMultiplier == 1 && noBonus.MineMultiplier == 1 && noBonus.PetFeedCooldown == PetRules.FeedCooldown && noBonus.PetsBlessingMultiplier == 1, "sin nada: todo ×1 y la espera de las mascotas es la hora de siempre");
var lv5 = all.ToDictionary(b => b.Key, _ => 5);
var maxed = new PlayerBonuses(new PetBonuses(10, 20, 6, 6), 10, lv5);
Check(Near(maxed.HuntDropMultiplier, 4.0 * 1.06 * 1.25, 1e-9) && Near(maxed.TravelDropMultiplier, FuegoNuevoRules.TravelDropMultiplier(10) * 1.06 * 1.25, 1e-9), "el drop multiplica Fuego Nuevo × mascota × bendición (los tres factores)");
Check(Near(maxed.XpMultiplier, 6.45 * 1.2 * 1.2, 1e-9) && Near(maxed.GoldMultiplier, 1.1 * 1.15, 1e-9), "EXP y oro también multiplican sus fuentes");
Check(Near(maxed.AttackMultiplier, 1.05, 1e-9) && Near(maxed.DefenseMultiplier, 1.06 * 1.05, 1e-9) && Near(maxed.MaxHpMultiplier, 1.10, 1e-9), "poder: ataque +5 %, defensa +5 % (y +6 % de la mascota), vida +10 % al nivel V (CHICO a propósito)");
Check(Near(maxed.ChopMultiplier, 3.0 * 1.25, 1e-9) && Near(maxed.MineMultiplier, 3.0 * 1.25, 1e-9) && maxed.PetFeedCooldown == TimeSpan.FromMinutes(35) && Near(maxed.PetsBlessingMultiplier, 1.5, 1e-9), "recolección ×3 × 1,25; Buen Pienso V deja la espera en 35 min; Manada V = ×1,5");
Check(new PlayerBonuses(PetBonuses.None, 0, new Dictionary<string, int> { [BlessingCatalog.AttackKey] = 99, [BlessingCatalog.HpKey] = -4 }).AttackMultiplier == 1.05 && new PlayerBonuses(PetBonuses.None, 0, new Dictionary<string, int> { [BlessingCatalog.HpKey] = -4 }).MaxHpMultiplier == 1.0, "un nivel fuera de rango en la base se acota (nunca más que V ni menos que 0)");
PlayerBonuses implicitPets = new PetBonuses(5, 5, 5, 5);
Check(implicitPets.FuegoNuevo == 0 && implicitPets.Pets.GoldPercent == 5 && Near(implicitPets.GoldMultiplier, 1.05, 1e-9), "un PetBonuses suelto se convierte solo (compatibilidad)");
Check(PlayerBonuses.Scale(100, 1.0) == 100 && PlayerBonuses.Scale(100, 0.5) == 100 && PlayerBonuses.Scale(38, 1.05) == 40 && PlayerBonuses.Scale(0, 3) == 0, "Scale: no baja nunca y redondea al más cercano");

Console.WriteLine("\n== 4. Recompensas con bonus ==");
var fn1 = new PlayerBonuses(PetBonuses.None, 1);
var fn10 = new PlayerBonuses(PetBonuses.None, 10);
Check(Near(CombatRewardCalculator.HuntDropChance(null), 6, 1e-9) && Near(CombatRewardCalculator.HuntDropChance(fn1), 7.8, 1e-9) && Near(CombatRewardCalculator.HuntDropChance(fn10), 24, 1e-9), "chance de drop de /hunt: 6 % → 7,8 % (FN 1) → 24 % (FN 10)");
Check(Near(CombatRewardCalculator.TravelBoxChance(40, null), 40, 1e-9) && Near(CombatRewardCalculator.TravelBoxChance(40, fn1), 44, 1e-9) && CombatRewardCalculator.TravelBoxChance(40, new PlayerBonuses(PetBonuses.None, 45)) == 100.0 && CombatRewardCalculator.HuntDropChance(new PlayerBonuses(PetBonuses.None, 400)) == 100.0, "/travel: 40 % → 44 % (FN 1), y las dos chances topan en 100 %");
int dropsHunt = 0, dropsNone = 0;
for (int i = 0; i < 40000; i++) { if (CombatRewardCalculator.RollHuntReward(10, 0, 0, fn10).DroppedSomething) dropsHunt++; if (CombatRewardCalculator.RollHuntReward(10).DroppedSomething) dropsNone++; }
Check(Near(dropsHunt / 40000.0, 0.24, 0.012) && Near(dropsNone / 40000.0, 0.06, 0.008), $"medido en 40.000 cacerías: FN 10 = {dropsHunt / 400.0:0.#} % (esperado 24) y sin Fuego Nuevo {dropsNone / 400.0:0.#} % (esperado 6)");
double xpBase = Enumerable.Range(0, 20000).Average(_ => CombatRewardCalculator.RollHuntReward(10).Xp);
double xpFn = Enumerable.Range(0, 20000).Average(_ => CombatRewardCalculator.RollHuntReward(10, 0, 0, fn10).Xp);
Check(Near(xpFn / xpBase, 6.45, 0.12), $"la EXP del FN 10 es ×{xpFn / xpBase:0.00} (esperado ×6,45, la fórmula de EPIC RPG)");
var bossFn = Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollBossReward(10, 100, 100, true, fn10)).ToList();
var bossNo = Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollBossReward(10, 100, 100, true)).ToList();
Check(bossFn.All(r => r.DroppedSomething) && bossNo.All(r => r.DroppedSomething) && bossFn.Min(r => r.Gold) == bossNo.Min(r => r.Gold), "el cofre del jefe NO cambia con Fuego Nuevo (primera vez 100 % en los dos) y el oro tampoco");
var rep = Enumerable.Range(0, 20000).Select(_ => CombatRewardCalculator.RollBossReward(10, 100, 100, false, fn10, 40)).Count(r => r.DroppedSomething) / 20000.0;
Check(Near(rep, 0.40, 0.012), $"y las repeticiones del jefe siguen en 40 % ({rep * 100:0.#} %)");
var withPets = new PlayerBonuses(new PetBonuses(50, 0, 0, 0), 0);
Check(Enumerable.Range(0, 3000).All(_ => CombatRewardCalculator.RollHuntReward(10, 0, 0, withPets).Gold >= PlayerBonuses.Scale(5 + 20, 1.5)), "las mascotas siguen pagando oro igual que en la v0.10 (+50 % sube el piso)");

Console.WriteLine("\n== 5. Cantidad de /chop y /mine ==");
var r1 = new Random(7);
Check(Enumerable.Range(0, 5000).All(_ => GatheringYield.RollScaled("Común", 1.0, r1) is >= 1 and <= 5) && Enumerable.Range(0, 5000).All(_ => GatheringYield.RollScaled("Legendario", 1.0, r1) == 1) && Enumerable.Range(0, 5000).All(_ => GatheringYield.RollScaled("Raro", 0.5, r1) is >= 1 and <= 3), "con factor 1 (o menos) es el sorteo de siempre: Común 1-5, Legendario 1, Raro 1-3");
double meanLeg = Enumerable.Range(0, 60000).Average(_ => GatheringYield.RollScaled("Legendario", 1.2, r1));
double meanCom = Enumerable.Range(0, 60000).Average(_ => GatheringYield.RollScaled("Común", 1.2, r1));
Check(Near(meanLeg, 1.2, 0.01) && Near(meanCom, 3.6, 0.03), $"con ×1,2 el PROMEDIO es exacto: un Legendario da {meanLeg:0.000} (esperado 1,2) y un Común {meanCom:0.00} (esperado 3,6)");
var legSample = Enumerable.Range(0, 5000).Select(_ => GatheringYield.RollScaled("Legendario", 1.2, r1)).Distinct().OrderBy(x => x).ToList();
Check(legSample.SequenceEqual([1, 2]), "con ×1,2 un Legendario da 1 (80 %) o 2 (20 %): nunca 0 ni 3");
Check(Enumerable.Range(0, 3000).All(_ => GatheringYield.RollScaled("Legendario", 3.0, r1) == 3) && Enumerable.Range(0, 3000).All(_ => GatheringYield.RollScaled("Común", 3.0, r1) is >= 3 and <= 15), "×3 (FN 10): Legendario siempre 3, Común 3 a 15");
await Reset();
await db.ExecuteAsync("UPDATE users SET fuego_nuevo = 10 WHERE discord_id = @id", new { id = (long)U });
await SetBlessing(BlessingCatalog.ChopKey, 5);
var player = await Me();
var gatherBonuses = await bonusSvc.GetAsync(U, player.FuegoNuevo);
double chopMean = 0, mineMean = 0;
for (int i = 0; i < 4000; i++) { chopMean += GatheringYield.RollScaled("Legendario", gatherBonuses.ChopMultiplier); mineMean += GatheringYield.RollScaled("Legendario", gatherBonuses.MineMultiplier); }
chopMean /= 4000; mineMean /= 4000;
Check(Near(chopMean, 3.75, 0.1) && Near(mineMean, 3.0, 0.08), $"/chop en FN 10 con Mano de Leñador V: {chopMean:0.00} (esperado 3,75); /mine sin Pico Fino: {mineMean:0.00} (esperado 3)");

Console.WriteLine("\n== 6. Perfil de combate ==");
var weaponId = await FirstOfType("Weapon"); var amuletId = await FirstOfType("Amulet");
var weaponItem = await itemsRepo.GetByIdAsync(weaponId); var amuletItem = await itemsRepo.GetByIdAsync(amuletId);
var hero = new User { DiscordId = (long)U, Class = "Guerrero", Level = 20, Xp = 0, MaxHp = 400, CurrentHp = 400 };
var baseProfile = PlayerCombatProfileCalculator.Resolve(hero, weaponItem, amuletItem, 0);
var nullProfile = PlayerCombatProfileCalculator.Resolve(hero, weaponItem, amuletItem, 0, null);
var noneProfile = PlayerCombatProfileCalculator.Resolve(hero, weaponItem, amuletItem, 0, PlayerBonuses.None);
Check(baseProfile == nullProfile && baseProfile.Damage == noneProfile.Damage && baseProfile.Defense == noneProfile.Defense && baseProfile.CombatMaxHp == noneProfile.CombatMaxHp, "sin bonus el perfil es EXACTAMENTE el de siempre");
var power = new PlayerBonuses(PetBonuses.None, 0, new Dictionary<string, int> { [BlessingCatalog.AttackKey] = 5, [BlessingCatalog.DefenseKey] = 5, [BlessingCatalog.HpKey] = 5 });
var powerProfile = PlayerCombatProfileCalculator.Resolve(hero, weaponItem, amuletItem, 0, power);
Check(powerProfile.Damage == PlayerBonuses.Scale(baseProfile.Damage, 1.05) && powerProfile.Defense == PlayerBonuses.Scale(baseProfile.Defense, 1.05) && powerProfile.Damage > baseProfile.Damage, $"ataque {baseProfile.Damage} → {powerProfile.Damage} y defensa {baseProfile.Defense} → {powerProfile.Defense} (+5 %)");
double classHp = ClassPassives.For("Guerrero").MaxHpMultiplier;
Check(powerProfile.CombatMaxHp == (int)Math.Round(400 * classHp * 1.10) && Near(powerProfile.Passives.MaxHpMultiplier, classHp * 1.10, 1e-9), $"vida de combate {baseProfile.CombatMaxHp} → {powerProfile.CombatMaxHp} (Guerrero ×{classHp} y Corazón de Brasa V ×1,10, juntos)");
var csState = new CombatState("hunt", "m", "m", 10, 10, 1, [], 0, 0, null, powerProfile.CombatMaxHp, powerProfile.CombatCurrentHp, powerProfile.CombatCurrentHp, powerProfile.Damage, powerProfile.Defense, 20, "Guerrero", powerProfile.Passives, Bonuses: power);
Check(Math.Abs(csState.ToDbHpDelta(-powerProfile.CombatCurrentHp) + 400) <= 1 && Math.Abs(csState.ToDbHpDelta(powerProfile.CombatMaxHp - powerProfile.CombatCurrentHp)) <= 1, "el delta de vida que se guarda sigue siendo HP real: perder todo el HP de combate resta 400 reales (no 440)");
var fakeBanquete = PlayerCombatProfileCalculator.Resolve(hero, weaponItem, amuletItem, 15, power);
Check(fakeBanquete.Damage > powerProfile.Damage && fakeBanquete.AttackBuffPercent == 15, "el banquete y las bendiciones se suman en el mismo ataque");

Check(File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Services/DuelFighterFactory.cs")).Contains("Resolve(player, weapon, PvpTuning.Amulet(amulet), buff?.AttackPercent ?? 0)") , "los duelos y la Arena arman a los luchadores SIN bonus y con la DEF de amuleto de PvP (nada permanente cuenta contra otros jugadores)");

Console.WriteLine("\n== 7. /class solo empezando de cero ==");
await Reset();
var c1 = await users.TrySetClassWhileFreshAsync(U, "Ninja");
Check(c1 is { Class: "Ninja" } && (await Me()).Class == "Ninja", "con nivel 1 y 0 de EXP cambia la clase");
await db.ExecuteAsync("UPDATE users SET xp = 5 WHERE discord_id = @id", new { id = (long)U });
Check(await users.TrySetClassWhileFreshAsync(U, "Arquero") is null && (await Me()).Class == "Ninja", "con algo de EXP ya no (y no toca nada)");
await db.ExecuteAsync("UPDATE users SET xp = 0, level = 2 WHERE discord_id = @id", new { id = (long)U });
Check(await users.TrySetClassWhileFreshAsync(U, "Arquero") is null && (await Me()).Class == "Ninja", "con nivel 2 tampoco");
Check(await users.TrySetClassWhileFreshAsync(U + 1, "Arquero") is null, "un jugador que no existe devuelve null (sin crearlo)");
var lockedMsg = ClassModule.BuildLockedMessage(await Me());
Check(lockedMsg.Contains("Ninja") && lockedMsg.Contains("/fuegonuevo"), "el mensaje de clase bloqueada explica cómo se vuelve a elegir");

Console.WriteLine("\n== 8. El reinicio (RenewAsync) ==");
await Reset();
int wood = await FirstOfType("Madera"), mineral = await FirstOfType("Mineral"), drop = await FirstOfType("Material"), consumable = await FirstOfType("Consumable"), box = await FirstOfType("Caja");
int egg = await ItemId("Huevo de la Pradera"), petFood = await ItemId(PetRules.FoodItemName), pineBox = await ItemId(BlessingCatalog.SatchelBoxName);
await Give(wood, 120); await Give(mineral, 45); await Give(drop, 7); await Give(consumable, 3); await Give(box, 2); await Give(egg, 1); await Give(petFood, 9);
var species = (await pets.GetSpeciesAsync()).ToList();
await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points) VALUES (@id, @s, 3)", new { id = (long)U, s = species[0].SpeciesId });
await db.ExecuteAsync(
    "UPDATE users SET class = 'Hechicero', level = 12, xp = 345, gold = 7777, max_hp = 265, current_hp = 100, dust = 88, weapon_id = @w, amulet_id = @a, weapon_enchant = 3, amulet_enchant = 2, current_zone_id = @z, highest_zone_cleared = @z, has_bank = true, bank_gold = 5000, daily_streak = 6, last_daily_claim = now() WHERE discord_id = @id",
    new { id = (long)U, w = weaponId, a = amuletId, z = thirdZone });
await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, 'hunt_win', 5)", new { id = (long)U });
await db.ExecuteAsync("INSERT INTO player_buffs (discord_id, buff_key, percent, expires_at) VALUES (@id, 'attack', 15, now() + interval '1 hour')", new { id = (long)U });
await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'hunt', now()), (@id, 'boss', now()), (@id, 'buybox', now())", new { id = (long)U });
await SetBlessing(BlessingCatalog.PetsKey, 2);
var prev = (await fnRepo.GetPreviewAsync(U))!;
Check(prev.Class == "Hechicero" && prev.Level == 12 && prev.FuegoNuevo == 0 && !prev.GateCleared && prev.Gold == 7777 && prev.BankGold == 5000 && prev.HasBank && prev.Dust == 88 && prev.WeaponEnchant == 3 && prev.AmuletEnchant == 2 && prev.WeaponName == weaponItem!.Name && prev.AmuletName == amuletItem!.Name, "el resumen previo lee nivel, clase, oro, banco, Polvo y las dos piezas con sus encantamientos");
Check(prev.LostByType["Madera"] == 120 && prev.LostByType["Mineral"] == 45 && prev.LostByType["Material"] == 7 && prev.LostByType["Consumable"] == 3 && prev.LostByType["Caja"] == 2 && !prev.LostByType.ContainsKey("Huevo") && !prev.LostByType.ContainsKey("PetFood") && prev.KeptEggs == 1 && prev.KeptPetFood == 9 && prev.Pets == 1 && prev.Blessings == 1, "se va: 120 madera, 45 mineral, 7 drops, 3 consumibles, 2 cajas; se queda: 1 huevo, 9 comidas, 1 mascota, 1 bendición");
Check(await fnRepo.GetPreviewAsync(U + 7) is null, "sin cuenta el resumen es null");

var rejected = await fnRepo.RenewAsync(U, 0, "Ninja");
var u1 = await Me();
Check(rejected.Status == RenewStatus.NotCleared && u1.Level == 12 && u1.FuegoNuevo == 0 && u1.WeaponId == weaponId && await Qty(wood) == 120, "sin haber vencido al Asador: NotCleared y no se toca nada");
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
Check((await fnRepo.RenewAsync(U, 5, "Ninja")).Status == RenewStatus.StaleCount && (await Me()).Level == 12, "un contador viejo (botón de otra vuelta): StaleCount y no se toca nada");
Check((await fnRepo.RenewAsync(U, 0, "Paladín")).Status == RenewStatus.InvalidClass && (await Me()).Level == 12, "una clase que no existe: InvalidClass y no se toca nada");
Check((await fnRepo.RenewAsync(U + 9, 0, "Ninja")).Status == RenewStatus.NoAccount, "sin cuenta: NoAccount");

var ok1 = await fnRepo.RenewAsync(U, 0, "Ninja");
var u2 = await Me();
Check(ok1.Status == RenewStatus.Ok && ok1.NewNumber == 1 && ok1.ClassBefore == "Hechicero" && ok1.LevelBefore == 12 && ok1.SatchelPetFood == 0 && ok1.SatchelBoxes == 0, "el reinicio sale Ok: Fuego Nuevo 1, antes Hechicero nivel 12 y sin Alforja");
Check(u2.Class == "Ninja" && u2.Level == 1 && u2.Xp == 0 && u2.MaxHp == 100 && u2.CurrentHp == 100 && u2.WeaponId is null && u2.AmuletId is null && u2.WeaponEnchant == 0 && u2.AmuletEnchant == 0 && u2.Dust == 0, "se va: nivel 1, 0 EXP, 100 de vida, arma, amuleto, encantamientos y Polvo; la clase es la nueva");
Check(u2.CurrentZoneId == firstZone && u2.HighestZoneCleared == 0 && !u2.InGate && !u2.GateCleared && u2.FuegoNuevo == 1 && (DateTime.UtcNow - u2.RunStartedAt).TotalMinutes < 2, "vuelve a la primera zona, sin jefes vencidos, sin puerta, y arranca la vuelta nueva");
Check(u2.Gold == 7777 && u2.HasBank && u2.BankGold == 5000 && u2.DailyStreak == 6 && u2.LastDailyClaim is not null, "se queda: el oro, el banco, la racha diaria");
Check(await Qty(wood) == 0 && await Qty(mineral) == 0 && await Qty(drop) == 0 && await Qty(consumable) == 0 && await Qty(box) == 0, "se va toda la mochila: madera, mineral, drops, consumibles y cajas");
Check(await Qty(egg) == 1 && await Qty(petFood) == 9, "se quedan el huevo y la comida de mascotas (mis dos decisiones, a avisarle al dueño)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_pets WHERE discord_id = @id", new { id = (long)U }) == 1 && await db.ExecuteScalarAsync<int>("SELECT feed_points FROM player_pets WHERE discord_id = @id", new { id = (long)U }) == 3 && await Level(BlessingCatalog.PetsKey) == 2 && await db.ExecuteScalarAsync<long>("SELECT value FROM player_stats WHERE discord_id = @id AND stat_key = 'hunt_win'", new { id = (long)U }) == 5, "se quedan la mascota (con su nivel), la bendición y las estadísticas (los logros salen de ahí)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_buffs WHERE discord_id = @id", new { id = (long)U }) == 0 && (await db.QueryAsync<string>("SELECT command_name FROM cooldowns WHERE discord_id = @id", new { id = (long)U })).SequenceEqual(["buybox"]), "se van los banquetes y los enfriamientos de la vuelta (el de comprar cajas es de la economía y se queda)");
var hist = await db.QuerySingleAsync("SELECT number, class_before, class_after, level_before, started_at, finished_at FROM fuego_nuevo_history WHERE discord_id = @id", new { id = (long)U });
Check((int)hist.number == 1 && (string)hist.class_before == "Hechicero" && (string)hist.class_after == "Ninja" && (int)hist.level_before == 12 && (DateTime)hist.finished_at >= (DateTime)hist.started_at, "queda UNA fila de historial con la clase de antes y de después y el nivel que tenía");
var pend = await blessings.GetPendingOfferAsync(U);
Check(ok1.Offer is { FuegoNuevoNo: 1 } && ok1.Offer.Keys.Count == 3 && pend is { FuegoNuevoNo: 1 } && pend.Keys.SequenceEqual(ok1.Offer.Keys) && pend.Keys.All(k => BlessingCatalog.Find(k) is not null), "la oferta de 3 bendiciones queda guardada (si cierra Discord la encuentra)");

Console.WriteLine("\n== 9. Doble click y concurrencia ==");
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
var race = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => fnRepo.RenewAsync(U, 1, i % 2 == 0 ? "Arquero" : "Guerrero"))));
Check(race.Count(r => r.Status == RenewStatus.Ok) == 1 && race.Count(r => r.Status is RenewStatus.StaleCount or RenewStatus.NotCleared) == 19 && (await Me()).FuegoNuevo == 2, $"20 reinicios a la vez: UNO solo sale Ok (el resto Stale/NotCleared) y el contador queda en 2 ({race.Count(r => r.Status == RenewStatus.Ok)} Ok)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM fuego_nuevo_history WHERE discord_id = @id", new { id = (long)U }) == 2 && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM blessing_offers WHERE discord_id = @id", new { id = (long)U }) == 2, "y quedan exactamente 2 filas de historial y 2 ofertas (sin duplicados)");
Check((await fnRepo.RenewAsync(U, 2, "Ninja")).Status == RenewStatus.NotCleared && (await Me()).FuegoNuevo == 2, "tocar de nuevo sin haber vuelto a vencer al Asador: NotCleared (cada vuelta pide el Asador otra vez)");

Console.WriteLine("\n== 10. Elegir bendición ==");
await db.ExecuteAsync("UPDATE blessing_offers SET offered_keys = 'filo_antiguo,piel_curtida,alforja_del_fogonero' WHERE discord_id = @id AND fuego_nuevo_no = 1", new { id = (long)U });
await db.ExecuteAsync("UPDATE blessing_offers SET offered_keys = 'filo_antiguo,corazon_de_brasa,manada' WHERE discord_id = @id AND fuego_nuevo_no = 2", new { id = (long)U });
Check((await blessings.ChooseAsync(U, 1, "mano_lenador")).Status == ChooseStatus.NotOffered && (await blessings.ChooseAsync(U, 1, "no_existe")).Status == ChooseStatus.NotOffered && (await blessings.ChooseAsync(U, 99, "filo_antiguo")).Status == ChooseStatus.NoOffer && (await blessings.ChooseAsync(U + 3, 1, "filo_antiguo")).Status == ChooseStatus.NoAccount, "una que no estaba en la oferta, una inexistente, una oferta que no hay y un jugador sin cuenta: se rechazan");
Check(await Level("filo_antiguo") == 0 && (await blessings.GetPendingOfferAsync(U))!.FuegoNuevoNo == 1, "y no quedó nada elegido ni se tocó la oferta");
var chooseRace = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => blessings.ChooseAsync(U, 1, "filo_antiguo"))));
Check(chooseRace.Count(r => r.Status == ChooseStatus.Ok) == 1 && chooseRace.Count(r => r.Status == ChooseStatus.AlreadyChosen) == 19 && await Level("filo_antiguo") == 1, $"20 clicks a la vez en la misma bendición: suma UNA ({chooseRace.Count(r => r.Status == ChooseStatus.Ok)} Ok), nivel I");
Check((await blessings.ChooseAsync(U, 1, "piel_curtida")).Status == ChooseStatus.AlreadyChosen && await Level("piel_curtida") == 0, "una vez elegida, otra de la misma oferta ya no (una por Fuego Nuevo)");
Check((await blessings.GetPendingOfferAsync(U))!.FuegoNuevoNo == 2, "la oferta pendiente pasa a ser la siguiente");
var rep2 = await blessings.ChooseAsync(U, 2, "filo_antiguo");
Check(rep2.Status == ChooseStatus.Ok && rep2.NewLevel == 2 && await Level("filo_antiguo") == 2 && await blessings.GetPendingOfferAsync(U) is null, "repetir una bendición la sube de nivel (II) y ya no queda oferta");
var levelsNow = await blessings.GetLevelsAsync(U);
Check(levelsNow["filo_antiguo"] == 2 && levelsNow["manada"] == 2 && levelsNow.Count == 2, "GetLevelsAsync devuelve clave → nivel");
int foodBefore = await Qty(petFood), boxBefore = await Qty(pineBox);
await db.ExecuteAsync("INSERT INTO blessing_offers (discord_id, fuego_nuevo_no, offered_keys) VALUES (@id, 101, 'alforja_del_fogonero,aprendiz,pico_fino')", new { id = (long)U });
var sat = await blessings.ChooseAsync(U, 101, "alforja_del_fogonero");
Check(sat.Status == ChooseStatus.Ok && sat.NewLevel == 1 && sat.PetFoodGranted == 3 && sat.BoxesGranted == 1 && await Qty(petFood) == foodBefore + 3 && await Qty(pineBox) == boxBefore + 1, "la Alforja del Fogonero da 3 comidas de mascota y 1 Cajón de Pino al elegirla (en la misma transacción)");
await SetBlessing("filo_antiguo", 5);
await db.ExecuteAsync("INSERT INTO blessing_offers (discord_id, fuego_nuevo_no, offered_keys) VALUES (@id, 102, 'filo_antiguo,aprendiz,pico_fino')", new { id = (long)U });
Check((await blessings.ChooseAsync(U, 102, "filo_antiguo")).Status == ChooseStatus.MaxLevel && await Level("filo_antiguo") == 5 && (await blessings.GetPendingOfferAsync(U))!.FuegoNuevoNo == 102, "una que ya está al nivel máximo se rechaza y la oferta sigue pendiente");

Console.WriteLine("\n== 11. La Alforja al renacer y la oferta cuando casi todo está al máximo ==");
foreach (var b in all) await SetBlessing(b.Key, 5);
await SetBlessing(BlessingCatalog.HuntDropKey, 0 + 3);
await db.ExecuteAsync("DELETE FROM blessing_offers WHERE discord_id = @id AND fuego_nuevo_no > 2", new { id = (long)U });
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
int food2 = await Qty(petFood), box2 = await Qty(pineBox);
var ok3 = await fnRepo.RenewAsync(U, 2, "Guerrero");
Check(ok3.Status == RenewStatus.Ok && ok3.NewNumber == 3 && ok3.SatchelPetFood == 15 && ok3.SatchelBoxes == 5 && await Qty(petFood) == food2 + 15 && await Qty(pineBox) == 5 && await Qty(egg) == 1, "con la Alforja al nivel V, renacer deja 15 comidas de mascota y 5 Cajones de Pino (y el huevo sigue ahí)");
Check(ok3.Offer is { } o3 && o3.Keys.SequenceEqual([BlessingCatalog.HuntDropKey]), "con todas al máximo menos una, la oferta es solo esa");
await SetBlessing(BlessingCatalog.HuntDropKey, 5);
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
var ok4 = await fnRepo.RenewAsync(U, 3, "Ninja");
Check(ok4.Status == RenewStatus.Ok && ok4.Offer is null && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM blessing_offers WHERE discord_id = @id AND fuego_nuevo_no = 4", new { id = (long)U }) == 0, "con todas al máximo no hay oferta (y no se guarda una vacía)");

Console.WriteLine("\n== 12. PlayerBonusService ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET fuego_nuevo = 4 WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points) SELECT @id, species_id, 9 FROM pet_species", new { id = (long)U });
var ownedPets = await pets.GetOwnedAsync(U);
var rawPets = PetRules.Total(ownedPets);
var bb0 = await bonusSvc.GetAsync(U, 4);
Check(bb0.FuegoNuevo == 4 && bb0.BlessingLevels is null && Near(bb0.Pets.GoldPercent, rawPets.GoldPercent, 1e-9) && Near(bb0.HuntDropMultiplier, 2.2 * (1 + rawPets.DropPercent / 100), 1e-9), "el servicio junta Fuego Nuevo (4 → ×2,2) y mascotas; sin bendiciones BlessingLevels es null");
await SetBlessing(BlessingCatalog.PetsKey, 3); await SetBlessing(BlessingCatalog.HpKey, 2);
var bb1 = await bonusSvc.GetAsync(U, 4);
Check(Near(bb1.Pets.GoldPercent, rawPets.GoldPercent * 1.3, 1e-9) && Near(bb1.Pets.DefensePercent, rawPets.DefensePercent * 1.3, 1e-9) && bb1.BlessingLevel(BlessingCatalog.HpKey) == 2 && Near(bb1.MaxHpMultiplier, 1.04, 1e-9), "Manada III escala TODOS los bonus de las mascotas ×1,3 (una sola vez) y la vida suma sus niveles");
var broken = new PlayerBonusService(pets, new ThrowingBlessings(), new GameEventRepository(factory));
var bb2 = await broken.GetAsync(U, 4);
Check(bb2.FuegoNuevo == 4 && bb2.BlessingLevels is null && Near(bb2.Pets.GoldPercent, rawPets.GoldPercent, 1e-9), "si falla la lectura de bendiciones NO se cae: pelea con Fuego Nuevo y mascotas, sin bendiciones");
Check(Near((await bonusSvc.GetAsync(U + 55, 0)).HuntDropMultiplier, 1.0, 1e-9), "un jugador sin nada da ×1");

Console.WriteLine("\n== 13. Mascotas: Buen Pienso ==");
var pet0 = (await pets.GetOwnedAsync(U))[0];
var ago40 = DateTime.UtcNow.AddMinutes(-40);
var petAgo = new OwnedPet(pet0.Species, 2, ago40);
Check(!PetRules.CanEat(petAgo, DateTime.UtcNow) && PetRules.CanEat(petAgo, DateTime.UtcNow, TimeSpan.FromMinutes(35)) && PetRules.RemainingCooldown(petAgo, DateTime.UtcNow, TimeSpan.FromMinutes(35)) == TimeSpan.Zero && PetRules.RemainingCooldown(petAgo, DateTime.UtcNow).TotalMinutes is > 19 and < 21, "a los 40 min no puede comer con la hora de siempre, pero sí con 35 min (Buen Pienso V)");
Check(PetRules.CooldownText(null) == "1 hora" && PetRules.CooldownText(TimeSpan.FromMinutes(35)) == "35 min", "el texto de la espera: «1 hora» / «35 min»");
await db.ExecuteAsync("DELETE FROM player_pets WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points, last_fed_at) VALUES (@id, @s, 0, now() - interval '40 minutes')", new { id = (long)U, s = species[0].SpeciesId });
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U }); await Give(petFood, 5);
var cd1 = await pets.FeedAsync(U, species[0].SpeciesId);
Check(cd1.Status == FeedStatus.OnCooldown && cd1.Remaining.TotalMinutes is > 19 and < 21 && await Qty(petFood) == 5, "con la hora de siempre sigue en espera (quedan ~20 min) y no gasta comida");
var cd2 = await pets.FeedAsync(U, species[0].SpeciesId, TimeSpan.FromMinutes(35));
Check(cd2.Status == FeedStatus.Ok && cd2.Remaining == TimeSpan.FromMinutes(35) && await Qty(petFood) == 4, "con 35 min (Buen Pienso V) come: gasta 1 comida y la próxima espera es de 35 min");
Check((await pets.FeedAsync(U, species[0].SpeciesId, TimeSpan.FromMinutes(35))).Status == FeedStatus.OnCooldown, "y enseguida vuelve a estar en espera");
await db.ExecuteAsync("UPDATE player_pets SET last_fed_at = now() - interval '40 minutes' WHERE discord_id = @id", new { id = (long)U });
await SetBlessing(BlessingCatalog.PetFeedKey, 5);
var feedRes = await PetModule.ExecuteFeedAsync(users, pets, evts, U, null, bonusSvc);
Check(feedRes.Embed is not null && feedRes.Embed.Footer?.Text?.Contains("35 min") == true && AllText(feedRes.Embed).Contains(species[0].Name), "/pet feed con la bendición Buen Pienso V alimenta a los 40 min y el pie dice «35 min»");
var feedOld = await PetModule.ExecuteFeedAsync(users, pets, evts, U, null, null);
Check(feedOld.PlainMessage is not null && feedOld.PlainMessage.Contains("todavía"), "y sin la bendición (el llamador viejo) la misma mascota todavía no puede comer");
var viewRes = await PetModule.ExecuteViewAsync(users, pets, new InventoryRepository(factory), U, bonusService: bonusSvc);
Check(viewRes.Embed is not null && AllText(viewRes.Embed).Contains("🐾 Manada") && AllText(viewRes.Embed).Contains("35 min") && viewRes.Embed.Length <= 6000, "/pet view muestra la Manada (+30 %) y la espera de 35 min");

Console.WriteLine("\n== 14. El arranque de una pelea con bonus ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET level = 20, max_hp = 400, current_hp = 400, fuego_nuevo = 10, weapon_id = @w, amulet_id = @a, current_zone_id = @z, class = 'Guerrero' WHERE discord_id = @id", new { id = (long)U, w = weaponId, a = amuletId, z = firstZone });
foreach (var key in new[] { BlessingCatalog.AttackKey, BlessingCatalog.DefenseKey, BlessingCatalog.HpKey }) await SetBlessing(key, 5);
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
var starter = new AdventureCombatStarter(new CooldownRepository(factory), adventure, users, itemsRepo, monstersRepo, zonesRepo, buffRepo, bonusSvc, combat, raids);
var started = await starter.PrepareHuntAsync(U);
var st = started.State!;
var expectBonuses = await bonusSvc.GetAsync(U, 10);
var expectProfile = PlayerCombatProfileCalculator.Resolve(await Me(), weaponItem, amuletItem, 0, expectBonuses);
Check(started.Status == CombatStartStatus.Started && st.Bonuses is { FuegoNuevo: 10 } && st.PlayerDamage == expectProfile.Damage && st.PlayerDefense == expectProfile.Defense && st.PlayerMaxHp == expectProfile.CombatMaxHp, "/hunt arranca con el perfil de combate CON los bonus (ataque, defensa y vida) y guarda los bonus en el estado");
Check(st.PlayerDamage > baseProfile.Damage && st.Bonuses!.HuntDropMultiplier > 3.9 && Near(CombatRewardCalculator.HuntDropChance(st.Bonuses), 24, 1e-6), "y el drop de ese estado es el 24 % del FN 10");
var rgAdd = raids.TryRegisterParticipant(U, Guid.NewGuid());
Check(rgAdd && (await starter.PrepareHuntAsync(U)).Status == CombatStartStatus.AlreadyInCombat, "estar en un raid sigue bloqueando el arranque");
raids.UnregisterParticipant(U);

Console.WriteLine("\n== 15. Perfil ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET level = 20, max_hp = 400, current_hp = 400, fuego_nuevo = 3, weapon_id = @w, amulet_id = @a, class = 'Ninja' WHERE discord_id = @id", new { id = (long)U, w = weaponId, a = amuletId });
await SetBlessing(BlessingCatalog.AttackKey, 5); await SetBlessing(BlessingCatalog.DefenseKey, 3); await SetBlessing(BlessingCatalog.ColorKey, 2);
var prof = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets, bonusSvc);
var profPlain = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets);
var meNow = await Me();
var profBonuses = await bonusSvc.GetAsync(U, 3);
var profCombat = PlayerCombatProfileCalculator.Resolve(meNow, weaponItem, amuletItem, 0, profBonuses);
string ProfBlock(Embed e) => ProfileProbe.Stats(e);
string ProfLine(Embed e, string label) => ProfileProbe.Line(e, label);
string atkField = ProfLine(prof, "ATQ"), defField = ProfLine(prof, "DEF");
Check(prof.Description.Contains("FN **3**") && !prof.Title.Contains("FN") && !prof.Fields.Any(f => f.Name.Contains("Fuego Nuevo") || f.Name.Contains("Bendiciones") || f.Name.Contains("Mascotas") || f.Name.Contains("Oficios")), "el perfil muestra «FN 3» en la primera línea de la descripción (v0.19.4: ya no en el título) pero YA NO lista porcentajes, bendiciones, mascotas ni oficios (v0.14.3: están en aa fn, aa pet, aa bendiciones y aa oficios)");
Check(ProfileProbe.Number(prof, "ATQ") == profCombat.Damage && ProfileProbe.Number(prof, "DEF") == profCombat.Defense && profCombat.Damage > PlayerCombatProfileCalculator.Resolve(meNow, weaponItem, amuletItem, 0).Damage, $"el ataque ({profCombat.Damage}) y la defensa ({profCombat.Defense}) del perfil son los que pelean");
Check(!prof.Description.Contains(BlessingCatalog.ColorTitle(2)) && TitleCatalog.IsUnlocked(TitleCatalog.Find("color_2")!, new TitleContext(new Dictionary<string, long>(), 1, new HashSet<int>(), new HashSet<string>(), 2)) && prof.Color == new Color((uint)BlessingCatalog.ColorValue(2)) && profPlain.Color == Color.Orange && !profPlain.Fields.Any(f => f.Name.Contains("Bendiciones")), "Brasa de Color II cambia el color del perfil; su título (v0.23.0) ya no es una línea fija: es el título «Llama Dorada», que se desbloquea con el nivel de la bendición y se pone con /titles; sin el servicio no hay bendiciones ni color");
foreach (var b in all) await SetBlessing(b.Key, 5);
await db.ExecuteAsync("UPDATE users SET fuego_nuevo = 40 WHERE discord_id = @id", new { id = (long)U });
var profMax = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "T", "https://example.com/a.png", pets, bonusSvc);
Check(profMax.Length <= 6000 && profMax.Fields.All(f => f.Value.Length <= 1024), $"el perfil con las 13 bendiciones al máximo y FN 40 entra en los límites de Discord ({profMax.Length})");

Console.WriteLine("\n== 16. Pantallas y comandos ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET level = 18, xp = 40, gold = 12345, weapon_id = @w, amulet_id = @a, weapon_enchant = 2, class = 'Arquero', gate_cleared = true, fuego_nuevo = 2, has_bank = true, bank_gold = 99 WHERE discord_id = @id", new { id = (long)U, w = weaponId, a = amuletId });
await Give(wood, 5000); await Give(mineral, 300); await Give(drop, 75); await Give(consumable, 12); await Give(box, 3); await Give(egg, 1); await Give(petFood, 4);
await db.ExecuteAsync("UPDATE users SET dust = 700 WHERE discord_id = @id", new { id = (long)U });
var stNone = await FuegoNuevoModule.ExecuteStatusAsync(fnRepo, blessings, U);
var seEmbed = stNone.Embed!;
Check(seEmbed.Title.Contains("llevás 2") && seEmbed.Description.Contains("Asador Eterno") && seEmbed.Fields.Select(f => f.Name).Any(n => n.Contains("Fuego Nuevo ×3")) && seEmbed.Fields.Any(f => f.Name.Contains("Lo que se va") && f.Value.Contains("5.000") && f.Value.Contains("700 de Polvo") && f.Value.Contains("nivel 18")) && seEmbed.Fields.Any(f => f.Name.Contains("Lo que se queda") && f.Value.Contains("12.345") && f.Value.Contains("99")), "con el Asador vencido: lo que ganás (×3), lo que se va (con cantidades y el Polvo) y lo que se queda (oro y banco)");
Check(seEmbed.Length <= 6000 && seEmbed.Fields.All(f => f.Value.Length <= 1024) && AllText(seEmbed).Contains("🪵 Madera: 5.000") && AllText(seEmbed).Contains("📦 Cajas: 3") && AllText(seEmbed).Contains("huevos"), $"la pantalla entra en los límites y nombra madera, cajas y huevos ({seEmbed.Length})");
var rows = stNone.Components!.Components.OfType<ActionRowComponent>().ToList();
Check(rows.Count == 1 && rows[0].Components.Single() is ButtonComponent { CustomId: var startId } && startId == $"fn_start:{U}:2", "sin oferta pendiente hay UN botón «Hacer Fuego Nuevo» con el id del dueño y el contador");
var prev2 = (await fnRepo.GetPreviewAsync(U))!;
Check(seEmbed.Fields.Any(f => f.Name.Contains("Lo que te da hoy") && f.Name.Contains("×2") && f.Value.Contains("+60 %") && f.Value.Contains("+40 %")), "aa fn muestra «Lo que te da hoy (Fuego Nuevo ×2)» con los porcentajes también cuando ya podés volver a reiniciar (+60 % en /hunt, +40 % en /chop)");
var withBless = FuegoNuevoModule.BuildStatusEmbed(prev2, null, new Dictionary<string, int> { [BlessingCatalog.AttackKey] = 2, [BlessingCatalog.DefenseKey] = 1 });
Check(withBless.Fields.Any(f => f.Name == "🙏 Tus bendiciones" && f.Value.Contains("Filo Antiguo II") && f.Value.Contains("Piel Curtida I")) && withBless.Length <= 6000, "aa fn lista las bendiciones que juntó (el perfil ya no las muestra)");
var pick = FuegoNuevoModule.BuildClassPickButtons(U, prev2).Components.OfType<ActionRowComponent>().Single().Components.OfType<ButtonComponent>().ToList();
Check(pick.Count == 5 && pick.Take(4).Select(b => b.CustomId).SequenceEqual(ClassCatalog.All.Select(c => $"fn_go:{U}:2:{c.Name}")) && pick[4].CustomId == $"fn_cancel:{U}" && pick.Single(b => b.Label.Contains("la tuya")).Label.StartsWith("Arquero"), "el paso 2 son 4 clases + Cancelar en UNA fila (los ids llevan dueño, contador y clase) y marca la actual");
var pickEmbed = FuegoNuevoModule.BuildClassPickEmbed(prev2);
Check(pickEmbed.Description.Contains("No se puede deshacer") && pickEmbed.Fields.Length == 4 && pickEmbed.Length <= 6000, "el paso 2 avisa que no se deshace y describe las 4 clases");
await db.ExecuteAsync("UPDATE users SET gate_cleared = false WHERE discord_id = @id", new { id = (long)U });
var stNot = await FuegoNuevoModule.ExecuteStatusAsync(fnRepo, blessings, U);
Check(stNot.Embed!.Description.Contains("volver a vencer al **Asador Eterno**") && stNot.Embed.Fields.Any(f => f.Name.Contains("te da hoy")) && !stNot.Embed.Fields.Any(f => f.Name.Contains("Lo que se va")) && stNot.Components!.Components.Count == 0, "sin el Asador vencido: explica qué falta, muestra lo que da hoy y no hay botón");
await db.ExecuteAsync("UPDATE users SET fuego_nuevo = 0 WHERE discord_id = @id", new { id = (long)U });
var st0 = await FuegoNuevoModule.ExecuteStatusAsync(fnRepo, blessings, U);
Check(st0.Embed!.Description.Contains("Asador Eterno") && st0.Embed.Description.Contains("Fogón") && !st0.Embed.Fields.Any(f => f.Name.Contains("te da hoy")), "sin ningún Fuego Nuevo: explica cómo se habilita");
Check((await FuegoNuevoModule.ExecuteStatusAsync(fnRepo, blessings, U + 8)).PlainMessage == FuegoNuevoModule.NoAccount, "sin cuenta: «empezá con /start»");

var offerNow = new BlessingOffer(2, ["manada", "filo_antiguo", "buen_pienso"]);
var withOffer = FuegoNuevoModule.BuildStatusButtons(U, prev2, offerNow, new Dictionary<string, int> { ["manada"] = 4 }).Components.OfType<ActionRowComponent>().ToList();
var offerRow = withOffer.Single(r => r.Components.OfType<ButtonComponent>().Any(b => b.CustomId.StartsWith("fn_bless")));
Check(withOffer.Count == 1 || withOffer.Count == 2, "los botones de la oferta van en su propia fila (no se mezclan con el de Fuego Nuevo si lo hay)");
var offerButtons = offerRow.Components.OfType<ButtonComponent>().ToList();
Check(offerButtons.Count == 3 && offerButtons.Select(b => b.CustomId).SequenceEqual(new[] { "manada", "filo_antiguo", "buen_pienso" }.Select(k => $"fn_bless:{U}:2:{k}")) && offerButtons[0].Label == "Manada V" && offerButtons[1].Label == "Filo Antiguo I", "un botón por bendición ofrecida, con el nivel al que quedaría (Manada ya en IV → V)");
var offerText = AllText(FuegoNuevoModule.BuildStatusEmbed(prev2, offerNow, new Dictionary<string, int> { ["manada"] = 4 }));
Check(offerText.Contains("Manada V") && offerText.Contains("sube de nivel") && offerText.Contains("Filo Antiguo I"), "la oferta dice qué da cada una y marca la que sube de nivel");
var bEmbed0 = FuegoNuevoModule.BuildBlessingsEmbed(new Dictionary<string, int>(), null);
var bEmbedMax = FuegoNuevoModule.BuildBlessingsEmbed(lv5, offerNow);
Check(bEmbed0.Description.Contains("Todavía no tenés ninguna") && bEmbed0.Fields.Single().Name.Contains("existen") && bEmbed0.Length <= 6000 && bEmbed0.Fields.All(f => f.Value.Length <= 1024), "/blessings sin ninguna lo explica y lista las 13 que existen");
Check(bEmbedMax.Length <= 6000 && bEmbedMax.Description.Length <= 4096 && bEmbedMax.Fields.All(f => f.Value.Length <= 1024) && bEmbedMax.Description.Split('\n').Length == 13, $"con las 13 al máximo y una oferta entra en los límites ({bEmbedMax.Length})");
var bTitles = bEmbedMax.Fields.Single(f => f.Name.Contains("existen")).Value;
Check(all.All(b => bTitles.Contains(b.Name)), "y el campo de «las que existen» nombra todas");

Console.WriteLine("\n== 17. Los pasos del comando de punta a punta ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET level = 9, gate_cleared = true, fuego_nuevo = 0, class = 'Guerrero', weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = weaponId });
await Give(wood, 10);
var stale = await FuegoNuevoModule.ExecuteStartAsync(fnRepo, combat, raids, U, 7);
var go1 = await FuegoNuevoModule.ExecuteStartAsync(fnRepo, combat, raids, U, 0);
Check(stale.PlainMessage == FuegoNuevoModule.StaleMessage && stale.Embed is null && go1.Embed is not null && go1.Embed.Title.Contains("vuelta 2") && go1.Components is not null, "el paso 1 con un contador viejo se rechaza y con el bueno muestra la elección de clase");
raids.TryRegisterParticipant(U, Guid.NewGuid());
var busyStart = await FuegoNuevoModule.ExecuteStartAsync(fnRepo, combat, raids, U, 0);
var busyRenew = await FuegoNuevoModule.ExecuteRenewAsync(fnRepo, combat, raids, evts, U, 0, "Ninja");
Check(busyStart.PlainMessage == FuegoNuevoModule.BusyMessage && busyRenew.PlainMessage == FuegoNuevoModule.BusyMessage && (await Me()).Level == 9 && await Qty(wood) == 10, "en un raid no se puede ni empezar ni confirmar, y no se toca nada");
raids.UnregisterParticipant(U);
Check((await FuegoNuevoModule.ExecuteRenewAsync(fnRepo, combat, raids, evts, U, 4, "Ninja")).PlainMessage == FuegoNuevoModule.StaleMessage && (await FuegoNuevoModule.ExecuteRenewAsync(fnRepo, combat, raids, evts, U, 0, "Paladín")).PlainMessage!.Contains("no existe"), "confirmar con un contador viejo o una clase inexistente se rechaza con su mensaje");
var done = await FuegoNuevoModule.ExecuteRenewAsync(fnRepo, combat, raids, evts, U, 0, "Hechicero");
var meDone = await Me();
Check(done.Embed is not null && done.Embed.Title.Contains("FUEGO NUEVO ×1") && AllText(done.Embed).Contains("Hechicero") && AllText(done.Embed).Contains("Elegí tu bendición") && done.Embed.Length <= 6000 && evts.Kinds.Contains(GameEventKinds.FuegoNuevo) && meDone.Level == 1 && meDone.Class == "Hechicero" && meDone.FuegoNuevo == 1, "confirmar: el banner del Fuego Nuevo ×1 con las 3 bendiciones, el evento «fuego_nuevo» y el jugador reiniciado");
var doneButtons = done.Components!.Components.OfType<ActionRowComponent>().Single().Components.OfType<ButtonComponent>().ToList();
Check(doneButtons.Count == 3 && doneButtons.All(b => b.CustomId.StartsWith($"fn_bless:{U}:1:")), "el banner trae 3 botones de bendición con el número del Fuego Nuevo");
var chosen = await FuegoNuevoModule.ExecuteChooseAsync(blessings, evts, U, 1, doneButtons[0].CustomId.Split(':')[3]);
Check(chosen.Embed is not null && chosen.Embed.Title.Contains("Bendición elegida") && evts.Kinds.Contains(GameEventKinds.BlessingChosen), "elegir una: la pantalla de la bendición y el evento «blessing_chosen»");
var chosen2 = await FuegoNuevoModule.ExecuteChooseAsync(blessings, evts, U, 1, doneButtons[1].CustomId.Split(':')[3]);
var chosen3 = await FuegoNuevoModule.ExecuteChooseAsync(blessings, evts, U, 1, "no_existe");
Check(chosen2.Embed is null && chosen2.PlainMessage!.Contains("ya está elegida") && chosen3.Embed is null && chosen3.PlainMessage!.Contains("no estaba"), "elegir otra de la misma oferta o una que no estaba: se rechaza con texto (no pisa la pantalla)");
var blessView = await FuegoNuevoModule.ExecuteBlessingsAsync(blessings, U);
Check(blessView.Embed is not null && blessView.Embed.Description.Split('\n').Length == 1 && blessView.Components!.Components.Count == 0, "/blessings ahora muestra la elegida y ya no hay botones");

Console.WriteLine("\n== 18. Comandos registrados, botones y DI (InteractionService real) ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var fnCmds = interactions.SlashCommands.Where(c => c.Name is "fuegonuevo" or "blessings").ToList();
Check(fnCmds.Count == 2 && fnCmds.All(c => c.Module.SlashGroupName is null && validName(c.Name) && c.Description.Length is >= 1 and <= 100 && c.Parameters.Count == 0), "/fuegonuevo y /blessings existen, sin grupo ni parámetros y con nombre y descripción válidos");
var classCmd = interactions.SlashCommands.Single(c => c.Name == "class");
Check(classCmd.Description.Length <= 100, "/class sigue entrando en los 100 caracteres con su descripción nueva");
Discord.IComponentInteraction Click(string id) => FakeProxy.Make<Discord.IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<Discord.IComponentInteractionData>(new() { ["CustomId"] = id }) });
(string? Method, string? Module, string[] Groups) Route(string id) { var r = interactions.SearchComponentCommand(Click(id)); return r.IsSuccess ? (r.Command.MethodName, r.Command.Module.Name, r.RegexCaptureGroups ?? []) : (null, null, []); }
var rStart = Route($"fn_start:{U}:2"); var rGo = Route($"fn_go:{U}:2:Hechicero"); var rCancel = Route($"fn_cancel:{U}"); var rBless = Route($"fn_bless:{U}:3:brasa_vieja");
Check(rStart is { Method: "HandleStartAsync", Module: "FuegoNuevoModule" } && rStart.Groups.SequenceEqual([U.ToString(), "2"]), "«fn_start:{dueño}:{contador}» llega a HandleStartAsync con sus dos capturas");
Check(rGo is { Method: "HandleGoAsync" } && rGo.Groups.SequenceEqual([U.ToString(), "2", "Hechicero"]), "«fn_go:{dueño}:{contador}:{clase}» llega a HandleGoAsync con sus tres capturas");
Check(rCancel is { Method: "HandleCancelAsync" } && rCancel.Groups.SequenceEqual([U.ToString()]), "«fn_cancel:{dueño}» llega a HandleCancelAsync");
Check(rBless is { Method: "HandleBlessAsync" } && rBless.Groups.SequenceEqual([U.ToString(), "3", "brasa_vieja"]), "«fn_bless:{dueño}:{n}:{clave}» llega a HandleBlessAsync, y la clave con guion bajo se captura entera");
Check(Route("fn_bless:1").Method is null && Route("fn_start:1").Method is null, "ids armados a medias no llegan a ningún método (no hay ambigüedad con otros botones)");
var otherRoutes = new[] { $"pet_feed:{U}", $"ach_claim:{U}:2", $"class-select:Ninja" }.Select(Route).ToList();
Check(otherRoutes[0].Method == "HandleFeedAsync" && otherRoutes[1].Module is not null && otherRoutes[2].Method == "HandleClassSelectionAsync", "los botones de siempre (mascotas, logros, clase) siguen llegando a su método");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "fuegonuevo", "fn", "renacer", "bendiciones", "blessings", "bendicion" }.All(textNames.Contains) && textNames.Count(n => n == "fn") == 1, "aa fuegonuevo / fn / renacer / bendiciones / blessings están registrados, sin repetirse");
Check(interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "siguen entrando en el tope de 100 comandos de nivel superior");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || t.Name.EndsWith("ModuleBase") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built} armados{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");

Console.WriteLine("\n== 19. Ayuda y logros ==");
var topic = HelpTopics.Find("fuego");
Check(topic?.Key == "fuego" && new[] { "fuegonuevo", "fuego nuevo", "fn", "reinicio", "renacer", "reset", "bendiciones", "blessings", "prestigio" }.All(a => HelpTopics.Find(a)?.Key == "fuego") && HelpTopics.Find("fogon")?.Key == "fogon" && HelpTopics.Find("asador")?.Key == "fogon", "«info fuego / fn / reinicio / renacer / bendiciones...» abren el tema nuevo y el del Fogón sigue en su lugar");
var te = topic!.Build();
string tt = AllText(te);
Check(te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024) && tt.Contains("/fuegonuevo") && tt.Contains("/blessings") && tt.Contains("+30 %") && tt.Contains("+300 %") && tt.Contains("+20 %") && tt.Contains("+10 %") && tt.Contains("6 %") && tt.Contains("caja") && tt.Contains("Sin reembolso") && tt.Contains("duelos y la Arena"), $"el tema explica el comando, los porcentajes (leídos de las constantes), lo que se va y los duelos, dentro de los límites ({te.Length})");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m) || m == "info") , $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var info = HelpModule.BuildInfoEmbed();
Check(AllText(info).Contains("/fuegonuevo") && AllText(info).Contains("fuego") && info.Length <= 6000 && info.Fields.All(f => f.Value.Length <= 1024), $"/info lista /fuegonuevo y el tema dentro de los límites ({info.Length})");
Check(AllText(HelpTopics.Find("fogon")!.Build()).Contains("/fuegonuevo") && !FogonRules.ClearedText.Contains("próxima versión") && FogonRules.ClearedText.Contains("/fuegonuevo"), "el tema del Fogón y el mensaje de victoria ya apuntan a /fuegonuevo (no dicen «próxima versión»)");
Check(AllText(HelpTopics.Find("classes")!.Build()).Contains("Fuego Nuevo"), "el tema de clases avisa que la clase se vuelve a elegir con cada Fuego Nuevo");
var ren = AchievementCatalog.Find("renacido");
Check(ren is not null && ren.Tiers.Select(t => t.Threshold).SequenceEqual([1L, 5L, 20L]) && ren.Tiers.All(t => t.Reward.Box == BoxGrant.None) && ren.StatKind == GameEventKinds.FuegoNuevo && AchievementCatalog.Categories[3].Keys.Contains("renacido") && AchievementCatalog.All.Count == 23, "logro Renacido (1/5/20 Fuegos Nuevos): solo oro y XP, en Constancia (23 logros en total desde la v0.18.3)");
Check(AchievementCatalog.Categories.SelectMany(c => c.Keys).OrderBy(k => k).SequenceEqual(AchievementCatalog.All.Select(a => a.Key).OrderBy(k => k)), "cada logro está en exactamente una página");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}

sealed class ThrowingBlessings : IBlessingRepository
{
    public Task<IReadOnlyDictionary<string, int>> GetLevelsAsync(ulong discordId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
    public Task<BlessingOffer?> GetPendingOfferAsync(ulong discordId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
    public Task<ChooseOutcome> ChooseAsync(ulong discordId, int fuegoNuevoNo, string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
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
