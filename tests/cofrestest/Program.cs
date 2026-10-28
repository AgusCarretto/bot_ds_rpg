using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.14.0: cofres parejos. zone_boxes (tabla pura y la de la base), las dos cajas nuevas, el cofre de repetición del jefe, el Asador (huevo + premio), la 6.ª mascota (recolección).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var monstersRepo = new MonsterRepository(factory);
var adventure = new AdventureRepository(factory);
var pets = new PetRepository(factory);
var boxesRepo = new BoxRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000014000UL;
async Task Reset(int level = 30, int highest = 0)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET level = @level, max_hp = 600, current_hp = 600, highest_zone_cleared = @highest, gold = 0 WHERE discord_id = @id", new { id = (long)U, level, highest });
}
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });

var ordered = ZoneRanking.OrderByDifficulty((await zonesRepo.GetAllAsync()).ToList());
int lastZoneId = ordered[^1].ZoneId;

Console.WriteLine("== 1. La tabla pura (ZoneBoxTable) ==");
var t = new ZoneBoxTable([
    new(10, 1, ZoneBoxRole.Daily, "A", 100), new(10, 1, ZoneBoxRole.Repeat, "A", 70),
    new(20, 2, ZoneBoxRole.Daily, "B", 100), new(20, 2, ZoneBoxRole.Repeat, "B", 100),
    new(0, 0, ZoneBoxRole.Repeat, "G", 100),
]);
Check(t.MaxRank == 2 && t.ForRank(1, ZoneBoxRole.Daily)!.BoxName == "A" && t.ForRank(2, ZoneBoxRole.Repeat)!.BoxName == "B", "ForRank: por posición");
Check(t.ForRank(0, ZoneBoxRole.Daily)!.BoxName == "A" && t.ForRank(-4, ZoneBoxRole.Daily)!.BoxName == "A" && t.ForRank(9, ZoneBoxRole.Daily)!.BoxName == "B", "una posición fuera de rango se acota (0 → la primera, 9 → la última)");
Check(t.ForRank(1, ZoneBoxRole.Weekly) is null && ZoneBoxTable.Empty.ForRank(1, ZoneBoxRole.Daily) is null, "un rol que no está → null (y la tabla vacía no rompe)");
Check(t.ForZone(10, ZoneBoxRole.Repeat)!.ChancePercent == 70 && t.ForZone(0, ZoneBoxRole.Repeat)!.BoxName == "G" && t.ForZone(99, ZoneBoxRole.Repeat) is null, "ForZone: por id de zona (la puerta es la 0)");
var missing = t.Missing([(10, "Z1"), (20, "Z2")], [(0, "Fogón")]);
Check(missing.Count == 4 * 2 && missing.Contains("Z1:weekly") && missing.Contains("Z2:prize_top") && missing.Contains("Z1:travel") && !missing.Any(m => m.StartsWith("Fogón")), "Missing lista lo que falta (4 roles por zona normal, el del viaje incluido; la puerta solo pide el de repetición)");
Check(CombatRewardCalculator.BossChestChancePercent(true, 0) == 100 && CombatRewardCalculator.BossChestChancePercent(false, 60) == 60 && CombatRewardCalculator.BossChestChancePercent(false, 0) == 0
    && CombatRewardCalculator.BossChestChancePercent(false, 250) == 100 && CombatRewardCalculator.BossChestChancePercent(false, -5) == 0, "chance del cofre: 100 la primera vez; en repetición la de la tabla (acotada a 0..100)");
