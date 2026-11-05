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

// v0.49.0 (tanda 1): la COCINA (`/cook`, `aa cook`, `aa cook recipes`), diseño en docs/superpowers/specs/2026-11-05-cocina-design.md. Lo que se prueba: el catálogo de los 3 platos permanentes (cada uno suma su
// stat cada vez que se cocina, se apilan, se comen en el momento), cómo se lee lo que escribe el jugador, la transacción contra la base LOCAL (ingredientes + combustible descontados juntos, si falta algo no cambia
// NADA, sin cuenta no crea una, 12 pedidos a la vez no dejan nada en negativo), que lo cocinado entra por PlayerBonuses (vida, DEF y ATQ planos) SOLO contra monstruos, que el Fuego Nuevo lo borra, las pantallas en los
// dos idiomas y el registro con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U1 = 999000000000098201UL, U2 = 999000000000098202UL, U3 = 999000000000098203UL, UFn = 999000000000098204UL, UEn = 999000000000098205UL, UGhost = 999000000000098299UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [U1, U2, U3, UFn, UEn, UGhost];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(CookModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var cooking = sp.GetRequiredService<ICookingRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var bonusService = sp.GetRequiredService<IPlayerBonusService>();
var fnRepo = sp.GetRequiredService<IFuegoNuevoRepository>();
var itemsRepo = sp.GetRequiredService<IItemRepository>();
var buffRepo = sp.GetRequiredService<IBuffRepository>();
var dragonRepo = sp.GetRequiredService<IDragonRepository>();
var zonesRepo = sp.GetRequiredService<IZoneRepository>();
var petRepo = sp.GetRequiredService<IPetRepository>();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

