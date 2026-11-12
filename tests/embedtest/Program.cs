using System.Globalization;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Microsoft.Extensions.Configuration;

// /forge recipes y /drops rediseñados: límites de Discord con datos reales (todas las zonas y clases) + una muestra.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var zoneRepo = new ZoneRepository(factory);
var recipeRepo = new RecipeRepository(factory);
var zones = ZoneRanking.ForPlayer(await zoneRepo.GetAllAsync(), 0);

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
var allRecipesDb = (await recipeRepo.GetAllAsync()).Where(r => r.ZoneId is not (>= 6 and <= 10)).ToList(); // v0.47.0: las recetas de las zonas 6 a 10 esperan su arte
var emojiMissing = allRecipesDb.Where(r => string.IsNullOrWhiteSpace(r.ResultItem.Emoji) || r.Ingredients.Any(i => string.IsNullOrWhiteSpace(i.Emoji))).Select(r => r.ResultItem.Name).ToList();
bool everyEmojiShown = sample.Fields.SelectMany(f => RecipeLines(f.Value)).All(l => System.Text.RegularExpressions.Regex.Matches(l, "<a?:[A-Za-z0-9_]+:\\d+>").Count >= 3);
Check(emojiMissing.Count == 0 && everyEmojiShown, $"los emojis: el resultado y cada ingrediente de cada línea van con su emoji propio ({(emojiMissing.Count == 0 ? "todas las recetas tienen emoji en todo" : "sin emoji en: " + string.Join(", ", emojiMissing))})");

Console.WriteLine("\n== /drops ==");
// v0.53.2: una zona por mensaje, con un desplegable para pasar a otra; un renglón por monstruo y solo el emoji del ítem.
var dropUsers = new UserRepository(factory);
var dropMonsters = new MonsterRepository(factory);
var dropItems = new ItemRepository(factory);
var dropBoxes = new ZoneBoxService(factory, zoneRepo);
var ladder = ZoneRanking.ForPlayer(await zoneRepo.GetAllAsync(), 0);
var perZone = new List<DropsModule.DropsMessage>();
foreach (var z in ladder) perZone.Add(await DropsModule.BuildDropsMessageAsync(dropUsers, zoneRepo, dropMonsters, dropItems, 1UL, dropBoxes, z.ZoneId));
var msg = perZone[0];
Check(perZone.All(m => m.Embeds.Length == 1 && m.Embeds[0].Fields.Length == 3 && m.Embeds[0].Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256)), $"{perZone.Count} zonas, cada una en su mensaje con sus 3 bloques Cazar / Viajar / Jefe");
Check(perZone.Select((m, i) => m.Embeds[0].Title!.Contains($"Zona {ladder[i].ZoneId} ")).All(x => x), "cada pedido abre la zona pedida");
Check(perZone.Max(m => m.Text.Length + m.Embeds.Sum(Total)) < 2000, $"cada mensaje es corto (el más largo mide {perZone.Max(m => m.Text.Length + m.Embeds.Sum(Total))})");
Check(perZone.All(m => m.Embeds[0].Fields.All(f => f.Value.Split('\n').Where(l => l.StartsWith("**")).All(l => l.Contains(" → ")) && !f.Value.Contains("└"))), "cada monstruo es UN renglón «**Monstruo** → ítem», sin └");
Check(perZone.All(m => m.Embeds[0].Fields.Single(f => f.Name.Contains("Viajar")).Name.Contains("% de caja") && m.Embeds[0].Fields.Single(f => f.Name.Contains("Viajar")).Value.Contains(" → ") && !m.Embeds[0].Fields.Single(f => f.Name.Contains("Viajar")).Value.Contains("📦")), "el viaje (v0.36.0) no suelta material: dice la chance de caja y qué caja, sin el 📦 de adorno");
Check(perZone.Select(m => m.Embeds[0].Color).Distinct().Count() == Math.Min(5, ladder.Count), "cada zona con su color");
var menuJson = msg.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single();
Check(menuJson.CustomId == "drops_zone:1" && menuJson.Options.Count == ladder.Count && menuJson.Options.Count(o => o.IsDefault == true) == 1 && menuJson.Options.All(o => o.Emote is null), "el desplegable lleva una opción por zona, marca la que se ve y no lleva emojis");
var fallback = await DropsModule.BuildDropsMessageAsync(dropUsers, zoneRepo, dropMonsters, dropItems, 1UL, dropBoxes, 999);
Check(fallback.Embeds[0].Title!.Contains($"Zona {ladder[0].ZoneId} "), "una zona que no está en la escalera abre la primera");
foreach (var e in perZone.Take(2).Select(m => m.Embeds[0])) { Console.WriteLine($"\n--- {e.Title} ({e.Description})"); foreach (var f in e.Fields) Console.WriteLine($"{f.Name}\n{f.Value}\n"); }

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