var rnd = new Random(7);
int Drops(bool first, int chance, int n) { int c = 0; for (int i = 0; i < n; i++) if (CombatRewardCalculator.RollBossReward(20, 100, 100, first, null, chance).DroppedSomething) c++; return c; }
Check(Drops(true, 0, 2000) == 2000 && Drops(false, 0, 2000) == 0 && Math.Abs(Drops(false, 60, 40000) / 40000.0 - 0.60) < 0.01 && Drops(false, 100, 500) == 500, "RollBossReward: primera vez siempre; repetición 0 % nunca, 60 % ≈ 60 %, 100 % siempre");
Check(BossChest.IsFirstClear(null, 3) && BossChest.IsFirstClear(new User { HighestZoneCleared = 2 }, 3) && !BossChest.IsFirstClear(new User { HighestZoneCleared = 3 }, 3) && !BossChest.IsFirstClear(new User { HighestZoneCleared = 5 }, 3), "IsFirstClear (escalera): por highest_zone_cleared");
Check(BossChest.IsFirstClear(new User { GateCleared = false, HighestZoneCleared = 5 }, 0) && !BossChest.IsFirstClear(new User { GateCleared = true }, 0), "IsFirstClear (Fogón): por gate_cleared, que vuelve a false con cada Fuego Nuevo");

Console.WriteLine("\n== 2. La tabla zone_boxes de la base ==");
ZoneBoxTable live;
await using (var c = new NpgsqlConnection(conn)) { await c.OpenAsync(); live = await ZoneBoxQueries.LoadAsync(c, null); }
Check(live.Rows.Count == 31 && live.MaxRank == 5, $"31 filas (5 zonas × 6 roles + el repeat del Fogón) y la posición máxima es 5 ({live.Rows.Count} / {live.MaxRank})");
var svc = new ZoneBoxService(factory, zonesRepo);
Check((await svc.MissingAsync()).Count == 0, "el servicio no encuentra nada faltante (todas las zonas con sus seis roles y la puerta con el de repetición)");
string B(int rank, string role) => live.ForRank(rank, role)!.BoxName;
Check(B(1, "repeat") == "Cajón de Pino" && B(2, "daily") == "Baúl de Roble" && B(3, "prize") == "Arcón de Hierro" && B(3, "prize_top") == "Cofre de Oro" && B(4, "weekly") == "Cofre de Oro" && B(5, "daily") == "Arcón de Hierro",
    "algunas filas puntuales: Z1 repeat Pino, Z2 daily Roble, Z3 prize Arcón y prize_top Cofre de Oro, Z4 weekly Cofre de Oro, Z5 daily Arcón");
Check(live.ForZone(ordered[3].ZoneId, ZoneBoxRole.Repeat)!.ChancePercent == 60 && live.ForZone(0, ZoneBoxRole.Repeat)!.BoxName == "Arcón de Hierro"
    && live.Rows.Where(r => (r.Role != ZoneBoxRole.Repeat || r.Rank != 4) && r.Role != ZoneBoxRole.Travel).All(r => r.ChancePercent == 100), "las únicas chances distintas de 100 son el repeat de la Zona 4 (60 %) y las cajas del viaje; el Fogón repite con Arcón de Hierro");
// v0.36.0: el viaje no suelta material; puede dar la caja chica de la zona con una chance BAJA (Database/remove_travel_drops.sql).
var travelRows = Enumerable.Range(1, 5).Select(r => live.ForRank(r, ZoneBoxRole.Travel)).ToList();
Check(travelRows.All(r => r is not null) && travelRows.Select(r => r!.BoxName).SequenceEqual(["Cajón de Pino", "Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Arcón de Hierro"]) && travelRows.Select(r => r!.ChancePercent).SequenceEqual([8, 15, 6, 2, 3]) && live.ForZone(0, ZoneBoxRole.Travel) is null,
    "las cajas del viaje: Pino 8 % / Pino 15 % / Roble 6 % / Arcón 2 % / Arcón 3 % (la puerta no viaja) y ninguna pasa del 15 %");
// El Cofre de Oro YA NO sale en los premios de las zonas bajas: solo desde la Zona 3 (prize_top) en adelante.
Check(live.Rows.Where(r => r.BoxName == "Cofre de Oro").All(r => r.Rank >= 3) && !live.Rows.Any(r => r.BoxName is "Arca del Soberano" or "Cofre de Escoria" or "Brasero del Fogón"), "el Cofre de Oro solo aparece desde la Zona 3, y el Arca / Escoria / Brasero no se regalan por tabla");