async Task Clean()
{
    foreach (var id in all)
    {
        await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

async Task Player(ulong id)
{
    await users.GetOrCreateUserAsync(id);
    // Hechicero: su factor de vida contra monstruos es 1,0, así lo plano se ve exacto.
    await db.ExecuteAsync("UPDATE users SET class = 'Hechicero', level = 20, max_hp = 300, current_hp = 300 WHERE discord_id = @id", new { id = (long)id });
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
}

async Task Give(ulong id, string item, int qty) =>
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity", new { id = (long)id, n = item, q = qty });

async Task<int> Have(ulong id, string item) => await db.ExecuteScalarAsync<int>(
    "SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = item });

// Todo lo que pide cocinar `times` veces el plato (ingredientes y combustible).
async Task Add(ulong id, string item, int qty) =>
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + EXCLUDED.quantity", new { id = (long)id, n = item, q = qty });

async Task Stock(ulong id, Dish dish, int times)
{
    // SUMA a lo que ya tenga (los platos comparten ingredientes: Surubí, Gema de Zafiro, Carbón...).
    foreach (var ing in dish.Ingredients) await Add(id, ing.ItemName, ing.Quantity * times);
    await Add(id, CookingCatalog.FuelItemName, dish.Fuel * times);
}

async Task<int> Cooked(ulong id, Dish dish) => (await cooking.GetCookedAsync(id)).GetValueOrDefault(dish.Key);

await Clean();

Console.WriteLine("== 1. El catálogo ==");
var dishes = CookingCatalog.All;
var disco = CookingCatalog.Find("surubi al disco")!;
var parrilla = CookingCatalog.Find("Dorado a la Parrilla")!;
var escabeche = CookingCatalog.Find("MOJARRAS EN ESCABECHE")!;
Check(dishes.Count == 10 && dishes.Select(d => d.Key).Distinct().Count() == 10 && dishes.Count(d => d.Kind == DishKind.Permanent) == 3 && dishes.Count(d => d.Kind == DishKind.Timed) == 7
    && dishes.All(d => d.Ingredients.Count is >= 1 and <= 4 && d.Fuel > 0 && d.Name.Length > 3),
    "hay 10 platos (3 permanentes y 7 de tiempo), con clave distinta, hasta 4 tipos de ingrediente y combustible");
Check(CookingCatalog.TimedMinutes == 60 && dishes.Where(d => d.Kind == DishKind.Timed).All(d => d.MaxHp == 0 && d.Defense == 0 && d.Attack == 0)
    && CookingCatalog.Find("empanada de surubi")!.GoldPercent == 20 && CookingCatalog.Find("locro del pescador")!.XpPercent == 20 && CookingCatalog.Find("chupin de dorado")!.DropPercent == 15
    && CookingCatalog.Find("pastel de mojarra")!.GatherPercent == 25 && CookingCatalog.Find("brocheta de dorado")!.CritPoints == 10 && CookingCatalog.Find("surubi a la manteca")!.DodgePoints == 10
    && CookingCatalog.Find("caldo del ultimo aliento")!.LastStand, "los 7 de tiempo dan lo del diseño: oro +20 %, EXP +20 %, drop +15 %, cantidad +25 %, crítico +10, esquive +10 y el último aliento");
Check(disco.MaxHp == 10 && disco.Defense == 0 && disco.Attack == 0 && parrilla.Defense == 1 && parrilla.MaxHp == 0 && escabeche.Attack == 2 && escabeche.Defense == 0,
    "cada plato suma lo suyo: Surubí al Disco +10 vida máx, Dorado a la Parrilla +1 DEF, Mojarras en Escabeche +2 ATQ");
Check(disco.Fuel == 16 && disco.Ingredients.Single(i => i.ItemName == "Surubí").Quantity == 7 && disco.Ingredients.Single(i => i.ItemName == "Gema de Zafiro").Quantity == 2 && disco.Ingredients.Single(i => i.ItemName == "Oro Puro").Quantity == 6
    && parrilla.Fuel == 20 && parrilla.Ingredients.Single(i => i.ItemName == "Dorado").Quantity == 40 && parrilla.Ingredients.Single(i => i.ItemName == "Surubí").Quantity == 1
    && escabeche.Fuel == 20 && escabeche.Ingredients.Single(i => i.ItemName == "Mojarra").Quantity == 150, "las recetas son las del diseño (Surubí al Disco 7 Surubí + 2 Zafiro + 6 Oro Puro + 16 Carbón...)");
var catalogNames = (await db.QueryAsync<string>("SELECT name FROM items")).ToHashSet();
Check(dishes.All(d => d.Ingredients.All(i => catalogNames.Contains(i.ItemName))) && catalogNames.Contains(CookingCatalog.FuelItemName), "todos los ingredientes y el combustible existen en el catálogo de la base");
Check(CookingCatalog.Find("surubi_disco")?.Key == disco.Key && CookingCatalog.Find("  Surubí al Disco ")?.Key == disco.Key && CookingCatalog.Find("sopa") is null && CookingCatalog.Find("") is null,
    "el plato se encuentra por clave o por nombre sin importar mayúsculas ni tildes; uno que no existe no se encuentra");
var r0 = CookingCatalog.ParseRequest("");
var r1 = CookingCatalog.ParseRequest("recipes");
var r2 = CookingCatalog.ParseRequest("surubi al disco");
var r3 = CookingCatalog.ParseRequest("surubi al disco 5");
var r4 = CookingCatalog.ParseRequest("surubi al disco 99");
var r5 = CookingCatalog.ParseRequest("recetas");
Check(r0.ShowRecipes && r1.ShowRecipes && r5.ShowRecipes && !r2.ShowRecipes && r2.Dish == "surubi al disco" && r2.Times == 1 && r3.Dish == "surubi al disco" && r3.Times == 5 && r4.Error is not null && CookingCatalog.ParseRequest("x 0").Error is not null,
    "lo que se escribe: vacío o recipes/recetas = las recetas; «plato» cocina 1; «plato 5» cocina 5; más de 10 o 0 es un error");

Console.WriteLine("\n== 2. Cocinar (la transacción) ==");
await Player(U1);
await Stock(U1, disco, 3);
var ok1 = await cooking.CookAsync(U1, disco, 1);
Check(ok1.Status == CookStatus.Cooked && ok1.NewCount == 1 && await Have(U1, "Surubí") == 14 && await Have(U1, "Gema de Zafiro") == 4 && await Have(U1, "Oro Puro") == 12 && await Have(U1, "Carbón") == 32,
    "cocinar 1 descuenta ingredientes Y combustible juntos y suma 1 al contador del plato");
var ok2 = await cooking.CookAsync(U1, disco, 2);
Check(ok2.Status == CookStatus.Cooked && ok2.NewCount == 3 && await Cooked(U1, disco) == 3 && await Have(U1, "Surubí") == 0 && await Have(U1, "Carbón") == 0
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM inventory WHERE discord_id = @id AND quantity <= 0", new { id = (long)U1 }) == 0, "cocinar 2 más se apila (3 en total) y las filas que quedan en 0 se borran de la mochila");

await Player(U2);
await Stock(U2, disco, 1);
await Give(U2, "Gema de Zafiro", 1); // falta UNO
var miss = await cooking.CookAsync(U2, disco, 1);
Check(miss.Status == CookStatus.Missing && miss.Missing.Count == 1 && miss.Missing[0].ItemName == "Gema de Zafiro" && miss.Missing[0].Have == 1 && miss.Missing[0].Need == 2, "si falta algo dice QUÉ falta, con lo que tenés y lo que pide");
Check(await Have(U2, "Surubí") == 7 && await Have(U2, "Oro Puro") == 6 && await Have(U2, "Carbón") == 16 && await Have(U2, "Gema de Zafiro") == 1 && await Cooked(U2, disco) == 0, "y no cambia NADA (ni los ingredientes que sí alcanzaban, ni el contador)");
await Give(U2, "Surubí", 0);
var miss2 = await cooking.CookAsync(U2, disco, 2);
Check(miss2.Status == CookStatus.Missing && miss2.Missing.Select(m => m.ItemName).OrderBy(n => n).SequenceEqual(new[] { "Carbón", "Gema de Zafiro", "Oro Puro", "Surubí" }.OrderBy(n => n)), "con varios faltantes los dice todos juntos");
var ghost = await cooking.CookAsync(UGhost, disco, 1);
Check(ghost.Status == CookStatus.NoAccount && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM users WHERE discord_id = @id", new { id = (long)UGhost }) == 0, "sin cuenta no cocina y NO le crea una");

await Player(U3);
await Stock(U3, disco, 5);
var race = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => cooking.CookAsync(U3, disco, 1)));
Check(race.Count(r => r.Status == CookStatus.Cooked) == 5 && race.Count(r => r.Status == CookStatus.Missing) == 7 && await Cooked(U3, disco) == 5 && await Have(U3, "Surubí") == 0 && await Have(U3, "Carbón") == 0
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM inventory WHERE discord_id = @id AND quantity < 0", new { id = (long)U3 }) == 0, "12 pedidos a la vez con materiales para 5: se cocinan exactamente 5 y nada queda en negativo");

Console.WriteLine("\n== 3. Lo cocinado entra al combate (solo contra monstruos) ==");
await Player(U1);
await db.ExecuteAsync("DELETE FROM player_cooked WHERE discord_id = @id", new { id = (long)U1 });
await Stock(U1, disco, 10); await Stock(U1, parrilla, 4); await Stock(U1, escabeche, 3);
await cooking.CookAsync(U1, disco, 10);
await cooking.CookAsync(U1, parrilla, 4);
await cooking.CookAsync(U1, escabeche, 3);
var me = (await users.GetByDiscordIdAsync(U1))!;
var bonuses = await bonusService.GetAsync(U1, 0);
var baseline = PlayerCombatProfileCalculator.Resolve(me, null, null, 0, PlayerBonuses.None);
var cookedProfile = PlayerCombatProfileCalculator.Resolve(me, null, null, 0, bonuses);
Check(bonuses.CookedMaxHp == 100 && bonuses.CookedDefense == 4 && bonuses.CookedAttack == 6, $"los bonus leen la tabla: 10 Surubí al Disco = +100 vida, 4 Dorado = +4 DEF, 3 Mojarras = +6 ATQ ({bonuses.CookedMaxHp}/{bonuses.CookedDefense}/{bonuses.CookedAttack})");
Check(cookedProfile.CombatMaxHp == baseline.CombatMaxHp + 100 && cookedProfile.CombatCurrentHp == baseline.CombatCurrentHp + 100 && cookedProfile.Defense == baseline.Defense + 4 && cookedProfile.Damage == baseline.Damage + 6,
    $"contra monstruos suman exacto: vida {baseline.CombatMaxHp} -> {cookedProfile.CombatMaxHp}, DEF {baseline.Defense} -> {cookedProfile.Defense}, ATQ {baseline.Damage} -> {cookedProfile.Damage}");
var duel = PlayerCombatProfileCalculator.Resolve(me, null, null, 0, null);
Check(duel.CombatMaxHp == baseline.CombatMaxHp && duel.Defense == baseline.Defense && duel.Damage == baseline.Damage, "en un duelo o en la Arena (sin bonuses) lo cocinado NO cuenta");
Check(PlayerCombatProfileCalculator.HpMultiplier(me, bonuses) > PlayerCombatProfileCalculator.HpMultiplier(me, null) && PlayerCombatProfileCalculator.HpMultiplier(me, PlayerBonuses.None) == PlayerCombatProfileCalculator.HpMultiplier(me, null),
    "el factor de vida que se muestra en /heal, el perfil y la pelea ya incluye lo cocinado (una sola cuenta)");
Check(!bonuses.IsNone && PlayerBonuses.None.CookedMaxHp == 0 && PlayerBonuses.None.IsNone, "con platos los bonus ya no son «ninguno»; sin ellos todo en 0");

Console.WriteLine("\n== 4. El Fuego Nuevo los borra ==");
await Player(UFn);
await Stock(UFn, parrilla, 2);
await cooking.CookAsync(UFn, parrilla, 2);
var empFn = CookingCatalog.Find("empanada de surubi")!;
await Stock(UFn, empFn, 1);
await cooking.CookAsync(UFn, empFn, 1);
Check((await cooking.GetActiveBuffsAsync(UFn)).Count == 1, "antes del Fuego Nuevo tiene un plato de tiempo corriendo");
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)UFn });
var renewed = await fnRepo.RenewAsync(UFn, 0, "Guerrero");
Check(renewed.Status == RenewStatus.Ok && await Cooked(UFn, parrilla) == 0 && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM player_cooked WHERE discord_id = @id", new { id = (long)UFn }) == 0
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM player_cook_buffs WHERE discord_id = @id", new { id = (long)UFn }) == 0,
    "después del Fuego Nuevo no queda nada cocinado, ni permanente ni de tiempo (los bufos de la vuelta anterior no están)");

Console.WriteLine("\n== 4b. Los platos de tiempo ==");
var empanada = CookingCatalog.Find("empanada de surubi")!;
var locro = CookingCatalog.Find("locro del pescador")!;
var chupin = CookingCatalog.Find("chupin de dorado")!;
await Player(U1);
await db.ExecuteAsync("DELETE FROM player_cook_buffs WHERE discord_id = @id", new { id = (long)U1 });
await Stock(U1, empanada, 3);
var t1 = await cooking.CookAsync(U1, empanada, 1);
Check(t1.Status == CookStatus.Cooked && t1.Remaining is { } rem1 && rem1 > TimeSpan.FromMinutes(59) && rem1 <= TimeSpan.FromMinutes(60) && (await cooking.GetActiveBuffsAsync(U1)).ContainsKey(empanada.Key) && await Cooked(U1, empanada) == 0,
    "un plato de tiempo corre 60 minutos y NO se cuenta como permanente");
var t2 = await cooking.CookAsync(U1, empanada, 2);
Check(t2.Remaining is { } rem2 && rem2 > TimeSpan.FromMinutes(179) && rem2 <= TimeSpan.FromMinutes(180), "comer el mismo otra vez SUMA el reloj (60 + 120 = 180 minutos)");
await Stock(U1, locro, 1);
await cooking.CookAsync(U1, locro, 1);
var act = await cooking.GetActiveBuffsAsync(U1);
Check(act.Count == 2 && act.ContainsKey(locro.Key) && act.ContainsKey(empanada.Key), "varios platos distintos corren a la vez");
await db.ExecuteAsync("UPDATE player_cook_buffs SET expires_at = now() - interval '5 minutes' WHERE discord_id = @id AND dish_key = @k", new { id = (long)U1, k = empanada.Key });
Check(!(await cooking.GetActiveBuffsAsync(U1)).ContainsKey(empanada.Key) && (await cooking.GetActiveBuffsAsync(U1)).ContainsKey(locro.Key), "un plato vencido ya no cuenta (y el otro sigue)");
await Stock(U1, empanada, 1);
var t3 = await cooking.CookAsync(U1, empanada, 1);
Check(t3.Remaining is { } rem3 && rem3 > TimeSpan.FromMinutes(59) && rem3 <= TimeSpan.FromMinutes(60), "si ya se había vencido, el reloj arranca de AHORA (no de cuando se venció)");

await Player(U3);
await db.ExecuteAsync("DELETE FROM player_cook_buffs WHERE discord_id = @id", new { id = (long)U3 });
await Stock(U3, empanada, 4);
var race2 = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => cooking.CookAsync(U3, empanada, 1)));
var left3 = (await cooking.GetActiveBuffsAsync(U3))[empanada.Key];
Check(race2.Count(r => r.Status == CookStatus.Cooked) == 4 && race2.Count(r => r.Status == CookStatus.Missing) == 8 && left3 > TimeSpan.FromMinutes(239) && left3 <= TimeSpan.FromMinutes(240),
    "12 pedidos a la vez con materiales para 4: se cocinan 4 y el reloj suma exacto 4 horas");

