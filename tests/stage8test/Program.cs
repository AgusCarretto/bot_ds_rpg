using System.Globalization;
using System.Reflection;
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

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var zones = sp.GetRequiredService<IZoneRepository>();
var recipes = sp.GetRequiredService<IRecipeRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var payouts = sp.GetRequiredService<IMiniEventRepository>();
var gameEvents = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const ulong BASE = 999000000000009000UL;
ulong[] P = Enumerable.Range(1, 8).Select(i => BASE + (ulong)i).ToArray();
const ulong GHOST = BASE + 99;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids); DELETE FROM game_events WHERE discord_id = ANY(@ids);", new { ids = P.Append(GHOST).Select(x => (long)x).ToArray() });
async Task<int> Have(ulong id, string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
async Task Give(ulong id, string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)id, n = name, q });
async Task Reset() { await Cleanup(); foreach (var p in P) { await users.GetOrCreateUserAsync(p); await db.ExecuteAsync("UPDATE users SET gold = 0, current_zone_id = 1 WHERE discord_id = @id", new { id = (long)p }); } }

Console.WriteLine("== 1. El consejo de qué farmear (reglas puras, recetas reales) ==");
await Reset();
var player = (await users.GetByDiscordIdAsync(P[0]))!;
var allRecipes = await recipes.GetAllAsync();
var allZones = await zones.GetAllAsync();
var view = RecipeCatalog.ViewFor(allRecipes, allZones, player.Class, player.CurrentZoneId);
var all = await new MonsterRepository(new BotDsRpg.Data.NpgsqlConnectionFactory(cfg)).GetAllAsync();
var woods = (await items.GetAllByTypeAsync("Madera")).Select(i => i.Name).ToHashSet();
var minerals = (await items.GetAllByTypeAsync("Mineral")).Select(i => i.Name).ToHashSet();
FarmSource Src(string n) => woods.Contains(n) ? FarmSource.Chop : minerals.Contains(n) ? FarmSource.Mine
    : all.Where(m => m.ZoneId == view.Zone!.ZoneId).FirstOrDefault(m => m.Monster.DropItemNames.Contains(n)) is { } z ? z.Kind switch { MonsterKind.Hunt => FarmSource.Hunt, MonsterKind.Travel => FarmSource.Travel, _ => FarmSource.Boss } : FarmSource.Unknown;
var empty = new Dictionary<string, int>();
var a0 = FarmAdvisor.Choose(view.Recipes, empty, 0, Src)!;
Check(a0 is not null && !a0.CraftableNow && a0.Missing.Count is >= 1 and <= 2 && a0.Missing.All(p => p.Missing > 0), $"sin nada: aconseja «{a0.RecipeName}», le faltan como mucho 2 cosas ({string.Join(", ", a0.Missing.Select(m => $"{m.Missing}× {m.ItemName} {m.Source}"))}) y oro {a0.GoldShort}");
Check(a0.Missing.All(m => m.Source != FarmSource.Unknown), "cada material faltante dice de dónde se consigue (/chop, /mine, /hunt...)");
var target = view.Recipes.First(r => r.ResultItem.Name == a0.RecipeName);
var full = target.Ingredients.ToDictionary(i => i.ItemName, i => i.Quantity);
var ready = FarmAdvisor.Choose(view.Recipes, full, target.GoldCost, Src)!;
Check(ready.CraftableNow && FarmAdvisor.ToField(ready).Title.Contains("Ya podés") && FarmAdvisor.ToField(ready).Value.Contains("/forge"), $"con todo para «{ready.RecipeName}»: avisa que ya puede forjar y manda a /forge");
var almost = full.ToDictionary(kv => kv.Key, kv => kv.Value);
string firstIng = target.Ingredients[0].ItemName; almost[firstIng] -= 1;
var near = FarmAdvisor.Choose(view.Recipes, almost, target.GoldCost, Src)!;
Check(!near.CraftableNow && near.RecipeName == a0.RecipeName && near.Missing.Count == 1 && near.Missing[0].ItemName == firstIng && near.Missing[0].Missing == 1, $"le falta 1 unidad de una receta: la elige y dice cuál ({near.Missing.FirstOrDefault()?.ItemName})");
var onlyGold = FarmAdvisor.Choose(view.Recipes, full, 0, Src)!;
var goldField = FarmAdvisor.ToField(onlyGold);
Check(!onlyGold.CraftableNow && onlyGold.GoldShort == target.GoldCost && onlyGold.Missing.Count == 0 && goldField.Value.Contains("💰"), $"con materiales pero sin oro: lo dice ({target.GoldCost} de oro)");
var crafted = view.Recipes.ToDictionary(r => r.ResultItem.Name, _ => 1);
Check(FarmAdvisor.Choose(view.Recipes, crafted, 0, Src) is null, "si ya tiene forjado todo lo que ve: no aconseja nada (null)");
var skip = view.Recipes.Where(r => r.ResultItem.Name != a0.RecipeName).ToDictionary(r => r.ResultItem.Name, _ => 1);
Check(FarmAdvisor.Choose(view.Recipes, skip, 0, Src)!.RecipeName == a0.RecipeName, "las recetas que ya forjó se saltean");
var field = FarmAdvisor.ToField(a0);
Check(field.Title.Contains("próxima forja") && field.Value.Contains(a0.RecipeName) && field.Value.Split((char)10).Length <= 4 && (field.Value.Contains("`/mine`") || field.Value.Contains("`/chop`") || field.Value.Contains("`/hunt`")) && field.Value.Length < 400, $"el campo es corto y claro:\n{field.Title}\n{field.Value}");

