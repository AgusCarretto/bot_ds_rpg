using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.22.2 (el dueño, 2026-10-22): «agregar los emojis a los combos desplegables, el forje y todo; donde se pueda suma mucho».
// Cada opción de un desplegable de ítems lleva el emoji PROPIO del ítem en el campo de emoji de la opción, pero SOLO si la aplicación lo tiene de verdad (UiEmojis.IsKnownEmoji): con un emoji que Discord
// no conoce, rechaza el mensaje ENTERO (en el menú de curación de un /travel o un /boss no se podría ni empezar la pelea). Las listas de autocompletado de los comandos no pueden llevarlo (límite de Discord).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

// Un código de emoji con la forma real de Discord (id de 18 cifras inventado).
string Code(string name, ulong id, bool animated = false) => $"<{(animated ? "a" : string.Empty)}:{name}:{id}>";
IEnumerable<SelectMenuComponent> Menus(MessageComponent? c) => c?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>() ?? [];
ulong IdOf(string code) => Emote.TryParse(code, out var e) ? e.Id : 0;

Console.WriteLine("== 1. La lista de emojis que la aplicación tiene ==");
UiEmojis.Set([]);
Check(!UiEmojis.IsKnownEmoji(Code("ex", 111111111111111111UL)), "sin la lista cargada (recién arrancado, o Discord no respondió) ningún emoji se considera conocido");
UiEmojis.Set([("ex", Code("ex", 111111111111111111UL)), ("ye", Code("ye", 222222222222222222UL, animated: true))]);
Check(UiEmojis.IsKnownEmoji(Code("ex", 111111111111111111UL)) && UiEmojis.IsKnownEmoji(Code("ye", 222222222222222222UL, animated: true)), "un emoji de la lista es conocido, también el animado «<a:...>»");
Check(UiEmojis.IsKnownEmoji(Code("otro_nombre", 111111111111111111UL)), "lo que decide es el id (Discord identifica al emoji por el id, el nombre es decorativo)");
Check(!UiEmojis.IsKnownEmoji(Code("ex", 333333333333333333UL)), "un id que no está en la lista (borrado del portal, o de otra aplicación) NO es conocido");
Check(!UiEmojis.IsKnownEmoji(null) && !UiEmojis.IsKnownEmoji("") && !UiEmojis.IsKnownEmoji("   ") && !UiEmojis.IsKnownEmoji("🗡️") && !UiEmojis.IsKnownEmoji("<:x:1>") && !UiEmojis.IsKnownEmoji("basura"), "nulo, vacío, un emoji común, un id corto o texto suelto no son conocidos y no tiran");
UiEmojis.Set([("ex", "no es un emoji"), ("ye", Code("ye", 444444444444444444UL))]);
Check(!UiEmojis.IsKnownEmoji(Code("ex", 111111111111111111UL)) && UiEmojis.IsKnownEmoji(Code("ye", 444444444444444444UL)), "cada refresco REEMPLAZA la lista (lo que ya no está deja de ser conocido) y un código roto no entra");

Console.WriteLine("\n== 2. Qué emoji puede llevar un ítem en un desplegable ==");
UiEmojis.Set([("espada", Code("espada", 555555555555555555UL)), ("pan", Code("pan", 666666666666666666UL, animated: true))]);
ItemEmojiRegistry.Set([
    ("Espada", Code("espada", 555555555555555555UL)), ("Pan", Code("pan", 666666666666666666UL, animated: true)),
    ("Fantasma", Code("fantasma", 777777777777777777UL)), ("Comun", "🗡️"), ("SinArte", null), ("Roto", "<:roto:12>")]);
Check(ItemEmojiRegistry.MenuCodeOf("Espada") == Code("espada", 555555555555555555UL) && ItemEmojiRegistry.MenuCodeOf("espada") == ItemEmojiRegistry.MenuCodeOf("ESPADA"), "un ítem con emoji en la lista lo lleva (sin mirar mayúsculas)");
Check(ItemEmojiRegistry.MenuCodeOf("Pan") == Code("pan", 666666666666666666UL, animated: true), "el animado también");
Check(ItemEmojiRegistry.MenuCodeOf("Fantasma") is null, "un ítem cuyo emoji NO está en la lista de la aplicación sale sin emoji (así nunca llega a Discord uno que rompa el mensaje)");
Check(ItemEmojiRegistry.MenuCodeOf("Comun") is null && ItemEmojiRegistry.MenuCodeOf("SinArte") is null && ItemEmojiRegistry.MenuCodeOf("Roto") is null && ItemEmojiRegistry.MenuCodeOf("No existe") is null, "un emoji común (no se puede validar), un ítem sin arte, un código roto o un ítem que no existe: sin emoji");
Check(ItemEmojiRegistry.IconOf("Espada", "x") == Code("espada", 555555555555555555UL) && ItemEmojiRegistry.IconOf("Fantasma", "x") == Code("fantasma", 777777777777777777UL), "el texto de los embeds NO cambia: sigue usando el emoji del ítem esté o no en la lista");
UiEmojis.Set([]);
Check(ItemEmojiRegistry.MenuCodeOf("Espada") is null, "si la lista se vacía (Discord dejó de responder y se reinició) las opciones vuelven a salir sin emoji");

