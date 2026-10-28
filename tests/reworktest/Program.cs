using Microsoft.Extensions.Configuration;
using BotDsRpg.GameData;

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== Tarea 1: rendimiento de /chop y /mine por rareza ==");
Check(GatheringYield.RunMultiplier == 1, "RunMultiplier sigue en 1 (el reset lo sube después)");
foreach (var (rarity, min, max) in new[] { ("Común", 1, 5), ("Raro", 1, 3), ("Épico", 1, 2), ("Legendario", 1, 1), ("Mítico", 1, 1) })
{
    Check(GatheringYield.RangeFor(rarity) == (min, max), $"{rarity}: rango {min}-{max} (da {GatheringYield.RangeFor(rarity)})");
    int lo = int.MaxValue, hi = 0;
    for (int i = 0; i < 5000; i++) { int q = GatheringYield.Roll(rarity); lo = Math.Min(lo, q); hi = Math.Max(hi, q); }
    Check(lo == min && hi == max, $"{rarity}: 5000 tiradas, salió entre {lo} y {hi}");
}
Check(GatheringYield.Average("Épico") == 1.5 && GatheringYield.Average("Raro") == 2.0 && GatheringYield.Average("Común") == 3.0, "promedios 3 / 2 / 1,5 (los que usa el modelo de ritmos)");
Check(GatheringYield.RangeFor("Común").Max > GatheringYield.RangeFor("Raro").Max && GatheringYield.RangeFor("Raro").Max > GatheringYield.RangeFor("Épico").Max && GatheringYield.RangeFor("Épico").Max > GatheringYield.RangeFor("Legendario").Max, "el máximo baja con CADA escalón de rareza (nunca 5 de algo raro)");

Console.WriteLine("\n== Tarea 2: el jefe da un cofre ==");
Check(CombatRewardCalculator.BossChestChancePercent(true, 0) == 100 && CombatRewardCalculator.BossChestChancePercent(false, 40) == 40, "100% la primera vez, 40% después");
int firstDrops = 0, repeatDrops = 0;
for (int i = 0; i < 3000; i++) if (CombatRewardCalculator.RollBossReward(10, 100, 100, firstClear: true).DroppedSomething) firstDrops++;
for (int i = 0; i < 20000; i++) if (CombatRewardCalculator.RollBossReward(10, 100, 100, firstClear: false, repeatChancePercent: 40).DroppedSomething) repeatDrops++;
Check(firstDrops == 3000, $"primera vez: {firstDrops}/3000 tiradas dan cofre (tienen que ser todas)");
Check(repeatDrops is > 7600 and < 8400, $"repetición: {repeatDrops}/20000 = {repeatDrops / 200.0:F1}% (tiene que rondar 40%)");
var chestBoss = new MonsterTemplate("Rey Jabalí", "🐗", 140, 196, 23, 37, ["Cajón de Pino"], 150, 135);
var hunter = new MonsterTemplate("Jabalí Rabioso", "🐗", 64, 97, 9, 19, ["Colmillo de Jabalí"]);
var items = new Dictionary<string, DropItemInfo> { ["Cajón de Pino"] = new("Cajón de Pino", "Común", "<:cofre_comun:1555578513682075809>"), ["Colmillo de Jabalí"] = new("Colmillo de Jabalí", "Raro", null) };
var blocks = DropsCatalog.BuildZoneBlocks([new ZoneMonster(1, MonsterKind.Hunt, hunter), new ZoneMonster(1, MonsterKind.Boss, chestBoss)], items, new ZoneBoxRow(1, 1, "repeat", "Cajón de Pino", 40));
var bossBlock = blocks.Single(b => b.Title.Contains("Jefe"));
Check(bossBlock.Title.Contains("100%") && bossBlock.Title.Contains("cofre") && bossBlock.Text.Contains("40%"), "/drops: el bloque del jefe dice cofre 100% la primera vez y (con la fila repeat) 40% después: " + bossBlock.Title);
Check(bossBlock.Text.Contains("<:cofre_comun:1555578513682075809> Cajón de Pino"), "/drops: el cofre del jefe se muestra con su emoji");
Check(blocks.Single(b => b.Title.Contains("Cazar")).Title.Contains("6%"), "/drops: el bloque de cazar sigue con su 6%");

