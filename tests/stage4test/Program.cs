using System.Globalization;
using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Tanda de ajustes: formato de tiempo, probabilidades de recolección, nombres con espacios, cooldown de cajas, listas de /shop sell y /use.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var shop = sp.GetRequiredService<IShopRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var cooldowns = sp.GetRequiredService<ICooldownRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
var events = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000005001UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id;", new { id = (long)U });
async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task SetGold(int g) => await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)U, g });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task Reset(int gold = 100000)
{
    await Cleanup();
    await users.GetOrCreateUserAsync(U);
    await SetGold(gold);
}

object Fake(Type t, Func<string, object?[], object?> h) => FakeProxy.Create(t, h);
async Task<IReadOnlyList<AutocompleteResult>> RunHandler(AutocompleteHandler handler, ulong userId, string typed)
{
    var option = (AutocompleteOption)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AutocompleteOption));
    typeof(AutocompleteOption).GetField("<Value>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(option, typed);
    var data = Fake(typeof(IAutocompleteInteractionData), (n, a) => n == "get_Current" ? option : throw new NotSupportedException(n));
    var inter = Fake(typeof(IAutocompleteInteraction), (n, a) => n == "get_Data" ? data : throw new NotSupportedException(n));
    var user = Fake(typeof(IUser), (n, a) => n == "get_Id" ? userId : throw new NotSupportedException(n));
    var ctx = Fake(typeof(IInteractionContext), (n, a) => n == "get_User" ? user : throw new NotSupportedException(n));
    var result = await handler.GenerateSuggestionsAsync((IInteractionContext)ctx, (IAutocompleteInteraction)inter, null!, sp);
    return result.IsSuccess ? result.Suggestions.ToList() : throw new Exception("handler falló: " + result.ErrorReason);
}

Console.WriteLine("== 1. Formato del tiempo (días, horas, minutos, segundos) ==");
string T(double seconds) => TimeFormat.Remaining(TimeSpan.FromSeconds(seconds));
Check(T(35) == "35s" && T(0.4) == "1s" && T(65) == "1m 5s" && T(59 * 60 + 59) == "59m 59s", $"menos de una hora: 35s -> «{T(35)}», 0,4s -> «{T(0.4)}», 65s -> «{T(65)}», 59m59s -> «{T(59 * 60 + 59)}»");
Check(T(3600) == "1h 0m 0s" && T(3725) == "1h 2m 5s" && T(23 * 3600 + 59 * 60 + 10) == "23h 59m 10s", $"horas: 1h -> «{T(3600)}», 3725s -> «{T(3725)}», casi un día -> «{T(23 * 3600 + 59 * 60 + 10)}»");
Check(T(86400) == "1d 0h 0m" && T(2 * 86400 + 3 * 3600 + 5 * 60 + 10) == "2d 3h 5m" && T(10 * 86400) == "10d 0h 0m", $"días: 1d -> «{T(86400)}», 2d 3h 5m 10s -> «{T(2 * 86400 + 3 * 3600 + 5 * 60 + 10)}»");
Check(TimeFormat.Remaining(TimeSpan.Zero) == "0s" && TimeFormat.Remaining(TimeSpan.FromSeconds(-5)) == "0s", "cero o negativo = 0s");
Check(!Enumerable.Range(0, 200000).Select(i => TimeSpan.FromMilliseconds(i * 997.3)).Any(t => TimeFormat.Remaining(t).Contains("1440m") || (t.TotalSeconds > 0 && TimeFormat.Remaining(t) == "0s")), "200.000 duraciones: ninguna sale en 'minutos de más' (1440m) ni en '0s' si falta algo");

