using System.Reflection;
using System.Text.RegularExpressions;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.8.3: (1) TODOS los monstruos hablan (al aparecer, al caer y al vencerte) y la cara sale siempre en la victoria; (2) el inventario en columnas.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var monsterRepo = new MonsterRepository(factory);
var inventoryRepo = new InventoryRepository(factory);
var userRepo = new UserRepository(factory);

Console.WriteLine("== 1. Todos los monstruos tienen sus frases ==");
var all = await monsterRepo.GetAllAsync();
var nonBoss = all.Where(m => m.Kind != MonsterKind.Boss).Select(m => m.Monster.Name).ToHashSet();
var bosses = all.Where(m => m.Kind == MonsterKind.Boss).Select(m => m.Monster.Name).ToHashSet();
Check(nonBoss.Count == 15 && nonBoss.SetEquals(NpcDialogue.KnownMonsters), $"los {nonBoss.Count} monstruos de cacería y de viaje tienen frases propias (faltan: {string.Join(", ", nonBoss.Except(NpcDialogue.KnownMonsters))})");
Check(bosses.Count == 5 && bosses.SetEquals(NpcDialogue.KnownBosses.Where(b => b != "El Asador Eterno")) && NpcDialogue.KnownBosses.Contains("El Asador Eterno"), "y los 5 jefes de la escalera siguen con las suyas (más las del Asador Eterno, el jefe del Fogón)");
var tables = NpcDialogue.AllTables().ToList();
foreach (string name in nonBoss) foreach (var line in Enum.GetValues<NpcDialogue.BossLine>())
{
    var t = tables.FirstOrDefault(x => x.Key == $"{name}:{line}");
    if (t.Key is null || t.Lines.Count < 2 || t.Lines.Distinct().Count() != t.Lines.Count || t.Lines.Any(l => l.Length is < 6 or > 120)) { Check(false, $"{name}: {line} necesita 2 o más frases distintas y razonables"); }
}
Check(tables.All(t => t.Lines.Count >= 2 && t.Lines.Distinct().Count() == t.Lines.Count), $"las {tables.Count} situaciones de todos los personajes y monstruos tienen 2 o más frases distintas");
Check(NpcDialogue.Monster("Jabalí Rabioso", "🐗", NpcDialogue.BossLine.Intro).StartsWith("🐗 **Jabalí Rabioso:** «"), "el formato es el de siempre: '🐗 **Nombre:** «frase»'");
Check(NpcDialogue.Monster("Bicho Nuevo", "🐛", NpcDialogue.BossLine.Defeated).Contains("Bicho Nuevo") && NpcDialogue.Monster("Jefe Nuevo", "👑", NpcDialogue.BossLine.Victory, isBoss: true).Contains("Jefe Nuevo"), "un monstruo o un jefe que no está en la tabla usa frases genéricas (nunca queda mudo)");
Check(NpcDialogue.Monster("Rey Jabalí", "👑🐗", NpcDialogue.BossLine.Intro, isBoss: true) is { } s && NpcDialogue.AllTables().First(t => t.Key == "Rey Jabalí:Intro").Lines.Any(s.Contains), "los jefes siguen diciendo SUS frases por el mismo camino");

Console.WriteLine("\n== 2. Lo que dicen, en cada mensaje de combate ==");
string face = "<:zona1_jabali:1556680082343460985>";
string faceUrl = ItemDisplay.ImageUrl(face)!;
CombatState State(string name, string emoji, string command = "hunt") => new(command, name, emoji, 100, 60, 10, [], 0, 0, command == "boss" ? 1 : null, 100, 80, 100, 10, 2, 5, "Guerrero", ClassPassives.For("Guerrero"), MonsterPortrait: face);
var hunt = State("Jabalí Rabioso", "🐗");
var boss = State("Rey Jabalí", "👑🐗", "boss");
var turn = new TurnResult(null, false, 12, 0, 0, 60, 100, new MonsterHitOutcome(false, 7), 93, default);
var player = new User { MaxHp = 100, CurrentHp = 80, Level = 5, Class = "Guerrero" };
var outcome = new LevelUpOutcome(player, 0);
var reward = new CombatReward(10, 10, false);
var dropped = new Item { ItemId = 1, Name = "Colmillo de Jabalí", Type = "Material", Rarity = "Raro", Emoji = "<:colmillo_jabali:1555578251605442581>" };
var food = new Item { ItemId = 2, Name = "Mate Amargo", Type = "Consumable", Rarity = "Común", Emoji = "<:mate_comun:1555578601892749433>" };
const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
Embed Call(Type t, string method, params object?[] args) => (Embed)t.GetMethod(method, Any)!.Invoke(null, args)!;
bool Says(Embed e, string monster, NpcDialogue.BossLine line) => NpcDialogue.AllTables().First(t => t.Key == $"{monster}:{line}").Lines.Any(l => (e.Description ?? "").Contains($"«{l}»") || e.Fields.Any(f => f.Value.Contains($"«{l}»")));

