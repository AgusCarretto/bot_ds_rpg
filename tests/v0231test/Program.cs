using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.23.1 (el dueño, 2026-10-22): «que se pueda hacer aa sell all, aa sell arma, aa sell amuleto sin pasar por shop; lo mismo con deposit y withdraw sin poner bank antes; en el play aa cf h o t y un número;
// y que exista la forma de poner 10k por 10000, y m de millones.» Los atajos llaman a la misma lógica de siempre: acá se prueba lo que se lee del texto y el efecto contra la base real.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Console.WriteLine("== 1. Montos con k y m ==");
foreach (var (text, expected) in new (string, int)[] { ("10k", 10_000), ("10K", 10_000), ("1m", 1_000_000), ("1M", 1_000_000), ("1.5m", 1_500_000), ("1,5m", 1_500_000), ("2.5k", 2_500), ("0.5k", 500), (".5k", 500), ("100k", 100_000), ("2147m", 2_147_000_000), (" 3k ", 3_000) })
{
    var r = AmountParser.Parse(text, 0);
    Check(r is { Ok: true, IsAll: false } && r.Amount == expected, $"«{text}» = {expected:N0} (da {(r.Ok ? r.Amount.ToString("N0") : r.Error)})");
}

foreach (var text in new[] { "k", "m", "kk", "10kk", "1.2.3k", "-5k", "abc", "1e3", "0k", "0.0001k", "3000m", "99999999999", "2148m", "k10", "10km", "1,5,5m", "..k" })
{
    Check(!AmountParser.Parse(text, 100).Ok, $"«{text}» no se entiende como un monto");
}

Check(AmountParser.Parse("0k", 100).Error!.Contains("al menos 1") && AmountParser.Parse("3000m", 100).Error!.Contains("grande") && AmountParser.Parse("kk", 100).Error!.Contains("10k"), "los motivos: cero, demasiado grande, y lo que no se entiende explica que se puede escribir 10k o 1m");
Check(AmountParser.Parse("1.000", 0).Amount == 1000 && AmountParser.Parse("1,000", 0).Amount == 1000 && AmountParser.Parse("12 345", 0).Amount == 12345 && AmountParser.Parse("1.500", 0).Amount == 1500 && AmountParser.Parse("500", 0).Amount == 500,
    "sin k ni m el punto y la coma siguen siendo separadores de miles (1.500 = 1500), como siempre");
Check(AmountParser.Parse("1.5k", 0).Amount == 1500 && AmountParser.Parse("all", 750) is { Ok: true, Amount: 750, IsAll: true }, "con k el punto es decimal (1.5k = 1500) y «all» sigue tomando todo");

Console.WriteLine("\n== 2. Qué se pidió vender ==");
SellRequest S(string? t) => TextShortcuts.ParseSell(t);
Check(S(null).Kind == SellKind.Help && S("").Kind == SellKind.Help && S("   ").Kind == SellKind.Help, "sin nada: la ayuda");
Check(new[] { "all", "ALL", "todo", " todo ", "inventario", "mochila" }.All(t => S(t).Kind == SellKind.All), "all / todo / inventario / mochila = todo lo vendible de la mochila");
Check(new[] { "arma", "Arma", "weapon" }.All(t => S(t).Kind == SellKind.Weapon) && new[] { "amuleto", "AMULETO", "amulet" }.All(t => S(t).Kind == SellKind.Amulet), "arma y amuleto = lo que llevás puesto");
Check(S("Hierro") is { Kind: SellKind.Item, Item: "Hierro", Quantity: "1" } && S("10 Hierro") is { Kind: SellKind.Item, Item: "Hierro", Quantity: "10" } && S("all Hierro") is { Item: "Hierro", Quantity: "all" } && S("todo Madera de Roble") is { Item: "Madera de Roble", Quantity: "todo" } && S("1k Hierro").Quantity == "1k",
    "un ítem, con cantidad (número, 1k) o con «all»/«todo» delante");
Check(S("Madera de Roble") is { Kind: SellKind.Item, Item: "Madera de Roble", Quantity: "1" } && S("arma extra") is { Kind: SellKind.Item, Item: "arma extra" }, "un nombre de varias palabras entero es el ítem (la primera palabra no es una cantidad), y «arma» con algo más ya no es la pieza equipada");

