using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
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

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var recipes = sp.GetRequiredService<IRecipeRepository>();
var zones = sp.GetRequiredService<IZoneRepository>();
var crafting = sp.GetRequiredService<ICraftingRepository>();
var shop = sp.GetRequiredService<IShopRepository>();
var buffs = sp.GetRequiredService<IBuffRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
var eventRepo = sp.GetRequiredService<IGameEventRepository>();
var realEvents = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const long BaseId = 999000000000013000L;
ulong U = (ulong)BaseId + 1;
async Task Cleanup()
{
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
}

async Task Make(ulong id, string cls = "Guerrero", int level = 5, int gold = 0, int zone = 1)
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET class = @c, level = @l, gold = @g, current_zone_id = @z, weapon_id = NULL, amulet_id = NULL, xp = 0, daily_streak = 0 WHERE discord_id = @id", new { id = (long)id, c = cls, l = level, g = gold, z = zone });
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
}

async Task Give(ulong id, string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + @q", new { id = (long)id, n = name, q });
async Task<int> Have(ulong id, string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<int> GoldOf(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
async Task<string?> Worn(ulong id, string slot) => await db.ExecuteScalarAsync<string?>($"SELECT i.name FROM users u JOIN items i ON i.item_id = u.{slot}_id WHERE u.discord_id = @id", new { id = (long)id });
async Task GiveRecipe(ulong id, string result)
{
    var r = (await recipes.GetByResultItemNameAsync(result))!;
    foreach (var ing in r.Ingredients) await Give(id, ing.ItemName, ing.Quantity);
    await db.ExecuteAsync("UPDATE users SET gold = gold + @g WHERE discord_id = @id", new { id = (long)id, g = r.GoldCost });
}

var events = new FakeEvents();
var zoneList = await zones.GetAllAsync();

Console.WriteLine("== 1. Comandos ==");
await Cleanup();
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var slash = interactions.SlashCommands.Select(c => (c.Module.SlashGroupName ?? "") + "/" + c.Name).ToList();
Check(!slash.Contains("/equip") && slash.Contains("/history") && slash.Contains("/duels") && slash.Contains("/forge") && slash.Contains("/taberna") && slash.Contains("shop/sell"), "ya no existe /equip; existen /history, /duels, /forge, /taberna y /shop sell");
var comps = interactions.ComponentCommands.Select(c => c.Name).ToList();
Check(comps.Any(c => c.StartsWith("taberna_gearyes")) && comps.Any(c => c.StartsWith("taberna_gearno")), "los botones de confirmar la venta de lo equipado tienen handler");
Check(!texts.Commands.Any(c => c.Name == "equip" || c.Aliases.Contains("eq")) && texts.Commands.Any(c => c.Name == "history" && c.Aliases.Contains("historial")) && texts.Commands.Any(c => c.Name == "duels" && c.Aliases.Contains("duelos")), "en texto: no hay 'aa equip'; hay 'aa history' (historial) y 'aa duels' (duelos)");
var fakeCtx = (ICommandContext)FakeProxy.Create(typeof(ICommandContext), (n, a) => null);
async Task<List<string>> ParsesOk(string input)
{
    var found = texts.Search(input);
    var ok = new List<string>();
    foreach (var m in found.Commands)
    {
        var pr = await m.ParseAsync(fakeCtx, found, Discord.Commands.PreconditionResult.FromSuccess(), sp);
        if (pr.IsSuccess) ok.Add(m.Command.Name);
    }

    return ok;
}

Check((await ParsesOk("history")).SequenceEqual(["history"]) && (await ParsesOk("historial")).SequenceEqual(["history"]) && (await ParsesOk("duels")).SequenceEqual(["duels"]) && (await ParsesOk("duelos")).SequenceEqual(["duels"]), "'aa history/historial' y 'aa duels/duelos' parsean cada uno a UN comando");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && !x.IsSealed && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
var tooLong = interactions.SlashCommands.Where(c => c.Description.Length > 100 || c.Parameters.Any(p => p.Description.Length > 100)).Select(c => c.Name).ToList();
Check(tooLong.Count == 0, $"ninguna descripción de comando pasa los 100 caracteres ({string.Join(",", tooLong)})");

Console.WriteLine("\n== 2. La forja equipa directo ==");
await Make(U, "Guerrero", 5, gold: 0);
await GiveRecipe(U, "Espada de Madera");
int goldBefore = await GoldOf(U);
var forgedSword = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Espada de Madera");
Check(forgedSword.Embed is not null && forgedSword.Embed.Title == "⚒️ ¡Forjado y equipado!" && forgedSword.Embed.Description.Contains("equipada") && forgedSword.Embed.Description.Contains("arma") && forgedSword.Embed.Description.Contains("+9 ATQ"), $"forjar un arma: «{forgedSword.Embed?.Title}» y dice que quedó equipada con su ataque");
Check(await Worn(U, "weapon") == "Espada de Madera" && await Have(U, "Espada de Madera") == 0 && await GoldOf(U) == goldBefore - 100 && await Have(U, "Madera de Pino") == 0 && await Have(U, "Hierro") == 0, "queda en weapon_id, NO en el inventario; se cobró el oro y se gastaron los materiales");
Console.WriteLine("   " + forgedSword.Embed!.Description.Replace("\n", " / "));

await GiveRecipe(U, "Hoja de Acero Puro");
int goldBlocked = await GoldOf(U); int hierroBlocked = await Have(U, "Hierro");
var blocked = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Hoja de Acero Puro");
Check(blocked.PlainMessage is not null && blocked.PlainMessage.Contains("arma equipada") && blocked.PlainMessage.Contains("/taberna") && await GoldOf(U) == goldBlocked && await Have(U, "Hierro") == hierroBlocked && await Worn(U, "weapon") == "Espada de Madera", "con un arma puesta no se forja otra: avisa que la venda, y NO se cobra ni se gasta nada");
Console.WriteLine("   " + blocked.PlainMessage!.Replace("\n", " / "));
var same = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Espada de Madera");
Check(same.PlainMessage is not null && same.PlainMessage.Contains("ya es tu arma equipada"), "pedir de nuevo la que ya llevás puesta: lo dice");

await GiveRecipe(U, "Hombreras de Cuero Grueso");
var forgedAmulet = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Hombreras de Cuero Grueso");
Check(forgedAmulet.Embed is not null && forgedAmulet.Embed.Description.Contains("equipado") && forgedAmulet.Embed.Description.Contains("amuleto") && await Worn(U, "amulet") == "Hombreras de Cuero Grueso" && await Worn(U, "weapon") == "Espada de Madera" && await Have(U, "Hombreras de Cuero Grueso") == 0,
    "el amuleto es otro casillero: se forja y queda equipado al lado del arma");
await GiveRecipe(U, "Talismán de Ceniza Bendita");
var blockedAmulet = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Talismán de Ceniza Bendita");
Check(blockedAmulet.PlainMessage is not null && blockedAmulet.PlainMessage.Contains("amuleto equipado"), "con un amuleto puesto tampoco se forja otro");

// El repositorio también lo valida (si dos forjas se cruzan)
await Make(U, "Guerrero", 5, gold: 0); await GiveRecipe(U, "Espada de Madera"); await GiveRecipe(U, "Daga Oxidada"); await GiveRecipe(U, "Arco Corto de Sauce");
var swordR = (await recipes.GetByResultItemNameAsync("Espada de Madera"))!; var daggerR = (await recipes.GetByResultItemNameAsync("Daga Oxidada"))!; var bowR = (await recipes.GetByResultItemNameAsync("Arco Corto de Sauce"))!;
var racers = await Task.WhenAll(new[] { swordR, daggerR, bowR }.Select(r => Task.Run(() =>
    crafting.CraftAsync(U, r.GoldCost, r.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList(), r.ResultItem.ItemId))));
Check(racers.Count(r => r.Success) == 1 && racers.Count(r => r.SlotOccupied) == 2 && racers.Single(r => r.Success).EquippedSlot == "weapon", $"tres forjas de armas a la vez con materiales para todas: sale UNA y las otras dos dicen casillero ocupado ({racers.Count(r => r.Success)} ok, {racers.Count(r => r.SlotOccupied)} ocupadas)");
var winner = racers.Single(r => r.Success).Player!;
int wonId = winner.WeaponId!.Value;
string wonName = (await items.GetByIdAsync(wonId))!.Name;
var winnerRecipe = new[] { swordR, daggerR, bowR }.Single(r => r.ResultItem.ItemId == wonId);
int expectedGold = 100 * 2; // las otras dos recetas siguen pagas (oro) y sin gastar
Check(await GoldOf(U) == expectedGold, $"solo se cobró la que se forjó ({wonName}); quedó el oro de las otras dos ({await GoldOf(U)})");

Console.WriteLine("\n== 3. Vender lo equipado ==");
var sword = (await items.GetByNameAsync("Espada de Madera"))!;
await Make(U, "Guerrero", 5, gold: 100); await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId });
var sold = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Espada de Madera", 1);
Check(sold.Embed is not null && sold.Embed.Description.Contains("equipada") && sold.Embed.Description.Contains($"**{sword.SellPrice}** de oro") && await Worn(U, "weapon") is null && await GoldOf(U) == 100 + sword.SellPrice, $"vender el arma equipada: se desequipa y paga {sword.SellPrice} de oro");
Console.WriteLine("   " + sold.Embed!.Description.Replace("\n", " / "));
var again = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Espada de Madera", 1);
Check(again.PlainMessage is not null && again.PlainMessage.Contains("No tenés") && await GoldOf(U) == 100 + sword.SellPrice, "venderla otra vez no paga nada (ya no la tenés)");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, gold = 0 WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId });
var dbl = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => ShopModule.ExecuteSellAsync(items, shop, events, U, "Espada de Madera", 1))));
Check(dbl.Count(r => r.Embed is not null) == 1 && await GoldOf(U) == sword.SellPrice, $"12 clicks a la vez en vender: paga una sola vez ({await GoldOf(U)} de oro)");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, gold = 0 WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId });
var two = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Espada de Madera", 2);
Check(two.PlainMessage is not null && await Worn(U, "weapon") == "Espada de Madera" && await GoldOf(U) == 0, "pedir vender 2 de un arma equipada no la vende (solo hay una)");
// Vender y volver a forjar
await db.ExecuteAsync("UPDATE users SET gold = 0 WHERE discord_id = @id", new { id = (long)U });
await ShopModule.ExecuteSellAsync(items, shop, events, U, "Espada de Madera", 1);
await GiveRecipe(U, "Espada de Madera");
var reforge = await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Espada de Madera");
Check(reforge.Embed is not null && await Worn(U, "weapon") == "Espada de Madera", "vender la equipada y forjar otra: el ciclo vender -> forjar anda");
// Una copia suelta en el inventario (de antes del cambio) todavía se puede vender
await Make(U, "Guerrero", 5, gold: 0); await Give(U, "Espada de Madera", 1);
var legacy = await ShopModule.ExecuteSellAsync(items, shop, events, U, "Espada de Madera", 1);
Check(legacy.Embed is not null && await Have(U, "Espada de Madera") == 0 && await GoldOf(U) == sword.SellPrice, "una copia suelta en el inventario (de antes del cambio) se vende como siempre");
// sellall no toca lo equipado
await Make(U, "Guerrero", 5, gold: 0); await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId }); await Give(U, "Piedra", 3);
await ShopModule.ExecuteSellAllAsync(shop, events, U);
Check(await Worn(U, "weapon") == "Espada de Madera" && await Have(U, "Piedra") == 0, "/shop sellall vende la mochila y NO toca lo equipado");