Console.WriteLine("\n== 2. El servicio del consejo (base real) ==");
var advisor = new FarmAdviceService(users, inventory, recipes, zones, items, new MonsterRepository(new BotDsRpg.Data.NpgsqlConnectionFactory(cfg)));
var s0 = await advisor.AdviceAsync(P[0]);
Check(s0 is not null && s0.RecipeName == a0.RecipeName && s0.Missing.Select(m => m.ItemName).SequenceEqual(a0.Missing.Select(m => m.ItemName)), "el servicio da el mismo consejo que la lógica pura con los datos reales");
foreach (var ing in target.Ingredients) await Give(P[0], ing.ItemName, ing.Quantity);
await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)P[0], g = target.GoldCost });
var s1 = await advisor.AdviceAsync(P[0]);
Check(s1 is { CraftableNow: true } && s1.RecipeName == a0.RecipeName, "al juntar todo, el consejo cambia solo a '¡ya podés forjar!' (lee el inventario real cada vez)");
Check(await advisor.AdviceAsync(GHOST) is null, "un jugador sin cuenta: sin consejo y sin error");
var gather = GatheringModule.BuildResultEmbed(CooldownCatalog.Mine, (await items.GetByNameAsync("Hierro"))!, 2);
Check(!gather.Fields.Any(f => f.Name.Contains("💡")) && gather.Fields.Length == 1, "el resultado de /mine y /chop YA NO lleva el consejo (ahora está en /tips)");
var victory = typeof(AdventureModule).GetMethod("BuildVictoryEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
var turn = CombatTurnResolver.ResolveTurn(new CombatantProfile(100, 0, "Guerrero", ClassPassives.For("Guerrero")), default, PlayerAction.Attack, 100, 100, 1, 5);
var cs = new CombatState("hunt", "Jabalí Rabioso", "🐗", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
var withTip = (Embed)victory.Invoke(null, [cs, turn, new CombatReward(10, 10, false), null, new LevelUpOutcome(player, 0), true])!;
Check(!withTip.Fields.Any(f => f.Name.Contains("💡")) && withTip.Length < 6000, "la victoria de combate YA NO lleva el consejo (ahora está en /tips)");

Console.WriteLine("\n== 3. Reglas del minievento ==");
Check(MiniEventRules.Reward(MiniEventKind.Stones, 1, 1) == new MiniEventReward("Piedra", 2, 0) && MiniEventRules.Reward(MiniEventKind.Stones, 3, 1).Quantity == 6 && MiniEventRules.Reward(MiniEventKind.Wood, 4, 1) == new MiniEventReward("Madera de Pino", 8, 0), "piedra y madera: 2 unidades solo y +2 por cada persona más (1 -> 2, 3 -> 6, 4 -> 8)");
Check(MiniEventRules.Reward(MiniEventKind.Stones, 10, 1).Quantity == 20 && MiniEventRules.Reward(MiniEventKind.Stones, 50, 1).Quantity == 20 && MiniEventRules.Reward(MiniEventKind.Stones, 0, 1).Quantity == 2, "tope en 10 personas (20 unidades) y nunca menos que el mínimo");
Check(MiniEventRules.Reward(MiniEventKind.Silver, 1, 1).Gold == 14 && MiniEventRules.Reward(MiniEventKind.Silver, 4, 1).Gold == 56 && MiniEventRules.Reward(MiniEventKind.Silver, 4, 3).Gold == 4 * 118 && MiniEventRules.Reward(MiniEventKind.Silver, 4, 5).Gold == 4 * 375, "plata: tantas cacerías de oro de TU zona como personas (1 persona en zona 1 = 14, 4 personas en zona 3 = 472)");
foreach (var kind in Enum.GetValues<MiniEventKind>())
{
    var rewards = Enumerable.Range(1, 10).Select(n => MiniEventRules.Reward(kind, n, 2)).Select(r => r.ItemName is null ? r.Gold : r.Quantity).ToList();
    if (!rewards.Zip(rewards.Skip(1), (x, y) => y > x).All(b => b)) { Check(false, $"{kind}: la recompensa NO crece con la gente"); }
}

Check(true, "para los tres tipos: cuantas más personas, más se lleva cada uno (estrictamente creciente de 1 a 10)");
Check(MiniEventRules.AllAnnouncements().All(a => a.Stories.Count >= 2 && a.Title.Length > 5) && MiniEventRules.AllAnnouncements().Count() == 3, "los 3 tipos tienen aviso con 2 o más relatos para variar");
Check(MiniEventSettings.Default == new MiniEventSettings(3, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15)), "por defecto: 3% por comando, uno cada 30 min por canal, 15 s para sumarse");
Environment.SetEnvironmentVariable("MiniEvent__ChancePercent", "100"); Environment.SetEnvironmentVariable("MiniEvent__ChannelCooldownMinutes", "0"); Environment.SetEnvironmentVariable("MiniEvent__JoinSeconds", "1");
var envSettings = MiniEventSettings.FromEnvironment();
Environment.SetEnvironmentVariable("MiniEvent__ChancePercent", null); Environment.SetEnvironmentVariable("MiniEvent__ChannelCooldownMinutes", null); Environment.SetEnvironmentVariable("MiniEvent__JoinSeconds", null);
Check(envSettings.ChancePercent == 100 && envSettings.ChannelCooldown == TimeSpan.Zero && envSettings.JoinWindow == TimeSpan.FromSeconds(3), "las variables de entorno cambian la frecuencia (y la ventana nunca baja de 3 segundos)");

Console.WriteLine("\n== 4. El minievento funcionando (base real, ventana corta) ==");
(int seed, MiniEventKind kind)[] seeds = [];
int SeedFor(MiniEventKind want) { for (int s = 0; s < 10000; s++) { var r = new Random(s); r.Next(100); if ((MiniEventKind)r.Next(3) == want) return s; } throw new Exception("sin semilla"); }
MiniEventService Svc(int chance, TimeSpan cooldown, int seed) => new(users, zones, items, payouts, gameEvents, new MiniEventSettings(chance, cooldown, TimeSpan.FromMilliseconds(700)), new Random(seed));
var sent = new List<(ulong Channel, Embed Embed, MessageComponent Buttons, FakeAnnouncement Ann)>();
Func<ulong, Func<Embed, MessageComponent, Task<IEventAnnouncement>>> Publisher(List<(ulong, Embed, MessageComponent, FakeAnnouncement)> log) => ch => (e, b) => { var a = new FakeAnnouncement(); log.Add((ch, e, b, a)); return Task.FromResult<IEventAnnouncement>(a); };
Guid IdOf(MessageComponent b) => Guid.Parse(b.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Single().CustomId![(("miniev_join:").Length)..]);

// Stones con 4 participantes: 3 se suman (uno clickea 5 veces a la vez), uno sin cuenta no cuenta.
await Reset();
var log1 = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var svc = Svc(100, TimeSpan.Zero, SeedFor(MiniEventKind.Stones));
await svc.MaybeSpawnAsync(555, Publisher(log1)(555));
Check(log1.Count == 1 && log1[0].Item2.Title!.Contains("🪨") && log1[0].Item3.Components.Count > 0, $"con 100% aparece el aviso con su botón: «{log1.FirstOrDefault().Item2?.Title}»");
await svc.MaybeSpawnAsync(555, Publisher(log1)(555));
Check(log1.Count == 1, "mientras hay un minievento abierto en el canal no aparece otro");
var evId = IdOf(log1[0].Item3);
var clicks = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => svc.JoinAsync(evId, P[0])).Concat([svc.JoinAsync(evId, P[1]), svc.JoinAsync(evId, P[2]), svc.JoinAsync(evId, GHOST), svc.JoinAsync(Guid.NewGuid(), P[3])]));
Check(clicks.Count(c => c.Status == JoinStatus.Joined) == 3 && clicks.Count(c => c.Status == JoinStatus.AlreadyJoined) == 4 && clicks.Count(c => c.Status == JoinStatus.NoAccount) == 1 && clicks.Count(c => c.Status == JoinStatus.Gone) == 1,
    $"9 clics a la vez: se suman 3 personas (el que clickeó 5 veces cuenta UNA), sin cuenta no cuenta, y un aviso inexistente da 'ya terminó'");
