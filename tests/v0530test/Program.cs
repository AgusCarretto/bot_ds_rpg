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

// v0.53.0 (tres arreglos del dueño, 2026-11-12): (1) «sell all» pregunta antes («¿estás seguro?») y vende TAMBIÉN el arma y el amuleto puestos, con lo que paga cada cosa a la vista; (2) /stats y «aa stats» de OTRO jugador
// (mención, ID, nombre exacto, o respondiendo su mensaje); (3) las horas de expedición de una mascota solo cuentan para evolucionar cuando está en el nivel tope de su tier, y los textos lo dicen.
// Contra la base LOCAL (usuarios descartables) y con Discord.Net de verdad para el registro de comandos y de botones.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => LeakCheck.AllText(e);
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

const ulong U = 999000000000053001UL, T = 999000000000053002UL, GHOST = 999000000000053999UL;
ulong[] all = [U, T, GHOST];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(StatsModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var shop = sp.GetRequiredService<IShopRepository>();
var events = sp.GetRequiredService<IGameEvents>();
var petRepo = sp.GetRequiredService<IPetRepository>();
var expRepo = sp.GetRequiredService<IPetExpeditionRepository>();
var buffs = sp.GetRequiredService<IBuffRepository>();
var bonusSvc = sp.GetRequiredService<IPlayerBonusService>();
var cooking = sp.GetRequiredService<ICookingRepository>();
var dragons = sp.GetRequiredService<IDragonRepository>();
var zoneBoxes = sp.GetRequiredService<IZoneBoxService>();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

async Task Clean()
{
    foreach (var id in all)
    {
        await db.ExecuteAsync("DELETE FROM pet_expeditions WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

async Task Give(ulong id, string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)id, n = name, q = qty });
async Task<int> Have(ulong id, string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
IEnumerable<IMessageComponent> Flat(MessageComponent? c) => c is null ? [] : c.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components);
List<ButtonComponent> Buttons(MessageComponent? c) => Flat(c).OfType<ButtonComponent>().ToList();

var weapon = await db.QuerySingleAsync<(int Id, string Name, int Price)>("SELECT item_id, name, sell_price FROM items WHERE type = 'Weapon' AND sell_price > 0 ORDER BY item_id LIMIT 1");
var amulet = await db.QuerySingleAsync<(int Id, string Name, int Price)>("SELECT item_id, name, sell_price FROM items WHERE type = 'Amulet' AND sell_price > 0 ORDER BY item_id LIMIT 1");
string boxName = await db.ExecuteScalarAsync<string>("SELECT name FROM items WHERE type = 'Caja' ORDER BY item_id LIMIT 1") ?? "?";
int pinePrice = (await items.GetByNameAsync("Madera de Pino"))!.SellPrice, oakPrice = (await items.GetByNameAsync("Madera de Roble"))!.SellPrice;

await Clean();

// =====================================================================================================================================================================================================
Console.WriteLine("== 1. Pets: las horas de expedición solo cuentan en el nivel tope de su tier ==");
Check(PetRules.CountsForEvolution(PetRules.PointsToReach(10), 1) && !PetRules.CountsForEvolution(PetRules.PointsToReach(9), 1)
    && !PetRules.CountsForEvolution(PetRules.PointsToReach(10), 2) && !PetRules.CountsForEvolution(PetRules.PointsToReach(19), 2) && PetRules.CountsForEvolution(PetRules.PointsToReach(20), 2)
    && !PetRules.CountsForEvolution(PetRules.PointsToReach(29), 3) && PetRules.CountsForEvolution(PetRules.PointsToReach(30), 3),
    "regla pura: tier I cuenta desde el nivel 10, tier II solo en el 20 (no en 10 a 19) y tier III en el 30");

await users.GetOrCreateUserAsync(U);
int speciesId = await db.ExecuteScalarAsync<int>("SELECT species_id FROM pet_species WHERE bonus_kind = 'gold' ORDER BY zone_id LIMIT 1");
async Task SetPet(int level, int tier, int hours = 0) => await db.ExecuteAsync(
    "INSERT INTO player_pets (discord_id, species_id, feed_points, tier, expedition_hours) VALUES (@u, @s, @p, @t, @h) ON CONFLICT (discord_id, species_id) DO UPDATE SET feed_points = @p, tier = @t, expedition_hours = @h",
    new { u = (long)U, s = speciesId, p = PetRules.PointsToReach(level), t = tier, h = hours });
async Task PutExpedition(int hours) => await db.ExecuteAsync(
    "INSERT INTO pet_expeditions (discord_id, species_id, hours, ends_at) VALUES (@u, @s, @h, now() - interval '1 minute') ON CONFLICT (discord_id) DO UPDATE SET species_id = @s, hours = @h, ends_at = now() - interval '1 minute'", new { u = (long)U, s = speciesId, h = hours });
async Task<int> Hours() => await db.ExecuteScalarAsync<int>("SELECT expedition_hours FROM player_pets WHERE discord_id = @u AND species_id = @s", new { u = (long)U, s = speciesId });

await SetPet(10, 1); await PutExpedition(4);
var c1 = await expRepo.ClaimAsync(U, new Random(1));
Check(c1.Status == ExpeditionClaimStatus.Ok && c1.HoursCounted && await Hours() == 4, "tier I en el nivel 10 (su tope): las 4 horas suman");
await SetPet(15, 2, 100); await PutExpedition(4);
var c2 = await expRepo.ClaimAsync(U, new Random(1));
Check(c2.Status == ExpeditionClaimStatus.Ok && !c2.HoursCounted && c2.PetLevel == 15 && c2.LevelCap == 20 && await Hours() == 100, "tier II en el nivel 15 (de 20): el premio se paga pero las horas NO suman (siguen en 100)");
await SetPet(20, 2, 100); await PutExpedition(8);
var c3 = await expRepo.ClaimAsync(U, new Random(1));
Check(c3.Status == ExpeditionClaimStatus.Ok && c3.HoursCounted && await Hours() == 108, "tier II en el nivel 20 (su tope): las 8 horas suman (108)");
await SetPet(15, 2, 100); await PutExpedition(1);
int goldBefore = await Gold(U);
var c4 = await expRepo.ClaimAsync(U, new Random(1));
Check(!c4.HoursCounted && c4.Receipt!.Reward.Gold > 0 && await Gold(U) == goldBefore + c4.Receipt.Reward.Gold, "que las horas no sumen no le saca el oro ni la EXP de la expedición");

var player = (await users.GetByDiscordIdAsync(U))!;
await SetPet(15, 2, 100); await PutExpedition(4);
var shownNo = await PetModule.ClaimExpeditionAsync(expRepo, events, player, new Random(2));
string shownNoText = AllText(shownNo.Embed!);
await SetPet(20, 2, 100); await PutExpedition(4);
var shownYes = await PetModule.ClaimExpeditionAsync(expRepo, events, player, new Random(2));
Check(shownNoText.Contains("no cuentan para evolucionar") && shownNoText.Contains("15/20") && !AllText(shownYes.Embed!).Contains("no cuentan para evolucionar"),
    "al volver, la pantalla avisa que esas horas no cuentan (con el nivel «15/20»); en el tope no dice nada");

await SetPet(15, 2, 100);
var viewLow = await PetModule.ExecuteViewAsync(users, petRepo, inventory, U);
await SetPet(20, 2, 100);
var viewCap = await PetModule.ExecuteViewAsync(users, petRepo, inventory, U);
await SetPet(5, 1, 0);
var viewNew = await PetModule.ExecuteViewAsync(users, petRepo, inventory, U);
Check(AllText(viewLow.Embed!).Contains("solo cuentan para evolucionar") && !AllText(viewCap.Embed!).Contains("solo cuentan para evolucionar") && !AllText(viewNew.Embed!).Contains("solo cuentan para evolucionar"),
    "/pet view lo dice en la mascota que puede salir de expedición pero todavía no está en su tope (tier II nivel 15), y no donde no corresponde (en el tope, o una de nivel 5)");
await SetPet(10, 1);
var ownedNow = await petRepo.GetOwnedAsync(U);
var menu = PetModule.BuildExpeditionMenuEmbed(new PetModule.ExpeditionMenuData(100, ownedNow, []));
Check(menu.Footer!.Value.Text.Contains("nivel tope") || AllText(menu).Contains("nivel tope"), "el menú de expediciones también lo explica");
using (Loc.Use("en"))
{
    await SetPet(15, 2, 100); await PutExpedition(4);
    var noEn = await PetModule.ClaimExpeditionAsync(expRepo, events, player, new Random(2));
    var viewEn = await PetModule.ExecuteViewAsync(users, petRepo, inventory, U);
    var menuEn = PetModule.BuildExpeditionMenuEmbed(new PetModule.ExpeditionMenuData(100, await petRepo.GetOwnedAsync(U), []));
    string text = AllText(noEn.Embed!) + " " + AllText(viewEn.Embed!) + " " + AllText(menuEn);
    var leak = LeakCheck.NoSpanish(text, ownedNow.Select(p => p.Species.Name).Concat(["Ñandusito", "Alimentar", PetRules.FoodItemName]));
    Check(text.Contains("don't count") || text.Contains("do not count"), "en inglés: el aviso de las horas también está");
    Check(leak.Ok, $"y no queda español suelto ({leak.Found})");
}

// =====================================================================================================================================================================================================
Console.WriteLine("\n== 2. «Sell all»: pregunta antes y vende también el arma y el amuleto puestos ==");
async Task ResetSeller()
{
    await Clean();
    await users.GetOrCreateUserAsync(U); await users.GetOrCreateUserAsync(T);
    await db.ExecuteAsync("UPDATE users SET gold = 0, weapon_id = @w, amulet_id = @a, weapon_enchant = 3, weapon_enchant_kind = 'filo', amulet_enchant = 2, amulet_enchant_kind = 'guarda' WHERE discord_id = @id", new { id = (long)U, w = weapon.Id, a = amulet.Id });
    await Give(U, "Madera de Pino", 5); await Give(U, "Madera de Roble", 3); await Give(U, PetRules.FoodItemName, 4); await Give(U, boxName, 2);
}

int bagGold = (5 * pinePrice) + (3 * oakPrice);
await ResetSeller();
var preview = await shop.PreviewSellAllAsync(U);
Check(preview.ItemsCount == 8 && preview.BagGold == bagGold && preview.KeptCount == 4 && preview.Gear.Count == 2 && preview.Gear[0].IsWeapon && preview.Gear[0].Name == weapon.Name && preview.Gear[0].SellPrice == weapon.Price
    && !preview.Gear[1].IsWeapon && preview.Gear[1].Name == amulet.Name && preview.TotalGold == bagGold + weapon.Price + amulet.Price && !preview.IsEmpty,
    $"la vista previa cuenta la mochila (8 unidades, {bagGold} de oro), lo que se conserva en el Fuego Nuevo (4), el arma y el amuleto puestos y el total ({preview.TotalGold})");
var userAfterPreview = await users.GetByDiscordIdAsync(U);
Check(userAfterPreview!.WeaponId == weapon.Id && userAfterPreview.AmuletId == amulet.Id && await Gold(U) == 0 && await Have(U, "Madera de Pino") == 5, "mirar la vista previa no vende nada");
var previewT = await shop.PreviewSellAllAsync(T);
Check(previewT.IsEmpty && previewT.Gear.Count == 0 && previewT.TotalGold == 0, "sin nada en la mochila ni puesto: la vista previa está vacía");
await db.ExecuteAsync("UPDATE users SET weapon_id = NULL WHERE discord_id = @id", new { id = (long)U });
var previewNoWeapon = await shop.PreviewSellAllAsync(U);
Check(previewNoWeapon.Gear.Count == 1 && !previewNoWeapon.Gear[0].IsWeapon, "sin arma puesta solo entra el amuleto");

await ResetSeller();
var confirm = await ShopModule.BuildSellAllConfirmAsync(shop, U);
string confirmText = AllText(confirm.Embed!);
var confirmButtons = Buttons(confirm.Components);
int total = bagGold + weapon.Price + amulet.Price;
Check(confirm.Embed!.Title!.Contains("¿Vender todo?") && confirmText.Contains(weapon.Name) && confirmText.Contains(amulet.Name) && confirmText.Contains(total.ToString()) && confirmText.Contains(weapon.Price.ToString()),
    $"la pantalla de confirmación dice qué se vende (el arma y el amuleto con su precio) y el total ({total})");
Check(confirmButtons.Count == 2 && confirmButtons[0].CustomId == $"shop_sellall_no:{U}" && confirmButtons[1].CustomId == $"shop_sellall_yes:{U}:{total}" && confirmButtons[0].Style == ButtonStyle.Success && confirmButtons[1].Style == ButtonStyle.Danger,
    "tiene dos botones: «No» (verde, primero) y «Sí» (rojo, con el total en el id para no vender otra cosa distinta de lo que se mostró)");
Check(await Gold(U) == 0 && (await users.GetByDiscordIdAsync(U))!.WeaponId == weapon.Id && await Have(U, "Madera de Pino") == 5, "preguntar no vende nada todavía");
var confirmEmpty = await ShopModule.BuildSellAllConfirmAsync(shop, T);
Check(confirmEmpty.Embed is null && confirmEmpty.Components is null && confirmEmpty.PlainMessage!.Contains("nada"), "sin nada para vender no pregunta: lo dice");

// Lo que pasa al apretar «Sí».
var wrongClicker = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, T, total);
Check(wrongClicker.Embed is null && wrongClicker.PlainMessage!.Contains("otra persona") && await Gold(U) == 0 && await Have(U, "Madera de Pino") == 5, "si aprieta otra persona no se vende nada");
await Give(U, "Madera de Pino", 6);
var changed = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, U, total);
Check(changed.Embed is null && changed.PlainMessage!.Contains("cambió") && await Gold(U) == 0 && await Have(U, "Madera de Pino") == 6 && (await users.GetByDiscordIdAsync(U))!.WeaponId == weapon.Id,
    "si entre la pregunta y el «Sí» cambió la mochila o el equipo (el total ya no es el mostrado), no vende nada y pide volver a empezar");