Console.WriteLine("\n== 3. Los premios de misiones y logros siguen la tabla ==");
var daily = MissionRewards.Resolve(new RewardSpec(3, 0, BoxGrant.Daily), 2, 10, live);
var top5 = MissionRewards.Resolve(new RewardSpec(0, 0, BoxGrant.ZoneTierPlusOne), 5, 10, live);
var clampHi = MissionRewards.Resolve(new RewardSpec(0, 0, BoxGrant.Weekly), 9, 10, live);
var clampLo = MissionRewards.Resolve(new RewardSpec(0, 0, BoxGrant.Weekly), 0, 10, live);
Check(daily.BoxName == "Baúl de Roble" && top5.BoxName == "Cofre de Oro" && clampHi.BoxName == "Cofre de Oro" && clampLo.BoxName == "Arcón de Hierro".Replace("Arcón de Hierro", "Baúl de Roble"), "Daily Z2 → Roble; prize_top Z5 → Cofre de Oro; una zona 9 cuenta como la última y la 0 como la primera");
Check(MissionRewards.Resolve(new RewardSpec(1, 0, BoxGrant.Mythic), 1, 1, ZoneBoxTable.Empty).BoxName == MissionRewards.MythicBox && MissionRewards.Resolve(new RewardSpec(5, 5), 1, 1, ZoneBoxTable.Empty).BoxName is null, "el Arca no depende de la tabla y un premio sin caja tampoco");
bool threw = false;
try { MissionRewards.Resolve(new RewardSpec(1, 0, BoxGrant.Daily), 1, 1, ZoneBoxTable.Empty); } catch (InvalidOperationException) { threw = true; }
Check(threw, "una caja de zona sin fila en la tabla es un error de carga de datos: tira (no se paga sin caja)");