Console.WriteLine("\n== 2. Probabilidades de /chop y /mine ==");
var chances = RarityCatalog.GatheringChances;
Check(Math.Abs(chances.Sum(c => c.Percent) - 100.0) < 1e-9, $"las chances suman 100%: {string.Join(" / ", chances.Select(c => $"{c.Rarity} {c.Percent:0.#}%"))}");
Check(chances[0].Percent == 68 && chances[1].Percent == 21 && chances[2].Percent == 7 && chances[3].Percent == 3.5 && chances[4].Percent == 0.5, "Común 68 / Raro 21 / Épico 7 / Legendario 3,5 / Mítico 0,5");
Check(chances.Zip(chances.Skip(1), (a, b) => a.Percent > b.Percent).All(x => x), "cada rareza es más rara que la anterior");
var counts = new Dictionary<string, int>();
const int N = 2_000_000;
for (int i = 0; i < N; i++) { var r = RarityCatalog.RollGatheringRarity(); counts[r] = counts.GetValueOrDefault(r) + 1; }
Check(chances.All(c => Math.Abs(counts.GetValueOrDefault(c.Rarity) * 100.0 / N - c.Percent) < Math.Max(0.1, c.Percent * 0.04)), $"el sorteo real (2 millones de tiradas) respeta la tabla: {string.Join(" / ", chances.Select(c => $"{c.Rarity} {counts.GetValueOrDefault(c.Rarity) * 100.0 / N:0.00}%"))}");

Console.WriteLine("\n   LISTA DE PROBABILIDADES (por cada /chop o /mine; los ítems de una misma rareza se reparten su chance):");
var gatherables = (await db.QueryAsync<(string type, string rarity, string name)>("SELECT type, rarity, name FROM items WHERE type IN ('Madera','Mineral') ORDER BY type, name")).ToList();
foreach (var type in new[] { "Madera", "Mineral" })
{
    Console.WriteLine($"   {(type == "Madera" ? "/chop (Madera)" : "/mine (Mineral)")}:");
    foreach (var g in gatherables.Where(g => g.type == type).OrderBy(g => RarityCatalog.RankOf(g.rarity)).ThenBy(g => g.name))
    {
        double percent = chances.First(c => c.Rarity == g.rarity).Percent / gatherables.Count(x => x.type == type && x.rarity == g.rarity);
        Console.WriteLine($"     {g.rarity,-10} {g.name,-26} {percent,5:0.##}%   (1 de cada {100 / percent:0.#} acciones)");
    }
}

Console.WriteLine("\n== 3. Nombres de ítems: mayúsculas, tildes y espacios ==");
string NoAccents(string t) => new string(t.Normalize(System.Text.NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
var shopNames = new[] { "Mate Amargo", "Empanada de Carne", "Asado de Tira", "Cordero Patagónico", "Asado Completo del Domingo en Familia", "Mate Dulce de la Abuela", "Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro" };
var bad = new List<string>();
foreach (var name in shopNames)
{
    foreach (var variant in new[] { name, name.ToLowerInvariant(), name.ToUpperInvariant(), NoAccents(name).ToLowerInvariant(), "  " + name + "  ", " " + NoAccents(name).ToUpperInvariant() + "\t" })
    {
        var found = await items.GetByNameAsync(variant);
        if (found is null || found.Name != name) bad.Add($"«{variant}»");
    }
}

Check(bad.Count == 0, $"los 10 ítems de la tienda se encuentran con 6 formas de escribirlos cada uno (exacto, minúsculas, MAYÚSCULAS, sin tildes, con espacios) ({(bad.Count == 0 ? "60 de 60" : string.Join(", ", bad.Take(5)))})");
Check(await items.GetByNameAsync("   ") is null && await items.GetByNameAsync("Mate") is null, "un texto vacío o a medias no encuentra nada (la búsqueda sigue siendo exacta)");

Console.WriteLine("\n== 4. Comprar cajas: una por compra y una por hora ==");
await Reset(100000);
var boxCd = CooldownCatalog.BoxBuy;
Check(boxCd.Duration == TimeSpan.FromHours(2) && CooldownCatalog.All.Contains(boxCd) && boxCd.DisplayName == "Comprar caja" && ShopCatalog.BoxesPerPurchase == 1, "el cooldown de comprar cajas dura 2 horas, figura en la lista de /cd y se compra de a 1");
var b1 = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1);
Check(b1.Embed is not null && b1.Embed.Title!.Contains("Compra realizada") && await Have("Cajón de Pino") == 1 && await Gold() == 100000 - 2300, "primera caja: se compra (oro 100000 -> 97700, 1 Cajón de Pino)");
var rem = await cooldowns.GetRemainingAsync(U, boxCd.CommandName, boxCd.Duration);
Check(rem is { } r1 && r1 > TimeSpan.FromMinutes(119) && r1 <= TimeSpan.FromHours(2), $"queda el cooldown: faltan {TimeFormat.Remaining(rem!.Value)}");
int goldBefore = await Gold();
var b2 = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1);
Check(b2.Embed is not null && b2.Embed.Title!.Contains("Ya compraste") && b2.Embed.Description.Contains("una vez cada 2 horas") && (b2.Embed.Description.Contains("1h 59m") || b2.Embed.Description.Contains("2h 0m 0s")) && await Have("Cajón de Pino") == 1 && await Gold() == goldBefore,
    $"segunda caja enseguida: no se compra, no cobra y avisa cuánto falta. Texto: «{b2.Embed?.Description}»");
var b3 = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Baúl de Roble", 1);
Check(b3.Embed is not null && b3.Embed.Title!.Contains("Ya compraste") && await Have("Baúl de Roble") == 0, "el cooldown es de TODAS las cajas (no por tipo de caja): pedir otra distinta tampoco deja");

// Comida: sin cooldown.
var food1 = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Mate Amargo", 5);
var food2 = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "mate amargo ", 5);
Check(food1.Embed is not null && food2.Embed is not null && await Have("Mate Amargo") == 10, "la comida no tiene cooldown: dos compras seguidas de 5 mates (la segunda escrita en minúsculas y con un espacio de más)");

// Vence el cooldown -> se puede de nuevo.
await db.ExecuteAsync("UPDATE cooldowns SET last_executed_at = now() - interval '121 minutes' WHERE discord_id = @id AND command_name = @c", new { id = (long)U, c = boxCd.CommandName });
var b4 = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Baúl de Roble", 1);
Check(b4.Embed is not null && b4.Embed.Title!.Contains("Compra realizada") && await Have("Baúl de Roble") == 1, "pasada la hora se puede comprar otra caja");

// Pedir varias: se rechaza SIN gastar el cooldown.
await Reset(100000);
var many = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 3);
Check(many.PlainMessage is not null && many.PlainMessage.Contains("de a 1") && await Have("Cajón de Pino") == 0 && await Gold() == 100000 && await cooldowns.GetRemainingAsync(U, boxCd.CommandName, boxCd.Duration) is null,
    $"pedir 3 cajas: se rechaza, no cobra y NO gasta el cooldown. Texto: «{many.PlainMessage}»");