// Lo que hacen en el combate: todos activos a la vez contra la fila de un jugador sin nada.
await Player(U2);
await db.ExecuteAsync("DELETE FROM player_cook_buffs WHERE discord_id = @id", new { id = (long)U2 });
var meT = (await users.GetByDiscordIdAsync(U2))!;
var b0 = await bonusService.GetAsync(U2, 0);
foreach (var d in CookingCatalog.All.Where(x => x.Kind == DishKind.Timed)) { await Stock(U2, d, 1); await cooking.CookAsync(U2, d, 1); }
await Stock(U2, chupin, 1);
await cooking.CookAsync(U2, chupin, 1); // el MISMO otra vez: suma tiempo, no efecto
var bAll = await bonusService.GetAsync(U2, 0);
bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
Check(Near(bAll.GoldMultiplier / b0.GoldMultiplier, 1.20) && Near(bAll.XpMultiplier / b0.XpMultiplier, 1.20) && Near(bAll.HuntDropMultiplier / b0.HuntDropMultiplier, 1.15) && Near(bAll.TravelDropMultiplier / b0.TravelDropMultiplier, 1.15)
    && Near(bAll.ChopMultiplier / b0.ChopMultiplier, 1.25) && Near(bAll.MineMultiplier / b0.MineMultiplier, 1.25) && Near(bAll.FishMultiplier / b0.FishMultiplier, 1.25),
    "con los platos activos: oro ×1,20, EXP ×1,20, drop ×1,15 (cacería y viaje) y cantidad ×1,25 (/chop, /mine y /fish); el mismo plato dos veces NO multiplica el efecto");
