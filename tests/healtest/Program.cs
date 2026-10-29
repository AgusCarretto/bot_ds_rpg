using System.Globalization;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// Verificación: rendimiento de /chop y /mine, cantidades de recetas, precios de comidas y el desplegable de curar
// (solo travel y boss, una vez por pelea, compartido con /use), contra la base REAL con un usuario descartable.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var monsters = new MonsterRepository(factory);
var items = new ItemRepository(factory);
var users = new UserRepository(factory);
var inventory = new InventoryRepository(factory);
var zoneRepo = new ZoneRepository(factory);
var buffRepo = new BuffRepository(factory);
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== 1. Rendimiento de /chop y /mine ==");
foreach (var (rarity, min, max, mean) in new[] { ("Común", 1, 5, 3.0), ("Raro", 1, 3, 2.0), ("Épico", 1, 2, 1.5), ("Legendario", 1, 1, 1.0), ("Mítico", 1, 1, 1.0) })
{
    var s = Enumerable.Range(0, 100000).Select(_ => GatheringYield.Roll(rarity)).ToList();
    Check(s.Min() == min && s.Max() == max && Math.Abs(s.Average() - mean) < 0.05 && GatheringYield.Average(rarity) == mean,
        $"{rarity}: {s.Min()}-{s.Max()}, promedio {s.Average():F2} (esperado {min}-{max}, {mean})");
}

Check(GatheringYield.Roll("Raro", runMultiplier: 3) is >= 3 and <= 9 && GatheringYield.Roll("Mítico", 2) == 2, "el multiplicador de run escala la cantidad (Raro x3 = 3-9, Mítico x2 = 2)");
Check(GatheringYield.Roll("Rareza inventada") == 1, "rareza desconocida = 1");

Console.WriteLine("\n== 2. Cantidades de las recetas ==");
var recipes = (await new RecipeRepository(factory).GetAllAsync()).Where(r => (r.ZoneId is null or > 0) && !ToolRules.IsToolType(r.ResultItem.Type)).ToList(); // la escalera + la Hoja de Acero Puro (sin zona); el Fogón (zona 0) pide más a propósito, ver fogontest; las herramientas (v0.29.0) son del Carpintero, ver toolstest
var gathered = await items.GetAllByTypeAsync("Madera");
var minerals = await items.GetAllByTypeAsync("Mineral");
var rarityOf = gathered.Concat(minerals).ToDictionary(i => i.Name, i => i.Rarity);
var bad = new List<string>();
foreach (var r in recipes)
{
    foreach (var ing in r.Ingredients.Where(i => rarityOf.ContainsKey(i.ItemName)))
    {
        // Legendarios y Míticos salen de a 1: ninguna receta pide más de 1 de ellos (Ébano, Zafiro, Corteza, Meteorito).
        if (rarityOf[ing.ItemName] is "Legendario" or "Mítico" && ing.Quantity != 1) bad.Add($"{r.ResultItem.Name} <- {ing.Quantity}x {ing.ItemName}");
        if (ing.Quantity < 1) bad.Add($"{r.ResultItem.Name} <- {ing.Quantity}x {ing.ItemName}");
    }
}

Check(bad.Count == 0, $"ninguna receta pide más de 1 Legendario/Mítico de recolección ({(bad.Count == 0 ? "ok" : string.Join("; ", bad))})");
int Qty(string recipe, string ing) => recipes.First(r => r.ResultItem.Name == recipe).Ingredients.First(i => i.ItemName == ing).Quantity;
Check(Qty("Hoja de Acero Puro", "Hierro") == 2 && Qty("Hoja de Acero Puro", "Madera de Pino") == 10 && Qty("Hombreras de Cuero Grueso", "Piedra") == 25, "la Hoja de Acero Puro (sin zona, solo materiales) pide 2 Hierro y 10 Madera de Pino (accesible); el amuleto de la Zona 1, 25 Piedra");
Check(Qty("Hacha de Hierro MK3", "Hierro") == 6 && Qty("Mazo de Escoria", "Hierro") == 6 && Qty("Arco de Caza Mayor", "Madera de Ébano") == 1 && Qty("Báculo del Árbol de Vida", "Madera de Nogal") == 3 && Qty("Báculo del Árbol de Vida", "Madera de Ébano") == 1 && Qty("Espada del Abismo", "Gema de Zafiro") == 1 && Qty("Hacha de Hierro MK3", "Garra de Puma Cenizo") == 4 && Qty("Talismán de Ceniza Bendita", "Esencia Espectral") == 4 && Qty("Espada del Abismo", "Corazón de Titán") == 4 && Qty("Corazón de Titán Engarzado", "Fragmento de Alma") == 4,
    "Zonas 2-5: 6 Hierro en las armas pesadas de clase; Ébano en 1; sin Mítico en la Zona 5 (Nogal x3 / Zafiro); el drop de cacería ×4 en el arma y ×4 de cada uno en el amuleto (v0.36.0: ya no hay drop de viaje)");