Console.WriteLine("\n== 4. La taberna ==");
var sellerItems = sp.GetRequiredService<IItemRepository>();
await Make(U, "Guerrero", 5, gold: 500); var amuleto1 = (await items.GetByNameAsync("Hombreras de Cuero Grueso"))!;
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId, a = amuleto1.ItemId }); await Give(U, "Piedra", 4);
var scene = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, U, null);
var sellMenu = scene.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId.StartsWith("taberna_sell"));
var opts = sellMenu.Options.ToList();
Check(opts.Count == 3 && opts[0].Value == "Espada de Madera" && opts[0].Label.Contains("equipada") && opts[0].Description.Contains("⚠️") && opts[1].Value == "Hombreras de Cuero Grueso" && opts[1].Label.Contains("equipado") && opts[2].Value == "Piedra", $"la lista de vender ofrece primero lo EQUIPADO (con aviso) y después la mochila ({string.Join(" | ", opts.Select(o => o.Label))})");
Check(opts.All(o => o.Label.Length <= 100 && o.Description.Length <= 100 && o.Emote is null) && sellMenu.Options.Count <= 25, "las opciones respetan los límites de Discord");
var confirmEquipped = await TabernaModule.BuildGearSellConfirmationAsync(users, items, U, "Espada de Madera");
var confirmButtons = confirmEquipped!.Value.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(confirmEquipped is not null && confirmEquipped.Value.Embed.Title.Contains("¿Vender tu arma?") && confirmEquipped.Value.Embed.Description.Contains($"**{sword.SellPrice}** de oro") && confirmButtons.Count == 2 && confirmButtons[0].CustomId == $"taberna_gearyes:{U}:{sword.ItemId}" && confirmButtons[1].CustomId == $"taberna_gearno:{U}",
    "vender lo equipado primero pregunta (cuánto te pagan y qué perdés) con dos botones que llevan tu id");