Check(Says(AdventureModule.BuildEncounterEmbed(hunt), "Jabalí Rabioso", NpcDialogue.BossLine.Intro), "/hunt: el monstruo saluda al aparecer");
Check(Says(AdventureModule.BuildEncounterEmbed(boss), "Rey Jabalí", NpcDialogue.BossLine.Intro), "/boss: el jefe también (como antes)");
var winNoDrop = Call(typeof(AdventureModule), "BuildVictoryEmbed", hunt, turn, reward, null, outcome, true);
var winDrop = Call(typeof(AdventureModule), "BuildVictoryEmbed", hunt, turn, reward, dropped, outcome, true);
Check(Says(winNoDrop, "Jabalí Rabioso", NpcDialogue.BossLine.Defeated) && Says(winDrop, "Jabalí Rabioso", NpcDialogue.BossLine.Defeated), "ganes con drop o sin drop, el monstruo dice algo al caer");
Check(winNoDrop.Thumbnail?.Url == faceUrl && winDrop.Thumbnail?.Url == faceUrl, "y la cara del monstruo SIEMPRE es la miniatura, también cuando suelta algo");
Check(winDrop.Fields.Any(f => f.Value.Contains("Colmillo de Jabalí")), "el ítem que soltó sigue anunciado en su campo");
var winBoss = Call(typeof(AdventureModule), "BuildVictoryEmbed", boss, turn, reward, dropped, outcome, true);
Check(Says(winBoss, "Rey Jabalí", NpcDialogue.BossLine.Defeated) && winBoss.Thumbnail?.Url == faceUrl, "el jefe cae diciendo lo suyo, con su cara");
Check(Says(Call(typeof(AdventureModule), "BuildDefeatEmbed", hunt, turn, null), "Jabalí Rabioso", NpcDialogue.BossLine.Victory) && Says(Call(typeof(AdventureModule), "BuildDefeatEmbed", boss, turn, null), "Rey Jabalí", NpcDialogue.BossLine.Victory), "si te vence, el monstruo (y el jefe) se burla");
Check(Says(Call(typeof(UseModule), "BuildCombatDefeatEmbed", hunt, food, 5, null), "Jabalí Rabioso", NpcDialogue.BossLine.Victory), "también si te vence mientras te curabas con /use");
Check(Says(Call(typeof(AutoHuntModule), "BuildVictoryEmbed", hunt, outcome, reward, null), "Jabalí Rabioso", NpcDialogue.BossLine.Defeated) && Says(Call(typeof(AutoHuntModule), "BuildDefeatEmbed", hunt, null), "Jabalí Rabioso", NpcDialogue.BossLine.Victory), "y en /autohunt, ganes o pierdas");
Check(Call(typeof(AutoHuntModule), "BuildVictoryEmbed", hunt, outcome, reward, dropped).Thumbnail?.Url == faceUrl, "/autohunt con drop: también la cara");

Console.WriteLine("\n== 3. El inventario en columnas ==");
const ulong U = 999000000000009005UL;
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
async Task Clean() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
async Task Fill(string where, int qty) => await db.ExecuteAsync($"INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE {where} ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, q = qty });
await Clean(); await userRepo.GetOrCreateUserAsync(U);

var empty = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "prueba");
Check(empty.Fields.Length == 0 && empty.Description.Contains("Todavía no tenés"), "inventario vacío: el mensaje de siempre y ningún campo");

