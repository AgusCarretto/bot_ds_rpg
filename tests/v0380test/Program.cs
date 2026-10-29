using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.38.0 (tres pedidos del dueño, 2026-10-29): (1) el orden de los ingredientes de las recetas: los drops primero por zona (5, 4, 3, 2, 1) y después los materiales de recolección del Mítico al Común;
// (2) «vender todo» no vende lo que se conserva en el Fuego Nuevo (huevos y comida de mascotas y de dragón) y avisa; (3) la Corteza y el Meteorito (los Míticos de recolección) no salen de las cajas, salvo el
// Brasero del Fogón. Además los amuletos piden sus dos drops ×4. Contra la base LOCAL (usuario descartable).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000099901UL;
Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var shop = new ShopRepository(factory);
var items = new ItemRepository(factory);
var events = new RecordingEvents();

async Task Clean()
{
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
}

await Clean();

Console.WriteLine("== 1. El orden de los ingredientes (pura) ==");
IngredientSortKey K(string type, string rarity, int? zone, string name) => new(type, rarity, zone, name);
var sample = new List<IngredientSortKey>
{
    K("Madera", "Común", null, "Madera de Pino"), K("Material", "Raro", 2, "Garra"), K("Mineral", "Mítico", null, "Meteorito"), K("Material", "Mítico", 5, "Corazón"),
    K("Mineral", "Raro", null, "Hierro"), K("Material", "Común", 1, "Pluma"), K("Madera", "Legendario", null, "Ébano"), K("Material", "Épico", 3, "Yunque"), K("Material", "Legendario", 4, "Núcleo"),
};
var sorted = IngredientOrder.Sort(sample, k => k).Select(k => k.Name).ToList();
Check(sorted.SequenceEqual(["Corazón", "Núcleo", "Yunque", "Garra", "Pluma", "Meteorito", "Ébano", "Hierro", "Madera de Pino"]),
    $"drops de la zona 5 a la 1 y después Mítico → Legendario → Raro → Común ({string.Join(", ", sorted)})");
var tie = IngredientOrder.Sort([K("Material", "Raro", 2, "Zeta"), K("Material", "Raro", 2, "Alfa"), K("Mineral", "Raro", null, "Hierro"), K("Madera", "Raro", null, "Roble")], k => k).Select(k => k.Name).ToList();
Check(tie.SequenceEqual(["Alfa", "Zeta", "Hierro", "Roble"]), "dentro de lo igual, por nombre");

Console.WriteLine("\n== 2. El orden en las recetas reales ==");
var recipes = await new RecipeRepository(factory).GetAllAsync();
bool allOrdered = true; string firstBad = "";
foreach (var r in recipes)
{
    // Se reconstruye la clave de cada ingrediente desde la base (tipo, rareza y zona del monstruo que lo suelta) y se comprueba que la lista ya viene en ese orden.
    var keys = new List<IngredientSortKey>();
    foreach (var ing in r.Ingredients)
    {
        var row = await db.QuerySingleAsync<(string Type, string Rarity, int? Zone)>(
            "SELECT i.type, i.rarity, (SELECT MAX(m.zone_id) FROM monster_drops md JOIN monsters m ON m.monster_id = md.monster_id WHERE md.item_id = i.item_id AND NOT m.is_boss) FROM items i WHERE i.item_id = @id", new { id = ing.ItemId });
        keys.Add(new IngredientSortKey(row.Type, row.Rarity, row.Zone, ing.ItemName));
    }

    if (!keys.SequenceEqual(IngredientOrder.Sort(keys, k => k)))
    {
        allOrdered = false; firstBad = r.ResultItem.Name;
    }
}

Check(allOrdered, $"las {recipes.Count} recetas del catálogo traen sus ingredientes en ese orden {(allOrdered ? "" : $"(la primera que no: {firstBad})")}");
List<string> NamesOf(string result) => recipes.First(r => r.ResultItem.Name == result).Ingredients.Select(i => i.ItemName).ToList();
Check(NamesOf("Trinche del Asador Eterno").Take(5).SequenceEqual(["Corazón de Titán", "Núcleo de Magma", "Yunque Fragmentado", "Garra de Puma Cenizo", "Colmillo de Jabalí"])
    && NamesOf("Trinche del Asador Eterno").Skip(5).First() == "Corteza del Árbol de Vida" && NamesOf("Trinche del Asador Eterno").Last() == "Madera de Pino",
    "el Trinche del Fogón: los cinco drops de la zona 5 a la 1, después la Corteza (Mítico) y al final la Madera de Pino (Común)");
