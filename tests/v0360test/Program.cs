using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.36.0: el /travel deja de soltar un material y da oro, EXP y una chance de CAJA de la zona (zone_boxes, rol "travel"); las 25 recetas de armas de clase y amuletos pierden el ingrediente de viaje (de 4 tipos a 3)
// y piden más drops de cacería para que el tiempo no cambie. El dueño (2026-10-28): «si sacamos el drop solo del travel… que el travel sea esa suma de xp y gold con probabilidad de sacar una caja de la zona»;
// «sumale algún drop a cada uno». Lo que se prueba, contra la base LOCAL (usuarios descartables): el estado final de los datos (monstruos de viaje sin drop, 10 drops, recetas), la migración
// Database/remove_travel_drops.sql con un drop de viaje fabricado (canje a la mochila, recetas, borrado) y que correrla otra vez no cambia nada, la recompensa del viaje (chance de caja con y sin bonus), la lectura
// de la fila de zone_boxes (y que una falla nunca rompe la victoria), un viaje ganado de punta a punta (la caja cae en la mochila y la pantalla lo dice), /drops y el ritmo de las recetas.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000099601UL, U2 = 999000000000099602UL;
Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var adventure = new AdventureRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var events = new RecordingEvents();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

const string FakeDrop = "Prueba Drop de Viaje";
async Task Clean()
{
    foreach (var id in new[] { U, U2 })
    {
        await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }

    // Si una corrida anterior se cortó con el drop fabricado puesto, la migración misma lo limpia; por las dudas:
    await db.ExecuteAsync("DELETE FROM items WHERE name = @n", new { n = FakeDrop });
}

await Clean();

Console.WriteLine("== 1. El estado final de los datos ==");
var travelMonsters = (await db.QueryAsync<(string Name, int ZoneId, int Drops)>(
    "SELECT m.name, m.zone_id, (SELECT count(*) FROM monster_drops d WHERE d.monster_id = m.monster_id)::int FROM monsters m WHERE m.is_travel AND m.zone_id > 0 ORDER BY m.zone_id")).ToList();
Check(travelMonsters.Count == 5 && travelMonsters.All(t => t.Drops == 0), $"los 5 monstruos de viaje existen y no sueltan nada ({string.Join(", ", travelMonsters.Select(t => t.Name))})");
var huntDrops = (await db.QueryAsync<(int ZoneId, string Item)>(
    "SELECT m.zone_id, i.name FROM monsters m JOIN monster_drops d ON d.monster_id = m.monster_id JOIN items i ON i.item_id = d.item_id WHERE NOT m.is_boss AND NOT m.is_travel AND m.zone_id > 0")).ToList();
Check(huntDrops.Count == 10 && huntDrops.GroupBy(h => h.ZoneId).Count() == 5 && huntDrops.GroupBy(h => h.ZoneId).All(g => g.Count() == 2), "quedan 10 drops de monstruos comunes: 2 por zona (los de cacería)");
int gone = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM items WHERE name IN ('Cuero Grueso','Ceniza Bendita','Escoria Metálica Densa','Aliento de Fuego Eterno','Ceniza del Abismo')");
Check(gone == 0, "los 5 drops de viaje ya no están en el catálogo");

var recipes = await new RecipeRepository(factory).GetAllAsync();
var gear = recipes.Where(r => r.ResultItem.Type is "Weapon" or "Amulet" && r.ZoneId is >= 1 and <= 5).ToList();
Check(gear.Count == 25 && gear.All(r => r.Ingredients.Count <= 3), $"las 25 armas de clase y amuletos de las zonas 1 a 5 piden 3 tipos de ingrediente como mucho ({gear.Count} recetas, máximo {gear.Max(r => r.Ingredients.Count)})");
var huntNames = huntDrops.Select(h => h.Item).ToHashSet();
int Q(string recipe, string ing) => recipes.First(r => r.ResultItem.Name == recipe).Ingredients.First(i => i.ItemName == ing).Quantity;
Check(Q("Hacha de Hierro MK3", "Garra de Puma Cenizo") == 4 && Q("Colmillo Nocturno", "Garra de Puma Cenizo") == 4 && Q("Arco Élfico Ancestral", "Esencia Espectral") == 4 && Q("Espada del Abismo", "Corazón de Titán") == 4 && Q("Báculo del Árbol de Vida", "Fragmento de Alma") == 4,
    "armas de las zonas 2-5: el drop de cacería de su ruta quedó en ×4 (remove_travel_drops.sql lo subió a ×5 y rebalance_weapon_drops.sql lo bajó 1, a pedido del dueño)");
