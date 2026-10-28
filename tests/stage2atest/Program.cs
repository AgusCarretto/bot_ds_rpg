using System.Globalization;
using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Etapa 2a: cajas. Contra la base REAL con usuarios descartables (se borran al final).
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var inventory = new InventoryRepository(factory);
var boxes = new BoxRepository(factory);
var shop = new ShopRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var events = new GameEventService(eventsRepo);
var combat = new CombatSessionService(users);
var boxCtx = new BoxContextService(items, new MonsterRepository(factory), new ZoneRepository(factory), users);
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000002001UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id;", new { id = (long)U });
async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task SetGold(int g) => await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)U, g });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task<Dictionary<string, int>> Inv() => (await db.QueryAsync<(string name, int q)>("SELECT i.name, inv.quantity FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id", new { id = (long)U })).ToDictionary(x => x.name, x => x.q);

Console.WriteLine("== 0. Registro de módulos y dependencias ==");
{
    var client = new DiscordSocketClient();
    var interactions = new InteractionService(client.Rest);
    var texts = new CommandService();
    texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
    var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
    try
    {
        var m = await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
        var t = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
        Check(m.Any(x => x.SlashCommands.Any(c => c.Name == "open")), $"/open registrado ({interactions.SlashCommands.Count} comandos de barra)");
        // v0.9.0: ahora también existe /bank open (subcomando del grupo bank): se busca el /open de nivel superior, el de las cajas.
        var abrir = interactions.SlashCommands.First(c => c.Name == "open" && c.Module.SlashGroupName is null);
        Check(abrir.Parameters.Any(p => p.Name == "box" && p.AutocompleteHandler is not null), "/abrir tiene la lista desplegable de cajas (parámetros: " + string.Join(", ", abrir.Parameters.Select(p => p.Name + "=" + (p.AutocompleteHandler is null ? "sin lista" : "con lista"))) + ")");
        Check(t.SelectMany(x => x.Commands).Count(c => c.Name == "open") == 2 && t.SelectMany(x => x.Commands).Any(c => c.Aliases.Contains("abrir")), "aa abrir / aa open registrados (con y sin cantidad)");
        var broken = new List<string>();
        foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
        {
            try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
        }

        Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
    }
    catch (Exception ex) { Check(false, "registro de módulos: " + ex.Message); }
}

Console.WriteLine("\n== 1. Datos de las cajas ==");
var boxNames = new[] { "Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro", "Arca del Soberano" };
var defs = new Dictionary<string, BoxDefinition>();
foreach (var n in boxNames)
{
    var item = await items.GetByNameAsync(n);
    Check(item is { Type: "Caja" }, $"{n}: existe como ítem de tipo Caja ({item?.Rarity}, compra {item?.BuyPrice}, venta {item?.SellPrice})");
    defs[n] = (await boxes.GetDefinitionAsync(item!.ItemId))!;
}

Check(defs.Values.All(d => d.Entries.Count > 0 && d.Entries.All(e => e.Weight > 0 && e.MinQty >= 1 && e.MaxQty >= e.MinQty)), "cada caja tiene botín y todo con pesos y cantidades válidos");
Check(defs["Cajón de Pino"] is { MinItems: 1, MaxItems: 5 } && defs["Baúl de Roble"] is { MinItems: 5, MaxItems: 10 } && defs["Arcón de Hierro"] is { MinItems: 10, MaxItems: 25 }
      && defs["Cofre de Oro"] is { MinItems: 25, MaxItems: 60 } && defs["Arca del Soberano"] is { MinItems: 60, MaxItems: 100 }, "ítems por apertura: 1-5 / 5-10 / 10-25 / 25-60 / 60-100 (los del dueño)");
Check((await items.GetByNameAsync("Arca del Soberano"))!.BuyPrice == 0, "la Arca del Soberano no se vende (precio 0)");
Check((await Task.WhenAll(boxNames.Select(async n => (await items.GetByNameAsync(n))!.SellPrice))).All(p => p == 0), "ninguna caja se puede revender (venta 0: un cofre de jefe no puede ser una fuente de oro)");
// Las entradas FIJAS (item) de las cajas no son drops de zona: esos salen por la tirada zone_drop, que respeta la zona desbloqueada.
var zoneDrops = (await db.QueryAsync<string>("SELECT i.name FROM monster_drops d JOIN items i USING (item_id) WHERE i.type = 'Material'")).ToHashSet();
var leaking = defs.SelectMany(kv => kv.Value.Entries.Where(e => e.Kind == LootKind.Item && zoneDrops.Contains(e.ItemName!)).Select(e => $"{kv.Key} -> {e.ItemName}")).ToList();
Check(leaking.Count == 0, $"ninguna entrada fija de las cajas es un drop de zona ({(leaking.Count == 0 ? "ninguna" : string.Join(", ", leaking))})");
Check(defs.Values.All(d => d.Entries.Count(e => e.Kind == LootKind.Gather) == 1 && d.Entries.Count(e => e.Kind == LootKind.ZoneDrop) == 1 && d.Entries.Where(e => e.Kind != LootKind.Gold).Sum(e => e.Weight) == 1000), "cada caja tiene UNA tirada de recolección y UNA de drops de zona, y sus pesos suman 1000");
var mythicOnly = defs.Where(kv => kv.Key != "Arca del Soberano").SelectMany(kv => kv.Value.Entries).Where(e => e.ItemName is "Corteza del Árbol de Vida" or "Fragmento de Meteorito").ToList();
Check(mythicOnly.Count == 0 && defs["Arca del Soberano"].Entries.Any(e => e.ItemName == "Corteza del Árbol de Vida"), "Corteza / Meteorito (objetivos de largo plazo) solo como entrada fija de la caja Mítica (en las demás, solo por la recolección al 0,1%)");