Check(recipes.Count == 26 && recipes.All(r => r.Ingredients.Count is >= 2 and <= 4) && recipes.Where(r => r.ResultItem.Type is "Weapon" or "Amulet" && r.ZoneId is >= 1 and <= 5).All(r => r.Ingredients.Count <= 3), $"26 recetas (4 armas de clase y 1 amuleto por zona, más la Hoja sin zona), ninguna con más de 4 tipos de ingrediente y las armas y amuletos de las zonas 1-5 con 3 como mucho (hay {recipes.Count})");

Console.WriteLine("\n== 3. Precios de las comidas ==");
var foods = (await items.GetAllByTypeAsync("Consumable")).OrderBy(i => i.StatValue).ThenBy(i => i.BuyPrice).ToList(); // el Cordero y el Asado Completo curan lo mismo (150) desde la v0.19.2: desempata el precio
var expectedBuy = new Dictionary<string, int> { ["Mate Amargo"] = 8, ["Empanada de Carne"] = 36, ["Asado de Tira"] = 160, ["Cordero Patagónico"] = 300, ["Asado Completo del Domingo en Familia"] = 2100, ["Mate Dulce de la Abuela"] = 2800 };
Check(foods.Count == 6 && foods.All(f => expectedBuy.TryGetValue(f.Name, out var p) && f.BuyPrice == p && f.SellPrice == (int)(p * 0.75)), $"las 6 comidas con los precios de la etapa 2b ({string.Join(", ", foods.Select(f => $"{f.StatValue}HP={f.BuyPrice}"))})");
var perHp = foods.Select(f => (double)f.BuyPrice / f.StatValue).ToList();
Check(perHp.Zip(perHp.Skip(1), (a, b) => b >= a - 0.01).All(x => x), $"el precio por HP sube con la curación ({perHp[0]:F2} -> {perHp[^1]:F2} oro/HP)");
Check(foods.All(f => f.SellPrice < f.BuyPrice), "vender siempre rinde menos que comprar");

Console.WriteLine("\n== 4. Componentes del combate ==");
var opts = CombatHeal.BuildOptions([new("Empanada de Carne", 20, 2), new("Mate Dulce", 300, 1), new("Vacío", 80, 0), new("Nada", 0, 5)]);
Check(opts.Count == 2 && opts[0].Name == "Mate Dulce" && opts[1].Name == "Empanada de Carne", "opciones: las que más curan primero, sin stock 0 ni 0 HP");
Check(CombatHeal.HasMenu("travel") && CombatHeal.HasMenu("boss") && !CombatHeal.HasMenu("hunt") && !CombatHeal.HasMenu("nada"), "el desplegable es de travel y boss, no de hunt");
Check(CombatHeal.IsLimited("hunt") && CombatHeal.IsLimited("travel") && CombatHeal.IsLimited("boss") && !CombatHeal.IsLimited("autohunt") && !CombatHeal.IsLimited("nada"), "el límite de UNA curación por pelea vale en hunt, travel y boss (v0.16.1: hunt también)");