Check(Q("Talismán de Ceniza Bendita", "Esencia Espectral") == 4 && Q("Talismán de Ceniza Bendita", "Garra de Puma Cenizo") == 4 && Q("Peto de Escoria Templada", "Gema en Bruto") == 4 && Q("Peto de Escoria Templada", "Yunque Fragmentado") == 4
    && Q("Talismán del Volcán", "Escama Ígnea") == 4 && Q("Talismán del Volcán", "Núcleo de Magma") == 4 && Q("Corazón de Titán Engarzado", "Corazón de Titán") == 4 && Q("Corazón de Titán Engarzado", "Fragmento de Alma") == 4,
    "amuletos de las zonas 2-5: los dos drops de cacería ×4 cada uno (remove_travel_drops.sql los subió a ×7 y rebalance_amulet_drops.sql los dejó en ×4: 8 en el amuleto y 4 en el arma)");
Check(Q("Hombreras de Cuero Grueso", "Colmillo de Jabalí") == 4 && Q("Hombreras de Cuero Grueso", "Pluma de Ñandú") == 4 && Q("Espada de Madera", "Colmillo de Jabalí") == 2 && Q("Arco Corto de Sauce", "Pluma de Ñandú") == 3,
    "Zona 1: el amuleto también ×4 y ×4; las armas solo perdieron el cuero (las frena la recolección)");
var fogon = recipes.Where(r => r.ZoneId == 0).ToList();
var fogonHunt = fogon.Select(r => r.Ingredients.Where(i => i.ItemName != "x" && huntNames.Contains(i.ItemName)).ToList()).ToList();
Check(fogon.Count == 2 && fogonHunt.All(l => l.Count == 5 && l.All(i => i.Quantity == 5)) && fogonHunt.SelectMany(l => l.Select(i => i.ItemName)).Distinct().Count() == 10,
    "el equipo del Fogón no se tocó: entre las dos piezas siguen pidiendo los 10 drops de cacería, 5 de cada uno (×5)");
// El ritmo (el modelo de Database/report_recipe_pacing.sql: 1 cacería por minuto, 6 % por pelea, 2 monstruos de cacería por zona → 0,03 por minuto de cada drop).
double perMin = CombatRewardCalculator.HuntDropChancePercent / 100.0 / 2;
double weaponMin = Q("Hacha de Hierro MK3", "Garra de Puma Cenizo") / perMin, amuletMin = Q("Talismán de Ceniza Bendita", "Esencia Espectral") / perMin;
Check(weaponMin is > 125 and < 150 && amuletMin is > 120 and < 150, $"el ritmo: arma ~{weaponMin:0} min de drops (con el drop de viaje eran ~150 y la recolección la frena en ~143) y amuleto ~{amuletMin:0} min de drops (la recolección lo frena en 143-190)");

Console.WriteLine("\n== 2. Las cajas del viaje en zone_boxes ==");
var zoneBoxes = new ZoneBoxService(factory, zonesRepo);
var table = await zoneBoxes.GetAsync();
var rows = Enumerable.Range(1, 5).Select(r => table.ForRank(r, ZoneBoxRole.Travel)).ToList();
Check(rows.All(r => r is not null) && rows.Select(r => (r!.BoxName, r.ChancePercent)).SequenceEqual([("Cajón de Pino", 20), ("Cajón de Pino", 20), ("Baúl de Roble", 20), ("Arcón de Hierro", 20), ("Arcón de Hierro", 20)]),
    "20 % en cada zona (el dueño: «ponele 20 % a cada zona»): Zona 1 y 2 Cajón de Pino, Zona 3 Baúl de Roble, Zona 4 y 5 Arcón de Hierro");
Check(rows.All(r => r!.ChancePercent == 20) && (await zoneBoxes.MissingAsync()).Count == 0 && ZoneBoxRole.All.Contains(ZoneBoxRole.Travel), "todas valen 20 % y no falta ninguna fila");
Check((await TravelBox.ChanceAsync(zoneBoxes, 1)) is (20, "Cajón de Pino") && (await TravelBox.ChanceAsync(zoneBoxes, 5)) is (20, "Arcón de Hierro") && (await TravelBox.ChanceAsync(zoneBoxes, 0)) is (0, null) && (await TravelBox.ChanceAsync(zoneBoxes, 99)) is (0, null),
    "TravelBox.ChanceAsync: la fila de la zona; la puerta y una zona que no existe → (0, nada)");