await Give(U, "Madera de Pino", 5);
var sold = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, U, total);
var after = (await users.GetByDiscordIdAsync(U))!;
Check(sold.Embed is not null && AllText(sold.Embed).Contains(weapon.Name) && AllText(sold.Embed).Contains(amulet.Name) && after.WeaponId is null && after.AmuletId is null && after.WeaponEnchant == 0 && after.AmuletEnchant == 0
    && after.WeaponEnchantKind == "filo" && after.AmuletEnchantKind == "guarda" && await Gold(U) == total,
    $"con «Sí» vende la mochila Y el arma y el amuleto puestos (pierden el encantamiento) y paga el total mostrado ({total})");
Check(await Have(U, "Madera de Pino") == 0 && await Have(U, "Madera de Roble") == 0 && await Have(U, PetRules.FoodItemName) == 4 && await Have(U, boxName) == 2,
    "lo que no se vende sigue ahí: la comida de mascotas (se conserva en el Fuego Nuevo) y las cajas (valen 0)");
Check(await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(amount), 0) FROM game_events WHERE discord_id = @id AND kind = @k AND detail = 'sellall'", new { id = (long)U, k = GameEventKinds.ShopGoldEarned }) == total, "se registra UN evento con el total vendido (mochila y equipo)");
var again = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, U, total);
Check(again.Embed is null && await Gold(U) == total, "apretar otra vez el mismo «Sí» no paga de nuevo");

