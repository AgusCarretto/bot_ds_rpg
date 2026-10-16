using System.Globalization;
using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.Repositories;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var inventory = new InventoryRepository(factory);
var recipes = new RecipeRepository(factory);
var zones = new ZoneRepository(factory);
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const ulong U = 999000000000008001UL;
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
await users.GetOrCreateUserAsync(U);
async Task Give(string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q });
void Show(Embed e) { if (!string.IsNullOrEmpty(e.Description)) Console.WriteLine("   [descripción]\n" + string.Join("\n", e.Description.Split((char)10).Select(l => "       " + l))); foreach (var f in e.Fields) Console.WriteLine($"   [{(f.Inline ? "col" : "fila")}] {f.Name.Replace(((char)0x200B).ToString(), "·")}\n{string.Join("\n", f.Value.Replace(((char)0x200B).ToString(), "·").Split((char)10).Select(l => "       " + l))}"); }

Console.WriteLine("== Inventario de ejemplo ==");
foreach (var (n, q) in new[] { ("Piedra", 12), ("Madera de Pino", 8), ("Hierro", 3), ("Madera de Roble", 4), ("Carbón", 2), ("Oro Puro", 1), ("Pluma de Ñandú", 5), ("Colmillo de Jabalí", 2), ("Pelaje Oscuro", 1), ("Espada de Madera", 1), ("Amuleto del Levantador", 1), ("Mate Amargo", 6), ("Cajón de Pino", 2), ("Arca del Soberano", 1) }) await Give(n, q);
var e1 = await BotDsRpg_GameModule.Build(inventory, U);
Show(e1);
string T(Embed e) => string.Join("\n", e.Fields.Select(f => f.Name + "\n" + f.Value)).Replace((char)0xA0, ' ').Replace(((char)0x2003).ToString(), "");   // todo el texto, en el orden en que salen (los espacios que no se parten, ya como comunes)
string d1 = T(e1);
int At(string s) => d1.IndexOf(s, StringComparison.Ordinal);
int Col(Embed e, string nameContains) => Array.FindIndex(e.Fields.ToArray(), f => f.Name.Contains(nameContains));
string ZW = ((char)0x200B).ToString();
Check(string.IsNullOrEmpty(e1.Description) && e1.Fields.Length % 3 == 0 && e1.Fields.All(f => f.Inline) && e1.Fields.Where((f, i) => i % 3 == 2).All(f => f.Name == ZW && f.Value == ZW), "DOS COLUMNAS (v0.9.3): todo en línea, de a tres por fila con el tercero vacío cerrando la fila, sin descripción");
Check(Col(e1, "Madera") == 0 && Col(e1, "Mineral") == 1 && Col(e1, "Drops de monstruo") == 3 && Col(e1, "Consumibles") == 4 && e1.Fields[4].Value.Contains("📦 **Cajas**"), "fila 1: Madera | Mineral; fila 2: Drops | Consumibles (con las Cajas debajo): " + string.Join(" | ", e1.Fields.Select(f => f.Name == ZW ? "·" : f.Name)));
Check(At("Madera de Pino") < At("Madera de Roble") && At("Madera de Roble") < At("Mineral") && At("Piedra") < At("Hierro") && At("Hierro") < At("Oro Puro"), "dentro de cada columna, de lo más común a lo más raro (la madera y los minerales no se mezclan)");
Check(e1.Fields[3].Value.Contains("Pluma de Ñandú") && e1.Fields[3].Value.Contains("Colmillo de Jabalí") && !e1.Fields[0].Value.Contains("Pluma") && !e1.Fields[1].Value.Contains("Pluma"), "los drops de monstruo están en SU campo (debajo de la madera), separados de los materiales de recolección");
Check(d1.Contains("**Piedra**: 12") && d1.Contains("**Cajón de Pino**: 2") && !d1.Contains("(Común)") && !d1.Contains("_(") && !d1.Contains(" ×"), "cada renglón es «ícono **Nombre**: cantidad», sin la rareza escrita");
Check(e1.Footer?.Text?.Contains("/open") == true && !d1.Contains("Armas") && !d1.Contains("Amuletos") && !d1.Contains("Espada de Madera") && !d1.Contains("Amuleto del Levantador") && !d1.Contains("(1/") && !d1.Contains("(2/"), "el aviso de /open en el pie; las armas y amuletos no van en el inventario; nunca '(1/2)'");

Console.WriteLine("\n== Solo drops (una columna) ==");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await Give("Pluma de Ñandú", 1);
var e2 = await BotDsRpg_GameModule.Build(inventory, U);
Check(e2.Fields.Length == 1 && e2.Fields[0].Name == "🩸 Drops de monstruo" && T(e2).Contains("**Pluma de Ñandú**: 1"), "con una sola categoría: solo ese campo, sin huecos");