var baseState = new CombatState("travel", "Toro Bravo", "🐂", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
List<ActionRowComponent> Rows(MessageComponent c) => c.Components.OfType<ActionRowComponent>().ToList();
SelectMenuComponent? Menu(MessageComponent c) => Rows(c).SelectMany(r => r.Components).OfType<SelectMenuComponent>().FirstOrDefault();

var travelUi = AdventureModule.BuildCombatButtons(baseState, opts);
var m1 = Menu(travelUi);
Check(Rows(travelUi).Count == 2 && m1 is { IsDisabled: false, CustomId: CombatHeal.MenuCustomId } && m1.Options.Count == 2, $"travel con comida: 2 filas, desplegable activo con {m1?.Options.Count} opciones");
Check(m1!.Options.First().Label == "Mate Dulce" && m1.Options.First().Value == "Mate Dulce" && m1.Options.First().Description == "+300 HP · tenés 1", $"primera opción: {m1.Options.First().Label} — {m1.Options.First().Description}");
var noFood = Menu(AdventureModule.BuildCombatButtons(baseState, []));
Check(noFood is { IsDisabled: true } && noFood.Placeholder.Contains("No tenés comida"), $"travel sin comida: deshabilitado ({noFood?.Placeholder})");
var used = Menu(AdventureModule.BuildCombatButtons(baseState with { HealUsed = true }, opts));
Check(used is { IsDisabled: true } && used.Placeholder.Contains("Ya te curaste"), $"curación usada: deshabilitado ({used?.Placeholder})");
var boss = Menu(AdventureModule.BuildCombatButtons(baseState with { CommandName = "boss" }, opts));
Check(boss is { IsDisabled: false }, "boss: desplegable activo");
var huntUi = AdventureModule.BuildCombatButtons(baseState with { CommandName = "hunt" }, opts);
Check(Menu(huntUi) is null && Rows(huntUi).Count == 1, "hunt: sin desplegable (una sola fila de botones)");
Check(AdventureModule.BuildEncounterEmbed(baseState).Footer?.Text?.Contains("UNA vez") == true && AdventureModule.BuildEncounterEmbed(baseState with { CommandName = "hunt" }).Footer?.Text?.Contains("/use") == true, "el encuentro avisa de la curación: travel/boss con el desplegable y hunt con /use");

Console.WriteLine("\n== 5. Curar de punta a punta: una vez por pelea en travel/boss, compartido con /use ==");
const ulong testId = 999000000000000779UL;
var combat = new CombatSessionService(users);
var starter = new AdventureCombatStarter(new CooldownRepository(factory), new AdventureRepository(factory), users, items, monsters, zoneRepo, buffRepo, new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), new GameEventRepository(factory)), combat, new RaidSessionService());
var target = new FakeTarget();
try
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)testId });
    await users.GetOrCreateUserAsync(testId);
    async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)testId, n = name, q = qty });
    async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)testId, n = name });
    await Give("Empanada de Carne", 3); await Give("Mate Amargo", 3);

    var loaded = await AdventureModule.LoadHealOptionsAsync(inventory, buffRepo, testId, baseState);
    Check(loaded is { Count: 2 } && loaded[0].Name == "Empanada de Carne" && loaded[0].Quantity == 3 && loaded[0].HealHp == 40, $"lee la comida real de la mochila: {string.Join(", ", loaded!.Select(o => $"{o.Name} x{o.Quantity} (+{o.HealHp})"))}");
    Check(await AdventureModule.LoadHealOptionsAsync(inventory, buffRepo, testId, baseState with { CommandName = "hunt" }) is null && await AdventureModule.LoadHealOptionsAsync(inventory, buffRepo, testId, baseState with { HealUsed = true }) is null, "hunt y 'ya curado' no consultan la mochila");

    foreach (var cmd in new[] { "travel", "boss" })
    {
        await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)testId });
        await db.ExecuteAsync("UPDATE users SET current_zone_id = 1, level = 1, current_hp = max_hp WHERE discord_id = @id", new { id = (long)testId });
        var started = await starter.PrepareTravelAsync(testId);
        var wounded = started.State! with { CommandName = cmd, PlayerCurrentHp = 20 };
        Check(combat.TryStart(testId, wounded, target), $"[{cmd}] pelea iniciada con 20/{wounded.PlayerMaxHp} HP");
        int panBefore = await Have("Empanada de Carne");

        var first = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Empanada de Carne");
        var after1 = combat.Peek(testId)!.State;
        Check(first.Embed is not null && await Have("Empanada de Carne") == panBefore - 1 && after1.HealUsed, $"[{cmd}] 1ª curación: gasta 1 Empanada de Carne ({panBefore} -> {await Have("Empanada de Carne")}) y marca HealUsed");
        var lastUi = target.Updates.Last().Components;
        Check(Menu(lastUi) is { IsDisabled: true } && Menu(lastUi)!.Placeholder.Contains("Ya te curaste"), $"[{cmd}] el mensaje del combate queda con el desplegable deshabilitado");

        int mateBefore = await Have("Mate Amargo");
        var second = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Mate Amargo");
        Check(second.PlainMessage?.Contains("Ya te curaste") == true && await Have("Mate Amargo") == mateBefore && combat.Peek(testId)!.State.TurnsElapsed == after1.TurnsElapsed,
            $"[{cmd}] 2ª curación (también con /use escrito): rechazada, NO gasta comida ni turno");
        combat.TryAdvance(testId, combat.Peek(testId)!, null, null);
    }

    // Hunt (v0.16.1): también UNA sola curación por pelea, con /use escrito (no tiene desplegable).
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)testId });
    await Give("Mate Amargo", 3);
    var huntStart = await starter.PrepareHuntAsync(testId);
    var huntState = huntStart.State! with { PlayerCurrentHp = 20, MonsterDamage = 1 };
    Check(combat.TryStart(testId, huntState, target), "[hunt] pelea iniciada con 20 HP y un monstruo que pega 1");
    int huntMateBefore = await Have("Mate Amargo");
    var h1 = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Mate Amargo");
    var hs1 = combat.Peek(testId);
    Check(h1.Embed is not null && hs1 is not null && hs1.State.HealUsed && await Have("Mate Amargo") == huntMateBefore - 1, $"[hunt] 1ª curación con /use: gasta 1 Mate Amargo ({huntMateBefore} -> {await Have("Mate Amargo")}) y marca HealUsed");
    Check(Menu(target.Updates.Last().Components) is null, "[hunt] el mensaje del combate sigue SIN desplegable (la curación de hunt es el /use escrito)");
    int turnsAfterFirst = hs1!.State.TurnsElapsed;
    var h2 = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Mate Amargo");
    Check(h2.PlainMessage?.Contains("Ya te curaste") == true && h2.PlainMessage.Contains("una vez") && await Have("Mate Amargo") == huntMateBefore - 1 && combat.Peek(testId)!.State.TurnsElapsed == turnsAfterFirst,
        "[hunt] 2ª curación: rechazada («solo se puede una vez por combate»), NO gasta comida ni turno");
    var h3 = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Empanada de Carne");
    Check(h3.PlainMessage?.Contains("Ya te curaste") == true, "[hunt] tampoco con OTRA comida: el límite es de la pelea, no de la comida");
    combat.TryAdvance(testId, combat.Peek(testId)!, null, null);
    // La pelea siguiente arranca limpia: otra vez se puede curar UNA vez.
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)testId });
    var huntNext = await starter.PrepareHuntAsync(testId);
    combat.TryStart(testId, huntNext.State! with { PlayerCurrentHp = 20, MonsterDamage = 1 }, target);
    var h4 = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Mate Amargo");
    Check(h4.Embed is not null && combat.Peek(testId)!.State.HealUsed, "[hunt] la pelea siguiente vuelve a permitir una curación (el límite no se arrastra)");
    combat.TryAdvance(testId, combat.Peek(testId)!, null, null);
    // Fuera de combate /use sigue sin límite.
    var outside = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Mate Amargo");
    var outside2 = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffRepo, testId, "Mate Amargo");
    Check(outside.Embed is not null && outside2.Embed is not null, "fuera de combate /use no tiene límite (se come las veces que haga falta)");
    // Avisos del encuentro.
    string footerHunt = AdventureModule.BuildEncounterEmbed(huntState).Footer?.Text ?? "";
    string footerTravel = AdventureModule.BuildEncounterEmbed(baseState).Footer?.Text ?? "";
    Check(footerHunt.Contains("UNA vez") && footerHunt.Contains("/use") && footerTravel.Contains("UNA vez") && footerTravel.Contains("desplegable"), "el encuentro avisa «UNA vez»: en hunt con /use y en travel/boss con el desplegable");

    // Gathering end to end: la cantidad sorteada llega al inventario.
    var gatherRepo = new GatheringRepository(factory);
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)testId });
    var pino = (await items.GetByNameAsync("Madera de Pino"))!;
    int pinoBefore = await Have("Madera de Pino");
    bool applied = await gatherRepo.ApplyGatheringRewardAsync(testId, "chop", TimeSpan.FromMinutes(5), pino.ItemId, 4);
    Check(applied && await Have("Madera de Pino") == pinoBefore + 4, $"/chop con 4 unidades: la mochila pasa de {pinoBefore} a {await Have("Madera de Pino")}");
    var embed = GatheringModule.BuildResultEmbed(CooldownCatalog.Chop, pino, 4);
    Check(embed.Description.Contains("4×") && embed.Description.Contains("Madera de Pino"), $"el resultado muestra la cantidad: {embed.Description}");
}
finally
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)testId });
    Console.WriteLine($"\nLimpieza: usuarios de prueba restantes = {await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE discord_id = @id", new { id = (long)testId })}");
}

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeTarget : ICombatReplyTarget
{
    public List<(Embed Embed, MessageComponent Components)> Updates { get; } = [];
    public Task UpdateAsync(Embed embed, MessageComponent components) { Updates.Add((embed, components)); return Task.CompletedTask; }
}