// Ocho «Sí» a la vez: se vende UNA vez.
await ResetSeller();
var racers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ShopModule.ResolveSellAllConfirmAsync(shop, events, U, U, total))));
Check(racers.Count(r => r.Embed is not null) == 1 && await Gold(U) == total && (await users.GetByDiscordIdAsync(U))!.WeaponId is null, $"ocho «Sí» a la vez venden una sola vez (oro {await Gold(U)}, esperado {total})");

// La función de siempre sigue vendiendo solo la mochila (la usan otros arneses) y «aa sell all <ítem>» sigue vendiendo directo.
await ResetSeller();
var bagOnly = await ShopModule.ExecuteSellAllAsync(shop, events, U);
Check(bagOnly.Embed is not null && await Gold(U) == bagGold && (await users.GetByDiscordIdAsync(U))!.WeaponId == weapon.Id, "ExecuteSellAllAsync a secas sigue vendiendo solo la mochila (no toca lo puesto)");
await Give(U, "Madera de Pino", 3);
var direct = await TextShortcuts.ExecuteSellAsync(users, items, inventory, shop, events, U, "all Madera de Pino");
Check(direct.Embed is not null && direct.Components is null && await Have(U, "Madera de Pino") == 0, "«aa sell all <ítem>» sigue vendiendo ese ítem directo, sin preguntar");