Console.WriteLine("\n== Peor caso: todos los ítems del juego ==");
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 9999 FROM items ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = 9999", new { id = (long)U });
var e3 = await BotDsRpg_GameModule.Build(inventory, U);
Check(string.IsNullOrEmpty(e3.Description) && e3.Fields.All(f => f.Value.Length <= 1024) && e3.Fields.Length <= 25 && e3.Length < 6000 && !e3.Fields.Any(f => f.Name.Contains("(1/")), $"con los {await db.ExecuteScalarAsync<int>("SELECT count(*) FROM items")} ítems del catálogo y 9999 de cada uno: {e3.Fields.Length} columnas, total {e3.Length} de 6000, y nunca '(1/2)'");
Check(T(e3).Contains("**Colmillo de Jabalí**: 9.999") && T(e3).Contains("**Cajón de Pino**: 9.999") && T(e3).Split(": 9.999").Length - 1 == await db.ExecuteScalarAsync<int>("SELECT count(*) FROM items WHERE type IN ('Madera','Mineral','Material','Consumable','Caja','PetFood','Huevo')"), "aparece cada uno de los ítems de inventario, ninguno se pierde");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
var e4 = await BotDsRpg_GameModule.Build(inventory, U);
Check(e4.Description.Contains("Todavía no tenés"), "inventario vacío: el mensaje de siempre");

Console.WriteLine("\n== Recetas con 'tenés X de Y' ==");
var user = (await users.GetByDiscordIdAsync(U))!;
await db.ExecuteAsync("UPDATE users SET gold = 50 WHERE discord_id = @id", new { id = (long)U });
await Give("Madera de Pino", 2); await Give("Hierro", 5);
var owned = (await inventory.GetByDiscordIdAsync(U)).ToDictionary(x => x.ItemName, x => x.Quantity);
var r1 = await ForgeModule.BuildRecipesEmbed(recipes, zones, user.Class, user.CurrentZoneId, owned, 50);
Show(r1);
string all = string.Join("\n", r1.Fields.Select(f => f.Value));
Check(all.Contains("✅") && all.Contains("❌") && all.Contains("2/") && r1.Fields.All(f => f.Value.Contains((char)10 + "" + (char)10)), "cada ingrediente dice cuánto tenés de cuánto hace falta, con ✅/❌, el oro también, y una línea en blanco separa el resumen de los ingredientes");
var r0 = await ForgeModule.BuildRecipesEmbed(recipes, zones, user.Class, user.CurrentZoneId);
Check(r0.Fields.All(f => f.Value.Contains("• ")) && !string.Join("", r0.Fields.Select(f => f.Value)).Contains("✅"), "sin inventario (como antes) sigue la lista de siempre");

Console.WriteLine("\n== Íconos grandes: la imagen del emoji como miniatura ==");
string? U1 = BotDsRpg.GameData.ItemDisplay.ImageUrl("<:espada_madera:1555633462432636958>");
Check(U1 == "https://cdn.discordapp.com/emojis/1555633462432636958.png?size=128", "un emoji normal da la URL del PNG del CDN: " + U1);
Check(BotDsRpg.GameData.ItemDisplay.ImageUrl("<a:fuego:1555633462432636958>", 64) == "https://cdn.discordapp.com/emojis/1555633462432636958.gif?size=64", "uno animado da el GIF y respeta el tamaño");
Check(BotDsRpg.GameData.ItemDisplay.ImageUrl(null) is null && BotDsRpg.GameData.ItemDisplay.ImageUrl("") is null && BotDsRpg.GameData.ItemDisplay.ImageUrl("🔥") is null && BotDsRpg.GameData.ItemDisplay.ImageUrl("<:mal:12>") is null && BotDsRpg.GameData.ItemDisplay.ImageUrl("https://evil.example/x.png") is null, "sin emoji, o con algo que no es un código de emoji de Discord: null (nunca una URL inventada)");
var itemRepo = new ItemRepository(factory);
var hierroItem = (await itemRepo.GetByNameAsync("Hierro"))!; var cajonItem = (await itemRepo.GetByNameAsync("Cajón de Pino"))!; var madera = (await itemRepo.GetByNameAsync("Madera de Pino"))!;
Check(GatheringModule.BuildResultEmbed(BotDsRpg.GameData.CooldownCatalog.Mine, hierroItem, 2).Thumbnail?.Url == BotDsRpg.GameData.ItemDisplay.ImageUrl(hierroItem.Emoji) && hierroItem.Emoji is not null, "/mine y /chop: lo que juntaste va de miniatura");
Check(BoxModule.BuildOpenedEmbed(cajonItem, 1, 1, 10, [], 10).Thumbnail?.Url == BotDsRpg.GameData.ItemDisplay.ImageUrl(cajonItem.Emoji), "/open: la caja va de miniatura");
Check(GatheringModule.BuildResultEmbed(BotDsRpg.GameData.CooldownCatalog.Chop, madera, 3).Thumbnail?.Url?.StartsWith("https://cdn.discordapp.com/emojis/") == true, "las maderas (que siguen siendo emojis del servidor) también");
Check(GatheringModule.BuildResultEmbed(BotDsRpg.GameData.CooldownCatalog.Mine, new BotDsRpg.Models.Item { ItemId = hierroItem.ItemId, Name = hierroItem.Name, Type = hierroItem.Type, Rarity = hierroItem.Rarity, Emoji = null }, 1).Thumbnail is null, "un ítem sin emoji todavía no rompe nada: sin miniatura");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

static class BotDsRpg_GameModule
{
    public static async Task<Embed> Build(IInventoryRepository inventory, ulong id) => await GameModule.BuildInventoryEmbedAsync(inventory, id, "Tester");
}