Console.WriteLine("\n== 3. Cara o cruz ==");
CoinflipRequest? C(string a, string b) => TextShortcuts.ParseCoinflip(a, b);
Check(C("h", "100") is { Amount: "100", Side: "heads" } && C("t", "100") is { Side: "tails" } && C("100", "h") is { Amount: "100", Side: "heads" } && C("1k", "t") is { Amount: "1k", Side: "tails" }, "el lado y la apuesta, en cualquier orden");
Check(C("heads", "10k") is { Side: "heads", Amount: "10k" } && C("TAILS", "1m") is { Side: "tails" } && C("cara", "50") is { Side: "heads" } && C("cruz", "50") is { Side: "tails" } && C("all", "t") is { Amount: "all", Side: "tails" }, "heads, tails, cara, cruz, 10k, 1m y all");
Check(C("100", "200") is null && C("h", "") is null && C("", "") is null && C("100", "") is null && C("x", "y") is null, "sin lado claro o sin apuesta: null (el comando contesta cómo se escribe)");
Check(TextShortcuts.SideOf("H") == "heads" && TextShortcuts.SideOf(" t ") == "tails" && TextShortcuts.SideOf("c") is null && TextShortcuts.SideOf(null) is null, "los lados que se reconocen");

Console.WriteLine("\n== 4. Vender, contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var inventory = new InventoryRepository(factory);
var shop = new ShopRepository(factory);
var bank = new BankRepository(factory);
var casinoRepo = new CasinoRepository(factory);
var events = new GameEventService(new GameEventRepository(factory));
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000097001UL, GHOST = 999000000000097999UL;
async Task Reset() { await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)U, (long)GHOST } }); await users.GetOrCreateUserAsync(U); await db.ExecuteAsync("UPDATE users SET gold = 0 WHERE discord_id = @id", new { id = (long)U }); }
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
Task<ShopModule.ShopActionResult> Sell(string? args, ulong id = U) => TextShortcuts.ExecuteSellAsync(users, items, inventory, shop, events, id, args);
int pinePrice = (await items.GetByNameAsync("Madera de Pino"))!.SellPrice, oakPrice = (await items.GetByNameAsync("Madera de Roble"))!.SellPrice;
var weapon = await db.QuerySingleAsync<(int Id, string Name, int Price)>("SELECT item_id, name, sell_price FROM items WHERE type = 'Weapon' AND sell_price > 0 ORDER BY item_id LIMIT 1");
var amulet = await db.QuerySingleAsync<(int Id, string Name, int Price)>("SELECT item_id, name, sell_price FROM items WHERE type = 'Amulet' AND sell_price > 0 ORDER BY item_id LIMIT 1");
Check(pinePrice > 0 && oakPrice > 0, $"hay con qué probar: Madera de Pino ({pinePrice} c/u), Madera de Roble ({oakPrice}), «{weapon.Name}» ({weapon.Price}) y «{amulet.Name}» ({amulet.Price})");

await Reset();
var help = await Sell("");
Check(help.PlainMessage!.Contains("aa sell all") && help.Embed is null && await Gold() == 0, "aa sell a secas: explica los atajos y no vende nada");

await Give("Madera de Pino", 5); await Give("Madera de Roble", 3);
var one = await Sell("Madera de Pino");
Check(one.Embed is not null && await Have("Madera de Pino") == 4 && await Gold() == pinePrice, $"aa sell Madera de Pino: vende 1 ({pinePrice} de oro)");
var two = await Sell("2 Madera de Pino");
Check(two.Embed is not null && await Have("Madera de Pino") == 2 && await Gold() == 3 * pinePrice, "aa sell 2 Madera de Pino: vende 2");
var allOne = await Sell("all Madera de Pino");
Check(allOne.Embed is not null && await Have("Madera de Pino") == 0 && await Gold() == 5 * pinePrice && await Have("Madera de Roble") == 3, "aa sell all Madera de Pino: vende todas las que tenía (2) y no toca lo demás");
var noneLeft = await Sell("all Madera de Pino");
Check(noneLeft.Embed is null && noneLeft.PlainMessage is not null && await Gold() == 5 * pinePrice, $"con 0 unidades contesta lo de siempre y no cobra: «{noneLeft.PlainMessage}»");
var tooMany = await Sell("1k Madera de Roble");
Check(tooMany.Embed is null && await Have("Madera de Roble") == 3 && await Gold() == 5 * pinePrice, "aa sell 1k Madera de Roble con 3: se rechaza, no vende nada y no toca el oro");
var unknown = await Sell("all Cosa Inventada");
Check(unknown.PlainMessage!.Contains("Cosa Inventada") && unknown.Embed is null, "un ítem que no existe lo dice");
var badQty = await Sell("1.2.3k Madera de Roble");
Check(badQty.Embed is null && badQty.PlainMessage is not null && await Have("Madera de Roble") == 3, "una cantidad que no se entiende se rechaza sin vender");

await Give("Madera de Pino", 4);
int goldBefore = await Gold();
var everything = await Sell("all");
Check(everything.Embed is not null && await Have("Madera de Pino") == 0 && await Have("Madera de Roble") == 0 && await Gold() == goldBefore + (4 * pinePrice) + (3 * oakPrice), "aa sell all: vende todo lo vendible de la mochila");
var nothing = await Sell("all");
Check(nothing.Embed is null && nothing.PlainMessage is not null, "aa sell all con la mochila vacía: lo dice");