// Las puertas: ninguna vende sin preguntar.
await ResetSeller();
var doorShortcut = await TextShortcuts.ExecuteSellAsync(users, items, inventory, shop, events, U, "all");
Check(doorShortcut.Embed is not null && doorShortcut.Embed.Title!.Contains("¿Vender todo?") && Buttons(doorShortcut.Components).Count == 2 && await Gold(U) == 0 && await Have(U, "Madera de Pino") == 5, "«aa sell all» pregunta antes (y no vende nada todavía)");
string repoRoot = TestEnv.RepoRoot;
string slashSrc = File.ReadAllText(Path.Combine(repoRoot, "Modules", "ShopModule.cs"));
string textSrc = File.ReadAllText(Path.Combine(repoRoot, "Modules", "TextCommandModule.Economy.cs"));
string slashBody = slashSrc[slashSrc.IndexOf("public async Task HandleSellAllAsync", StringComparison.Ordinal)..];
slashBody = slashBody[..slashBody.IndexOf("// Todo lo que sigue es estático", StringComparison.Ordinal)];
string textBody = textSrc[textSrc.IndexOf("public async Task ShopSellAllAsync", StringComparison.Ordinal)..];
textBody = textBody[..textBody.IndexOf("[Command(\"forge recipes\")]", StringComparison.Ordinal)];
Check(slashBody.Contains("BuildSellAllConfirmAsync") && !slashBody.Contains("ExecuteSellAllAsync") && textBody.Contains("BuildSellAllConfirmAsync") && !textBody.Contains("ExecuteSellAllAsync"),
    "/shop sellall y «aa shop sellall» llaman a la pregunta, no a la venta");

// Los botones llegan a su método (el módulo de botones no es del grupo «shop»).
await interactions.AddModulesAsync(typeof(StatsModule).Assembly, sp);
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routedYes = interactions.SearchComponentCommand(Click($"shop_sellall_yes:{U}:12345"));
var routedNo = interactions.SearchComponentCommand(Click($"shop_sellall_no:{U}"));
Check(routedYes.IsSuccess && routedYes.Command.MethodName == "HandleSellAllYesAsync" && routedNo.IsSuccess && routedNo.Command.MethodName == "HandleSellAllNoAsync", "«shop_sellall_yes:{dueño}:{total}» y «shop_sellall_no:{dueño}» llegan a su método");
var sellallSlash = interactions.SlashCommands.Single(c => c.Name == "sellall");
Check(sellallSlash.Description.Length is > 0 and <= 100 && sellallSlash.Description.Contains("equip"), $"/shop sellall dice en su descripción que incluye el equipo y entra en los 100 caracteres ({sellallSlash.Description.Length})");

