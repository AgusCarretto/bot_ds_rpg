using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.14.1: /forge-special (el equipo del Fogón a la vista desde el principio) + las slots con 6 símbolos (esas, en casinotest).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + "\n" + f.Value)) + "\n" + (e.Footer?.Text ?? ""));

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var recipesRepo = new RecipeRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var inventory = new InventoryRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000015000UL;
async Task Clean() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });

var recipes = await recipesRepo.GetAllAsync();
var zones = await zonesRepo.GetAllAsync();
var ordered = ZoneRanking.OrderByDifficulty(zones);
var gate = recipes.Where(r => r.ZoneId == FogonRules.GateZoneId).ToList();

Console.WriteLine("== 1. Sin cuenta ==");
var anon = ForgeSpecialModule.BuildEmbed(recipes, zones, null, null);
string anonText = AllText(anon);
Check(anon.Fields.Length == 2 && anon.Fields.Any(f => f.Name.Contains(FogonRules.WeaponName)) && anon.Fields.Any(f => f.Name.Contains(FogonRules.AmuletName)), "dos recetas: el Trinche y la Brasa");
Check(anonText.Contains("/start") && anonText.Contains("No desmanteles") && !anonText.Contains("✅") && !anonText.Contains("❌"), "sin cuenta: manda a /start, avisa que no desmantelen y no muestra ✅/❌");
Check(gate.Count == 2 && gate.All(r => r.Ingredients.Count >= 5 && anon.Fields.Any(f => f.Value.Contains(r.Ingredients[0].ItemName))), "cada receta lista sus ingredientes");
Check(anon.Length < 6000 && anon.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256), $"dentro de los límites de Discord ({anon.Length} / 6000)");

Console.WriteLine("\n== 2. Jugador nuevo: la puerta cerrada ==");
await Clean(); await users.GetOrCreateUserAsync(U);
var fresh = (await users.GetByDiscordIdAsync(U))!;
var owned0 = new Dictionary<string, int>();
var e0 = ForgeSpecialModule.BuildEmbed(recipes, zones, fresh, owned0);
string t0 = AllText(e0);
Check(t0.Contains("🔒") && t0.Contains(ordered[^1].Name) && !t0.Contains("ya está abierta"), $"dice que se forjan al vencer al jefe de {ordered[^1].Name}");
Check(e0.Fields.All(f => f.Value.Contains("❌") && !f.Value.Contains("✅")) && t0.Contains("/0") == false && t0.Contains("— 0/"), "todo en ❌ con «0/N» por ingrediente (y el oro)");

Console.WriteLine("\n== 3. Con materiales y con la puerta abierta ==");
var first = gate[0];
var owned = new Dictionary<string, int>(StringComparer.Ordinal);
foreach (var ing in first.Ingredients) owned[ing.ItemName] = ing.Quantity;          // la primera receta, completa
owned[gate[1].Ingredients[0].ItemName] = Math.Max(1, gate[1].Ingredients[0].Quantity - 1); // la segunda: le falta 1 de un ingrediente
await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = @z, gold = 99999 WHERE discord_id = @id", new { id = (long)U, z = ordered[^1].ZoneId });
var open = (await users.GetByDiscordIdAsync(U))!;
var e1 = ForgeSpecialModule.BuildEmbed(recipes, zones, open, owned);
string t1 = AllText(e1);
var f1 = e1.Fields.Single(f => f.Name.Contains(first.ResultItem.Name));
var f2 = e1.Fields.Single(f => f.Name.Contains(gate[1].ResultItem.Name));
Check(t1.Contains("✅ La puerta ya está abierta") && !t1.Contains("🔒"), "con el jefe de la última zona vencido: «la puerta ya está abierta»");
Check(!f1.Value.Contains("❌") && f1.Value.Contains("✅ 💰"), "la receta con todo: ni un ❌ (oro incluido)");
Check(f2.Value.Contains("❌") && f2.Value.Contains("✅ 💰") && f2.Value.Split('\n').Count(l => l.StartsWith("❌")) == f2.Value.Split('\n').Count(l => l.Contains("— 0/") || l.Contains("— " + (owned[gate[1].Ingredients[0].ItemName]) + "/")),
    "la receta a medias: el oro en ✅ y en ❌ solo los ingredientes que no tenés completos");
string expected = $"{owned[gate[1].Ingredients[0].ItemName]}/{gate[1].Ingredients[0].Quantity}";
Check(f2.Value.Contains(expected), $"y muestra «tenés/hace falta» ({expected})");

Console.WriteLine("\n== 4. Por la base (mirar no crea cuenta) ==");
await Clean();
var viaDb = await ForgeSpecialModule.BuildEmbedAsync(users, recipesRepo, zonesRepo, inventory, U);
Check(viaDb.Fields.Length == 2 && await users.GetByDiscordIdAsync(U) is null, "sin cuenta se ven las dos recetas y NO se crea la cuenta");
await users.GetOrCreateUserAsync(U);
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 7 FROM items WHERE name = @n", new { id = (long)U, n = gate[0].Ingredients[0].ItemName });
var viaDb2 = await ForgeSpecialModule.BuildEmbedAsync(users, recipesRepo, zonesRepo, inventory, U);
Check(AllText(viaDb2).Contains($"{7}/") && AllText(viaDb2).Contains("🔒"), "con cuenta lee la mochila real (7 de un ingrediente) y la puerta cerrada");
await Clean();

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