Console.WriteLine("\n== Tarea 6: foco de revisión, contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new BotDsRpg.Data.NpgsqlConnectionFactory(cfg);
var userRepo = new BotDsRpg.Repositories.UserRepository(factory);
var itemRepo = new BotDsRpg.Repositories.ItemRepository(factory);
var zoneRepo = new BotDsRpg.Repositories.ZoneRepository(factory);
var recipeRepo = new BotDsRpg.Repositories.RecipeRepository(factory);
var monsterRepo = new BotDsRpg.Repositories.MonsterRepository(factory);
var adventureRepo = new BotDsRpg.Repositories.AdventureRepository(factory);
var inventoryRepo = new BotDsRpg.Repositories.InventoryRepository(factory);

var zones = await zoneRepo.GetAllAsync();
var allRecipes = await recipeRepo.GetAllAsync();
foreach (var cls in new[] { "Guerrero", "Ninja", "Arquero", "Hechicero" })
{
    var bad = new List<string>();
    foreach (var z in zones)
    {
        var view = RecipeCatalog.ViewFor(allRecipes, zones, cls, z.ZoneId);
        var groups = view.Recipes.Select(RecipeCatalog.GroupOf).OrderBy(g => g).ToList();
        if (groups.Count != 3 || !groups.SequenceEqual([RecipeGroup.ClassWeapon, RecipeGroup.GeneralWeapon, RecipeGroup.Amulet])) bad.Add($"zona {z.ZoneId}: {string.Join("/", groups)}");
        var mine = view.Recipes.Single(r => RecipeCatalog.GroupOf(r) == RecipeGroup.ClassWeapon).ResultItem;
        if (ClassCatalog.All.First(c => c.Name == cls).WeaponType != mine.WeaponFamily) bad.Add($"zona {z.ZoneId}: el arma de clase no es de {cls}");
    }
    Check(bad.Count == 0, $"{cls}: en cada zona ve su arma de clase y el amuleto, más la única arma general (sin zona) {(bad.Count == 0 ? "" : string.Join("; ", bad))}");
}
// v0.27.0 (el dueño, 2026-10-26: «nadie hace el arma general porque se ahorra los drops con la de sinergia o el amuleto»): se fue la sinergia ×1,5 (el número ya viene en el ATQ del arma de clase) y las armas
// generales de las zonas 2 a 5; queda UNA sola, la primera, sin zona y hecha solo con materiales de recolección, como base para cualquiera.
var normalZoneIds = zones.Select(z => z.ZoneId).ToHashSet();
var generalsInZones = allRecipes.Where(r => RecipeCatalog.GroupOf(r) == RecipeGroup.GeneralWeapon && r.ZoneId is int zid && normalZoneIds.Contains(zid)).ToList();
var baseGeneral = allRecipes.Where(r => RecipeCatalog.GroupOf(r) == RecipeGroup.GeneralWeapon && r.ZoneId is null).ToList();
Check(generalsInZones.Count == 0 && baseGeneral.Count == 1 && baseGeneral[0].ResultItem.Name == "Hoja de Acero Puro", "ninguna zona tiene arma general; hay UNA sola, sin zona: la Hoja de Acero Puro");
Check(baseGeneral[0].ResultItem.WeaponFamily is null && baseGeneral[0].ResultItem.ClassRequirement is null && baseGeneral[0].ResultItem.StatValue == 9, "la Hoja de Acero Puro es de cualquier clase y suma +9 ATQ");
var baseGeneralTypes = new List<string>();
foreach (var ing in baseGeneral[0].Ingredients) baseGeneralTypes.Add((await itemRepo.GetByNameAsync(ing.ItemName))!.Type);
Check(baseGeneralTypes.Count >= 2 && baseGeneralTypes.All(t => t is "Madera" or "Mineral"), $"la Hoja de Acero Puro se hace SOLO con materiales de recolección, sin drops de monstruos ({string.Join(", ", baseGeneral[0].Ingredients.Select(i => $"{i.Quantity}× {i.ItemName}"))})");
foreach (var retired in new[] { "Cuchilla de Cenizas", "Pico de Minero Reforzado", "Lanza de Magma", "Martillo del Titán" })
{
    Check(await itemRepo.GetByNameAsync(retired) is not null && !allRecipes.Any(r => r.ResultItem.Name == retired), $"{retired}: el ítem sigue en el catálogo (quien lo tenga lo conserva) pero ya no tiene receta");
}
Check(allRecipes.Where(r => RecipeCatalog.GroupOf(r) == RecipeGroup.ClassWeapon).All(r => r.ResultItem.WeaponFamily is not null && r.ResultItem.ClassRequirement is not null), "las 20 armas de clase tienen familia y son exclusivas de su clase");
var classStats = allRecipes.Where(r => RecipeCatalog.GroupOf(r) == RecipeGroup.ClassWeapon && r.ZoneId is not null)
    .GroupBy(r => zones.First(z => z.ZoneId == r.ZoneId).MinLevel).OrderBy(g => g.Key)
    .Select(g => g.Select(r => r.ResultItem.StatValue).Distinct().ToList()).ToList();
Check(classStats.Count == 5 && classStats.All(s => s.Count == 1) && classStats.Select(s => s[0]).SequenceEqual([14, 30, 48, 75, 120]), $"las 4 armas de clase de cada zona suman el MISMO ATQ fijo: {string.Join(" / ", classStats.Select(s => string.Join(",", s)))} (lo que antes salía de 9/20/32/50/80 × 1,5)");
Check(zones.All(z => allRecipes.Count(r => r.ZoneId == z.ZoneId && RecipeCatalog.GroupOf(r) == RecipeGroup.ClassWeapon) == 4 && allRecipes.Count(r => r.ZoneId == z.ZoneId && RecipeCatalog.GroupOf(r) == RecipeGroup.Amulet) == 1), "cada zona: 4 armas de clase (una por clase) + 1 amuleto");

var monstersAll = await monsterRepo.GetAllAsync();
foreach (var z in zones)
{
    var hunt = monstersAll.Where(m => m.ZoneId == z.ZoneId && m.Kind == MonsterKind.Hunt).ToList();
    Check(hunt.Count == 2 && hunt.All(m => m.Monster.DropItemNames.Count == 1), $"Zona {z.ZoneId}: /hunt sale de un pool de 2 monstruos, cada uno con su drop ({string.Join(", ", hunt.Select(m => m.Monster.Name))})");
}

var dropsMsg = await DropsModule.BuildDropsMessageAsync(userRepo, zoneRepo, monsterRepo, itemRepo, 1UL);
int dropsTotal = dropsMsg.Embeds.Sum(e => (e.Title?.Length ?? 0) + (e.Description?.Length ?? 0) + e.Fields.Sum(f => f.Name.Length + f.Value.Length));
Check(dropsMsg.Embeds.Length == 5 && dropsTotal < 6000, $"/drops: 5 embeds (uno por zona) y {dropsTotal} caracteres en total (límite 6000)");
Check(dropsMsg.Embeds.SelectMany(e => e.Fields).Any(f => f.Name.Contains("Jefe") && f.Name.Contains("100%") && f.Name.Contains("cofre")), "/drops: el bloque del jefe dice cofre 100% la primera vez (lo de las repeticiones sale de zone_boxes: lo prueba cofrestest)");

// El jefe da su cofre y llega a la mochila por la ruta de siempre (ApplyBossVictoryAsync); el primer jefe de la zona sube highest_zone_cleared.
const ulong T = 999000000000009001UL;
await using (var c = new Npgsql.NpgsqlConnection(conn)) { await c.OpenAsync(); await Dapper.SqlMapper.ExecuteAsync(c, "DELETE FROM users WHERE discord_id = @id", new { id = (long)T }); }
await userRepo.GetOrCreateUserAsync(T);
var z1 = zones.OrderBy(z => z.MinLevel).First();
var boss1 = (await monsterRepo.GetBossByZoneAsync(z1.ZoneId))!;
var chestItem = (await itemRepo.GetByNameAsync(boss1.DropItemNames.Single()))!;
Check(chestItem.Type == "Caja" && chestItem.Name == "Cajón de Pino", $"el jefe de la Zona 1 ({boss1.Name}) suelta {chestItem.Name} (tipo {chestItem.Type})");
await adventureRepo.ApplyBossVictoryAsync(T, 100, 100, 0, chestItem.ItemId, 1, z1.ZoneId);
var bag = await inventoryRepo.GetByDiscordIdAsync(T);
var afterBoss = (await userRepo.GetByDiscordIdAsync(T))!;
Check(bag.Any(e => e.ItemName == "Cajón de Pino" && e.Quantity == 1) && afterBoss.HighestZoneCleared >= z1.ZoneId, "el cofre del jefe entra a la mochila y el jefe queda derrotado (highest_zone_cleared sube)");
bool firstAgain = afterBoss.HighestZoneCleared < z1.ZoneId;
Check(!firstAgain && CombatRewardCalculator.BossChestChancePercent(firstAgain, 40) == 40, "después de derrotarlo, la siguiente vez ya NO es 'primera vez' (40%)");
await using (var c = new Npgsql.NpgsqlConnection(conn)) { await c.OpenAsync(); await Dapper.SqlMapper.ExecuteAsync(c, "DELETE FROM users WHERE discord_id = @id", new { id = (long)T }); }

Console.WriteLine("\n== v0.8.0 · Tarea 1: cooldowns y recompensas ==");
Check(CooldownCatalog.Travel.Duration == TimeSpan.FromMinutes(30), "travel: cooldown de 30 minutos");
Check(CooldownCatalog.Boss.Duration == TimeSpan.FromHours(5) && CooldownCatalog.Raid.CommandName == CooldownCatalog.Boss.CommandName && CooldownCatalog.Raid.Duration == CooldownCatalog.Boss.Duration, "jefe y raid: UN cooldown de 5 horas");
Check(CooldownCatalog.Boss.RetryAfterFailure == TimeSpan.FromMinutes(30) && CooldownCatalog.Travel.RetryAfterFailure is null && CooldownCatalog.Hunt.RetryAfterFailure is null, "perder o huir deja 30 minutos (solo el jefe tiene esa regla)");
Check(CombatRewardCalculator.TravelRewardMultiplier == 30 && CombatRewardCalculator.BossRewardMultiplier == 15, "travel x30 (ya sin drop de material desde la v0.36.0: da una caja con su chance); jefe x15");
Check(CombatRewardCalculator.TravelRewardMultiplier / CooldownCatalog.Travel.Duration.TotalMinutes == 1.0, "travel paga lo mismo POR MINUTO que con 10 minutos (1 cacería por minuto)");

Console.WriteLine("\n== v0.8.0 · Tarea 1 (base): el cooldown del jefe ==");
const ulong BossTester = 999000000000009002UL;
var cooldownRepo = new BotDsRpg.Repositories.CooldownRepository(factory);
await using (var c0 = new Npgsql.NpgsqlConnection(conn)) { await c0.OpenAsync(); await Dapper.SqlMapper.ExecuteAsync(c0, "DELETE FROM users WHERE discord_id = @id", new { id = (long)BossTester }); }
await userRepo.GetOrCreateUserAsync(BossTester);
var bossCd = CooldownCatalog.Boss;
bool claimedStart = await adventureRepo.TryClaimCooldownAsync(BossTester, bossCd.CommandName, bossCd.Duration, bossCd.RetryAfterFailure);
var remStart = await cooldownRepo.GetRemainingAsync(BossTester, bossCd.CommandName, bossCd.Duration);
Check(claimedStart && remStart is { } rs && rs > TimeSpan.FromMinutes(29) && rs <= TimeSpan.FromMinutes(30), $"al empezar la pelea del jefe queda un cooldown de 30 minutos ({remStart})");
Check(!await adventureRepo.TryClaimCooldownAsync(BossTester, bossCd.CommandName, bossCd.Duration, bossCd.RetryAfterFailure), "con el cooldown corriendo no se puede empezar otra pelea del jefe (la guarda atómica sigue)");
var zoneOne = zones.OrderBy(z => z.MinLevel).First();
await adventureRepo.ApplyBossVictoryAsync(BossTester, 10, 10, 0, null, 1, zoneOne.ZoneId);
var remWin = await cooldownRepo.GetRemainingAsync(BossTester, bossCd.CommandName, bossCd.Duration);
Check(remWin is { } rw && rw > TimeSpan.FromMinutes(299), $"ganarle al jefe completa las 5 horas ({remWin})");
bool huntClaim = await adventureRepo.TryClaimCooldownAsync(BossTester, CooldownCatalog.Hunt.CommandName, CooldownCatalog.Hunt.Duration);
var huntRem = await cooldownRepo.GetRemainingAsync(BossTester, "hunt", CooldownCatalog.Hunt.Duration);
Check(huntClaim && huntRem is { } hr && hr > TimeSpan.FromSeconds(55) && hr <= TimeSpan.FromMinutes(1), $"un comando común (hunt) sigue reclamando el cooldown entero ({huntRem})");
await using (var c1 = new Npgsql.NpgsqlConnection(conn)) { await c1.OpenAsync(); await Dapper.SqlMapper.ExecuteAsync(c1, "DELETE FROM users WHERE discord_id = @id", new { id = (long)BossTester }); }

Console.WriteLine("\n== v0.8.0 · Tarea 2: el sorteo de las cajas (puro) ==");
{
    var gatherPool = new List<GatherCandidate>
    {
        new(1, "Madera de Pino", "Común", null, "Madera"), new(2, "Madera de Roble", "Raro", null, "Madera"), new(3, "Madera de Nogal", "Épico", null, "Madera"),
        new(4, "Madera de Ébano", "Legendario", null, "Madera"), new(5, "Corteza del Árbol de Vida", "Mítico", null, "Madera"),
        new(6, "Piedra", "Común", null, "Mineral"), new(7, "Carbón", "Raro", null, "Mineral"), new(8, "Hierro", "Raro", null, "Mineral"),
        new(9, "Oro Puro", "Épico", null, "Mineral"), new(10, "Gema de Zafiro", "Legendario", null, "Mineral"), new(11, "Fragmento de Meteorito", "Mítico", null, "Mineral"),
    };
    var zonePool = new List<ZoneDropCandidate>();
    for (int z = 1; z <= 5; z++)
    {
        zonePool.Add(new(100 + z * 10, $"Hunt A z{z}", "Común", null, z, false)); zonePool.Add(new(101 + z * 10, $"Hunt B z{z}", "Común", null, z, false)); zonePool.Add(new(102 + z * 10, $"Travel z{z}", "Común", null, z, true));
    }

    BoxDefinition Box(int min, int max, int tier, params BoxLootEntry[] entries) => new(1, "Caja de prueba", min, max, tier, entries);
    BoxLootEntry Gather(int w) => new(LootKind.Gather, null, null, null, null, w, 1, 1);
    BoxLootEntry Zone(int w) => new(LootKind.ZoneDrop, null, null, null, null, w, 1, 1);
    BoxLootEntry Fixed(int id, string name, int w) => new(LootKind.Item, id, name, "Común", null, w, 1, 1);
    BoxLootEntry Gold(int perMille, int min, int max) => new(LootKind.Gold, null, null, null, null, perMille, min, max);
    int Units(BoxLootResult r) => r.Items.Sum(i => i.Quantity);
    var rng = new Random(12345);

    // 1) El rango dicho es el real
    var cajon = Box(1, 10, 1, Gather(79), Zone(12), Fixed(900, "Mate Amargo", 6), Fixed(901, "Collar de Cuero Viejo", 3), Gold(30, 100, 300));
    var ctx = new BoxRollContext(5, gatherPool, zonePool);
    int lo = int.MaxValue, hi = 0; var seen = new HashSet<int>();
    for (int i = 0; i < 20000; i++) { int u = Units(BoxLootRoller.Roll(cajon, ctx, rng)); lo = Math.Min(lo, u); hi = Math.Max(hi, u); seen.Add(u); }
    Check(lo == 1 && hi == 10 && seen.Count == 10, $"'entre 1 y 10 ítems': 20000 aperturas salieron entre {lo} y {hi} y aparecieron los 10 valores");
    Check(BoxCatalog.RangeText(1, 10) == "entre 1 y 10 ítems" && BoxCatalog.RangeText(null, null) == string.Empty, "el texto del rango: «" + BoxCatalog.RangeText(1, 10) + "»");

    // 2) La recolección dentro de una caja tiene las chances de /chop y /mine (Mítico, 0,1% por ítem)
    var soloRecolecta = Box(200000, 200000, 1, Gather(1));
    var res = BoxLootRoller.Roll(soloRecolecta, ctx, rng);
    double Share(string name) => res.Items.Where(i => i.Name == name).Sum(i => i.Quantity) / 200000.0;
    double commonTotal = Share("Madera de Pino") + Share("Piedra"), rareTotal = Share("Madera de Roble") + Share("Carbón") + Share("Hierro");
    Check(Math.Abs(commonTotal - 0.682) < 0.006 && Math.Abs(rareTotal - 0.2106) < 0.005, $"Común {commonTotal:P1} y Raro {rareTotal:P1} (las chances reales de /chop y /mine: 68% y 21%, sobre el total sin el Mítico de /chop)");
    Check(Share("Madera de Pino") > 0.33 && Share("Madera de Pino") < 0.35 && Share("Hierro") > 0.050 && Share("Hierro") < 0.055, $"Pino {Share("Madera de Pino"):P1} (la mitad del Común) y Hierro {Share("Hierro"):P2} (el Raro de mineral se reparte entre Carbón e Hierro)");
    double corteza = Share("Corteza del Árbol de Vida"), meteorito = Share("Fragmento de Meteorito");
    Check(corteza is > 0.0006 and < 0.0014 && meteorito is > 0.0006 and < 0.0014, $"Mítico: Corteza {corteza:P3} y Meteorito {meteorito:P3} por ítem (0,1%)");
    var real = RarityCatalog.GatheringChances.ToDictionary(c => c.Rarity, c => c.Percent);
    Check(BoxLootRoller.GatherWeights.Where(w => w.Rarity != "Mítico").All(w => Math.Abs(w.Weight / 10.0 - real[w.Rarity]) < 0.0001), "Común, Raro, Épico y Legendario: EXACTAMENTE las de RarityCatalog (solo cambia el Mítico)");

    // 3) Zonas: nunca drops de una zona que el jugador no desbloqueó ni mayor que la de la caja
    var soloZona = Box(1000, 1000, 5, Zone(1));
    int maxSeen = 0;
    for (int i = 0; i < 200; i++) foreach (var it in BoxLootRoller.Roll(soloZona, new BoxRollContext(1, gatherPool, zonePool), rng).Items.Where(x => x.Name.Contains(" z"))) maxSeen = Math.Max(maxSeen, int.Parse(it.Name[^1].ToString()));
    Check(maxSeen == 1, $"jugador con la Zona 1 desbloqueada y una caja de Zona 5: solo salen drops de la Zona 1 (máxima vista: {maxSeen})");
    int cap = 0;
    foreach (var it in BoxLootRoller.Roll(Box(5000, 5000, 2, Zone(1)), new BoxRollContext(5, gatherPool, zonePool), rng).Items.Where(x => x.Name.Contains(" z"))) cap = Math.Max(cap, int.Parse(it.Name[^1].ToString()));
    Check(cap == 2, $"jugador de Zona 5 con una caja de Zona 2: solo drops hasta la Zona 2 (máxima vista: {cap})");
    var allZones = BoxLootRoller.Roll(Box(20000, 20000, 5, Zone(1)), new BoxRollContext(5, gatherPool, zonePool), rng);
    var zoneDrops = allZones.Items.Where(x => x.Name.Contains(" z")).ToList();
    double travelShare = zoneDrops.Where(x => x.Name.StartsWith("Travel")).Sum(x => x.Quantity) / (double)zoneDrops.Sum(x => x.Quantity);
    Check(zoneDrops.Select(x => x.Name[^1]).Distinct().Count() == 5 && Math.Abs(travelShare - 0.25) < 0.02, $"con todo desbloqueado salen las 5 zonas y el travel pesa 2 de 8 como en la realidad ({travelShare:P1})");
    var sinZona = BoxLootRoller.Roll(Box(50, 50, 3, Zone(1)), new BoxRollContext(3, gatherPool, []), rng);
    Check(Units(sinZona) == 50, "si no hay drops de zona cargados, esas tiradas caen en recolección (no se pierde ningún ítem de los prometidos)");

    // 4) El oro es un bono raro y no cuenta como ítem
    int golds = 0, goldMin = int.MaxValue, goldMax = 0;
    for (int i = 0; i < 100000; i++) { var r = BoxLootRoller.Roll(Box(1, 1, 1, Gather(1), Gold(30, 100, 300)), ctx, rng); if (r.Gold > 0) { golds++; goldMin = Math.Min(goldMin, r.Gold); goldMax = Math.Max(goldMax, r.Gold); } }
    Check(golds is > 2700 and < 3300 && goldMin >= 100 && goldMax <= 300, $"oro: {golds / 1000.0:F1}% de las aperturas (30 por mil), de {goldMin} a {goldMax}");
    bool threw = false; try { BoxLootRoller.Roll(Box(1, 5, 1, Gold(30, 1, 2)), ctx, rng); } catch (InvalidOperationException) { threw = true; }
    Check(threw, "una caja sin nada que sortear (solo oro) avisa en vez de devolver vacío");
}


Console.WriteLine("\n== v0.8.0 · Tarea 3: las cajas contra la base, la tienda y la zona desbloqueada ==");
{
    var ordered = ZoneRanking.OrderByDifficulty(zones);
    int Z(int rank) => ordered[rank - 1].ZoneId;
    Check(ZoneRanking.MaxUnlockedRank(ordered, 1, 0) == 1 && ZoneRanking.MaxUnlockedRank(ordered, 99, 0) == 1, "sin ningún jefe derrotado solo está la Zona 1, aunque tengas nivel de sobra");
    Check(ZoneRanking.MaxUnlockedRank(ordered, ordered[1].MinLevel, Z(1)) == 2 && ZoneRanking.MaxUnlockedRank(ordered, ordered[1].MinLevel - 1, Z(1)) == 1, "con el jefe 1 derrotado y el nivel de la Zona 2 se abre la 2 (y con un nivel menos, no)");
    Check(ZoneRanking.MaxUnlockedRank(ordered, 99, Z(4)) == 5 && ZoneRanking.MaxUnlockedRank(ordered, 99, Z(5)) == 5, "la última zona es el tope");

    var boxService = new BotDsRpg.Services.BoxContextService(itemRepo, monsterRepo, zoneRepo, userRepo);
    var boxRepo = new BotDsRpg.Repositories.BoxRepository(factory);
    foreach (var (name, min, max, tier) in new[] { ("Cajón de Pino", 1, 5, 1), ("Baúl de Roble", 5, 10, 2), ("Arcón de Hierro", 10, 25, 3), ("Cofre de Oro", 25, 60, 4), ("Arca del Soberano", 60, 100, 5) })
    {
        var boxItem = (await itemRepo.GetByNameAsync(name))!;
        var def = (await boxRepo.GetDefinitionAsync(boxItem.ItemId))!;
        Check(def.MinItems == min && def.MaxItems == max && def.TierRank == tier && boxItem.BoxMinItems == min && boxItem.BoxMaxItems == max
              && def.Entries.Count(e => e.Kind == LootKind.Gather) == 1 && def.Entries.Count(e => e.Kind == LootKind.ZoneDrop) == 1 && boxItem.SellPrice == 0,
            $"{name}: {min}-{max} ítems, zona {tier}, venta 0, una entrada de recolección y una de drops de zona (el Item también trae el rango)");
    }

    // Un jugador de Zona 1 abre Baúles de Zona 2: nunca le salen drops de otra zona
    const ulong Opener = 999000000000009003UL;
    var events = new BotDsRpg.Services.GameEventService(new BotDsRpg.Repositories.GameEventRepository(factory));
    var combat = new BotDsRpg.Services.CombatSessionService(userRepo);
    await using var dbx = new Npgsql.NpgsqlConnection(conn); await dbx.OpenAsync();
    async Task Clean() => await Dapper.SqlMapper.ExecuteAsync(dbx, "DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id", new { id = (long)Opener });
    await Clean(); await userRepo.GetOrCreateUserAsync(Opener);
    await Dapper.SqlMapper.ExecuteAsync(dbx, "INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 10 FROM items WHERE name = 'Baúl de Roble'", new { id = (long)Opener });
    var zone1Drops = monstersAll.Where(m => m.ZoneId == Z(1) && m.Kind != MonsterKind.Boss).SelectMany(m => m.Monster.DropItemNames).ToHashSet();
    var otherZoneDrops = monstersAll.Where(m => m.ZoneId != Z(1) && m.Kind != MonsterKind.Boss).SelectMany(m => m.Monster.DropItemNames).ToHashSet();
    var opened = await BoxModule.ExecuteOpenAsync(itemRepo, boxRepo, boxService, combat, events, Opener, "Baúl de Roble", 10);
    Check(opened.Embed is not null, "abrir 10 Baúles anda: " + (opened.PlainMessage ?? "(embed)"));
    string lootText = opened.Embed?.Description ?? string.Empty;
    Check(opened.Embed!.Title.Contains("10×") && !otherZoneDrops.Any(n => lootText.Contains(n)) && zone1Drops.Any(n => lootText.Contains(n)),
        "10 Baúles (Zona 2) abiertos por un jugador de Zona 1: hay drops de la Zona 1 y NINGUNO de otra zona");
    Check(opened.Embed!.Footer?.Text?.Contains("entre 5 y 10 ítems") == true, "el mensaje al abrir dice «Cada Baúl de Roble trae entre 5 y 10 ítems»: " + opened.Embed.Footer?.Text);
    int totalUnits = await Dapper.SqlMapper.ExecuteScalarAsync<int>(dbx, "SELECT COALESCE(SUM(quantity),0)::int FROM inventory WHERE discord_id = @id", new { id = (long)Opener });
    Check(totalUnits >= 50 && totalUnits <= 100, $"10 Baúles dieron {totalUnits} ítems en total (entre 10×5 = 50 y 10×10 = 100)");

    // Comprar: una caja de una zona que no desbloqueaste no cobra nada
    await Dapper.SqlMapper.ExecuteAsync(dbx, "UPDATE users SET gold = 200000 WHERE discord_id = @id", new { id = (long)Opener });
    var shopRepo = new BotDsRpg.Repositories.ShopRepository(factory);
    var locked = await ShopModule.ExecuteBuyAsync(userRepo, itemRepo, shopRepo, combat, events, Opener, "Cofre de Oro", 1, boxService);
    int goldAfterLocked = await Dapper.SqlMapper.ExecuteScalarAsync<int>(dbx, "SELECT gold FROM users WHERE discord_id = @id", new { id = (long)Opener });
    var boxBuyCd = await cooldownRepo.GetRemainingAsync(Opener, CooldownCatalog.BoxBuy.CommandName, CooldownCatalog.BoxBuy.Duration);
    Check(locked.PlainMessage is not null && locked.PlainMessage.Contains("🔒") && locked.PlainMessage.Contains("Zona 4") && goldAfterLocked == 200000 && boxBuyCd is null,
        "un Cofre de Oro (Zona 4) con la Zona 1 desbloqueada: rechazado, SIN cobrar y sin gastar el cooldown de compra");
    var okBuy = await ShopModule.ExecuteBuyAsync(userRepo, itemRepo, shopRepo, combat, events, Opener, "Cajón de Pino", 1, boxService);
    int goldAfterBuy = await Dapper.SqlMapper.ExecuteScalarAsync<int>(dbx, "SELECT gold FROM users WHERE discord_id = @id", new { id = (long)Opener });
    Check(okBuy.Embed is not null && goldAfterBuy == 200000 - 2300, $"el Cajón de Pino (Zona 1) sí se compra: cuesta 2.300 (quedan {goldAfterBuy})");

    // Las pantallas
    var shopItems = await ShopModule.LoadShopItemsAsync(itemRepo);
    var view = ShopModule.BuildViewEmbed(shopItems, null, null, 5000, 1);
    string boxesText = view.Fields.Single(f => f.Name == "Cajas").Value;
    Check(boxesText.Contains("entre 1 y 5 ítems · 2.300 oro") && boxesText.Contains("🔒 Zona 2 · entre 5 y 10 ítems · 14.000 oro") && boxesText.Contains("🔒 Zona 4 · entre 25 y 60 ítems · 120.000 oro") && !boxesText.Contains("Arca"),
        "/shop view: «entre 1 y 5 ítems · 2.300 oro», las de zonas cerradas con 🔒 y el Arca no figura");
    var viewAll = ShopModule.BuildViewEmbed(shopItems, null, null, 5000, 5);
    Check(!viewAll.Fields.Single(f => f.Name == "Cajas").Value.Contains("🔒") && view.Length < 6000, "con todas las zonas abiertas no hay candados, y el embed entra en los límites");
    var buyChoices = ItemChoices.ForBuy(shopItems, 5000, "", null, 1);
    Check(buyChoices.Any(c => c.Name.StartsWith("Cajón de Pino") && c.Name.Contains("entre 1 y 5 ítems") && c.Name.Contains("2.300 oro")) && buyChoices.Any(c => c.Name.StartsWith("Baúl de Roble") && c.Name.Contains("🔒")) && buyChoices.All(c => c.Name.Length <= 100),
        "la lista de /shop buy dice el rango y marca con 🔒 las cerradas: " + buyChoices.FirstOrDefault(c => c.Name.StartsWith("Baúl"))?.Name);
    var ownedBoxes = await inventoryRepo.GetOwnedByTypeAsync(Opener, "Caja");
    var openChoices = ItemChoices.ForOwnedBoxes(ownedBoxes, "");
    Check(openChoices.Count > 0 && openChoices.All(c => c.Name.Contains("entre ") && c.Name.Length <= 100), "la lista de /open dice cuántos ítems trae cada caja: " + openChoices.FirstOrDefault()?.Name);

    var buffRepo = new BotDsRpg.Repositories.BuffRepository(factory);
    var scene = await TabernaModule.BuildSceneAsync(userRepo, itemRepo, inventoryRepo, buffRepo, Opener, null, boxService);
    var boxMenu = scene.Components!.Components.OfType<Discord.ActionRowComponent>().SelectMany(r => r.Components).OfType<Discord.SelectMenuComponent>().Single(m => m.CustomId.StartsWith("taberna_box"));
    Check(boxMenu.Options.Count == 4 && boxMenu.Options.All(o => o.Description.Length <= 100) && boxMenu.Options.Any(o => o.Description.Contains("🔒")),
        "la taberna: 4 cajas en el menú, con el rango y el candado, dentro de los 100 caracteres");
    await Clean();
}

Console.WriteLine("\n== v0.8.0 · revisión: /shop sellall no destruye lo que no se puede vender (las cajas valen 0) ==");
{
    const ulong Seller = 999000000000009004UL;
    var shopRepoS = new BotDsRpg.Repositories.ShopRepository(factory);
    var eventsS = new BotDsRpg.Services.GameEventService(new BotDsRpg.Repositories.GameEventRepository(factory));
    await using var dbs = new Npgsql.NpgsqlConnection(conn); await dbs.OpenAsync();
    async Task CleanS() => await Dapper.SqlMapper.ExecuteAsync(dbs, "DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id", new { id = (long)Seller });
    async Task GiveS(string name, int q) => await Dapper.SqlMapper.ExecuteAsync(dbs, "INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)Seller, n = name, q });
    async Task<int> HaveS(string name) => await Dapper.SqlMapper.ExecuteScalarAsync<int>(dbs, "SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)Seller, n = name });
    async Task<int> GoldS() => await Dapper.SqlMapper.ExecuteScalarAsync<int>(dbs, "SELECT gold FROM users WHERE discord_id = @id", new { id = (long)Seller });
    int hierroPrice = (await itemRepo.GetByNameAsync("Hierro"))!.SellPrice;

    await CleanS(); await userRepo.GetOrCreateUserAsync(Seller);
    await Dapper.SqlMapper.ExecuteAsync(dbs, "UPDATE users SET gold = 0 WHERE discord_id = @id", new { id = (long)Seller });
    await GiveS("Hierro", 3); await GiveS("Cajón de Pino", 2); await GiveS("Arca del Soberano", 1);
    var sold = await ShopModule.ExecuteSellAllAsync(shopRepoS, eventsS, Seller);
    Check(sold.Embed is not null && await HaveS("Hierro") == 0 && await GoldS() == 3 * hierroPrice, $"vender todo: el Hierro se vende (3 × {hierroPrice} = {await GoldS()} de oro)");
    Check(await HaveS("Cajón de Pino") == 2 && await HaveS("Arca del Soberano") == 1, $"y las cajas NO se tocan (Cajón ×{await HaveS("Cajón de Pino")}, Arca ×{await HaveS("Arca del Soberano")}): valen 0 al vender, no se regalan");

    int goldBefore = await GoldS();
    var onlyBoxes = await ShopModule.ExecuteSellAllAsync(shopRepoS, eventsS, Seller);
    Check(onlyBoxes.PlainMessage is not null && await HaveS("Cajón de Pino") == 2 && await GoldS() == goldBefore, "con la mochila de solo cajas no vende nada, no cobra y no borra nada: " + onlyBoxes.PlainMessage);
    await CleanS();
}
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