Check(NamesOf("Brasa del Fogón Eterno").Take(5).SequenceEqual(["Fragmento de Alma", "Escama Ígnea", "Gema en Bruto", "Esencia Espectral", "Pluma de Ñandú"]) && NamesOf("Brasa del Fogón Eterno").Skip(5).First() == "Fragmento de Meteorito",
    "la Brasa del Fogón: de la zona 5 a la 1 y después el Meteorito");
Check(NamesOf("Talismán de Ceniza Bendita").SequenceEqual(["Esencia Espectral", "Garra de Puma Cenizo", "Oro Puro"]) && NamesOf("Hacha de Hierro MK3").First() == "Garra de Puma Cenizo", "un amuleto y un arma de la Zona 2: el drop primero y la recolección después");
Check(NamesOf("Hacha de Escoria Templada").First() == "Madera de Ébano", "una herramienta (solo recolección): del más raro al más común");

Console.WriteLine("\n== 3. Los amuletos piden sus dos drops ×4 ==");
var amulets = recipes.Where(r => r.ResultItem.Type == "Amulet" && r.ZoneId is >= 1 and <= 5).ToList();
bool fourEach = amulets.Count == 5 && amulets.All(a => a.Ingredients.Where(i => i.ItemName is not ("Piedra" or "Oro Puro" or "Gema de Zafiro")).Count(i => i.Quantity == 4) == 2);
Check(fourEach, "los 5 amuletos de las zonas 1 a 5 piden dos drops de cacería ×4 (8 en total; las armas, 4)");

Console.WriteLine("\n== 4. «Vender todo» no vende lo que se conserva en el Fuego Nuevo ==");
await users.GetOrCreateUserAsync(U);
await db.ExecuteAsync("UPDATE users SET gold = 0 WHERE discord_id = @id", new { id = (long)U });
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, q = qty, n = name });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
string egg = await db.ExecuteScalarAsync<string>("SELECT name FROM items WHERE type = 'Huevo' ORDER BY item_id LIMIT 1") ?? "?";
await Give("Mate Amargo", 3); await Give("Madera de Pino", 5); await Give("Comida para Mascotas", 4); await Give("Comida de Dragón", 2); await Give(egg, 1); await Give("Cajón de Pino", 2);
int expected = await db.ExecuteScalarAsync<int>("SELECT (SELECT sell_price FROM items WHERE name = 'Mate Amargo') * 3 + (SELECT sell_price FROM items WHERE name = 'Madera de Pino') * 5");
var sold = await ShopModule.ExecuteSellAllAsync(shop, events, U);
Check(sold.Embed is not null && await Have("Mate Amargo") == 0 && await Have("Madera de Pino") == 0 && await Have("Cajón de Pino") == 2, "vende la comida y la madera; las cajas se quedan como siempre");
Check(await Have("Comida para Mascotas") == 4 && await Have("Comida de Dragón") == 2 && await Have(egg) == 1, "la comida de mascotas, la de dragón y el huevo NO se venden: se conservan en el Fuego Nuevo");
Check(await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U }) == expected, $"el oro es solo el de lo vendido ({expected})");
Check(sold.Embed!.Footer?.Text is { } foot && foot.Contains("6 unidades") && foot.Contains("Fuego Nuevo") && foot.Contains("/shop sell"), $"la pantalla avisa cuántas unidades se quedaron y cómo venderlas una por una ({sold.Embed.Footer?.Text})");
var nothing = await ShopModule.ExecuteSellAllAsync(shop, events, U);
Check(nothing.PlainMessage is not null && nothing.PlainMessage.Contains("Fuego Nuevo") && await Have("Comida de Dragón") == 2, "con solo eso en la mochila, «vender todo» dice que no hay nada para vender (y no toca nada)");
var one = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Comida para Mascotas", 1);
Check((one.Embed is not null || one.PlainMessage is not null) && await Have("Comida para Mascotas") == 3, "venderla a propósito, una por una, sigue andando");
await Give("Mate Amargo", 1); await Give("Comida de Dragón", 1);
using (Loc.Use("en"))
{
    var soldEn = await ShopModule.ExecuteSellAllAsync(shop, events, U);
    Check(soldEn.Embed?.Footer?.Text is { } fe && fe.Contains("units that stay through the Fuego Nuevo") && LeakCheck.NoSpanish(fe.Replace("Fuego Nuevo", " "), []).Ok, $"el aviso en inglés ({soldEn.Embed?.Footer?.Text})");
}