Console.WriteLine("\n== 3. La opción del desplegable ==");
UiEmojis.Set([("espada", Code("espada", 555555555555555555UL)), ("pan", Code("pan", 666666666666666666UL, animated: true))]);
var emoteEspada = ItemMenuExtensions.ItemEmote("Espada") as Emote;
var emotePan = ItemMenuExtensions.ItemEmote("Pan") as Emote;
Check(emoteEspada is { Id: 555555555555555555UL, Name: "espada", Animated: false } && emotePan is { Id: 666666666666666666UL, Animated: true }, "el emoji de la opción se arma con el id, el nombre y si es animado");
Check(ItemMenuExtensions.ItemEmote("Fantasma") is null && ItemMenuExtensions.ItemEmote("Comun") is null && ItemMenuExtensions.ItemEmote("No existe") is null, "sin emoji conocido no hay emoji de opción");
var built = new SelectMenuBuilder().WithCustomId("t").AddItemOption("Espada", "Espada ×2", "espada_valor", "una descripción").AddItemOption("Fantasma", "Fantasma", "Fantasma").Build();
var bo = built.Options.ToList();
Check(bo.Count == 2 && bo[0].Emote is Emote e0 && e0.Id == 555555555555555555UL && bo[0].Value == "espada_valor" && bo[0].Label == "Espada ×2" && bo[0].Description == "una descripción" && bo[1].Emote is null,
    "la opción lleva el emoji del ítem y conserva su etiqueta, su valor (que puede ser otra cosa que el nombre) y su descripción; la que no tiene emoji sale como siempre");

Console.WriteLine("\n== 4. Las pantallas reales ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var recipes = new RecipeRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var dust = new DustRepository(factory);
var exchange = new DropExchangeRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000095001UL, D = 999000000000095002UL;
foreach (var id in new[] { U, D })
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    await users.GetOrCreateUserAsync(id);
}

// La aplicación "tiene" todos los emojis que el catálogo local usa (como si el dueño hubiera subido todo): la lista sale de las filas de items.
var catalog = (await db.QueryAsync<(string Name, string Emoji)>("SELECT name, emoji FROM items WHERE emoji LIKE '<%'")).ToList();
List<(string Name, string Code)> AppList(Func<string, bool>? skip = null) =>
    catalog.Where(x => skip is null || !skip(x.Name)).Select(x => (Emote.TryParse(x.Emoji, out var em) ? em.Name : x.Name, x.Emoji)).ToList();
async Task Reload(List<(string Name, string Code)> app) { UiEmojis.Set(app); await new ItemEmojiRefresher(itemsRepo).RefreshAsync(); }
Check(catalog.Count > 60, $"el catálogo local trae {catalog.Count} ítems con emoji propio");