Console.WriteLine("   " + confirmEquipped!.Value.Embed.Description.Replace("\n", " / "));
Check(await TabernaModule.BuildGearSellConfirmationAsync(users, items, U, "Piedra") is null && await TabernaModule.BuildGearSellConfirmationAsync(users, items, U, "Hoja de Acero Puro") is null && await TabernaModule.BuildGearSellConfirmationAsync(users, items, U, "Cosa inventada") is null, "lo que NO es lo equipado (un material, otra arma, algo inexistente) se vende directo, sin preguntar");
Check(await Worn(U, "weapon") == "Espada de Madera", "preguntar no vendió nada");
string talk = await TabernaModule.ExecuteActionAsync("taberna_sell", users, items, inventory, shop, buffs, combat, events, U, "Espada de Madera");
Check(talk.Contains("Vendiste tu arma equipada") && await Worn(U, "weapon") is null, "confirmar ('Sí, venderla') vende el arma");
var sceneAfter = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, U, null);
var sellAfter = sceneAfter.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId.StartsWith("taberna_sell")).Options.Select(o => o.Value).ToList();
Check(sellAfter.SequenceEqual(["Hombreras de Cuero Grueso", "Piedra"]), "la lista se rearma sin el arma que vendiste");

Console.WriteLine("\n== 5. La lista de la herrería sabe qué casillero está ocupado ==");
await Make(U, "Guerrero", 5, gold: 1000);
foreach (var r in new[] { "Espada de Madera", "Hoja de Acero Puro", "Hombreras de Cuero Grueso" }) await GiveRecipe(U, r);
async Task<IReadOnlyList<Discord.AutocompleteResult>> Choices()
{
    var p = (await users.GetByDiscordIdAsync(U))!;
    var owned = (await inventory.GetByDiscordIdAsync(U)).ToDictionary(e => e.ItemName, e => e.Quantity);
    return ForgeChoices.For(await recipes.GetAllAsync(), zoneList, p, owned, "");
}