var okAfter = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1);
Check(okAfter.Embed is not null && await Have("Cajón de Pino") == 1, "y enseguida se puede comprar una");

// Sin oro: no se gasta el cooldown.
await Reset(100);
var poor = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cofre de Oro", 1);
Check(poor.PlainMessage is not null && poor.PlainMessage.Contains("No te alcanza") && await Gold() == 100 && await Have("Cofre de Oro") == 0 && await cooldowns.GetRemainingAsync(U, boxCd.CommandName, boxCd.Duration) is null,
    $"sin oro suficiente: no compra y el cooldown queda libre. Texto: «{poor.PlainMessage}»");
await SetGold(200000);
var nowOk = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cofre de Oro", 1);
Check(nowOk.Embed is not null && await Have("Cofre de Oro") == 1, "al juntar el oro, la compra pasa (el intento fallido no había gastado el cooldown)");

// Carrera: 10 compras de caja a la vez -> exactamente UNA.
await Reset(100000);
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
{
    try { var r = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, "Cajón de Pino", 1); return r.Embed?.Title ?? r.PlainMessage; }
    catch (Exception ex) { return "EXCEPCIÓN " + ex.Message.Split((char)10)[0]; }
})));
Check(race.Count(t => t!.Contains("Compra realizada")) == 1 && race.Count(t => t!.Contains("Ya compraste")) == 9 && await Have("Cajón de Pino") == 1 && await Gold() == 100000 - 2300,
    $"10 compras de caja a la vez: {race.Count(t => t!.Contains("Compra realizada"))} se compra, {race.Count(t => t!.Contains("Ya compraste"))} esperan (oro -{100000 - await Gold()}, 1 caja)");