await Fill("type IN ('Madera','Mineral','Material','Consumable','Caja')", 9999);   // el peor caso: TODO el catálogo con 9.999 de cada uno
var big = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "prueba");
int expectedItems = await db.ExecuteScalarAsync<int>("SELECT count(*) FROM items WHERE type IN ('Madera','Mineral','Material','Consumable','Caja')");
Console.WriteLine($"   ({expectedItems} ítems, {big.Fields.Length} campos, {big.Length} caracteres)");
Check(big.Fields.Length >= 6 && big.Fields.Length <= 25 && big.Length < 6000 && big.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256), "todo el catálogo a 9.999: entra en los límites de Discord (campos de a lo sumo 1024, embed bajo 6000)");
string ZWl = ((char)0x200B).ToString();
var leftCol = big.Fields.Where((f, i) => i % 3 == 0).ToList(); var rightCol = big.Fields.Where((f, i) => i % 3 == 1).ToList();
Check(big.Fields.Length % 3 == 0 && big.Fields.All(f => f.Inline) && big.Fields.Where((f, i) => i % 3 == 2).All(f => f.Name == ZWl && f.Value == ZWl), "v0.9.3: DOS columnas, todo en línea de a tres por fila con el tercero vacío cerrando cada fila");
// v0.36.0: con los 10 drops que quedan (ya no hay drops de viaje) entran en UN campo: Madera | Mineral y debajo Drops | Consumibles.
Check(leftCol[0].Name.Contains("Madera") && rightCol[0].Name.Contains("Mineral") && leftCol[1].Name.Contains("Drops") && rightCol[1].Name.Contains("Consumibles") && big.Fields.Length == 6, "filas: Madera | Mineral y Drops | Consumibles: " + string.Join(" | ", big.Fields.Select(f => f.Name == ZWl ? "·" : f.Name)));
// El reparto cuando los drops NO entran en un campo (v0.26.3) sigue valiendo para cuando haya más: se prueba sumando 8 drops sintéticos a los reales. Madera | Mineral, luego los drops EN DOS COLUMNAS parejas y debajo
// los Consumibles: así en el celu (que apila los campos en el orden en que vienen) la segunda mitad de los drops queda pegada a la primera y no después de los Consumibles, como pasaba con «Fragmento de Alma» (2026-10-26).
var realEntries = await inventoryRepo.GetByDiscordIdAsync(U);
var extraDrops = realEntries.Where(e => e.Type == "Material").Take(8).Select(e => new InventoryEntry { ItemName = e.ItemName + " (extra)", Type = "Material", Rarity = e.Rarity, Quantity = e.Quantity, Emoji = e.Emoji }).ToList();
var bigFull = GameModule.BuildInventoryView(realEntries.Concat(extraDrops).ToList(), "prueba", U, U, GameModule.InvAll).Embed;
var leftCol2 = bigFull.Fields.Where((f, i) => i % 3 == 0).ToList(); var rightCol2 = bigFull.Fields.Where((f, i) => i % 3 == 1).ToList();
Check(leftCol2[0].Name.Contains("Madera") && rightCol2[0].Name.Contains("Mineral") && leftCol2[1].Name.Contains("Drops") && rightCol2[1].Name == ZWl && rightCol2[1].Value != ZWl && leftCol2[2].Name.Contains("Consumibles"), "con drops de más: Madera | Mineral, Drops | (resto de los drops) y Consumibles: " + string.Join(" | ", bigFull.Fields.Select(f => f.Name == ZWl ? "·" : f.Name)));
var phoneOrder = bigFull.Fields.Where(f => f.Value != ZWl).Select(f => f.Name == ZWl ? "·" : f.Name).ToList();   // el celu apila los campos con contenido en este orden (los vacíos no se ven)
int phoneDrops = phoneOrder.FindIndex(n => n.Contains("Drops"));
Check(phoneOrder.Count >= 5 && phoneDrops == 2 && phoneOrder[phoneDrops + 1] == "·" && phoneOrder[phoneDrops + 2].Contains("Consumibles"), "en el celu: Madera, Mineral, los drops (con su segunda mitad pegada, sin título) y recién después los Consumibles: " + string.Join(" | ", phoneOrder));
var dropHalves = new[] { bigFull.Fields[3], bigFull.Fields[4] }.Select(f => f.Value.Split('\n').Length).ToArray();
Check(Math.Abs(dropHalves[0] - dropHalves[1]) <= 1, $"los drops se reparten parejos entre las dos columnas ({dropHalves[0]} y {dropHalves[1]} renglones), no 14 y uno suelto");
Check(big.Fields.Any(f => f.Name.Contains("Drops")) && big.Fields.Any(f => f.Name.Contains("Cajas") || f.Value.Contains("**Cajas**")), "los drops y las cajas también están");
const char Nb = (char)0xA0;                       // espacio que no se parte: mantiene unido a cada ítem aunque la línea se corte
string Sep = ((char)0x2003).ToString() + " ";     // lo que separa una ficha de la siguiente (puede cortarse la línea ahí)
var lineRegex = new Regex(@"^(<a?:[A-Za-z0-9_]+:\d+>|[⚪🔵🟣🟡🔴]|📦|🥚|🔥) \*\*[^*]+\*\*: [\d.]+$");
string ZWs = ((char)0x200B).ToString();
var chips = big.Fields.SelectMany(f => f.Value.Split('\n')).Where(l => l.Length > 0 && l != "📦 **Cajas**" && l != ZWs).ToList();   // v0.9.1: los drops también son un renglón por ítem   // el encabezado de las cajas apiladas bajo la comida no es un ítem
Check(chips.Count == expectedItems && chips.All(l => lineRegex.IsMatch(l.Replace(Nb, ' '))), $"{chips.Count} ítems, todos con el formato «emoji **Nombre**: cantidad» (el ejemplo: {chips.FirstOrDefault()}; el que no calza: {chips.FirstOrDefault(l => !lineRegex.IsMatch(l.Replace(Nb, (char)32)))})");
Check(chips.Any(l => l.EndsWith(": 9.999") || l.EndsWith(Nb + "9.999")), "las cantidades llevan el separador de miles");
var dropFields = big.Fields.SkipWhile(f => !f.Name.Contains("Drops")).TakeWhile((f, i) => i == 0 || f.Name == ZWs).Where(f => f.Value != ZWs).ToList();   // el campo de los drops y su continuación (sin título), hasta los Consumibles
var dropChips = dropFields.SelectMany(f => f.Value.Split('\n')).ToList();
Check(dropChips.Count == 10 && dropChips.All(c => lineRegex.IsMatch(c)) && dropFields.All(f => !f.Value.Contains(Nb) && !f.Value.Contains(((char)0x2003).ToString())), $"los {dropChips.Count} drops van UNO POR RENGLÓN (v0.9.1: las fichas dejaban el ícono huérfano al final de la línea anterior)");
Check(dropFields.Count >= 1 && dropFields.Skip(1).All(f => f.Name == ((char)0x200B).ToString()) && !dropFields.Any(f => f.Name.Contains("(1/")), $"los drops caben en {dropFields.Count} campos: el primero con título y los demás sin título (nunca «(1/2)»)");
Check(big.Footer?.Text?.Contains("/open") == true, "con cajas, el pie dice cómo abrirlas");