var free = await Choices();
Check(free.Count == 3 && free.Where(c => c.Name.StartsWith("✅")).Count() >= 3, $"sin nada puesto, lo que tenés para forjar sale con ✅ ({string.Join(" | ", free.Select(c => c.Name.Split('—')[0].Trim()))})");
await ForgeModule.ExecuteMakeAsync(users, recipes, crafting, events, U, "Espada de Madera");
await GiveRecipe(U, "Espada de Madera");
var withWeapon = await Choices();
var sameC = withWeapon.Single(c => c.Value.ToString() == "Espada de Madera"); var otherW = withWeapon.Single(c => c.Value.ToString() == "Hoja de Acero Puro"); var amuC = withWeapon.Single(c => c.Value.ToString() == "Hombreras de Cuero Grueso");
Check(sameC.Name.StartsWith("🟢") && sameC.Name.Contains("ya la llevás equipada") && otherW.Name.StartsWith("🔁") && otherW.Name.Contains("primero vendé tu arma equipada") && amuC.Name.StartsWith("✅"), $"con una espada puesta: la espada dice 🟢 ya equipada, la otra arma 🔁 vendé primero, y el amuleto sigue ✅ ({otherW.Name})");
Check(withWeapon.ToList().FindIndex(c => c.Value.ToString() == "Hombreras de Cuero Grueso") < withWeapon.ToList().FindIndex(c => c.Value.ToString() == "Hoja de Acero Puro") && withWeapon.ToList().FindIndex(c => c.Value.ToString() == "Hoja de Acero Puro") < withWeapon.ToList().FindIndex(c => c.Value.ToString() == "Espada de Madera") && withWeapon.All(c => c.Name.Length <= 100),
    "el orden: lo que se puede forjar ya, después lo que pide vender, al final la que ya llevás (y todas dentro de 100 caracteres)");

Console.WriteLine("\n== 6. El consejo (/tips) con equipo puesto ==");
var advisor = sp.GetRequiredService<IFarmAdvisor>();
await Make(U, "Guerrero", 5, gold: 5000);
// Con la Hoja general puesta (+9, SIN boost) la Espada de Madera de su clase (+9 x1,5) sí MEJORA: se la aconseja, pero hay que vender primero.
await GiveRecipe(U, "Espada de Madera");
var hoja = (await items.GetByNameAsync("Hoja de Acero Puro"))!;
await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = hoja.ItemId });
var adviceReady = await advisor.AdviceAsync(U);
Check(adviceReady is { CraftableNow: true } && adviceReady.RecipeName == "Espada de Madera" && adviceReady.SellFirst == "tu arma equipada", $"con la Hoja general puesta y todo para la Espada de su clase: «lista, pero primero vendé tu arma equipada» ({adviceReady?.RecipeName}, vender: {adviceReady?.SellFirst})");
var (adviceTitle, adviceValue) = FarmAdvisor.ToField(adviceReady!);
Check(adviceValue.Contains("primero vendé tu arma equipada en `/taberna`"), "el texto del consejo lo dice");
await Make(U, "Guerrero", 5, gold: 5000);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId });
var adviceBest = await advisor.AdviceAsync(U);
Check(adviceBest is null || (adviceBest.RecipeName != "Hoja de Acero Puro" && adviceBest.RecipeName != "Espada de Madera" && adviceBest.RecipeName != "Daga Oxidada" && adviceBest.RecipeName != "Arco Corto de Sauce" && adviceBest.RecipeName != "Grimorio Desgastado"), $"con la mejor arma de la zona puesta no se aconseja ninguna arma de esa zona (aconseja: {adviceBest?.RecipeName ?? "nada"})");
await Make(U, "Guerrero", 5, gold: 5000);
var adviceFresh = await advisor.AdviceAsync(U);
Check(adviceFresh is not null, "sin nada puesto, se aconseja algo (el flujo de siempre)");

