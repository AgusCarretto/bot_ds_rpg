using System.Globalization;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Microsoft.Extensions.Configuration;

// /forge recipes y /drops rediseñados: límites de Discord con datos reales (todas las zonas y clases) + una muestra.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var zoneRepo = new ZoneRepository(factory);
var recipeRepo = new RecipeRepository(factory);
var zones = ZoneRanking.OrderByDifficulty(await zoneRepo.GetAllAsync());

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
int Total(Embed e) => (e.Title?.Length ?? 0) + (e.Description?.Length ?? 0) + (e.Footer?.Text.Length ?? 0) + e.Fields.Sum(f => f.Name.Length + f.Value.Length);

Console.WriteLine("== /forge recipes ==");
// v0.27.0: el recetario completo, una línea por receta con los emojis de cada ítem. Cada zona muestra SU arma de clase y el amuleto; al final, «Sin zona específica» (la Hoja de Acero Puro).
// v0.28.1: es la opción «completo» de /config (el clásico es el de por defecto). Cada parte menos la última termina con una línea en blanco (un espacio de ancho cero) que NO es una receta: se descarta al contar las líneas.
string[] RecipeLines(string value) => value.Split('\n').Where(l => l.Trim((char)0x200B).Length > 0).ToArray();
int worst = 0; bool allOk = true;
foreach (var cls in new[] { "Guerrero", "Hechicero", "Ninja", "Arquero" })
{
    var e = await ForgeModule.BuildRecipesEmbed(recipeRepo, zoneRepo, cls);
    worst = Math.Max(worst, Total(e));
    allOk &= e.Fields.Length == zones.Count + 1 && e.Fields.Take(zones.Count).All(f => RecipeLines(f.Value).Length == 2) && e.Fields.Last().Name == "🧰 Sin zona específica"
        && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256 && f.Value.Contains(" → ") && f.Value.Contains("`+") && !f.Inline) && Total(e) < 6000 && e.Footer is null;
}
Check(allOk, $"4 clases: un campo por zona con SUS 2 recetas (arma de clase y amuleto) y «Sin zona específica» al final, una línea por receta; el más grande mide {worst} de 6000");
var sample = await ForgeModule.BuildRecipesEmbed(recipeRepo, zoneRepo, "Guerrero");
Console.WriteLine($"\n--- {sample.Title}\n{sample.Description}");
foreach (var f in sample.Fields) Console.WriteLine($"\n{f.Name}\n{f.Value}");
var allRecipesDb = await recipeRepo.GetAllAsync();
// v0.29.0: las recetas de herramientas (Carpintero) no están en este recetario y su arte todavía no existe: se exceptúan.
var emojiMissing = allRecipesDb.Where(r => !BotDsRpg.GameData.ToolRules.IsToolType(r.ResultItem.Type)).Where(r => string.IsNullOrWhiteSpace(r.ResultItem.Emoji) || r.Ingredients.Any(i => string.IsNullOrWhiteSpace(i.Emoji))).Select(r => r.ResultItem.Name).ToList();
bool everyEmojiShown = sample.Fields.SelectMany(f => RecipeLines(f.Value)).All(l => System.Text.RegularExpressions.Regex.Matches(l, "<a?:[A-Za-z0-9_]+:\\d+>").Count >= 3);
Check(emojiMissing.Count == 0 && everyEmojiShown, $"los emojis: el resultado y cada ingrediente de cada línea van con su emoji propio ({(emojiMissing.Count == 0 ? "todas las recetas tienen emoji en todo" : "sin emoji en: " + string.Join(", ", emojiMissing))})");

Console.WriteLine("\n== /drops ==");
var msg = await DropsModule.BuildDropsMessageAsync(new UserRepository(factory), zoneRepo, new MonsterRepository(factory), new ItemRepository(factory), 1UL);
int dropsTotal = msg.Text.Length + msg.Embeds.Sum(Total);
Check(msg.Embeds.Length == 5 && msg.Embeds.All(e => e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256) && e.Fields.Length == 3), "5 tarjetas (una por zona), cada una con sus 3 bloques Cazar / Viajar / Jefe");
Check(dropsTotal < 6000, $"el mensaje entero mide {dropsTotal} de 6000");
Check(msg.Embeds.All(e => e.Fields.All(f => f.Value.Contains("\n└ "))), "cada monstruo en dos líneas: nombre y, abajo, lo que suelta");
Check(msg.Embeds.Select(e => e.Color).Distinct().Count() == 5, "cada zona con su color");
foreach (var e in msg.Embeds.Take(2)) { Console.WriteLine($"\n--- {e.Title} ({e.Description})"); foreach (var f in e.Fields) Console.WriteLine($"{f.Name}\n{f.Value}\n"); }

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