var pBase = PlayerCombatProfileCalculator.Resolve(meT, null, null, 0, b0);
var pAll = PlayerCombatProfileCalculator.Resolve(meT, null, null, 0, bAll);
var pDuel = PlayerCombatProfileCalculator.Resolve(meT, null, null, 0, null);
Check(Near(pAll.Passives.CritChanceBonus - pBase.Passives.CritChanceBonus, 0.10) && Near(pAll.Passives.DodgeChanceBonus - pBase.Passives.DodgeChanceBonus, 0.10) && pAll.Passives.LastStand && !pBase.Passives.LastStand,
    "el crítico sube 10 puntos, el esquive 10 y el Caldo da el último aliento (solo con los platos activos)");
Check(!pDuel.Passives.LastStand && Near(pDuel.Passives.CritChanceBonus, pBase.Passives.CritChanceBonus) && Near(pDuel.Passives.DodgeChanceBonus, pBase.Passives.DodgeChanceBonus) && !bAll.IsNone,
    "en un duelo o en la Arena (sin bonuses) los platos de tiempo no cuentan");

Console.WriteLine("\n== 5. Las pantallas ==");
var recorder = new RecordingEvents();
await Player(U1);
await Stock(U1, disco, 2);
var reply = await CookModule.ExecuteCookAsync(cooking, recorder, U1, "surubi al disco", 2);
string replyText = reply.Embed is null ? string.Empty : reply.Embed.Title + "\n" + reply.Embed.Description + "\n" + string.Join("\n", reply.Embed.Fields.Select(f => f.Name + " " + f.Value));
Check(reply.PlainMessage is null && reply.Embed is not null && replyText.Contains("Surubí al Disco") && replyText.Contains("+20") && replyText.Contains('2') && recorder.Kinds.Contains(GameEventKinds.Cook),
    $"cocinar 2 muestra el plato y lo que sumó (+20 de vida máx) y deja el evento cook ({replyText.Replace("\n", " | ")})");