Console.WriteLine("\n== 2. El sorteo (20.000 aperturas por caja, con el contexto real de un jugador de Zona 5) ==");
await Cleanup();
await users.GetOrCreateUserAsync(U);
// Un jugador con todas las zonas desbloqueadas: nivel de sobra y el jefe 5 derrotado.
await db.ExecuteAsync("UPDATE users SET level = 99, highest_zone_cleared = (SELECT MAX(zone_id) FROM zones) WHERE discord_id = @id", new { id = (long)U });
var ctxAll = await boxCtx.BuildAsync(U);
Check(ctxAll.MaxZoneRank == 5, $"el contexto del jugador de nivel 99 con todos los jefes dice zona {ctxAll.MaxZoneRank}");
foreach (var n in boxNames)
{
    var d = defs[n];
    var rng = new Random(42);
    int N = 20000;
    double goldSum = 0, unitSum = 0;
    var itemCount = new Dictionary<string, long>();
    int lo = int.MaxValue, hi = 0;
    for (int i = 0; i < N; i++)
    {
        var r = BoxLootRoller.Roll(d, ctxAll, rng);
        goldSum += r.Gold;
        int units = r.Items.Sum(it => it.Quantity);
        unitSum += units; lo = Math.Min(lo, units); hi = Math.Max(hi, units);
        foreach (var it in r.Items) itemCount[it.Name] = itemCount.GetValueOrDefault(it.Name) + it.Quantity;
    }

    // Esperado: ítems por apertura = (min+max)/2 y oro = suma de (chance en milésimos / 1000) x cantidad promedio de cada entrada de oro.
    double expUnits = (d.MinItems + d.MaxItems) / 2.0;
    double gotUnits = unitSum / N;
    Check(Math.Abs(gotUnits - expUnits) / expUnits < 0.02 && lo >= d.MinItems && hi <= d.MaxItems, $"{n}: {gotUnits:F1} ítems por apertura (esperado {expUnits:F1}), de {lo} a {hi} (la caja dice {d.MinItems}-{d.MaxItems})");
    double expGold = d.Entries.Where(e => e.Kind == LootKind.Gold).Sum(e => e.Weight / 1000.0 * (e.MinQty + e.MaxQty) / 2.0);
    double gotGold = goldSum / N;
    Check(Math.Abs(gotGold - expGold) / expGold < 0.15, $"  oro de bono por apertura {gotGold:F1} (esperado {expGold:F1}): sale poco y no cuenta como ítem");
    // Frecuencia de un ítem fijo puntual contra su peso.
    // (uno que NO se pueda recolectar: la Gema de Zafiro o la Madera de Ébano también salen por la tirada de recolección)
    var probe = d.Entries.Where(e => e.Kind == LootKind.Item && items.GetByNameAsync(e.ItemName!).Result!.Type is not ("Madera" or "Mineral")).OrderByDescending(e => e.Weight).First();
    double totalW = d.Entries.Where(e => e.Kind != LootKind.Gold).Sum(e => e.Weight);
    double expItem = expUnits * probe.Weight / totalW * (probe.MinQty + probe.MaxQty) / 2.0;
    double gotItem = itemCount.GetValueOrDefault(probe.ItemName!) / (double)N;
    Check(Math.Abs(gotItem - expItem) / expItem < 0.06, $"  {probe.ItemName}: {gotItem:F3} por apertura (esperado {expItem:F3})");
}