Console.WriteLine("\n== 7. Perfil e inventario ==");
await Make(U, "Guerrero", 5, gold: 321); var hojaItem = hoja; var levantador = amuleto1;
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a, daily_streak = 3 WHERE discord_id = @id", new { id = (long)U, w = hojaItem.ItemId, a = levantador.ItemId });
var profile = await GameModule.BuildProfileEmbedAsync(users, items, zones, buffs, U, "Tester", "https://example.com/a.png");
var names = profile.Fields.Select(f => f.Name).ToList();
string Z(string s) => s.Replace(((char)0x200B).ToString(), "·");
Check(Z(string.Join(",", names)).StartsWith("📊 Experiencia,❤️ Vida,⚔️ Ataque,🛡️ Defensa,💰 Oro,🎁 Racha,✨ Habilidad"), $"el perfil (v0.9.1, apilado y sin campos en blanco): XP, Vida, Ataque, Defensa, Oro, Racha, Habilidad; la zona va debajo del título ({Z(string.Join(" / ", names))})");
var atk = profile.Fields.Single(f => f.Name.Contains("Ataque")); var def = profile.Fields.Single(f => f.Name.Contains("Defensa")); var oro = profile.Fields.Single(f => f.Name.Contains("Oro")); var racha = profile.Fields.Single(f => f.Name.Contains("Racha"));
Check(!atk.Inline && !def.Inline && !oro.Inline && !atk.Value.Contains("🗡️") && atk.Value.Contains("<:hoja_acero_pura:") && atk.Value.Contains("Hoja de Acero Puro") && !def.Value.Contains("📿") && def.Value.Contains("<:amuleto_hombreras:") && def.Value.Contains("Hombreras de Cuero Grueso") && oro.Value.Contains("321") && racha.Value.Contains("día 3") && !atk.Value.Contains("Amuleto"),
    "el arma va debajo del ataque, el amuleto debajo de la defensa, el oro y la racha en su propio renglón, y con su emoji propio SIN el 🗡️ / 📿 de adelante");
Console.WriteLine("   " + string.Join(" | ", new[] { atk, def, oro, racha }.Select(f => f.Name + ": " + f.Value.Replace("\n", " / "))));
Check(!profile.Fields.Any(f => f.Name.Contains("PvP") || f.Name == "🗡️ Arma" || f.Name == "📿 Amuleto") && profile.Length < 6000, "ya no hay campos sueltos de Arma / Amuleto ni de PvP en el perfil");
await Make(U, "Guerrero", 5, gold: 0);
var profileBare = await GameModule.BuildProfileEmbedAsync(users, items, zones, buffs, U, "Tester", "https://example.com/a.png");
Check(profileBare.Fields.Single(f => f.Name.Contains("Ataque")).Value.Contains("Sin arma") && profileBare.Fields.Single(f => f.Name.Contains("Defensa")).Value.Contains("Sin amuleto") && profileBare.Fields.Single(f => f.Name.Contains("Racha")).Value.Contains("ninguna"), "sin equipo ni racha lo dice en vez de quedar vacío");
await Give(U, "Espada de Madera", 1); await Give(U, "Hombreras de Cuero Grueso", 1); await Give(U, "Piedra", 2);
var inv = await GameModule.BuildInventoryEmbedAsync(inventory, U, "Tester");
string invText13 = string.Join("\n", inv.Fields.Select(f => f.Name + "\n" + f.Value));
Check(!invText13.Contains("Armas") && !invText13.Contains("Amuletos") && !invText13.Contains("Espada de Madera") && !invText13.Contains("Hombreras de Cuero Grueso") && invText13.Contains("Piedra"), "el inventario no muestra armas ni amuletos (aunque quedara una copia vieja)");