Console.WriteLine("\n== 4. Las cajas nuevas y los jefes ==");
var escoria = await itemsRepo.GetByNameAsync("Cofre de Escoria");
var brasero = await itemsRepo.GetByNameAsync("Brasero del Fogón");
var huevoFogon = await itemsRepo.GetByNameAsync("Huevo del Fogón");
Check(escoria is { Type: "Caja", Rarity: "Mítico" } && brasero is { Type: "Caja", Rarity: "Mítico" } && huevoFogon is { Type: "Huevo" } && escoria.BuyPrice == 0 && brasero.BuyPrice == 0 && escoria.SellPrice == 0 && brasero.SellPrice == 0, "existen, son Caja / Huevo, no se compran ni se venden");
var escDef = await boxesRepo.GetDefinitionAsync(escoria!.ItemId);
var braDef = await boxesRepo.GetDefinitionAsync(brasero!.ItemId);
Check(escDef is { MinItems: 35, MaxItems: 65 } && braDef is { MinItems: 25, MaxItems: 45 }, "Escoria entrega entre 35 y 65 ítems; el Brasero entre 25 y 45");
Check(escDef!.Entries.Where(e => e.Kind != LootKind.Gold).Sum(e => e.Weight) == 1000 && braDef!.Entries.Where(e => e.Kind != LootKind.Gold).Sum(e => e.Weight) == 1000, "los pesos de ambas suman 1000 (sin contar el oro, que es un bonus)");
Check(braDef.Entries.Any(e => e.Kind == LootKind.Item && e.ItemName == PetRules.FoodItemName), "el Brasero da Comida para Mascotas (lo persistente: se queda en el Fuego Nuevo)");
Check(!escDef.Entries.Concat(braDef.Entries).Any(e => e.ItemName is "Corteza del Árbol de Vida" or "Fragmento de Meteorito"), "ninguna de las dos da la Corteza / el Meteorito como ítem fijo (solo el Arca)");
var soberano = await monstersRepo.GetBossByZoneAsync(lastZoneId);
var asador = await monstersRepo.GetBossByZoneAsync(FogonRules.GateZoneId);
Check(soberano!.DropItemNames.SequenceEqual(["Cofre de Escoria"]) && asador!.DropItemNames.SequenceEqual(["Brasero del Fogón"]), "el jefe de la Zona 5 suelta el Cofre de Escoria y el Asador el Brasero (uno solo cada uno)");
Check(asador!.GoldBonus == 3000 && asador.XpBonus == 300, $"el Asador paga 3000 de oro y 300 de XP de bono ({asador.GoldBonus}/{asador.XpBonus}): oro que se queda en el reinicio, XP que no sirve (se borra)");
// Abrir de verdad (tirada + pago en una transacción) con un jugador que tiene todo desbloqueado.
var itemsAll = new ItemRepository(factory);
var boxCtx = new BoxContextService(itemsAll, monstersRepo, zonesRepo, users);
await Reset(level: 30, highest: lastZoneId);
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@id, @e, 20), (@id, @b, 20) ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = 20", new { id = (long)U, e = escoria.ItemId, b = brasero.ItemId });
var ctx = await boxCtx.BuildAsync(U);
var rng = new Random(99);
long escUnits = 0, braUnits = 0, escGold = 0, braGold = 0; int escMin = int.MaxValue, escMax = 0, braMin = int.MaxValue, braMax = 0, braFood = 0;
for (int i = 0; i < 400; i++)
{
    var r1 = BoxLootRoller.Roll(escDef, ctx, rng); int u1 = r1.Items.Sum(x => x.Quantity); escUnits += u1; escGold += r1.Gold; escMin = Math.Min(escMin, u1); escMax = Math.Max(escMax, u1);
    var r2 = BoxLootRoller.Roll(braDef, ctx, rng); int u2 = r2.Items.Sum(x => x.Quantity); braUnits += u2; braGold += r2.Gold; braMin = Math.Min(braMin, u2); braMax = Math.Max(braMax, u2);
    braFood += r2.Items.Where(x => x.Name == PetRules.FoodItemName).Sum(x => x.Quantity);
}
Check(escMin >= 35 && escMax <= 65 + 0 && Math.Abs(escUnits / 400.0 - 50) < 3, $"Escoria: {escMin}..{escMax} sorteos y promedio {escUnits / 400.0:F1} (≈ 50 ítems)");
Check(braMin >= 25 && braMax <= 45 * 4 && braFood / 400.0 > 15, $"Brasero: {braMin}..{braMax} unidades y {braFood / 400.0:F1} de Comida para Mascotas por apertura en promedio (2–4 por sorteo, 65 % de los sorteos)");
Check(braGold / 400.0 is > 40000 and < 80000 && escGold / 400.0 < 5000, $"el oro: el Brasero da {braGold / 400.0:F0} por apertura (40–80 mil, siempre), Escoria solo un {escGold / 400.0:F0} de promedio (bonus raro)");
var openedBox = await boxesRepo.OpenAsync(U, brasero.ItemId, BoxLootRoller.Roll(braDef, ctx, rng));
Check(openedBox is not null && await Have("Brasero del Fogón") == 19, "abrir un Brasero de verdad lo gasta (20 → 19) y paga en la misma transacción");

Console.WriteLine("\n== 5. La repetición del jefe: la caja de la zona ==");
// Mismo cálculo que AdventureModule/RaidModule: BossChest.RepeatAsync(zoneBoxService, zona) → (chance, caja).
var rep4 = await BossChest.RepeatAsync(svc, ordered[3].ZoneId);
var rep0 = await BossChest.RepeatAsync(svc, FogonRules.GateZoneId);
var repNone = await BossChest.RepeatAsync(svc, 999);
Check(rep4 == (60, "Arcón de Hierro") && rep0 == (100, "Arcón de Hierro") && repNone == (0, null), "RepeatAsync: Zona 4 → 60 % de Arcón; el Fogón → 100 % de Arcón; una zona sin fila → (0, null) y no rompe");
var repBoxItem = await itemsRepo.GetByNameAsync(rep4.BoxName!);
await Reset(level: 30, highest: lastZoneId);
var z4id = ordered[3].ZoneId;
var v1 = await adventure.ApplyBossVictoryAsync(U, 10, 10, 0, repBoxItem!.ItemId, 1, z4id);
Check(v1.EggGranted is null && await Have("Arcón de Hierro") == 1, "una repetición paga la caja de repetición por la ruta de siempre (ApplyBossVictoryAsync) y no da huevo");