Check((await TravelBox.ChanceAsync(new ThrowingZoneBoxes(), 1)) is (0, null), "si la lectura falla, la victoria sigue (chance 0) y no tira");

Console.WriteLine("\n== 3. La recompensa del viaje ==");
int N = 200_000;
double rate(int basePct, PlayerBonuses? b) => Enumerable.Range(0, N).Count(_ => CombatRewardCalculator.RollTravelReward(5, 0, 0, b, basePct).DroppedSomething) * 100.0 / N;
double r8 = rate(8, null), r0 = rate(0, null), r15 = rate(15, null), r20 = rate(20, null);
var fn10 = new PlayerBonuses(PetBonuses.None, 10);
double r8fn = rate(8, fn10);
Check(Math.Abs(r8 - 8) < 0.3 && r0 == 0 && Math.Abs(r15 - 15) < 0.4 && Math.Abs(r20 - 20) < 0.4, $"la caja cae con la chance de la fila: 8 % → {r8:0.0} %, 0 % → {r0:0.0} %, 15 % → {r15:0.0} %, 20 % → {r20:0.0} %");
Check(Math.Abs(r8fn - 8 * FuegoNuevoRules.TravelDropMultiplier(10)) < 0.4 && r8fn > r8 * 1.5, $"el Fuego Nuevo 10 multiplica la chance de la caja (×{FuegoNuevoRules.TravelDropMultiplier(10):0.00}): 8 % → {r8fn:0.0} %");
var rw = Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollTravelReward(5)).ToList();
Check(rw.All(x => x.Gold is >= (5 + 10) * 30 and <= (15 + 10) * 30) && rw.All(x => x.Xp > 0) && CombatRewardCalculator.TravelRewardMultiplier == 30, "el oro y la EXP siguen siendo una cacería ×30 (nivel 5: 450 a 750 de oro)");

Console.WriteLine("\n== 4. Un viaje ganado de punta a punta ==");
async Task ResetPlayer(ulong id)
{
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET level = 5, xp = 100, max_hp = 100, current_hp = 100, gold = 0, current_zone_id = 1 WHERE discord_id = @id", new { id = (long)id });
}

CombatState Travel(int? zone) =>
    new("travel", "Toro Bravo", "🐂", 1, 1, 1, [], 0, 0, null, 100, 100, 100, 50, 0, 5, "Guerrero", ClassPassives.For("Guerrero"), TravelZoneId: zone);

async Task<(Embed? Embed, int Boxes)> WinTravel(IZoneBoxService svc, CombatState state)
{
    await ResetPlayer(U);
    var combat = new CombatSessionService(users);
    combat.TryStart(U, state, new FakeTarget());
    var output = new FakeOutput();
    await AdventureModule.ResolveTurnCoreAsync(combat, users, items, adventure, inventory, buffs, events, svc, U, false, false, output);
    int boxes = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @id AND i.type = 'Caja'", new { id = (long)U });
    return (output.Shown.FirstOrDefault().Embed, boxes);
}

try
{
    // Con la chance en 100 % la caja de la Zona 1 (Cajón de Pino) cae sí o sí.
    await db.ExecuteAsync("UPDATE zone_boxes SET chance_percent = 100 WHERE zone_id = 1 AND role = 'travel'");
    var always = new ZoneBoxService(factory, zonesRepo);
    var (embedBox, boxes) = await WinTravel(always, Travel(1));
    var boxField = embedBox?.Fields.FirstOrDefault(f => f.Name.Contains("Cayó una caja"));
    Check(boxes == 1 && boxField is { } bf && bf.Value.Contains("Cajón de Pino") && bf.Value.Contains("/open") && !(embedBox!.Fields.Any(f => f.Name.Contains("Material") || f.Name.Contains("Cofre del jefe"))),
        $"con la chance en 100 %: la caja cae a la mochila y la pantalla dice «¡Cayó una caja!» con su nombre ({boxes} caja)");
    var (embedEn, _) = await WinTravelEnglish(always);
    Check(embedEn?.Fields.Any(f => f.Name == "🎁 A box dropped!" && f.Value.Contains("open it with")) == true, "la misma pantalla en inglés");

    // Sin la zona en el estado (un combate viejo de antes del cambio) o sin la fila: se gana igual, sin caja.
    var (_, boxesNoZone) = await WinTravel(always, Travel(null));
    Check(boxesNoZone == 0, "un viaje sin zona en el estado (combate de antes de la v0.36.0) se gana igual, sin caja");
    await db.ExecuteAsync("DELETE FROM zone_boxes WHERE zone_id = 1 AND role = 'travel'");
    var (embedNoRow, boxesNoRow) = await WinTravel(new ZoneBoxService(factory, zonesRepo), Travel(1));
    Check(embedNoRow is not null && boxesNoRow == 0 && !embedNoRow.Fields.Any(f => f.Name.Contains("caja")), "si la zona no tiene la fila, el viaje se gana igual (oro y EXP) y no cae nada");
}
finally
{
    // Se deja la tabla como estaba: la fila del viaje de la Zona 1 (Pino, 20 %).
    await db.ExecuteAsync("INSERT INTO zone_boxes (zone_id, role, box_item_id, chance_percent) SELECT 1, 'travel', item_id, 20 FROM items WHERE name = 'Cajón de Pino' ON CONFLICT (zone_id, role) DO UPDATE SET chance_percent = 20");
}