var again = await CookModule.ExecuteCookAsync(cooking, recorder, U1, "surubi al disco", 1);
Check(again.Embed is null && again.PlainMessage!.Contains("Surubí") && again.PlainMessage.Contains("Gema de Zafiro") && again.PlainMessage.Contains("Carbón"), "sin materiales el mensaje dice qué falta (con los nombres)");
var unknown = await CookModule.ExecuteCookAsync(cooking, recorder, U1, "sopa de piedra", 1);
var noAcc = await CookModule.ExecuteCookAsync(cooking, recorder, UGhost, "surubi al disco", 1);
Check(unknown.Embed is null && unknown.PlainMessage!.Contains("recipes") && noAcc.PlainMessage!.Contains("/start"), "un plato que no existe manda a «aa cook recipes»; sin cuenta manda a /start");

await Stock(U1, escabeche, 1);
var recipes = await CookModule.BuildRecipesEmbedAsync(inventory, cooking, U1);
string recipesText = recipes.Title + "\n" + recipes.Description + "\n" + string.Join("\n", recipes.Fields.Select(f => f.Name + " " + f.Value));
Check(recipes.Fields.Length == dishes.Count && recipesText.Contains("Surubí al Disco") && recipesText.Contains("Dorado a la Parrilla") && recipesText.Contains("Mojarras en Escabeche") && recipesText.Contains("✅") && recipesText.Contains("❌") && recipes.Length <= 6000,
    $"las recetas muestran los 3 platos con ✅/❌ de lo que tenés ({recipes.Length} de 6000)");