Console.WriteLine("\n== 6. El Asador: huevo del Fogón, una vez, y nunca duplicado ==");
await Reset(level: 30, highest: lastZoneId);
await users.SetInGateAsync(U, true);
var g1 = await adventure.ApplyBossVictoryAsync(U, 3000, 300, 0, brasero.ItemId, 1, FogonRules.GateZoneId);
Check(g1.Gate == GateEvent.Cleared && g1.EggGranted is { Name: "Chispa del Asador" } && await Have("Huevo del Fogón") == 1 && await Have("Brasero del Fogón") == 1, "la primera vez: el Brasero y el huevo del Fogón (la sexta mascota)");
await users.SetInGateAsync(U, true);
var g2 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, FogonRules.GateZoneId);
Check(g2.EggGranted is null && await Have("Huevo del Fogón") == 1, "la segunda victoria de la MISMA vuelta no da otro huevo (gate_cleared ya estaba)");
// Un Fuego Nuevo deja gate_cleared en false pero conserva el huevo sin abrir (Huevo es un tipo que se queda): tampoco duplica.
await db.ExecuteAsync("UPDATE users SET gate_cleared = false, in_gate = true WHERE discord_id = @id", new { id = (long)U });
var g3 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, FogonRules.GateZoneId);
Check(g3.EggGranted is null && await Have("Huevo del Fogón") == 1, "en la vuelta siguiente, con el huevo todavía sin abrir, no le da un segundo");
var hatch = await pets.HatchAsync(U, huevoFogon!.ItemId);
Check(hatch.Status == HatchStatus.Ok && hatch.Species?.Name == "Chispa del Asador" && hatch.Species.BonusKind == "gather" && hatch.Species.ZoneId == 0 && await Have("Huevo del Fogón") == 0, "el huevo se abre y nace la Chispa del Asador (zona 0, bono de recolección)");
await db.ExecuteAsync("UPDATE users SET gate_cleared = false, in_gate = true WHERE discord_id = @id", new { id = (long)U });
var g4 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, FogonRules.GateZoneId);
Check(g4.EggGranted is null && await Have("Huevo del Fogón") == 0, "con la mascota ya nacida, otra vuelta tampoco da huevo");
// Una carrera: 6 victorias del Asador a la vez de alguien que nunca lo venció → exactamente UN huevo.
await Reset(level: 30, highest: lastZoneId);
await users.SetInGateAsync(U, true);
var race = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, FogonRules.GateZoneId)));
Check(race.Count(r => r.EggGranted is not null) == 1 && await Have("Huevo del Fogón") == 1, "6 victorias a la vez: se entrega exactamente un huevo");
// Las zonas de la escalera siguen igual: primera vez da el huevo de su zona, la segunda no.
await Reset(level: 30, highest: 0);
var l1 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, ordered[0].ZoneId);
var l2 = await adventure.ApplyBossVictoryAsync(U, 1, 1, 0, null, 1, ordered[0].ZoneId);
Check(l1.EggGranted is { ZoneId: > 0 } && l2.EggGranted is null, "la escalera no cambió: la primera vez de la Zona 1 da su huevo y la segunda no");