Console.WriteLine("\n== 3. Abrir de forma atómica ==");
await Cleanup();
await users.GetOrCreateUserAsync(U);
var ctx = await boxCtx.BuildAsync(U);
await SetGold(1000);
await Give("Cajón de Pino", 3);
var def = defs["Cajón de Pino"];
var pinoId = (await items.GetByNameAsync("Cajón de Pino"))!.ItemId;
var loot = BoxLootRoller.Roll(def, ctx, new Random(7));
var before = await Inv();
var o1 = await boxes.OpenAsync(U, pinoId, loot);
var after = await Inv();
bool lootArrived = loot.Items.All(li => after.GetValueOrDefault(li.Name) == before.GetValueOrDefault(li.Name) - (li.Name == "Cajón de Pino" ? 1 : 0) + li.Quantity)
    || loot.Items.All(li => after.GetValueOrDefault(li.Name) >= li.Quantity);
Check(o1 is not null && o1.GoldAfter == 1000 + loot.Gold && await Gold() == 1000 + loot.Gold, $"abrir 1: oro {1000} -> {await Gold()} (+{loot.Gold}), caja gastada ({before["Cajón de Pino"]} -> {after.GetValueOrDefault("Cajón de Pino")}), ítems: {string.Join(", ", loot.Items.Select(i => $"{i.Quantity}x {i.Name}"))}");
Check(await Have("Cajón de Pino") == 2 || loot.Items.Any(i => i.Name == "Cajón de Pino"), "la caja se descontó");

await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
var none = await boxes.OpenAsync(U, pinoId, loot);
Check(none is null && await Gold() == 1000 + loot.Gold, "sin la caja en el inventario: no abre y NO da nada");

// Concurrencia: 5 cajas, 30 aperturas simultáneas -> exactamente 5 pasan y el oro que se sumó es el de esas 5.
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await SetGold(0);
await Give("Cajón de Pino", 5);
var attempts = Enumerable.Range(0, 30).Select(i => (Loot: BoxLootRoller.Roll(def, ctx, new Random(1000 + i)), Index: i)).ToList();
var outcomes = await Task.WhenAll(attempts.Select(a => Task.Run(async () => (a.Loot, Outcome: await boxes.OpenAsync(U, pinoId, a.Loot)))));
var winners = outcomes.Where(x => x.Outcome is not null).ToList();
int expectedGold = winners.Sum(w => w.Loot.Gold);
Check(winners.Count == 5 && await Gold() == expectedGold && await Have("Cajón de Pino") == winners.Sum(w => w.Loot.Items.Where(i => i.Name == "Cajón de Pino").Sum(i => i.Quantity)),
    $"30 aperturas simultáneas con 5 cajas: pasaron {winners.Count} (esperado 5), oro {await Gold()} = suma del botín de las que pasaron ({expectedGold}), la caja quedó en 0");