async Task<(Embed? Embed, int Boxes)> WinTravelEnglish(IZoneBoxService svc)
{
    using (Loc.Use("en"))
    {
        return await WinTravel(svc, Travel(1));
    }
}

Console.WriteLine("\n== 5. /drops y la ayuda ==");
var z1 = new ZoneBoxRow(1, 1, ZoneBoxRole.Travel, "Cajón de Pino", 20);
var travelItems = new Dictionary<string, DropItemInfo> { ["Cajón de Pino"] = new("Cajón de Pino", "Común", null) };
var zoneMonsters = new[] { new ZoneMonster(1, MonsterKind.Travel, new MonsterTemplate("Toro Bravo", "🐂", 1, 1, 1, 1, [], 0, 0)) };
var blocks = DropsCatalog.BuildZoneBlocks(zoneMonsters, travelItems, null, z1);
Check(blocks.Count == 1 && blocks[0].Title == "🗺️ Viajar (élite) · 20% de caja" && blocks[0].Text.Contains("Toro Bravo") && blocks[0].Text.Contains("Cajón de Pino") && !blocks[0].Text.Contains("└"),
    $"/drops: el bloque del viaje dice la chance de caja y qué caja, sin lista de drops ({blocks.FirstOrDefault()?.Title})");
var blocksNoRow = DropsCatalog.BuildZoneBlocks(zoneMonsters, travelItems);
Check(blocksNoRow.Count == 1 && blocksNoRow[0].Title.Contains("sin material"), "sin la fila el bloque dice «sin material» y no inventa una chance");