await Task.Delay(1600);
var result1 = log1[0].Item4.Last;
int p0Pino = await Have(P[0], "Piedra");
Check(result1 is not null && result1.Title!.Contains("Entre 3") && result1.Description.Contains($"<@{P[0]}>") && result1.Description.Contains("6× Piedra"), $"al cerrar la ventana edita el mensaje: «{result1?.Title}» / «{result1?.Description.Split((char)10).First(l => l.Contains("Cada"))}»");
Check(await Have(P[0], "Piedra") == 6 && await Have(P[1], "Piedra") == 6 && await Have(P[2], "Piedra") == 6 && await Have(P[3], "Piedra") == 0, "cada uno de los 3 cobró 6 Piedra (2 + 2 por cada persona más); el que no se sumó, nada");
var late = await svc.JoinAsync(evId, P[3]);
Check(late.Status is JoinStatus.Gone or JoinStatus.Finished, "llegar tarde: ya no deja sumarse");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM game_events WHERE kind = 'mini_event' AND discord_id = ANY(@ids)", new { ids = P.Select(x => (long)x).ToArray() }) == 3, "quedaron 3 eventos mini_event registrados (uno por participante)");

// Madera con 1 persona y plata con 2 (zonas distintas).
await Reset();
var log2 = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var woodSvc = Svc(100, TimeSpan.Zero, SeedFor(MiniEventKind.Wood));
await woodSvc.MaybeSpawnAsync(556, Publisher(log2)(556));
await woodSvc.JoinAsync(IdOf(log2[0].Item3), P[0]);
await Task.Delay(1600);
Check(await Have(P[0], "Madera de Pino") == 2 && log2[0].Item4.Last!.Title!.Contains("Entre 1"), "madera con una sola persona: 2 Madera de Pino");
await Reset();
await db.ExecuteAsync("UPDATE users SET current_zone_id = (SELECT zone_id FROM zones ORDER BY min_level OFFSET 2 LIMIT 1) WHERE discord_id = @id", new { id = (long)P[1] });
var log3 = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var silverSvc = Svc(100, TimeSpan.Zero, SeedFor(MiniEventKind.Silver));
await silverSvc.MaybeSpawnAsync(557, Publisher(log3)(557));
await silverSvc.JoinAsync(IdOf(log3[0].Item3), P[0]); await silverSvc.JoinAsync(IdOf(log3[0].Item3), P[1]);
await Task.Delay(1600);
Check(await Gold(P[0]) == 2 * 14 && await Gold(P[1]) == 2 * 118 && log3[0].Item4.Last!.Description.Contains("oro"), $"plata con 2 personas: el de zona 1 cobra {await Gold(P[0])} (2 x 14) y el de zona 3 cobra {await Gold(P[1])} (2 x 118)");