// Sale en /cd con el tiempo en el formato nuevo.
var cdEmbed = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U);
string boxLine = cdEmbed.Description.Split((char)10).First(l => l.Contains("Comprar caja"));
Check(boxLine.Contains("📦") && (boxLine.Contains("1h 59m") || boxLine.Contains("2h 0m 0s")) && boxLine.Contains("restantes") && cdEmbed.Description.Contains("Cazar"), $"/cd muestra el cooldown de cajas: «{boxLine}»");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id AND command_name = @c", new { id = (long)U, c = boxCd.CommandName });
cdEmbed = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U);
Check(cdEmbed.Description.Split((char)10).First(l => l.Contains("Comprar caja")).Contains("¡Listo!"), "sin cooldown activo dice '¡Listo!'");
await users.GetOrCreateUserAsync(U);
await db.ExecuteAsync("UPDATE users SET last_daily_claim = now() - interval '1 hour' WHERE discord_id = @id", new { id = (long)U });
cdEmbed = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U);
string dailyLine = cdEmbed.Description.Split((char)10).First(l => l.Contains("Diario"));
Check(dailyLine.Contains("h ") && dailyLine.Contains("m ") && !dailyLine.Contains("m 0m"), $"el /daily (cooldown de ~23 horas) ahora se lee en horas, no en 1400 minutos: «{dailyLine}»");
Check(ShopModule.BuildViewEmbed(await ShopModule.LoadShopItemsAsync(items)).Description.Contains("una vez cada 2 horas"), "/shop view avisa que las cajas son de a una cada 2 horas");

Console.WriteLine("\n== 5. Listas desplegables nuevas (/shop sell y /use) y blindaje ==");
await Reset();
await Give("Hierro", 14); await Give("Mate Amargo", 3); await Give("Asado Completo del Domingo en Familia", 1); await Give("Cajón de Pino", 2); await Give("Arca del Soberano", 1); await Give("Madera de Pino", 40);
var sell = await RunHandler(new SellItemAutocompleteHandler(), U, "");
var sellNames = sell.Select(s => s.Value.ToString()!).ToList();
Check(sellNames.Contains("Hierro") && sellNames.Contains("Mate Amargo") && !sellNames.Contains("Cajón de Pino") && !sellNames.Contains("Arca del Soberano"), $"/shop sell ofrece lo que tenés y se puede vender, y NINGUNA caja (todas valen 0 al revender): {string.Join(", ", sellNames)}");
Check(sell.All(s => s.Name.Length <= 100 && s.Name.Contains("tenés") && s.Name.Contains("oro c/u")) && sell.Select(s => s.Value.ToString()).Distinct().Count() == sell.Count, $"etiquetas con cantidad y precio por unidad, hasta 100 caracteres. Ej: «{sell.First().Name}»");
var inv = (await inventory.GetByDiscordIdAsync(U)).ToDictionary(e => e.ItemName);
var values = sellNames.Select(n => (long)inv[n].SellPrice * inv[n].Quantity).ToList();
Check(values.Zip(values.Skip(1), (a, b) => a >= b).All(x => x), "ordenada de lo que más plata deja a lo que menos");
Check((await RunHandler(new SellItemAutocompleteHandler(), U, "HIERRO")).Select(s => s.Value.ToString()).SequenceEqual(["Hierro"]) && (await RunHandler(new SellItemAutocompleteHandler(), U, "MADERA")).Select(s => s.Value.ToString()).SequenceEqual(["Madera de Pino"]) && (await RunHandler(new SellItemAutocompleteHandler(), U, "zzz")).Count == 0, "filtra al escribir sin importar mayúsculas ni tildes ('HIERRO', 'MADERA' -> Madera de Pino, 'zzz' -> nada)");
Check((await RunHandler(new SellItemAutocompleteHandler(), 999000000000005999UL, "")).Count == 0, "un jugador sin cuenta ni inventario: lista vacía, sin error (y no se le crea cuenta)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE discord_id = 999000000000005999") == 0, "la lista no le creó cuenta a nadie");
var use = await RunHandler(new UseItemAutocompleteHandler(), U, "");
Check(use.Count == 2 && use[0].Value.ToString() == "Asado Completo del Domingo en Familia" && use[0].Name.Contains("+15% ATQ") && use[0].Name.Contains("cura 250 HP") && use[1].Name.Contains("cura 15 HP") && !use[1].Name.Contains("ATQ"),
    $"/use ofrece solo la comida que tenés, la que más cura primero, y el banquete avisa su buff: «{use[0].Name}» / «{use[1].Name}»");