Console.WriteLine("\n== 4. El comando /abrir ==");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await SetGold(0);
await Give("Baúl de Roble", 4);
var res = await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Baúl de Roble", 3, new Random(5));
Check(res.Embed is not null && res.Embed.Title.Contains("3×") && res.Embed.Description.Contains("×") && res.Embed.Footer?.Text.Contains("entre 5 y 10 ítems") == true, $"abrir 3: {res.Embed?.Title} · {res.Embed?.Footer?.Text}");
Check(await Have("Baúl de Roble") == 1, "gastó exactamente 3 cajas (quedó 1)");
var stat = await eventsRepo.GetStatsAsync(U);
Check(stat.GetValueOrDefault("box_opened") == 3, $"queda registrado: box_opened = {stat.GetValueOrDefault("box_opened")}");
var res2 = await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Baúl de Roble", 5, new Random(6));
Check(res2.Embed is not null && res2.Embed.Footer?.Text.Contains("solo tenías 1") == true, $"pedir 5 con 1 sola: abre 1 y avisa ({res2.Embed?.Footer?.Text})");
Check((await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Baúl de Roble", 1)).PlainMessage!.Contains("No tenés"), "sin cajas: 'No tenés'");
Check((await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Hierro", 1)).PlainMessage!.Contains("no es una caja"), "un ítem que no es caja: rechazado");
Check((await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Cosa que no existe", 1)).PlainMessage!.Contains("no es una caja"), "nombre inexistente: rechazado");
Check((await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Baúl de Roble", 0)).PlainMessage!.Contains("1 a 10") && (await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Baúl de Roble", 11)).PlainMessage!.Contains("1 a 10"), "cantidad fuera de 1 a 10: rechazada");
// En combate no se abre.
var baseState = new CombatState("hunt", "Jabalí", "🐗", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
await Give("Cajón de Pino", 1);
combat.TryStart(U, baseState, new FakeTarget());
var inCombat = await BoxModule.ExecuteOpenAsync(items, boxes, boxCtx, combat, events, U, "Cajón de Pino", 1);
Check(inCombat.PlainMessage!.Contains("combate") && await Have("Cajón de Pino") == 1, "en pleno combate no se abre (y no gasta la caja)");
combat.TryAdvance(U, combat.Peek(U)!, null, null);

Console.WriteLine("\n== 5. Tienda: comprar y vender cajas ==");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await SetGold(5000);
var buy = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1, boxCtx);
Check(buy.Embed is not null && await Gold() == 5000 - 2300 && await Have("Cajón de Pino") == 1, $"comprar 1 Cajón de Pino (2.300 c/u): oro 5000 -> {await Gold()}, tiene {await Have("Cajón de Pino")}");
// Las cajas se compran de a una y una por hora: el resto de la prueba parte de 2 cajas y 200 de oro, sin cooldown.
await Give("Cajón de Pino", 2); await SetGold(200);
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
Check((await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Arca del Soberano", 1, boxCtx)).PlainMessage!.Contains("no está disponible"), "la Arca del Soberano (premio) NO se puede comprar");
Check((await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1, boxCtx)).PlainMessage!.Contains("No te alcanza"), "sin oro para el Cajón de Pino (1.000): 'No te alcanza'");
await SetGold(500000);
var lockedBuy = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cofre de Oro", 1, boxCtx);
Check(lockedBuy.PlainMessage!.Contains("Zona 4") && lockedBuy.PlainMessage.Contains("🔒") && await Gold() == 500000 && await Have("Cofre de Oro") == 0, "con la Zona 1 desbloqueada, el Cofre de Oro (Zona 4) no se compra aunque sobre el oro, y no se cobra nada");
await SetGold(200);
Check((await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Hierro", 1, boxCtx)).PlainMessage!.Contains("no está disponible"), "un material no se compra");
var sell = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Cajón de Pino", 1);
Check(sell.PlainMessage is not null && sell.PlainMessage.Contains("no se puede vender") && await Gold() == 200 && await Have("Cajón de Pino") == 2, $"una caja NO se puede revender (venta 0): {sell.PlainMessage} · oro {await Gold()}, sigue teniendo {await Have("Cajón de Pino")}");
await Give("Arca del Soberano", 1);
Check((await ShopModule.ExecuteSellAsync(items, shop, events, U, "Arca del Soberano", 1)).PlainMessage!.Contains("no se puede vender") && await Have("Arca del Soberano") == 1, "la Arca del Soberano (premio) NO se puede vender");

var shopItems = await ShopModule.LoadShopItemsAsync(items);
var view = ShopModule.BuildViewEmbed(shopItems);
string viewText = string.Join("\n", view.Fields.Select(f => f.Name + " " + f.Value));
Check(shopItems.Count(i => i.Type == "Caja") == 4 && !viewText.Contains("Arca del Soberano"), $"la tienda lista las 4 cajas y NO la Mítica ({shopItems.Count(i => i.Type == "Caja")} cajas, {shopItems.Count(i => i.Type == "Consumable")} comidas)");
Check(view.Fields.Length == 3 && view.Fields[0].Name == "Comida" && view.Fields[1].Name == "Cajas" && view.Fields[1].Value.Contains("Cajón de Pino") && view.Fields[2].Name == "Mascotas" && view.Fields[2].Value.Contains("Comida para Mascotas"), "tres columnas: la comida, las cajas y (v0.10.0) la comida de las mascotas");

Console.WriteLine("\n== 6. Listas desplegables e inventario ==");
var forBuy = ItemChoices.ForBuy(shopItems, 100, "caj");
Check(forBuy.Any(c => c.Name.Contains("Cajón de Pino") && c.Name.Contains("entre 1 y 5 ítems") && c.Name.Contains("2.300 oro") && c.Name.Contains("te falta oro")), $"/shop buy ofrece cajas con su rango: {forBuy.FirstOrDefault()?.Name}");
Check(!ItemChoices.ForBuy(shopItems, null, "arca").Any(), "y no ofrece la Arca (no se vende)");
var owned = await inventory.GetOwnedByTypeAsync(U, "Caja");
var forBoxes = ItemChoices.ForOwnedBoxes(owned, "");
Check(forBoxes.Count == 2 && forBoxes[0].Value?.ToString() == "Arca del Soberano" && forBoxes[0].Name.Contains("tenés 1") && forBoxes[0].Name.Contains("entre 60 y 100 ítems"), $"/abrir ofrece solo las cajas que tenés, la más rara primero: {string.Join(" | ", forBoxes.Select(c => c.Name))}");
var inv = await GameModule.BuildInventoryEmbedAsync(inventory, U, "prueba");
string invText = string.Join("\n", inv.Fields.Select(f => f.Name + "\n" + f.Value));
Check(inv.Fields.Any(f => f.Name.Contains("Cajas") || f.Value.Contains("**Cajas**")) && invText.Contains("**Cajón de Pino**: 2"), "el inventario tiene la columna 📦 Cajas con lo que tenés");

await Cleanup();
Console.WriteLine($"\nLimpieza: usuarios de prueba restantes = {await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE discord_id = @id", new { id = (long)U })}");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
}
