using System.Globalization;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Microsoft.Extensions.Configuration;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var items = new ItemRepository(factory);
var boxes = new BoxRepository(factory);
var buffs = new BuffRepository(factory);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== 1. La tienda, más limpia ==");
var shopItems = await ShopModule.LoadShopItemsAsync(items);
var embed = ShopModule.BuildViewEmbed(shopItems, await buffs.GetItemBuffsAsync());
Console.WriteLine("   " + embed.Title + "\n   " + embed.Description.Replace("\n", "\n   "));
foreach (var f in embed.Fields) Console.WriteLine($"   --- {f.Name} (columna: {f.Inline})\n   " + f.Value.Replace("\n", "\n   "));
string all = embed.Description + string.Join("\n", embed.Fields.Select(f => f.Name + "\n" + f.Value));
Check(embed.Fields.Length == 3 && embed.Fields.All(f => f.Inline) && embed.Fields[0].Name == "Comida" && embed.Fields[1].Name == "Cajas" && embed.Fields[2].Name == "Mascotas", "columnas lado a lado: Comida | Cajas | Mascotas (v0.10.0)");
Check(!new[] { "[Común]", "[Raro]", "[Épico]", "[Legendario]", "[Mítico]", "Común", "Legendario", "❤️", "💰", "💸", "🎲" }.Any(all.Contains), "sin rareza escrita ni emojis de adorno (sin ❤️ 💰 💸 🎲 y sin '[Común]')");
Check(all.Contains("<:cofre_comun:1555578513682075809> Cajón de Pino") && all.Contains("<:cofre_legendario:1555578509601280113> Cofre de Oro") && all.Contains("<:mate_comun:1555578601892749433> Mate Amargo"), "cada ítem muestra SU emoji (los cofres nuevos y la comida que ya tenía)");
Check(all.Contains("Cura 150 HP · +15% ATQ 30 min · 2100 oro") && all.Contains("Cura 15 HP · 8 oro") && !all.Contains("Venta"), "detalle corto en una línea: 'Cura 15 HP · 8 oro' y el banquete con su buff; sin precio de venta");
Check(embed.Description.Contains("una vez cada 2 horas") && embed.Description.Contains("/open") && embed.Description.Contains("El Tabernero") && embed.Length < 2000, $"el aviso de las cajas y el saludo del tendero siguen, y el embed mide {embed.Length} caracteres");
Check(!all.Contains("Arca del Soberano") && shopItems.Count(i => i.Type == "Caja") == 4, "la caja Mítica (premio) no se ofrece");

Console.WriteLine("\n== 2. Las cajas con el sorteo REAL (20.000 aperturas por caja, jugador con TODO desbloqueado) ==");
var rng = new Random(2026);
var priceOf = (await items.GetAllByTypeAsync("Madera")).Concat(await items.GetAllByTypeAsync("Mineral")).Concat(await items.GetAllByTypeAsync("Material")).Concat(await items.GetAllByTypeAsync("Consumable")).Concat(await items.GetAllByTypeAsync("Caja")).ToDictionary(i => i.ItemId);
int RankOf(string r) => RarityCatalog.RankOf(r);
const ulong Rich = 999000000000009101UL;
var users = new UserRepository(factory);
await using var db = new Npgsql.NpgsqlConnection(conn);
await Dapper.SqlMapper.ExecuteAsync(db, "DELETE FROM users WHERE discord_id = @id", new { id = (long)Rich });
await users.GetOrCreateUserAsync(Rich);
await Dapper.SqlMapper.ExecuteAsync(db, "UPDATE users SET level = 99, highest_zone_cleared = (SELECT MAX(zone_id) FROM zones) WHERE discord_id = @id", new { id = (long)Rich });
var ctx = await new BotDsRpg.Services.BoxContextService(items, new MonsterRepository(factory), new ZoneRepository(factory), users).BuildAsync(Rich);
await Dapper.SqlMapper.ExecuteAsync(db, "DELETE FROM users WHERE discord_id = @id", new { id = (long)Rich });

// En un sumidero de oro vender lo que sale de una caja no puede devolver lo que costó. Antes (v0.6) el valor se medía en "oro de venta" también.
foreach (var name in new[] { "Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro" })
{
    var item = (await items.GetByNameAsync(name))!;
    var def = (await boxes.GetDefinitionAsync(item.ItemId))!;
    double sum = 0, units = 0; int ge1 = 0, epic = 0;
    const int N = 20000;
    for (int i = 0; i < N; i++)
    {
        var loot = BoxLootRoller.Roll(def, ctx, rng);
        double v = loot.Gold + loot.Items.Sum(l => priceOf[l.ItemId].SellPrice * l.Quantity);
        sum += v; units += loot.Items.Sum(l => l.Quantity); if (v >= item.BuyPrice) ge1++;
        if (loot.Items.Any(l => RankOf(l.Rarity) >= 2 && priceOf[l.ItemId].Type != "Caja")) epic++;
    }

    double ev = sum / N / item.BuyPrice * 100, back = ge1 * 100.0 / N;
    Console.WriteLine($"   {name,-16} precio {item.BuyPrice,7} · {units / N,5:0.0} ítems · vender todo devuelve {ev:0.0}% · {back:0.0}% de las aperturas devuelven más · {epic * 100.0 / N:0.0}% traen algo Épico o mejor");
    Check(ev < 10 && back < 0.5, $"{name} ({item.BuyPrice} oro): vender TODO lo que sale devuelve {ev:0.0}% del precio y en solo {back:0.0}% de las aperturas devuelve más de lo que costó (la caja es un sumidero de oro; el valor está en los materiales)");
}

Console.WriteLine("\n== 3. Emojis de cofres ==");
var expected = new Dictionary<string, string> { ["Cajón de Pino"] = "cofre_comun", ["Baúl de Roble"] = "cofre_raro", ["Arcón de Hierro"] = "cofre_epico", ["Cofre de Oro"] = "cofre_legendario", ["Arca del Soberano"] = "cofre_mitico" };
var ok = true;
foreach (var (name, emojiName) in expected) { var it = (await items.GetByNameAsync(name))!; if (it.Emoji is null || !it.Emoji.StartsWith($"<:{emojiName}:")) ok = false; }
Check(ok, "las 5 cajas tienen su cofre por rareza (común, raro, épico, legendario, mítico)");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