using (Loc.Use("en"))
{
    await ResetSeller();
    var confirmEn = await ShopModule.BuildSellAllConfirmAsync(shop, U);
    var buttonsEn = Buttons(confirmEn.Components);
    var wrongEn = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, T, total);
    await Give(U, "Madera de Pino", 6);
    var changedEn = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, U, total);
    await Give(U, "Madera de Pino", 5);
    var soldEn = await ShopModule.ResolveSellAllConfirmAsync(shop, events, U, U, total);
    var emptyEn = await ShopModule.BuildSellAllConfirmAsync(shop, T);
    string text = AllText(confirmEn.Embed!) + " " + string.Join(" ", buttonsEn.Select(b => b.Label)) + " " + wrongEn.PlainMessage + " " + changedEn.PlainMessage + " " + AllText(soldEn.Embed!) + " " + emptyEn.PlainMessage;
    var leak = LeakCheck.NoSpanish(text, [weapon.Name, amulet.Name, "Madera de Pino", "Madera de Roble", PetRules.FoodItemName, "El Tabernero"]);
    Check(AllText(confirmEn.Embed!).Contains("Sell everything?") && buttonsEn.Select(b => b.Label).SequenceEqual(["No, cancel", "Yes, sell everything"]), "en inglés: la pregunta y los botones");
    Check(leak.Ok && LeakCheck.FitsDiscord(confirmEn.Embed!) && LeakCheck.FitsDiscord(soldEn.Embed!), $"y no queda español suelto en nada de la venta ({leak.Found})");
}

// =====================================================================================================================================================================================================
Console.WriteLine("\n== 3. /stats y «aa stats» de otro jugador ==");
await Clean();
await users.GetOrCreateUserAsync(U); await users.GetOrCreateUserAsync(T);
await db.ExecuteAsync("UPDATE users SET level = 12, fuego_nuevo = 3, weapon_id = @w WHERE discord_id = @id", new { id = (long)T, w = weapon.Id });
Task<StatsReply> Stats(ulong id, int page = 0, string? name = null, ulong? viewer = null) => StatsModule.BuildStatsAsync(users, items, buffs, bonusSvc, cooking, dragons, id, zoneBoxes, page, name, viewer);

var own = await Stats(U);
Check(own.Embed!.Title == "📊 Tus stats" && own.Embed.Description.Contains("Ataque") && (own.Components is null || Buttons(own.Components).All(b => b.CustomId!.StartsWith($"stats_page:{U}:"))), "las stats propias siguen igual: «Tus stats» y los botones de siempre (si los hay)");
var other = await Stats(T, 0, "Fulano", U);
var tPlayer = (await users.GetByDiscordIdAsync(T))!;
var tCombat = PlayerCombatProfileCalculator.Resolve(tPlayer, await items.GetByIdAsync(weapon.Id), null, 0, await bonusSvc.GetAsync(T, tPlayer.FuegoNuevo));
Check(other.Embed!.Title!.Contains("Fulano") && !other.Embed.Title.Contains("Tus stats") && other.Embed.Description.Contains($"Ataque **{tCombat.Damage}**") && other.Embed.Description.Contains($"Vida máxima **{tCombat.CombatMaxHp}**"),
    $"las stats de otro llevan su nombre en el título y SUS números (ataque {tCombat.Damage}, vida {tCombat.CombatMaxHp}), no los de quien mira");
var otherButtons = Buttons(other.Components);
Check(otherButtons.Count == 1 && otherButtons[0].CustomId == $"stats_view:{U}:1:{T}", $"el botón de «Ver más stats» lleva quién mira, la pantalla y de quién es ({otherButtons.FirstOrDefault()?.CustomId})");
var otherMore = await Stats(T, 1, "Fulano", U);
var moreButtons = Buttons(otherMore.Components);
Check(otherMore.Embed!.Title!.Contains("Fulano") && AllText(otherMore.Embed).Contains("Fuego Nuevo 3") && moreButtons.Count == 1 && moreButtons[0].CustomId == $"stats_view:{U}:0:{T}", "la segunda pantalla también es de él y el botón de volver conserva los dos ids");
var ghost = await Stats(GHOST, 0, "Fantasma", U);
Check(ghost.Embed is null && ghost.PlainMessage!.Contains("Fantasma") && ghost.PlainMessage.Contains("personaje") && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM users WHERE discord_id = @id", new { id = (long)GHOST }) == 0,
    "si el otro no tiene personaje lo dice con su nombre y NO le crea la cuenta");

Check(StatsModule.ParseTextArgs("", "") == (0, null) && StatsModule.ParseTextArgs("mas", "") == (1, null) && StatsModule.ParseTextArgs("más", "") == (1, null) && StatsModule.ParseTextArgs("<@5>", "") == (0, "<@5>")
    && StatsModule.ParseTextArgs("<@5>", "mas") == (1, "<@5>") && StatsModule.ParseTextArgs("mas", "<@5>") == (1, "<@5>") && StatsModule.ParseTextArgs("Polar", "Frost") == (0, "Polar Frost")
    && StatsModule.ParseTextArgs("Polar", "Frost mas") == (1, "Polar Frost") && StatsModule.ParseTextArgs("Polar Frost", "") == (0, "Polar Frost") && StatsModule.ParseTextArgs("2", "") == (1, null),
    "lo que se escribe después de «aa stats»: la palabra de «más» y el jugador (mención, ID o nombre) en cualquier orden");