var recipesFresh = await CookModule.BuildRecipesEmbedAsync(inventory, cooking, U1);
Check(recipesFresh.Fields.All(f => f.Value.Length <= 1024), "cada campo entra en los 1024 de Discord");
await Stock(U1, locro, 1);
var timedReply = await CookModule.ExecuteCookAsync(cooking, recorder, U1, "locro del pescador", 1);
var withActive = await CookModule.BuildRecipesEmbedAsync(inventory, cooking, U1);
Check(timedReply.Embed is not null && timedReply.Embed.Description.Contains("Locro del Pescador") && timedReply.Embed.Description.Contains("Te quedan") && withActive.Fields.Any(f => f.Value.Contains("🟢") && f.Name.Contains("Locro del Pescador")),
    "cocinar uno de tiempo dice cuánto te queda y las recetas lo marcan como activo (🟢)");
// Con el emoji propio de cada ítem (cada código ronda los 40 caracteres) las 10 recetas siguen entrando en los 6000 de Discord.
var allItemNames = dishes.SelectMany(d => d.Ingredients.Select(i => i.ItemName)).Append(CookingCatalog.FuelItemName).Distinct().ToList();
ItemEmojiRegistry.Set(allItemNames.Select((n, i) => (n, (string?)$"<:item_with_a_long_emoji_name_{i:00}:123456789012345678>")));
var withEmojis = await CookModule.BuildRecipesEmbedAsync(inventory, cooking, U1);
ItemEmojiRegistry.Set([]);
Check(withEmojis.Length <= 6000 && withEmojis.Fields.All(f => f.Value.Length <= 1024), $"con los emojis propios de los ítems las 10 recetas entran en Discord ({withEmojis.Length} de 6000)");