await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await Fill("name IN ('Madera de Pino','Madera de Roble','Hierro','Mate Amargo','Colmillo de Jabalí')", 7);
var small = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "prueba");
Check(small.Fields.Length == 6 && small.Fields.All(f => f.Inline) && small.Fields[3].Name.Contains("Drops") && small.Fields[4].Name.Contains("Consumibles") && !small.Fields.Any(f => f.Name.Contains("Cajas")) && small.Footer is null, $"un inventario chico: solo lo que tiene algo ({string.Join(" | ", small.Fields.Select(f => f.Name == ZWl ? "·" : f.Name))}), sin cajas ni pie");
Check(small.Fields[0].Value.Split('\n').Length == 2 && small.Fields[0].Value.IndexOf("Pino") < small.Fields[0].Value.IndexOf("Roble"), "dentro de cada columna, de lo más común a lo más raro");
await Clean();
Console.WriteLine($"   (limpieza: usuarios de prueba restantes = {await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE discord_id = @id", new { id = (long)U })})");


Console.WriteLine("\n== Vista previa: el inventario de la captura del dueño ==");
await Clean(); await userRepo.GetOrCreateUserAsync(U);
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await Fill("name IN ('Madera de Pino')", 117); await Fill("name IN ('Madera de Roble')", 30); await Fill("name IN ('Madera de Nogal')", 13); await Fill("name IN ('Madera de Ébano')", 5);
await Fill("name IN ('Piedra')", 146); await Fill("name IN ('Carbón')", 16); await Fill("name IN ('Hierro')", 34); await Fill("name IN ('Oro Puro')", 20);
await Fill("type = 'Material' AND item_id % 3 <> 0", 3);   // unos 21 drops
await Fill("name IN ('Mate Amargo')", 1); await Fill("name IN ('Asado de Tira')", 14); await Fill("name IN ('Baúl de Roble')", 1);
var preview = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "agus_42");
Console.WriteLine($"   {preview.Title} · {preview.Fields.Length} campos · {preview.Length} caracteres");
for (int i = 0; i < preview.Fields.Length; i++)
{
    var f = preview.Fields[i];
    Console.WriteLine($"   [{i / 3 + 1}.{i % 3 + 1}] {(f.Name == ((char)0x200B).ToString() ? "(sin título)" : f.Name)} — {f.Value.Split('\n').Length} renglones, {f.Value.Length} caracteres");
}
await Clean();
await Clean(); await userRepo.GetOrCreateUserAsync(U);
await Fill("type = 'Material' AND item_id % 3 <> 0", 3);
var peek = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "agus_42");
foreach (var f in peek.Fields) Console.WriteLine("   >> " + System.Text.RegularExpressions.Regex.Replace(f.Value, "<a?:[A-Za-z0-9_]+:[0-9]+>", "[e]").Replace((char)0xA0, '_').Replace(((char)0x2003).ToString(), "<EM>"));
await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