await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a, weapon_enchant = 3, gold = 0 WHERE discord_id = @id", new { id = (long)U, w = weapon.Id, a = amulet.Id });
var soldWeapon = await Sell("arma");
var afterWeapon = await users.GetByDiscordIdAsync(U);
Check(soldWeapon.Embed is not null && afterWeapon!.WeaponId is null && afterWeapon.WeaponEnchant == 0 && afterWeapon.AmuletId == amulet.Id && await Gold() == weapon.Price, $"aa sell arma: vende el arma puesta por {weapon.Price} (y le saca el encantamiento) sin tocar el amuleto");
var noWeapon = await Sell("arma");
Check(noWeapon.Embed is null && noWeapon.PlainMessage!.Contains("arma") && await Gold() == weapon.Price, "sin arma equipada lo dice y no cobra");
var soldAmulet = await Sell("amuleto");
Check(soldAmulet.Embed is not null && (await users.GetByDiscordIdAsync(U))!.AmuletId is null && await Gold() == weapon.Price + amulet.Price, $"aa sell amuleto: vende el amuleto puesto por {amulet.Price}");
Check((await Sell("amuleto")).PlainMessage!.Contains("amuleto") && (await Sell("arma", GHOST)).PlainMessage!.Contains("/start"), "sin amuleto lo dice; sin cuenta pide /start");

Console.WriteLine("\n== 5. Depositar y retirar con k y m ==");
await db.ExecuteAsync("UPDATE users SET gold = 2000000, has_bank = true, bank_gold = 0 WHERE discord_id = @id", new { id = (long)U });
async Task<(int Gold, int Bank)> Money() => await db.QuerySingleAsync<(int, int)>("SELECT gold, bank_gold FROM users WHERE discord_id = @id", new { id = (long)U });
var dep = await BankModule.ExecuteDepositAsync(users, bank, events, U, "10k");
Check(await Money() is (1_990_000, 10_000), $"deposit 10k: pasan 10.000 al banco ({(await Money())})");
var wd = await BankModule.ExecuteWithdrawAsync(users, bank, U, "2.5k");
Check(await Money() is (1_992_500, 7_500), "withdraw 2.5k: vuelven 2.500 a la billetera");
await BankModule.ExecuteDepositAsync(users, bank, events, U, "1m");
Check(await Money() is (992_500, 1_007_500), "deposit 1m: pasa un millón");
var tooBig = await BankModule.ExecuteDepositAsync(users, bank, events, U, "5m");
Check(tooBig.PlainMessage is not null && await Money() is (992_500, 1_007_500), "deposit 5m sin tener tanto: se rechaza y no mueve nada");
await BankModule.ExecuteWithdrawAsync(users, bank, U, "all");
Check(await Money() is (2_000_000, 0), "withdraw all: saca todo");

Console.WriteLine("\n== 6. Apostar con k y m ==");
var casino = new CasinoService();
await db.ExecuteAsync("UPDATE users SET gold = 50000 WHERE discord_id = @id", new { id = (long)U });
var flip = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "coinflip", "1k", "heads", events);
int afterFlip = await Gold();
Check(flip.Embed is not null && (afterFlip == 51_000 || afterFlip == 49_000), $"coinflip con «1k»: se apostaron 1.000 (quedó en {afterFlip:N0})");
await db.ExecuteAsync("UPDATE users SET gold = 5000 WHERE discord_id = @id", new { id = (long)U });
var poor = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "coinflip", "1m", "tails", events);
Check(poor.Embed is null && poor.PlainMessage!.Contains("No tenés suficiente oro") && await Gold() == 5000, "apostar 1m sin tenerlo se rechaza sin tocar el oro");
var slotsBet = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", "10k", null, events);
Check(slotsBet.PlainMessage is not null && await Gold() == 5000, "slots con 10k teniendo 5.000: se rechaza");

Console.WriteLine("\n== 7. Los comandos, registrados con el CommandService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<Discord.IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var aliasCount = textMods.SelectMany(m => m.Commands).SelectMany(c => c.Aliases).GroupBy(a => a.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
string[] mine = ["sell", "vender", "deposit", "depositar", "dep", "withdraw", "retirar", "sacar", "wd", "cf", "coinflip", "flip", "moneda", "slots", "sl", "tragamonedas"];
Check(mine.All(a => aliasCount.GetValueOrDefault(a) == 1), $"los atajos están registrados y ningún nombre se repite con otro comando ({string.Join(", ", mine.Where(a => aliasCount.GetValueOrDefault(a) != 1))})");
Check(new[] { "play", "pl", "shop sell", "ss", "shop sellall", "sa", "bank", "banco" }.All(a => aliasCount.GetValueOrDefault(a) >= 1), "los comandos largos de siempre siguen registrados");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(x))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split('\n')[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids); DELETE FROM game_events WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)U, (long)GHOST } });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