await Player(UEn);
await Stock(UEn, parrilla, 1);
await Give(UEn, "Dorado", 3); // le falta dorado para probar el aviso en inglés
using (Loc.Use("en"))
{
    var enMiss = await CookModule.ExecuteCookAsync(cooking, new RecordingEvents(), UEn, "dorado a la parrilla", 1);
    var enRecipes = await CookModule.BuildRecipesEmbedAsync(inventory, cooking, UEn);
    await Stock(UEn, parrilla, 1);
    var enOk = await CookModule.ExecuteCookAsync(cooking, new RecordingEvents(), UEn, "dorado a la parrilla", 1);
    var enUnknown = await CookModule.ExecuteCookAsync(cooking, new RecordingEvents(), UEn, "xx", 1);
    string enText = enMiss.PlainMessage + "\n" + enUnknown.PlainMessage + "\n" + LeakCheck.AllText(enRecipes) + "\n" + (enOk.Embed is null ? "" : LeakCheck.AllText(enOk.Embed));
    var allowed = dishes.Select(d => d.Name).Concat(dishes.SelectMany(d => d.Ingredients.Select(i => i.ItemName))).Append(CookingCatalog.FuelItemName).Concat(["Dorado", "Surubí", "Mojarra"]).ToArray();
    var leak = LeakCheck.NoSpanish(enText, allowed);
    Check(enOk.Embed is not null && enMiss.PlainMessage!.Contains("You are missing") && leak.Ok && LeakCheck.FitsDiscord(enRecipes), $"en inglés las pantallas salen sin español colado ({leak.Found})");
}

Console.WriteLine("\n== 6b. /stats y el perfil ==");
// Un jugador con arma, amuleto, un banquete, un dragón de fuerza, platos apilados y uno de tiempo corriendo.
await Player(U1);
await db.ExecuteAsync("DELETE FROM player_cook_buffs WHERE discord_id = @id", new { id = (long)U1 });
await db.ExecuteAsync("DELETE FROM player_cooked WHERE discord_id = @id", new { id = (long)U1 });
await db.ExecuteAsync("DELETE FROM player_dragons WHERE discord_id = @id", new { id = (long)U1 });
await db.ExecuteAsync("DELETE FROM player_buffs WHERE discord_id = @id", new { id = (long)U1 });
int weaponId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = 'Weapon' ORDER BY item_id LIMIT 1");
int amuletId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = 'Amulet' ORDER BY item_id LIMIT 1");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a WHERE discord_id = @id", new { id = (long)U1, w = weaponId, a = amuletId });
await db.ExecuteAsync("INSERT INTO player_buffs (discord_id, buff_key, percent, expires_at) VALUES (@id, 'attack', 15, now() + interval '30 minutes')", new { id = (long)U1 });
await db.ExecuteAsync("INSERT INTO player_dragons (discord_id, kind, tier, level) VALUES (@id, 'fuerza', 3, 0)", new { id = (long)U1 });
var brochetaS = CookingCatalog.Find("brocheta de dorado")!;
await Stock(U1, disco, 10); await Stock(U1, escabeche, 3); await Stock(U1, brochetaS, 1);
await cooking.CookAsync(U1, disco, 10); await cooking.CookAsync(U1, escabeche, 3); await cooking.CookAsync(U1, brochetaS, 1);
var meS = (await users.GetByDiscordIdAsync(U1))!;
var bonS = await bonusService.GetAsync(U1, meS.FuegoNuevo);
var weaponS = await itemsRepo.GetByIdAsync(weaponId);
var amuletS = await itemsRepo.GetByIdAsync(amuletId);
var buffS = await buffRepo.GetActiveAttackAsync(U1);
var combatS = PlayerCombatProfileCalculator.Resolve(meS, weaponS, amuletS, buffS?.AttackPercent ?? 0, bonS);
var stats = await StatsModule.BuildStatsAsync(users, itemsRepo, buffRepo, bonusService, cooking, dragonRepo, U1);
string statsText = stats.Embed is null ? string.Empty : stats.Embed.Title + "\n" + stats.Embed.Description + "\n" + string.Join("\n", stats.Embed.Fields.Select(f => f.Name + " " + f.Value));
Check(stats.PlainMessage is null && stats.Embed is not null && stats.Embed.Fields.Any(f => f.Name == $"⚔️ Ataque: {combatS.Damage}") && stats.Embed.Fields.Any(f => f.Name == $"🛡️ Defensa: {combatS.Defense}") && stats.Embed.Fields.Any(f => f.Name == $"❤️ Vida máxima: {combatS.CombatMaxHp}"),
    $"la ficha muestra los MISMOS totales que pelean (ATQ {combatS.Damage}, DEF {combatS.Defense}, vida {combatS.CombatMaxHp})");
Check(statsText.Contains("platos +6") && statsText.Contains("platos +100") && statsText.Contains("banquete +15 %") && statsText.Contains("dragón +") && statsText.Contains("Surubí al Disco ×10") && statsText.Contains("Brocheta de Dorado") && statsText.Contains("Mojarras en Escabeche ×3"),
    "desglosa lo que aporta cada cosa: platos (+6 ATQ y +100 de vida), banquete, dragón, y lista lo cocinado y lo que corre");