string food = "Mate Amargo";
var drop = (await exchange.GetExchangeableDropsAsync()).First().Name;
var weapon = await db.QuerySingleAsync<(int Id, string Name)>("SELECT item_id, name FROM items WHERE type = 'Weapon' AND emoji LIKE '<%' AND sell_price > 0 ORDER BY item_id LIMIT 1");
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
await Give(food, 3); await Give("Madera de Roble", 4); await Give(drop, 6);
await db.ExecuteAsync("UPDATE users SET gold = 5000, current_hp = 10, weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = weapon.Id });
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE name IN ('Madera de Roble', @f)", new { f = food }) == 2, $"hay con qué probar: {food}, Madera de Roble, el drop «{drop}» y el arma «{weapon.Name}»");

async Task<TabernaScene> Taberna() => await TabernaModule.BuildSceneAsync(users, itemsRepo, inventory, buffs, U, null, null, exchange);
// Todas las opciones de la taberna cuyo valor es un ítem (sin «Curarme del todo»): (menú, opción).
List<(string Menu, SelectMenuOption Option)> TabernaOptions(TabernaScene s) => Menus(s.Components).SelectMany(m => m.Options.Select(o => (m.CustomId.Split(':')[0], o))).ToList();
string? CodeOfItem(string name) => catalog.Where(x => x.Name == name).Select(x => x.Emoji).FirstOrDefault();

// 4a. Con TODOS los emojis en la lista de la aplicación.
await Reload(AppList());
var all = TabernaOptions(await Taberna());
var menusSeen = all.Select(x => x.Menu).Distinct().ToList();
Check(new[] { "taberna_eat", "taberna_buyfood", "taberna_box", "taberna_sell", "taberna_swap" }.All(menusSeen.Contains) || menusSeen.Count >= 5, $"la taberna arma sus menús de ítems ({string.Join(", ", menusSeen)})");
var itemOptions = all.Where(x => x.Option.Value != "fullheal" && CodeOfItem(x.Option.Value) is not null).ToList();
Check(itemOptions.Count >= 6 && itemOptions.All(x => x.Option.Emote is Emote e && e.Id == IdOf(CodeOfItem(x.Option.Value)!)),
    $"con todos los emojis en la aplicación, cada una de las {itemOptions.Count} opciones de ítem lleva EL emoji de SU ítem (comer, comprar comida, comprar caja, vender y el 3 por 1)");
var sellMenu = all.Where(x => x.Menu.Contains("sell")).Select(x => x.Option).ToList();
var wornOption = sellMenu.First(o => o.Value == weapon.Name);
Check(wornOption.Emote is not null && !wornOption.Label.Contains("🗡") && wornOption.Label.Contains("equipad") && wornOption.Description.Contains("⚠️"), $"el arma puesta se ofrece con SU emoji y sin el 🗡️ de antes, avisando que está equipada: «{wornOption.Label}»");
var swapOptions = all.Where(x => x.Menu.Contains("swap")).Select(x => x.Option).ToList();
Check(swapOptions.Count > 0 && swapOptions.Any(o => o.Value == drop && o.Emote is not null), $"el menú del 3 por 1 lleva el emoji del drop «{drop}»");
var healAll = all.FirstOrDefault(x => x.Menu.Contains("eat") && x.Option.Value.Length > 0 && x.Option.Emote is null);
Check(healAll.Option is null || CodeOfItem(healAll.Option.Value) is null, "la única opción sin emoji del menú de comer es «Curarme del todo» (no es un ítem)");
Check(all.All(x => x.Option.Label.Length <= 100 && x.Option.Value.Length <= 100 && x.Option.Description.Length <= 100) && Menus((await Taberna()).Components).All(m => m.Options.Count <= 25), "los límites de Discord se mantienen (100 caracteres, 25 opciones)");

// 4b. Con la lista de la aplicación VACÍA (todavía no cargó): todo sale como antes.
await Reload([]);
var none = TabernaOptions(await Taberna());
Check(none.Count == all.Count && none.All(x => x.Option.Emote is null), "sin la lista de emojis cargada NINGUNA opción lleva emoji (el bot arranca y funciona igual que antes)");
Check(none.Select(x => (x.Menu, x.Option.Value, x.Option.Description)).SequenceEqual(all.Select(x => (x.Menu, x.Option.Value, x.Option.Description))), "las opciones, sus valores y sus descripciones son las mismas con o sin emojis (solo cambia el emoji y la marca del arma)");
Check(none.First(x => x.Option.Value == weapon.Name && x.Menu.Contains("sell")).Option.Label.StartsWith("🗡️ "), "sin el emoji del arma, el 🗡️ de antes vuelve a la etiqueta");

// 4c. Falta SOLO un ítem en la lista (se borró del portal): ese sale sin emoji, los demás no pierden el suyo.
await Reload(AppList(skip: n => n == food));
var partial = TabernaOptions(await Taberna());
Check(partial.Where(x => x.Option.Value == food).All(x => x.Option.Emote is null) && partial.Where(x => x.Option.Value == food).Any()
      && partial.Where(x => x.Option.Value != food && x.Option.Value != "fullheal" && CodeOfItem(x.Option.Value) is not null).All(x => x.Option.Emote is not null),
    $"si «{food}» no está en la lista de la aplicación, SOLO sus opciones salen sin emoji y el resto lo conserva");

// 4d. El herrero.
await Reload(AppList());
await db.ExecuteAsync("UPDATE users SET weapon_id = NULL, gold = 999999 WHERE discord_id = @id", new { id = (long)U });
var forge = await BlacksmithModule.BuildSceneAsync(users, recipes, zonesRepo, inventory, U);
var forgeOptions = Menus(forge.Components).Single(m => m.CustomId.StartsWith("blacksmith_pick")).Options.ToList();
Check(forgeOptions.Count >= 3 && forgeOptions.All(o => CodeOfItem(o.Value) is { } c ? o.Emote is Emote e && e.Id == IdOf(c) : o.Emote is null),
    $"el menú del herrero: cada una de las {forgeOptions.Count} recetas lleva el emoji del ítem que forja (y solo si tiene)");
Check(forgeOptions.All(o => o.Label.StartsWith("✅") || o.Label.StartsWith("❌") || o.Label.StartsWith("🔁") || o.Label.StartsWith("🟢")), "el ✅ / ❌ / 🔁 / 🟢 del estado de la receta se queda en el texto, el emoji del ítem va aparte");
await Reload([]);
var forgeNone = Menus((await BlacksmithModule.BuildSceneAsync(users, recipes, zonesRepo, inventory, U)).Components).Single(m => m.CustomId.StartsWith("blacksmith_pick")).Options;
Check(forgeNone.Count == forgeOptions.Count && forgeNone.All(o => o.Emote is null), "el herrero sin lista de emojis: las mismas recetas, sin emoji");
Check(Menus(forge.Components).Count() == 1 && !Menus(forge.Components).Any(m => m.CustomId.StartsWith("blacksmith_zone")), "la herrería ya no tiene el menú de zonas (v0.27.0: el botón «Ver todas las recetas» lo reemplaza), solo la lista para forjar");

// 4e. El menú de curación del combate (el que no puede romperse: sin él no se empieza un /travel ni un /boss).
await Reload(AppList());
var travel = new CombatState("travel", "Toro Bravo", "🐂", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
var healOptions = new List<HealOption> { new(food, 15, 3), new("Pan Inexistente", 30, 1) };
var healMenu = Menus(AdventureModule.BuildCombatButtons(travel, healOptions)).Single(m => m.CustomId == CombatHeal.MenuCustomId);
var ho = healMenu.Options.ToList();
Check(ho.Count == 2 && ho[0].Emote is Emote he && he.Id == IdOf(CodeOfItem(food)!) && ho[1].Emote is null,
    "el menú de curación de /travel y /boss: la comida lleva su emoji y una opción de un ítem sin emoji sale limpia");
await Reload([]);
var healNone = Menus(AdventureModule.BuildCombatButtons(travel, healOptions)).Single(m => m.CustomId == CombatHeal.MenuCustomId);
Check(healNone.Options.Count == 2 && healNone.Options.All(o => o.Emote is null), "con la lista de emojis vacía el menú de curación sale exactamente como antes: empezar una pelea nunca depende de un emoji");
var healEmpty = Menus(AdventureModule.BuildCombatButtons(travel, [])).Single(m => m.CustomId == CombatHeal.MenuCustomId);
Check(healEmpty.IsDisabled && healEmpty.Options.Single().Emote is null, "los estados «sin comida» / «ya te curaste» (opción de relleno, deshabilitada) no llevan emoji");

// 4f. El Tabernero del Inframundo.
var zonesOrdered = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
await db.ExecuteAsync("UPDATE users SET dust = 9000, highest_zone_cleared = @h WHERE discord_id = @id", new { id = (long)D, h = zonesOrdered[^1].ZoneId });
await Reload(AppList());
var deep = await DeepShopModule.BuildSceneAsync(users, zonesRepo, dust, itemsRepo, D, null, new Random(2));
var deepOptions = Menus(deep.Components).Single().Options.ToList();
Check(deepOptions.Count == 2 && deepOptions.Select(o => o.Value).SequenceEqual(["corteza", "meteorito"]) && deepOptions.Zip(DeepShop.Stock).All(p => p.First.Emote is Emote e && e.Id == IdOf(CodeOfItem(p.Second.ItemName)!)),
    "el Inframundo: «corteza» y «meteorito» (los valores no cambian) llevan el emoji de la Corteza del Árbol de Vida y del Fragmento de Meteorito");

Console.WriteLine("\n== 5. Todos los desplegables de ítems usan la ayuda ==");
string root = TestEnv.RepoRoot;
foreach (var file in new[] { "TabernaModule", "BlacksmithModule", "AdventureModule", "RaidModule", "DeepShopModule" })
{
    string text = File.ReadAllText(Path.Combine(root, "Modules", file + ".cs"));
    Check(text.Contains(".AddItemOption("), $"{file}.cs arma sus opciones de ítem con AddItemOption");
}

foreach (var id in new[] { U, D })
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id", new { id = (long)id });
}

UiEmojis.Set([]);
ItemEmojiRegistry.Set([]);
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