// Registro con Discord.Net de verdad.
var statsSlash = interactions.SlashCommands.Single(c => c.Name == "stats");
Check(statsSlash.Parameters.Count == 1 && statsSlash.Parameters[0].Name == "jugador" && !statsSlash.Parameters[0].IsRequired && statsSlash.Description.Length is > 0 and <= 100, "/stats tiene UN parámetro opcional «jugador» y su descripción entra en los 100 caracteres");
var routedPage = interactions.SearchComponentCommand(Click($"stats_page:{U}:1"));
var routedView = interactions.SearchComponentCommand(Click($"stats_view:{U}:1:{T}"));
Check(routedPage.IsSuccess && routedPage.Command.MethodName == "HandlePageAsync" && routedView.IsSuccess && routedView.Command.MethodName == "HandleOtherPageAsync", "«stats_page:{dueño}:{pantalla}» sigue llegando a su método y «stats_view:{quien mira}:{pantalla}:{de quién}» al nuevo (no se pisan)");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
IReadOnlyList<CommandMatch> Found(string input) => texts.Search(input) is { IsSuccess: true } s ? s.Commands : [];
string? Rw(string input) => ReplyUserInjection.Rewrite(input, Found(input), 777UL);
Check(Rw("stats") == "stats <@777>" && Rw("estadisticas") == "estadisticas <@777>" && Rw("ficha") == "ficha <@777>" && Rw("stats mas") is null && Rw("stats <@5>") is null && Rw("stats polar") is null,
    "respondiendo el mensaje de alguien, «aa stats» le pone su mención (y «aa stats mas», con mención o con un nombre escritos no se tocan)");
Check(Found("stats mas").Count == 1 && Found("stats <@5> mas").Count == 1 && Found("stats Polar Frost").Count == 1, "«aa stats» sigue siendo un solo comando que acepta «mas», una mención o un nombre con espacios");
Check(FightLock.IsAllowed("stats"), "se pueden mirar las stats en plena pelea (solo mira)");
bool built = true;
try { ActivatorUtilities.CreateInstance(sp, typeof(StatsModule)); ActivatorUtilities.CreateInstance(sp, typeof(ShopModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); var buttonsModule = typeof(StatsModule).Assembly.GetType("ShopButtonsModule") ?? typeof(StatsModule).Assembly.GetTypes().Single(t => t.Name == "ShopButtonsModule"); ActivatorUtilities.CreateInstance(sp, buttonsModule); }
catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built, "los módulos se construyen con el DI real");

using (Loc.Use("en"))
{
    var otherEn = await Stats(T, 0, "Fulano", U);
    var moreEn = await Stats(T, 1, "Fulano", U);
    var ghostEn = await Stats(GHOST, 0, "Fantasma", U);
    string text = AllText(otherEn.Embed!) + " " + AllText(moreEn.Embed!) + " " + ghostEn.PlainMessage + " " + string.Join(" ", Buttons(otherEn.Components).Concat(Buttons(moreEn.Components)).Select(b => b.Label));
    var leak = LeakCheck.NoSpanish(text, ["Fulano", "Fantasma", weapon.Name, "Fuego Nuevo", "Guerrero", "Hechicero", "Arquero", "Ninja"]);
    Check(otherEn.Embed!.Title!.Contains("Fulano") && otherEn.Embed.Title.Contains("Stats of") && ghostEn.PlainMessage!.Contains("character"), "en inglés: los títulos y el aviso de que no tiene personaje");
    Check(leak.Ok, $"y no queda español suelto en las stats de otro ({leak.Found})");
}

// =====================================================================================================================================================================================================
Console.WriteLine("\n== 4. El jefe de la Zona 5: +10 % de vida y +5 % de daño ==");
const string Boss5Sql = "SELECT min_hp, max_hp, min_damage, max_damage FROM monsters WHERE name = 'Soberano de la Escoria' AND is_boss";
var boss5 = await db.QuerySingleAsync<(int MinHp, int MaxHp, int MinDmg, int MaxDmg)>(Boss5Sql);
Check(boss5 == (2118, 2540, 212, 260), $"el Soberano de la Escoria tiene 2118-2540 de vida y 212-260 de daño ({boss5})");
string bossScript = File.ReadAllText(Path.Combine(repoRoot, "Database", "rebalance_zone5_boss.sql"));
await db.ExecuteAsync(bossScript); await db.ExecuteAsync(bossScript);
Check(await db.QuerySingleAsync<(int, int, int, int)>(Boss5Sql) == (2118, 2540, 212, 260), "el script de datos es absoluto y se puede correr dos veces: queda igual");
Check(File.ReadAllText(Path.Combine(repoRoot, "Database", "seed_zone_bosses.sql")).Contains("'Soberano de la Escoria', '👑☠️', 2118, 2540, 212, 260"), "la semilla de una instalación nueva trae los mismos números");
Check(Math.Abs(RaidDifficulty.BossHp(2118, 1, 5) - Math.Round(1925 * 1.2)) <= 2 && Math.Abs(RaidDifficulty.BossDamage(260, 5) - Math.Round(248 * 1.1)) <= 1 && RaidDifficulty.ForZoneRank(4).HpMultiplier == 1.2, "el raid de la Zona 5 sale igual de difícil que antes (y el de las otras zonas no se movió)");