Check((await RunHandler(new UseItemAutocompleteHandler(), U, "mate")).Count == 1, "/use filtra por lo que escribís");
var buyList = await RunHandler(new BuyItemAutocompleteHandler(), U, "");
var boxList = await RunHandler(new BoxAutocompleteHandler(), U, "");
Check(buyList.Count == 11 && boxList.Count == 2 && boxList[0].Value.ToString() == "Arca del Soberano" && boxList[1].Value.ToString() == "Cajón de Pino", "las listas de /shop buy (11, con la comida de mascotas), /abrir (2 cajas: la Mítica primero) y /equip (nada para equipar) siguen funcionando sobre la base nueva");
// El blindaje: una lista que se rompe NO tira excepción (devuelve vacío) y no deja a Discord sin respuesta.
var broken = await RunHandler(new ExplodingHandler(), U, "algo");
Check(broken.Count == 0, "una lista que falla por dentro devuelve vacío en vez de romperse (y deja el error en el log)");
// Registro de los comandos: las dos listas nuevas están enganchadas.
var modules = await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var sellCmd = interactions.SlashCommands.First(c => c.Name == "sell");
var useCmd = interactions.SlashCommands.First(c => c.Name == "use");
var buyCmd = interactions.SlashCommands.First(c => c.Name == "buy");
Check(sellCmd.Parameters.Any(p => p.Name == "item" && p.AutocompleteHandler is not null) && useCmd.Parameters.Any(p => p.Name == "item" && p.AutocompleteHandler is not null) && buyCmd.Parameters.Any(p => p.Name == "item" && p.AutocompleteHandler is not null),
    "/shop sell, /use y /shop buy tienen su lista desplegable registrada");
var allWithList = interactions.SlashCommands.Where(c => c.Parameters.Any(p => p.AutocompleteHandler is not null)).Select(c => c.Name).OrderBy(n => n).ToList();
Console.WriteLine($"   comandos con lista desplegable: {string.Join(", ", allWithList)}");
Check(new[] { "open", "buy", "sell", "use", "zona" }.All(allWithList.Contains) && !allWithList.Contains("equip"), "listas en: open, buy, sell, use y zona (ya no hay /equip, y forge no tiene subcomandos)");

await Cleanup();
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class ExplodingHandler : SafeAutocompleteHandler
{
    protected override Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services) =>
        throw new InvalidOperationException("falla de prueba (esperada)");
}

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