Console.WriteLine("\n== 8. Historial por juego (puro) ==");
var totals = new List<EventTotal>
{
    new("hunt_win", null, 110, 110), new("fight_lost", "hunt", 10, 10), new("travel_win", null, 5, 5), new("boss_win", null, 2, 2), new("fight_lost", "boss", 3, 3),
    new("duel_win", "Beto", 4, 4), new("duel_loss", "Caro", 1, 1), new("arena_join", "2026-10-01", 3, 3), new("arena_win", "2026-10-01", 1, 1),
    new("casino_win", "slots", 12, 2400), new("casino_loss", "slots", 28, 1100), new("casino_win", "coinflip", 3, 90), new("casino_loss", "coinflip", 9, 300),
    new("raid_win", null, 0, 0),
};
var report = GameHistory.Build(totals);
Check(report.Games.Select(g => g.Name).SequenceEqual(["Cacería", "Viaje", "Jefe", "Duelos", "Tragamonedas", "Cara o cruz"]) && report.ArenaJoined == 3 && report.ArenaTitles == 1, $"solo los juegos jugados y en orden fijo; la raid sin partidas no aparece ({string.Join(", ", report.Games.Select(g => g.Name))})");
var hunt = GameHistory.Describe(report.Games[0]); var slots = GameHistory.Describe(report.Games[4]); var flip = GameHistory.Describe(report.Games[5]);
Check(hunt.Title == "🏹 Cacería · 120 veces" && hunt.Value.Contains("**110** ganada(s)") && hunt.Value.Contains("**10** perdida(s)") && hunt.Value.Contains("92%"), $"cacería: {hunt.Title} / {hunt.Value.Replace("\n", " / ")}");
Check(slots.Value.Contains("+2.400") && slots.Value.Contains("−1.100") && slots.Value.Contains("📈 Balance: **+1.300**") && flip.Value.Contains("📉 Balance: **−210**"), $"casino: oro ganado y perdido con punto de miles, y balance con signo ({slots.Value.Replace("\n", " / ")} || {flip.Value.Replace("\n", " / ")})");
Check(GameHistory.Number(1234567) == "1.234.567" && GameHistory.Number(0) == "0" && GameHistory.Number(999) == "999", "los números llevan punto de miles, sin depender de la cultura del servidor");
Check(GameHistory.DescribeArena(report)!.Value.Value.Contains("**3** torneo") && GameHistory.DescribeArena(report)!.Value.Value.Contains("**1** campeonato") && GameHistory.DescribeArena(GameHistory.Build([])) is null, "la arena: torneos y campeonatos; sin haberse anotado nunca no aparece");
Check(GameHistory.Build([]).Games.Count == 0 && GameHistory.Describe(new GameRecord("🏹", "X", 1, 0)).Title.EndsWith("1 vez"), "sin eventos no hay juegos, y 1 se escribe 'vez'");

Console.WriteLine("\n== 9. Historial con la base real ==");
await Make(U, "Guerrero", 5, gold: 100000);
var ghostEmbed = await HistoryModule.BuildHistoryEmbedAsync(users, eventRepo, (ulong)BaseId + 998, "Fantasma");
Check(ghostEmbed.Description.Contains("/start") || ghostEmbed.Description.Contains("empezó"), "un jugador sin cuenta: se le dice, sin crearle nada");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users WHERE discord_id = @id", new { id = BaseId + 998 }) == 0, "mirar un historial no crea cuentas");
var empty = await HistoryModule.BuildHistoryEmbedAsync(users, eventRepo, U, "Tester");
Check(empty.Description.Contains("Todavía no hay nada") && empty.Fields.Length == 0, "sin jugadas: lo dice con una sugerencia");
for (int i = 0; i < 3; i++) await realEvents.RecordAsync(U, GameEventKinds.HuntWin);
await realEvents.RecordAsync(U, GameEventKinds.FightLost, detail: "hunt");
await realEvents.RecordAsync(U, GameEventKinds.FightLost, detail: "raid");
await realEvents.RecordAsync(U, GameEventKinds.DuelWin, detail: "Beto"); await realEvents.RecordAsync(U, GameEventKinds.DuelWin, detail: "Caro"); await realEvents.RecordAsync(U, GameEventKinds.DuelLoss, detail: "Dani");
await realEvents.RecordAsync(U, GameEventKinds.ArenaJoin, detail: "2026-10-01");
var embed = await HistoryModule.BuildHistoryEmbedAsync(users, eventRepo, U, "Tester");
var fieldNames = embed.Fields.Select(f => f.Name).Where(n => n != ((char)0x200B).ToString()).ToList();
Check(embed.Title == "📜 Historial de Tester" && fieldNames.SequenceEqual(["🏹 Cacería · 4 veces", "🛡️ Raid · 1 vez", "🥊 Duelos · 3 veces", "🏟️ Arena"]), $"el historial real: {string.Join(" | ", fieldNames)}");
Check(embed.Fields.Where(f => f.Name == ((char)0x200B).ToString()).Count() == 3 && embed.Footer!.Value.Text.Contains("/duels") && embed.Length < 6000, "una fila en blanco entre juegos, y el pie manda a /duels");
Console.WriteLine("   " + string.Join("\n   ", embed.Fields.Where(f => f.Name != ((char)0x200B).ToString()).Select(f => f.Name + " -> " + f.Value.Replace("\n", " / "))));