// =====================================================================================================================================================================================================
Console.WriteLine("\n== 5. Míticos en las armas y los amuletos de las zonas 6 a 10 ==");
var recipeRepo = sp.GetRequiredService<IRecipeRepository>();
var zoneRepo = sp.GetRequiredService<IZoneRepository>();
var crafting = sp.GetRequiredService<ICraftingRepository>();
const string CortezaName = "Corteza del Árbol de Vida", MeteoritoName = "Fragmento de Meteorito";
var recipesAll = await recipeRepo.GetAllAsync();
var gear610 = recipesAll.Where(r => r.ZoneId is >= 6 and <= 10 && (r.ResultItem.Type is "Weapon" or "Amulet")).ToList();
int Qty(RecipeDetails r, string name) => r.Ingredients.Where(i => i.ItemName == name).Select(i => i.Quantity).SingleOrDefault();
Check(gear610.Count == 25 && gear610.All(r => r.Ingredients.Count == 6), "las 25 recetas de equipo de las zonas 6 a 10 (4 armas y 1 amuleto por zona) tienen 6 tipos de ingrediente");
Check(gear610.Where(r => r.ResultItem.Type == "Weapon").All(r => Qty(r, CortezaName) == 1 && Qty(r, MeteoritoName) == 1), "cada ARMA pide 1 Corteza y 1 Meteorito (antes solo el de su material)");
Check(gear610.Where(r => r.ResultItem.Type == "Amulet").All(r => Qty(r, CortezaName) == 1 + (r.ZoneId!.Value - 6) && Qty(r, MeteoritoName) == 1 + (r.ZoneId!.Value - 6)), "cada AMULETO pide Corteza y Meteorito en cantidad 1, 2, 3, 4 y 5 (zonas 6 a 10)");
var relics = recipesAll.Where(r => r.ZoneId is >= 6 and <= 10 && r.ResultItem.Type == "Reliquia").ToList();
Check(relics.Count == 5 && relics.All(r => r.Ingredients.Count == 4 && Qty(r, CortezaName) == 0 && Qty(r, MeteoritoName) == 0), "las Reliquias no cambiaron (4 ingredientes, sin Míticos)");
Check(recipesAll.Where(r => r.ZoneId is >= 1 and <= 5).All(r => r.Ingredients.Count <= 4 && Qty(r, CortezaName) == 0 && Qty(r, MeteoritoName) == 0), "las recetas de las zonas 1 a 5 siguen como estaban (hasta 4 tipos, sin Míticos)");
var amuletSal = gear610.Single(r => r.ResultItem.Name == "Amuleto de Sal").Ingredients.Select(i => i.ItemName).ToList();
Check(amuletSal[0] == "Cristal de Sal" && amuletSal[1] == CortezaName && amuletSal[2] == MeteoritoName, $"en la receta, el drop de la zona va primero y los dos Míticos justo después ({string.Join(", ", amuletSal)})");
async Task<string> Snapshot() => string.Join("\n", await db.QueryAsync<string>("SELECT res.name || '|' || i.name || '|' || ri.quantity FROM recipes r JOIN items res ON res.item_id = r.result_item_id JOIN recipe_ingredients ri USING (recipe_id) JOIN items i ON i.item_id = ri.item_id WHERE r.zone_id BETWEEN 6 AND 10 ORDER BY 1"));
string before = await Snapshot();
string mythicScript = File.ReadAllText(Path.Combine(repoRoot, "Database", "rebalance_zone6_10_mythics.sql"));
await db.ExecuteAsync(mythicScript); await db.ExecuteAsync(mythicScript);
Check(before == await Snapshot() && before.Length > 0, "el script de datos para una base viva es absoluto y re-ejecutable: dos veces deja lo mismo (y ya era el estado de la semilla)");
string seedZones = File.ReadAllText(Path.Combine(repoRoot, "Database", "add_zones_6_10.sql"));
Check(seedZones.Contains("('amulet', 'Fragmento de Meteorito', 1, 1)") && seedZones.Contains("('weapon_wood', 'Fragmento de Meteorito', 1, 0)") && seedZones.Contains("('weapon_metal', 'Corteza del Árbol de Vida', 1, 0)"), "add_zones_6_10.sql (instalación nueva) trae los mismos Míticos");

// =====================================================================================================================================================================================================
Console.WriteLine("\n== 6. El Trinche y la Brasa desde el Fuego Nuevo 5 ==");
var zonesAll = await zoneRepo.GetAllAsync();
var ladder0 = ZoneRanking.ForPlayer(zonesAll, 0); var ladder4 = ZoneRanking.ForPlayer(zonesAll, 4); var ladder5 = ZoneRanking.ForPlayer(zonesAll, 5); var ladder17 = ZoneRanking.ForPlayer(zonesAll, 17);
Check(FogonRules.KeyGearNeeded(ladder0) && FogonRules.KeyGearNeeded(ladder4) && !FogonRules.KeyGearNeeded(ladder5) && !FogonRules.KeyGearNeeded(ladder17), "el equipo del Fogón hace falta con Fuego Nuevo 0 a 4 y deja de hacer falta desde el 5");
int last0 = ladder0[^1].ZoneId, last5 = ladder5[^1].ZoneId;
Check(FogonRules.OffersKeyGear(ladder0, last0) && !FogonRules.OffersKeyGear(ladder0, 0) && FogonRules.IsGateOpen(ladder5, last5) && !FogonRules.OffersKeyGear(ladder5, last5), "se ofrece con la puerta abierta Y si hace falta: con FN 5 la puerta se abre pero ya no se ofrece");