Check(statsText.Contains("Crítico") && statsText.Contains("20 %") && statsText.Contains("platos +10") && statsText.Contains("Esquive"), "muestra el crítico (base 10 + 10 de la Brocheta = 20 %) y el esquive");
var statsNoAccount = await StatsModule.BuildStatsAsync(users, itemsRepo, buffRepo, bonusService, cooking, dragonRepo, UGhost);
Check(statsNoAccount.Embed is null && statsNoAccount.PlainMessage!.Contains("/start") && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM users WHERE discord_id = @id", new { id = (long)UGhost }) == 0, "sin cuenta manda a /start y NO le crea una");
Check(stats.Embed!.Length <= 6000 && stats.Embed.Fields.All(f => f.Value.Length <= 1024), $"la ficha entra en los límites de Discord ({stats.Embed.Length} de 6000)");
using (Loc.Use("en"))
{
    var statsEn = await StatsModule.BuildStatsAsync(users, itemsRepo, buffRepo, bonusService, cooking, dragonRepo, U1);
    string enStats = statsEn.Embed is null ? string.Empty : LeakCheck.AllText(statsEn.Embed);
    var allowedS = dishes.Select(d => d.Name).Concat(dishes.SelectMany(d => d.Ingredients.Select(i => i.ItemName))).Concat([CookingCatalog.FuelItemName, weaponS!.Name, amuletS!.Name, "Hechicero", "Cría", "Dragoncito", "Joven", "Adolescente", "Adulto", "Curtido", "Anciano", "Ancestral", "Legendario", "Eterno"]).ToArray();
    var leakS = LeakCheck.NoSpanish(enStats, allowedS);
    Check(statsEn.Embed is not null && enStats.Contains("Attack:") && enStats.Contains("Max HP:") && leakS.Ok, $"en inglés la ficha sale sin español colado ({leakS.Found})");
}

// El perfil NO se carga: la línea de ATQ no lleva la marca del dragón (el número ya la incluye); la 🔥 queda solo para la bendición de ataque.
var profile = await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U1, "T", "https://example.com/a.png", petRepo, bonusService, dragonRepo);
string atqLine = ProfileProbe.Line(profile, "ATQ");
Check(atqLine.Contains(combatS.Damage.ToString()) && !atqLine.Contains("🔥") && atqLine.Contains("🍖+15"), $"el perfil muestra el ATQ con el dragón y los platos ya adentro, sin la marca 🔥 del dragón (y el 🍖 del banquete sigue): «{atqLine}»");
Check(!FightLock.IsAllowed("cook") && FightLock.IsAllowed("stats") && FightLock.IsAllowedInDungeon("stats"), "/stats se puede usar en plena pelea (solo mira) y en una mazmorra; /cook no");

Console.WriteLine("\n== 6. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(CookModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "cook");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Select(p => p.Name).SequenceEqual(["plato", "veces"]), $"/cook existe con sus parámetros plato y veces y {slash.Description.Length} caracteres de descripción (≤ 100)");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "cook", "cocinar" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "cook") && texts.Search("cook recipes") is { IsSuccess: true },
    "aa cook y aa cocinar son el mismo comando y ningún otro lo pisa");
Check(!FightLock.IsAllowed("cook"), "en plena pelea /cook queda bloqueado (como todo lo que no es de la pelea)");
Check(interactions.SlashCommands.Single(c => c.Name == "stats").Description.Length is > 0 and <= 100 && new[] { "stats", "estadisticas", "ficha" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "stats"),
    "/stats está registrado (descripción ≤ 100) y aa stats / estadisticas / ficha llegan a un solo comando; aa equipamiento sigue siendo del Carpintero");
Check(texts.Search("equipamiento") is { IsSuccess: true } eq && eq.Commands.Count == 1 && eq.Commands[0].Command.Name == "tools", "aa equipamiento NO se tocó: sigue siendo el Carpintero");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(StatsModule)); ActivatorUtilities.CreateInstance(sp, typeof(CookModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built, "los módulos se construyen con el DI real");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
