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
int worst = 0; bool allOk = true;
foreach (var z in zones)
    foreach (var cls in new[] { "Guerrero", "Hechicero", "Ninja", "Arquero" })
    {
        var e = await ForgeModule.BuildRecipesEmbed(recipeRepo, zoneRepo, cls, z.ZoneId);
        worst = Math.Max(worst, Total(e));
        allOk &= e.Fields.Length == 3 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256 && f.Value.Contains("• ") && f.Value.Contains("oro")) && Total(e) < 6000 && e.Fields.All(f => !f.Inline);
    }

Check(allOk, $"5 zonas x 4 clases: 3 recetas (clase, general y amuleto; una por bloque), cada una con sus ingredientes en líneas aparte; el más grande mide {worst} de 6000");
var sample = await ForgeModule.BuildRecipesEmbed(recipeRepo, zoneRepo, "Guerrero", zones[1].ZoneId);
Console.WriteLine($"\n--- {sample.Title}\n{sample.Description}");
foreach (var f in sample.Fields) Console.WriteLine($"\n{f.Name}\n{f.Value}");
Console.WriteLine($"\n[footer] {sample.Footer?.Text}");

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