async Task<User> PlayerAt(int fn, int highest, int zone)
{
    await db.ExecuteAsync("UPDATE users SET fuego_nuevo = @fn, highest_zone_cleared = @h, current_zone_id = @z, level = 60, gold = 500000, weapon_id = NULL, amulet_id = NULL WHERE discord_id = @id", new { id = (long)U, fn, h = highest, z = zone });
    return (await users.GetByDiscordIdAsync(U))!;
}
var noItems = new Dictionary<string, int>();
var pFn0 = await PlayerAt(0, last0, last0);
var choices0 = ForgeChoices.For(recipesAll, zonesAll, pFn0, noItems, "").Select(c => (string)c.Value).ToList();
var pFn5 = await PlayerAt(5, last5, last5);
var choices5 = ForgeChoices.For(recipesAll, zonesAll, pFn5, noItems, "").Select(c => (string)c.Value).ToList();
Check(choices0.Contains(FogonRules.WeaponName) && choices0.Contains(FogonRules.AmuletName) && !choices5.Contains(FogonRules.WeaponName) && !choices5.Contains(FogonRules.AmuletName) && choices5.Count > 0,
    "la lista de la herrería ofrece el Trinche y la Brasa con FN 0 (puerta abierta) y NO con FN 5 (sigue ofreciendo el equipo de la zona)");
var book0 = ForgeModule.RenderZonePageEmbed(recipesAll, ZoneRanking.ForPlayer(zonesAll, 0), pFn0.Class, last0, noItems, pFn0.Gold, pFn0.Level, highestZoneCleared: pFn0.HighestZoneCleared);
var book5 = ForgeModule.RenderZonePageEmbed(recipesAll, ZoneRanking.ForPlayer(zonesAll, 5), pFn5.Class, last5, noItems, pFn5.Gold, pFn5.Level, highestZoneCleared: pFn5.HighestZoneCleared);
Check(AllText(book0).Contains("Trinche del Asador Eterno") && !AllText(book5).Contains("Trinche del Asador Eterno") && !AllText(book5).Contains("Brasa del Fogón Eterno"), "el recetario (página de la última zona) muestra el equipo del Fogón con FN 0 y no con FN 5");
var special0 = ForgeSpecialModule.BuildEmbed(recipesAll, ZoneRanking.ForPlayer(zonesAll, 0), pFn0, noItems);
var special5 = ForgeSpecialModule.BuildEmbed(recipesAll, ZoneRanking.ForPlayer(zonesAll, 5), pFn5, noItems);
Check(special0.Fields.Length > 0 && !AllText(special0).Contains("ya no hacen falta") && special5.Fields.Length == 0 && AllText(special5).Contains("ya no hacen falta") && AllText(special5).Contains(ladder5[^1].Name) && AllText(special5).Contains("/dismantle"),
    "/forge-special con FN 0 lista las recetas con la advertencia de siempre; con FN 5 explica que ya no hacen falta (nombra la mazmorra de su zona tope) y no las lista");
var make5 = await ForgeModule.ExecuteMakeAsync(users, recipeRepo, crafting, events, U, FogonRules.WeaponName, zoneRepo);
var after5 = (await users.GetByDiscordIdAsync(U))!;
Check(make5.PlainMessage!.Contains("ya no hace falta") && after5.WeaponId is null && after5.Gold == 500000, "forjar el Trinche por nombre con FN 5 se rechaza antes de gastar nada");
await PlayerAt(0, last0, last0);
var make0 = await ForgeModule.ExecuteMakeAsync(users, recipeRepo, crafting, events, U, FogonRules.WeaponName, zoneRepo);
Check((make0.PlainMessage ?? make0.Embed?.Description) is { } make0Text && !make0Text.Contains("ya no hace falta"), "con FN 0 el Trinche se pide como siempre (acá falta material: no es el rechazo nuevo)");
using (Loc.Use("en"))
{
    var special5En = ForgeSpecialModule.BuildEmbed(recipesAll, ZoneRanking.ForPlayer(zonesAll, 5), pFn5, noItems);
    await PlayerAt(5, last5, last5);
    var make5En = await ForgeModule.ExecuteMakeAsync(users, recipeRepo, crafting, events, U, FogonRules.WeaponName, zoneRepo);
    string text = AllText(special5En) + " " + make5En.PlainMessage;
    var leak = LeakCheck.NoSpanish(text, ["El Fogón Eterno", "El Fogón", "Fogón", "Trinche del Asador Eterno", "Brasa del Fogón Eterno", ladder5[^1].Name, "El Herrero", "Herrero"]);
    Check(text.Contains("no longer need") && leak.Ok, $"en inglés: el aviso y el rechazo, sin español suelto ({leak.Found})");
}

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

public class FakeProxy : DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var v)) return v;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public static T Make<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)proxy).Values = values;
        return proxy;
    }
}