Console.WriteLine("\n== 10. Duelos con la base real ==");
var duelsEmbed = await HistoryModule.BuildDuelsEmbedAsync(users, eventRepo, U, "Tester", DateTime.UtcNow);
Check(duelsEmbed.Title == "🥊 Duelos de Tester" && duelsEmbed.Description.Contains("**2** ganado(s)") && duelsEmbed.Description.Contains("**1** perdido(s)") && duelsEmbed.Description.Contains("67%") && duelsEmbed.Description.Contains("Le ganaste a **Beto**") && duelsEmbed.Description.Contains("Perdiste contra **Dani**") && duelsEmbed.Description.Contains("<t:"), "récord, porcentaje y los últimos rivales con hora relativa");
Check(duelsEmbed.Description.IndexOf("**Dani**") < duelsEmbed.Description.IndexOf("**Caro**") && duelsEmbed.Description.IndexOf("**Caro**") < duelsEmbed.Description.IndexOf("**Beto**"), "del más nuevo al más viejo");
Console.WriteLine("   " + duelsEmbed.Description.Replace("\n", "\n   "));
for (int i = 0; i < 3; i++) await realEvents.RecordAsync(U, GameEventKinds.DuelWin, detail: $"Rival{i}");
var streak = await HistoryModule.BuildDuelsEmbedAsync(users, eventRepo, U, "Tester", DateTime.UtcNow);
Check(streak.Description.Contains("Racha: **3** victorias seguidas"), "una racha de victorias se destaca");
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id AND kind IN ('duel_win','duel_loss')", new { id = (long)U });
Check((await HistoryModule.BuildDuelsEmbedAsync(users, eventRepo, U, "Tester", DateTime.UtcNow)).Description.Contains("/fight"), "sin duelos invita a desafiar");
for (int i = 0; i < 14; i++) await realEvents.RecordAsync(U, i % 2 == 0 ? GameEventKinds.DuelWin : GameEventKinds.DuelLoss, detail: $"R{i}");
var many = await HistoryModule.BuildDuelsEmbedAsync(users, eventRepo, U, "Tester", DateTime.UtcNow);
Check(many.Description.Split('\n').Count(l => l.StartsWith("✅ Le ganaste") || l.StartsWith("❌ Perdiste")) == 10 && many.Description.Contains("R13") && !many.Description.Contains("R3 ") && many.Description.Length < 4096, "con 14 duelos lista solo los últimos 10");

// El servicio de duelos deja al rival en el evento
var recorder = new FakeEvents();
var duelSvc = new DuelService(recorder);
DuelFighter F(ulong id, string name) => new(id, name, 5, "Guerrero", new CombatantProfile(30, 5, "Guerrero", ClassPassives.For("Guerrero")), 100);
var session = duelSvc.TryStart(F(701, "Ana"), F(702, "Beto"), new FakeTarget(), s => DuelModule.Render(s))!;
duelSvc.TryForfeit(session.Id, 702, new FakeTarget());
await Task.Delay(100);
Check(recorder.DetailOf(701, GameEventKinds.DuelWin) == "Beto" && recorder.DetailOf(702, GameEventKinds.DuelLoss) == "Ana", "el duelo registra contra quién se ganó y se perdió (el nombre del rival)");

Console.WriteLine("\n== 11. El casino registra cada jugada ==");
await Make(U, "Guerrero", 5, gold: 100000);
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
var casino = sp.GetRequiredService<ICasinoService>(); var casinoRepo = sp.GetRequiredService<ICasinoRepository>();
int goldStart = await GoldOf(U);
for (int i = 0; i < 40; i++)
{
    await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, i % 2 == 0 ? "slots" : "coinflip", 10 + i, "heads", realEvents);
}

var casinoTotals = await eventRepo.GetBreakdownAsync(U, [GameEventKinds.CasinoWin, GameEventKinds.CasinoLoss]);
long won = casinoTotals.Where(t => t.Kind == GameEventKinds.CasinoWin).Sum(t => t.Amount), lost = casinoTotals.Where(t => t.Kind == GameEventKinds.CasinoLoss).Sum(t => t.Amount);
long plays = casinoTotals.Sum(t => t.Count);
Check(plays == 40 && casinoTotals.All(t => t.Detail is "slots" or "coinflip") && await GoldOf(U) == goldStart + won - lost, $"40 jugadas, 40 eventos; el oro final coincide con lo registrado (+{won} ganado, −{lost} perdido => {await GoldOf(U)} = {goldStart} + {won - lost})");
var casinoHistory = GameHistory.Build(casinoTotals);
Check(casinoHistory.Games.Sum(g => g.Played) == 40 && casinoHistory.Games.All(g => g.IsCasino) && casinoHistory.Games.Sum(g => g.GoldWon - g.GoldLost) == won - lost, "el historial del casino suma las 40 jugadas y el balance exacto");