Console.WriteLine("\n== 6. La migración Database/remove_travel_drops.sql con un drop de viaje fabricado ==");
{
    // Se fabrica lo que había antes: un drop de viaje que suelta el monstruo de viaje de la Zona 1, que lleva una receta y que una persona tiene en la mochila.
    int zone1Travel = await db.ExecuteScalarAsync<int>("SELECT monster_id FROM monsters WHERE is_travel AND zone_id = 1");
    await db.ExecuteAsync("INSERT INTO items (name, type, rarity, sell_price, buy_price) VALUES (@n, 'Material', 'Común', 2, 2)", new { n = FakeDrop });
    int fakeId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE name = @n", new { n = FakeDrop });
    await db.ExecuteAsync("INSERT INTO monster_drops (monster_id, item_id) VALUES (@m, @i)", new { m = zone1Travel, i = fakeId });
    int amuletRecipe = await db.ExecuteScalarAsync<int>("SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.name = 'Hombreras de Cuero Grueso'");
    int swordRecipe = await db.ExecuteScalarAsync<int>("SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.name = 'Espada de Madera'");
    // La migración pone el amuleto de la Zona 1 en ×5 y ×5 (su valor de entonces): se anotan las cantidades de AHORA para dejarlas como estaban al terminar.
    var origAmulet = (await db.QueryAsync<(int ItemId, int Quantity)>("SELECT item_id, quantity FROM recipe_ingredients WHERE recipe_id = @r", new { r = amuletRecipe })).ToList();
    await db.ExecuteAsync("UPDATE recipe_ingredients SET quantity = 4 WHERE recipe_id = @r AND item_id IN (SELECT item_id FROM items WHERE name IN ('Colmillo de Jabalí', 'Pluma de Ñandú'))", new { r = amuletRecipe });
    await db.ExecuteAsync("INSERT INTO recipe_ingredients (recipe_id, item_id, quantity) VALUES (@r, @i, 2), (@s, @i, 1)", new { r = amuletRecipe, s = swordRecipe, i = fakeId });
    await ResetPlayer(U);
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@u, @i, 7)", new { u = (long)U, i = fakeId });
    int colmillo = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE name = 'Colmillo de Jabalí'");
    int pluma = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE name = 'Pluma de Ñandú'");
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@u, @c, 3)", new { u = (long)U, c = colmillo });

    // La migración, tal cual está en el repo (sin BEGIN/COMMIT: el arnés la corre suelta).
    string sql = File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database", "remove_travel_drops.sql")).Replace("\r\n", "\n");
    sql = sql.Replace("\nBEGIN;\n", "\n").Replace("\nCOMMIT;\n", "\n");
    await db.ExecuteAsync(sql);

    int Have(int itemId) => db.ExecuteScalar<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory WHERE discord_id = @u AND item_id = @i", new { u = (long)U, i = itemId });
    Check(Have(colmillo) == 3 + 7 && Have(pluma) == 7 && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM items WHERE name = @n", new { n = FakeDrop }) == 0,
        "canje: las 7 unidades se convirtieron en 7 de CADA drop de cacería de su zona (3 + 7 Colmillos, 7 Plumas) y el drop fabricado se borró");
    var amuletAfter = (await new RecipeRepository(factory).GetAllAsync()).First(r => r.ResultItem.Name == "Hombreras de Cuero Grueso");
    var swordAfter = (await new RecipeRepository(factory).GetAllAsync()).First(r => r.ResultItem.Name == "Espada de Madera");
    Check(amuletAfter.Ingredients.Count == 3 && amuletAfter.Ingredients.Single(i => i.ItemName == "Colmillo de Jabalí").Quantity == 5 && amuletAfter.Ingredients.Single(i => i.ItemName == "Pluma de Ñandú").Quantity == 5
        && swordAfter.Ingredients.Count == 3 && swordAfter.Ingredients.Single(i => i.ItemName == "Colmillo de Jabalí").Quantity == 2,
        "recetas: el amuleto perdió el ingrediente de viaje y sus dos drops quedaron en ×5; la espada lo perdió y sigue con ×2");
    Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM monster_drops WHERE monster_id = @m", new { m = zone1Travel }) == 0, "el monstruo de viaje quedó sin drop");

    // Correrla otra vez no cambia nada.
    string snapshotSql = "SELECT md5(COALESCE((SELECT string_agg(recipe_id || ':' || item_id || ':' || quantity, ',' ORDER BY recipe_id, item_id) FROM recipe_ingredients), '') || COALESCE((SELECT string_agg(zone_id || role || box_item_id || chance_percent, ',' ORDER BY zone_id, role) FROM zone_boxes), '') || (SELECT count(*) FROM items)::text)";
    string before = await db.ExecuteScalarAsync<string>(snapshotSql) ?? "";
    int unitsBefore = Have(colmillo);
    await db.ExecuteAsync(sql);
    Check(before == await db.ExecuteScalarAsync<string>(snapshotSql) && Have(colmillo) == unitsBefore, "correrla otra vez no toca recetas, cajas, ítems ni mochilas (re-ejecutable)");
    foreach (var (itemId, quantity) in origAmulet)
    {
        await db.ExecuteAsync("UPDATE recipe_ingredients SET quantity = @q WHERE recipe_id = @r AND item_id = @i", new { q = quantity, r = amuletRecipe, i = itemId });
    }
}

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class ThrowingZoneBoxes : IZoneBoxService
{
    public Task<ZoneBoxTable> GetAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
    public Task<IReadOnlyList<string>> MissingAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("simulado");
}

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
    public Task ClearComponentsAsync() => Task.CompletedTask;
}

sealed class FakeOutput : ICombatTurnOutput
{
    public List<string> Notes { get; } = [];
    public List<(Embed Embed, MessageComponent Components)> Shown { get; } = [];
    public Task NoteAsync(string text) { Notes.Add(text); return Task.CompletedTask; }
    public ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous) => new FakeTarget();
    public Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous) { Shown.Add((embed, components)); return Task.CompletedTask; }
}

sealed class RecordingEvents : IGameEvents
{
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) => Task.CompletedTask;
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
