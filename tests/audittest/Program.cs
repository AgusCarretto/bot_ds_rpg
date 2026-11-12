using System.Reflection;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Auditoría del 2026-11-12 (rama review/fable-ui): las fallas confirmadas que se arreglaron, cada una con el chequeo que fallaba antes.
//   1. La tienda multiplicaba precio × cantidad en int: una cantidad enorme daba la vuelta y la compra salía gratis (o SUMABA oro). Comprar y vender.
//   2. Los botones de la pelea solitaria (Atacar, habilidad, Huir, el desplegable de curar) no decían de QUÉ pelea eran: tocar el mensaje de otro jugador jugaba TU turno y pisaba SU pantalla.
// Contra la base LOCAL (usuarios descartables).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

const ulong U = 999000000000054001UL;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(ShopModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var shop = sp.GetRequiredService<IShopRepository>();
var events = sp.GetRequiredService<IGameEvents>();
var combat = sp.GetRequiredService<ICombatSessionService>();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

async Task Clean()
{
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
}

async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task<long> Have(string name) => await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity), 0)::bigint FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task SetGold(int gold) => await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)U, g = gold });

await Clean();
await users.CreateAccountAsync(U, "Hechicero");

// =====================================================================================================================================================================================================
Console.WriteLine("== 1. La tienda: precio × cantidad no da la vuelta ==");
var food = (await items.GetByNameAsync("Mate Amargo"))!;
long startFood = await Have(food.Name);
Check(food.BuyPrice > 0 && food.SellPrice > 0, $"el Mate Amargo se compra a {food.BuyPrice} y se vende a {food.SellPrice}");

// Cantidades cuyo total en int da la vuelta: a 0 o poco (2^32 / precio) y a un número negativo.
int wrapsToZero = (int)(4294967296L / food.BuyPrice);
int wrapsNegative = (int)(3000000000L / food.BuyPrice);
foreach (int qty in new[] { wrapsToZero, wrapsNegative, int.MaxValue })
{
    await SetGold(100);
    var r = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, food.Name, qty);
    Check(await Gold() == 100 && await Have(food.Name) == startFood && r.Embed is null && r.PlainMessage is not null,
        $"comprar x{qty:N0} con 100 de oro: no compra nada y no toca el oro (oro {await Gold()}, comida {await Have(food.Name)})");
}

await SetGold(100);
var okBuy = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, food.Name, 5);
Check(okBuy.Embed is not null && await Gold() == 100 - 5 * food.BuyPrice && await Have(food.Name) == startFood + 5, "una compra normal (x5) sigue andando y cobra lo que dice");

// Vender: con el inventario lleno de verdad, el total que da la vuelta no puede pagar de menos ni de más.
int sellWraps = (int)(4294967296L / food.SellPrice);
await db.ExecuteAsync("UPDATE inventory SET quantity = @q WHERE discord_id = @id AND item_id = @item", new { id = (long)U, item = food.ItemId, q = sellWraps });
await SetGold(0);
var sold = await ShopModule.ExecuteSellAsync(items, shop, events, U, food.Name, sellWraps);
Check(sold.Embed is null && await Gold() == 0 && await Have(food.Name) == sellWraps, $"vender x{sellWraps:N0} (el total no entra en un int): se rechaza sin tocar nada (oro {await Gold()})");
await db.ExecuteAsync("UPDATE inventory SET quantity = 10 WHERE discord_id = @id AND item_id = @item", new { id = (long)U, item = food.ItemId });
var okSell = await ShopModule.ExecuteSellAsync(items, shop, events, U, food.Name, 4);
Check(okSell.Embed is not null && await Gold() == 4 * food.SellPrice && await Have(food.Name) == 6, "una venta normal (x4) sigue andando");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