Console.WriteLine("\n== 12. La migración de armas y amuletos del inventario ==");
await Cleanup();
var gearIds = new Dictionary<string, int>();
foreach (var n in new[] { "Espada de Madera", "Hoja de Acero Puro", "Daga Oxidada", "Grimorio Desgastado", "Hombreras de Cuero Grueso", "Talismán de Ceniza Bendita", "Arco Corto de Sauce" }) gearIds[n] = (await items.GetByNameAsync(n))!.ItemId;
int Sell(string n) => db.ExecuteScalar<int>("SELECT sell_price FROM items WHERE name = @n", new { n });
// A: lleva puesta la Espada (con su copia en el inventario) + tiene de más una Hoja, y 2 amuletos sin nada puesto
ulong A = (ulong)BaseId + 11; await Make(A, "Guerrero", 5, gold: 100);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)A, w = gearIds["Espada de Madera"] });
await Give(A, "Espada de Madera", 1); await Give(A, "Hoja de Acero Puro", 1); await Give(A, "Hombreras de Cuero Grueso", 1); await Give(A, "Talismán de Ceniza Bendita", 1); await Give(A, "Piedra", 5);
// B: no lleva nada; tiene un arma de su familia (Daga, con sinergia) y otra de otra familia con bastante más ataque
ulong B = (ulong)BaseId + 12; await Make(B, "Ninja", 5, gold: 0);
await Give(B, "Daga Oxidada", 1); await Give(B, "Hoja de Acero Puro", 1);
// C: una copia de más de la misma arma equipada
ulong C = (ulong)BaseId + 13; await Make(C, "Arquero", 5, gold: 0);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)C, w = gearIds["Arco Corto de Sauce"] }); await Give(C, "Arco Corto de Sauce", 3);
string migration = File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database/move_gear_to_equipment.sql"));
await db.ExecuteAsync(migration);
Check(await Worn(A, "weapon") == "Espada de Madera" && await Have(A, "Espada de Madera") == 0 && await Have(A, "Hoja de Acero Puro") == 0 && await Have(A, "Piedra") == 5, "A: lo equipado se queda, su copia del inventario se borra, y los materiales no se tocan");
Check(await Worn(A, "amulet") == "Talismán de Ceniza Bendita" && await Have(A, "Hombreras de Cuero Grueso") == 0, "A: sin amuleto puesto, se equipa el de más defensa del inventario (Hombreras +16 contra Levantador +10)");
Check(await GoldOf(A) == 100 + Sell("Hoja de Acero Puro") + Sell("Hombreras de Cuero Grueso"), $"A: lo que sobraba (la Hoja y el Levantador) se pagó a su precio de venta (oro {await GoldOf(A)})");
Check(await Worn(B, "weapon") == "Daga Oxidada" && await Have(B, "Hoja de Acero Puro") == 0 && await GoldOf(B) == Sell("Hoja de Acero Puro"), "B (Ninja): se equipa la que MÁS rinde contando la sinergia (la Daga +9 x1,5 = 14 le gana a la Hoja general +9 sin boost) y la otra se vende");
Check(await Worn(C, "weapon") == "Arco Corto de Sauce" && await Have(C, "Arco Corto de Sauce") == 0 && await GoldOf(C) == 2 * Sell("Arco Corto de Sauce"), "C: una copia era la equipada y las 2 de más se pagan");
long goldAfter = await db.ExecuteScalarAsync<long>("SELECT SUM(gold) FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
await db.ExecuteAsync(migration);
Check(await db.ExecuteScalarAsync<long>("SELECT SUM(gold) FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 }) == goldAfter && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory inv JOIN items i USING (item_id) WHERE i.type IN ('Weapon','Amulet')") == 0, "correrla dos veces no vuelve a pagar nada, y no queda ningún arma ni amuleto en el inventario de nadie");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

public class FakeProxy : DispatchProxy
{
    private Func<string, object?[], object?> _h = null!;
    public static object Create(Type iface, Func<string, object?[], object?> h)
    {
        var create = typeof(DispatchProxy).GetMethod("Create", 2, [])!.MakeGenericMethod(iface, typeof(FakeProxy));
        var proxy = create.Invoke(null, null)!;
        ((FakeProxy)proxy)._h = h;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? a) => _h(m!.Name, a ?? []);
}

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
}

sealed class FakeEvents : IGameEvents
{
    private readonly ConcurrentBag<(ulong Id, string Kind, string? Detail)> _recorded = [];
    public string? DetailOf(ulong id, string kind) => _recorded.FirstOrDefault(r => r.Id == id && r.Kind == kind).Detail;

    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null)
    {
        _recorded.Add((discordId, kind, detail));
        return Task.CompletedTask;
    }

    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}

static class DbExt
{
    public static int ExecuteScalar<T>(this NpgsqlConnection db, string sql, object p) => Dapper.SqlMapper.ExecuteScalar<int>(db, sql, p);
}
