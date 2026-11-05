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
Check(dishes.Count == 3 && dishes.Select(d => d.Key).Distinct().Count() == 3 && dishes.All(d => d.Kind == DishKind.Permanent && d.Ingredients.Count is >= 1 and <= 4 && d.Fuel > 0 && d.Name.Length > 3),
    "hay 3 platos permanentes, con clave distinta, hasta 4 tipos de ingrediente y combustible");
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
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id", new { id = (long)UFn });
var renewed = await fnRepo.RenewAsync(UFn, 0, "Guerrero");
Check(renewed.Status == RenewStatus.Ok && await Cooked(UFn, parrilla) == 0 && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM player_cooked WHERE discord_id = @id", new { id = (long)UFn }) == 0,
    "después del Fuego Nuevo no queda nada cocinado (los bufos de la vuelta anterior no están)");

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
Check(recipes.Fields.Length == 3 && recipesText.Contains("Surubí al Disco") && recipesText.Contains("Dorado a la Parrilla") && recipesText.Contains("Mojarras en Escabeche") && recipesText.Contains("✅") && recipesText.Contains("❌") && recipes.Length <= 6000,
    $"las recetas muestran los 3 platos con ✅/❌ de lo que tenés ({recipes.Length} de 6000)");
var recipesFresh = await CookModule.BuildRecipesEmbedAsync(inventory, cooking, U1);
Check(recipesFresh.Fields.All(f => f.Value.Length <= 1024), "cada campo entra en los 1024 de Discord");

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

Console.WriteLine("\n== 6. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(CookModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "cook");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Select(p => p.Name).SequenceEqual(["plato", "veces"]), $"/cook existe con sus parámetros plato y veces y {slash.Description.Length} caracteres de descripción (≤ 100)");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "cook", "cocinar" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "cook") && texts.Search("cook recipes") is { IsSuccess: true },
    "aa cook y aa cocinar son el mismo comando y ningún otro lo pisa");
Check(!FightLock.IsAllowed("cook"), "en plena pelea /cook queda bloqueado (como todo lo que no es de la pelea)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(CookModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
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