Console.WriteLine("\n== 5. Sin Míticos de recolección en las cajas (menos el Brasero) ==");
var pool = new List<GatherCandidate>
{
    new(1, "Madera de Pino", "Común", null, "Madera"), new(2, "Piedra", "Común", null, "Mineral"), new(3, "Hierro", "Raro", null, "Mineral"),
    new(4, "Corteza del Árbol de Vida", "Mítico", null, "Madera"), new(5, "Fragmento de Meteorito", "Mítico", null, "Mineral"),
};
var ctx = new BoxRollContext(5, pool, []);
BoxDefinition BoxOf(string name, int tier = 5) => new(1, name, 100_000, 100_000, tier, [new BoxLootEntry(LootKind.Gather, null, null, null, null, 1, 1, 1)]);
var rng = new Random(2026);
int mythicElsewhere = 0, mythicBrasero = 0, unitsElsewhere = 0;
foreach (string boxName in new[] { "Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro", "Arca del Soberano", "Cofre de Escoria" })
{
    var res = BoxLootRoller.Roll(BoxOf(boxName), ctx, rng);
    mythicElsewhere += res.Items.Where(i => i.Name is "Corteza del Árbol de Vida" or "Fragmento de Meteorito").Sum(i => i.Quantity);
    unitsElsewhere += res.Items.Sum(i => i.Quantity);
}

var brasero = BoxLootRoller.Roll(BoxOf(BoxLootRoller.MythicGatherBoxName), ctx, rng);
mythicBrasero = brasero.Items.Where(i => i.Name is "Corteza del Árbol de Vida" or "Fragmento de Meteorito").Sum(i => i.Quantity);
Check(mythicElsewhere == 0 && unitsElsewhere == 600_000, $"600.000 tiradas de recolección en las otras seis cajas (hasta el Arca): ni una Corteza ni un Meteorito, y no se pierde ninguna tirada ({mythicElsewhere} Míticos)");
Check(mythicBrasero is > 100 and < 300, $"en el Brasero del Fogón sí caen, a 0,1 % por ítem ({mythicBrasero} en 100.000 tiradas)");

var arcaFixed = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM box_loot bl JOIN items bi ON bi.item_id = bl.box_item_id JOIN items ii ON ii.item_id = bl.item_id WHERE bi.name = 'Arca del Soberano' AND ii.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito')");
var arcaSum = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(bl.weight), 0)::int FROM box_loot bl JOIN items bi ON bi.item_id = bl.box_item_id WHERE bi.name = 'Arca del Soberano' AND bl.kind <> 'gold'");
Check(arcaFixed == 0 && arcaSum == 1000, $"en la base: el Arca del Soberano ya no tiene las dos entradas fijas y sus tiradas siguen sumando {arcaSum}");
string snapshotSql = "SELECT md5(string_agg(box_item_id || kind || COALESCE(item_id, 0)::text || weight, ',' ORDER BY box_item_id, kind, COALESCE(item_id, 0), weight)) FROM box_loot";
string before = await db.ExecuteScalarAsync<string>(snapshotSql) ?? "";
await db.ExecuteAsync(File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database", "remove_mythic_from_boxes.sql")).Replace("\r\n", "\n").Replace("\nBEGIN;\n", "\n").Replace("\nCOMMIT;\n", "\n"));
Check(before == await db.ExecuteScalarAsync<string>(snapshotSql), "la migración remove_mythic_from_boxes.sql se puede correr otra vez sin cambiar nada");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) => Task.CompletedTask;
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