Console.WriteLine("\n== 7. La sexta mascota: recolección ==");
var species = await pets.GetSpeciesAsync();
Check(species.Count == 6 && species.Select(s => s.Name).Last() == "Chispa del Asador" && species.Select(s => s.EggItemId).Distinct().Count() == 6, "hay 6 especies y 6 huevos distintos; la última es la Chispa del Asador");
var chispa = species.Single(s => s.Name == "Chispa del Asador");
Check(chispa is { BonusKind: "gather", MaxBonusPercent: 10.0, ZoneId: 0, Emoji: "🔥" }, "Chispa: gather, tope 10 %, zona 0");
var pAll = new[] { new OwnedPet(chispa, 25, null) };
var tot = PetRules.Total(pAll);
Check(tot.GatherPercent == 10 && tot.GoldPercent == 0 && !tot.IsNone && PetRules.Total([new OwnedPet(chispa, 0, null)]).GatherPercent == 1.0, "Total suma la recolección (nivel 10 = 10 %, nivel 1 = 1 %)");
var pb = new PlayerBonuses(tot);
Check(Math.Abs(pb.ChopMultiplier - 1.10) < 1e-9 && Math.Abs(pb.MineMultiplier - 1.10) < 1e-9 && Math.Abs(new PlayerBonuses(tot.Scaled(1.1)).ChopMultiplier - 1.11) < 1e-9, "/chop y /mine rinden ×1,10; con la bendición Manada (×1,1) ×1,11");
Check(new PlayerBonuses(PetBonuses.None).ChopMultiplier == 1.0 && PlayerBonuses.None.IsNone && !pb.IsNone, "sin la mascota no cambia nada");
Check(PetRules.KindName("gather") == "recolección" && PetModule.BonusSummary(tot).Contains("recolección") && PetModule.BonusSummary(PetBonuses.None) == string.Empty, "los textos nombran el bono");
// Y contra la base: la mascota nacida cuenta en GetBonusesAsync.
await Reset(level: 30, highest: lastZoneId);
await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points) VALUES (@id, @s, 25)", new { id = (long)U, s = chispa.SpeciesId });
Check((await pets.GetBonusesAsync(U)).GatherPercent == 10, "GetBonusesAsync lee la mascota del Fogón de la base");

var ownedMix = new[] { new OwnedPet(chispa, 0, null), new OwnedPet(species[0], 0, null) };
Check(PetChoices.ForFeed(ownedMix, DateTime.UtcNow, "").Select(c => c.Value.ToString()).SequenceEqual([species[0].Name, "Chispa del Asador"]), "la lista de /pet feed pone a la Chispa (zona 0) DESPUÉS de las de la escalera, no primera por su id");

Console.WriteLine("\n== 8. Pantallas ==");
var drops = await DropsModule.BuildDropsMessageAsync(users, zonesRepo, monstersRepo, itemsRepo, U, svc);
string z4Text = string.Join("\n", drops.Embeds[3].Fields.Select(f => f.Name + "\n" + f.Value));
Check(drops.Embeds.Length == 5 && z4Text.Contains("100% la 1.ª vez") && z4Text.Contains("60%") && z4Text.Contains("Arcón de Hierro") && !z4Text.Contains("% después"), "/drops: el jefe de la Zona 4 dice 100 % la primera vez y 60 % de Arcón de Hierro después");
Check(drops.Embeds.Sum(e => e.Length) < 6000, $"los embeds de /drops siguen bajo los 6000 caracteres ({drops.Embeds.Sum(e => e.Length)})");
var dropsOld = await DropsModule.BuildDropsMessageAsync(users, zonesRepo, monstersRepo, itemsRepo, U);
Check(!string.Join("\n", dropsOld.Embeds[3].Fields.Select(f => f.Value)).Contains("Si ya lo vencías"), "sin el servicio de cajas, /drops no inventa la caja de repetición");
var pets6 = PetModule.BuildViewEmbed(new PetModule.PetViewData(species, [new OwnedPet(chispa, 4, null)], 0, new HashSet<string>(), DateTime.UtcNow));
Check(pets6.Length < 6000 && string.Join(" ", pets6.Fields.Select(f => f.Name + f.Value)).Contains("Chispa del Asador"), $"/pet view con 6 especies nombra a la Chispa y cabe en un embed ({pets6.Length} caracteres)");
var helpPets = HelpTopics.Find("pets");
var helpBoss = HelpTopics.Find("boss");
Check(helpPets is not null && helpBoss is not null, "los temas de ayuda pets y boss existen");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine($"\n{(fails == 0 ? "TODO OK" : fails + " FALLAS")}");
return fails == 0 ? 0 : 1;