// Nadie se suma.
await Reset();
var log4 = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
await Svc(100, TimeSpan.Zero, 1).MaybeSpawnAsync(558, Publisher(log4)(558));
await Task.Delay(1600);
Check(log4[0].Item4.Last!.Description.Contains("Nadie llegó") && log4[0].Item4.Last!.Title!.Contains("terminó"), "si nadie se suma, el mensaje lo dice y no se paga nada");

// Frecuencia y carreras.
var log5 = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var never = Svc(0, TimeSpan.Zero, 3);
for (int i = 0; i < 50; i++) await never.MaybeSpawnAsync(600 + (ulong)i, Publisher(log5)(600));
Check(log5.Count == 0, "con 0% nunca aparece");
var rate = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var three = Svc(3, TimeSpan.Zero, 42);
for (int i = 0; i < 20000; i++) await three.MaybeSpawnAsync(1000 + (ulong)i, Publisher(rate)(1000));
Check(Math.Abs(rate.Count / 200.0 - 3.0) < 0.6, $"con 3% en 20.000 comandos (canales distintos) aparece ~{rate.Count / 200.0:0.0}% de las veces");
var cooled = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var cool = Svc(100, TimeSpan.FromHours(1), 9);
await cool.MaybeSpawnAsync(700, Publisher(cooled)(700)); await Task.Delay(1200);
await cool.MaybeSpawnAsync(700, Publisher(cooled)(700)); await cool.MaybeSpawnAsync(701, Publisher(cooled)(701));
Check(cooled.Count == 2 && cooled[0].Item1 == 700 && cooled[1].Item1 == 701, "con 1 hora de espera por canal: en el mismo canal no vuelve a aparecer, en otro canal sí");
var parallel = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
var racing = Svc(100, TimeSpan.Zero, 5);
var lockObj = new object();
Func<Embed, MessageComponent, Task<IEventAnnouncement>> safePub = (e, b) => { lock (lockObj) { parallel.Add((800, e, b, new FakeAnnouncement())); } return Task.FromResult<IEventAnnouncement>(parallel[^1].Item4); };
await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => racing.MaybeSpawnAsync(800, safePub)));
Check(parallel.Count == 1, "30 comandos a la vez en el mismo canal: aparece UN solo minievento");
var failing = Svc(100, TimeSpan.Zero, 6);
await failing.MaybeSpawnAsync(900, (e, b) => throw new InvalidOperationException("sin permiso para escribir (simulado)"));
var afterFail = new List<(ulong, Embed, MessageComponent, FakeAnnouncement)>();
await failing.MaybeSpawnAsync(900, Publisher(afterFail)(900));
Check(afterFail.Count == 1, "si no se puede publicar (sin permisos): no tira excepción y el canal queda libre para el próximo");

Console.WriteLine("\n== 5. Registro ==");
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
Check(interactions.ComponentCommands.Any(c => c.Name.StartsWith("miniev_join")) && sp.GetService<IMiniEventService>() is not null && sp.GetService<IFarmAdvisor>() is not null && ActivatorUtilities.CreateInstance<MiniEventModule>(sp) is not null, "el botón del minievento, el servicio y el consejero están en el DI real");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeAnnouncement : IEventAnnouncement
{
    public Embed? Last { get; private set; }
    public Task UpdateAsync(Embed embed, MessageComponent? components) { Last = embed; return Task.CompletedTask; }
}
