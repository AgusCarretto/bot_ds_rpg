using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Commands;
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
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var buffs = sp.GetRequiredService<IBuffRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
var cooldowns = sp.GetRequiredService<ICooldownRepository>();
var adventure = sp.GetRequiredService<IAdventureRepository>();
var monsters = sp.GetRequiredService<IMonsterRepository>();
var zones = sp.GetRequiredService<IZoneRepository>();
var shop = sp.GetRequiredService<IShopRepository>();
var casinoRepo = sp.GetRequiredService<ICasinoRepository>();
var casino = sp.GetRequiredService<ICasinoService>();
var transfers = sp.GetRequiredService<ITransferRepository>();
var raids = sp.GetRequiredService<IRaidSessionService>();
var events = new FakeEvents();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const long BaseId = 999000000000014000L;
ulong U = (ulong)BaseId + 1;
async Task Cleanup()
{
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
}

async Task Make(ulong id, int gold = 0, int level = 10, int hp = 100, int maxHp = 100, string cls = "Guerrero")
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET class = @c, level = @l, gold = @g, max_hp = @m, current_hp = @h, current_zone_id = 1, weapon_id = NULL, amulet_id = NULL WHERE discord_id = @id", new { id = (long)id, c = cls, l = level, g = gold, m = maxHp, h = hp });
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
}

async Task Give(ulong id, string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + @q", new { id = (long)id, n = name, q });
async Task<int> Have(ulong id, string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<int> HpOf(ulong id) => await db.ExecuteScalarAsync<int>("SELECT current_hp FROM users WHERE discord_id = @id", new { id = (long)id });
async Task<int> GoldOf(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
IUser FakeUser(long id, string name, bool bot = false) => (IUser)FakeProxy.Create(typeof(IUser), (n, a) => n switch { "get_Id" => (ulong)id, "get_IsBot" => bot, "get_Username" => name, "get_Mention" => $"<@{id}>", _ => null });

Console.WriteLine("== 1. Cantidades: number o all ==");
await Cleanup();
Check(AmountParser.Parse("all", 750) is { Ok: true, Amount: 750, IsAll: true } && AmountParser.Parse("ALL", 750).Amount == 750 && AmountParser.Parse(" todo ", 750).Amount == 750 && AmountParser.Parse("Toda", 750).Amount == 750 && AmountParser.Parse("max", 750).IsAll && AmountParser.Parse("máximo", 5).Amount == 5, "all / todo / toda / max / máximo (con mayúsculas y espacios) toman TODO el oro");
Check(AmountParser.Parse("500", 10) is { Ok: true, Amount: 500, IsAll: false } && AmountParser.Parse("1.000", 0).Amount == 1000 && AmountParser.Parse("1,000", 0).Amount == 1000 && AmountParser.Parse("12 345", 0).Amount == 12345, "los números, también con punto / coma / espacio de miles");
var zeroAll = AmountParser.Parse("all", 0);
Check(!zeroAll.Ok && zeroAll.Error!.Contains("0"), "all sin oro: lo dice");
foreach (var bad in new[] { "", "   ", "abc", "-5", "5.5.x", "mucho", "1e3" })
{
    var r = AmountParser.Parse(bad, 100);
    if (r.Ok) Check(false, $"«{bad}» no debería entenderse como un monto ({r.Amount})");
}

Check(!AmountParser.Parse("0", 100).Ok && AmountParser.Parse("0", 100).Error!.Contains("al menos 1") && !AmountParser.Parse("99999999999", 100).Ok && AmountParser.Parse("99999999999", 100).Error!.Contains("grande"), "cero y montos gigantes se rechazan con su motivo");
Check(AmountParser.Parse("abc", 100).Error!.Contains("all") && AmountParser.Parse(null, 100).Error!.Contains("all"), "lo que no se entiende explica cómo escribirlo (número o all)");

Console.WriteLine("\n== 2. Planificador de curación (puro) ==");
FoodStock Mate(int q) => new(1, "Mate Amargo", null, 15, q);
FoodStock Emp(int q) => new(2, "Empanada de Carne", null, 40, q);
FoodStock Asado(int q) => new(3, "Asado de Tira", null, 100, q);
var p1 = HealPlanner.Plan(100, [Mate(10), Emp(2)]);
Check(p1.Covers && p1.TotalHeal == 100 && p1.Units == 5 && p1.Foods.Single(f => f.Food.ItemId == 2).Quantity == 1 && p1.Foods.Single(f => f.Food.ItemId == 1).Quantity == 4, $"faltan 100 con mates de 15 y empanadas de 40: {string.Join(" + ", p1.Foods.Select(f => $"{f.Quantity}x {f.Food.Name}"))} = {p1.TotalHeal}");
// exactitud: 2 empanadas (80) + 1 mate (15) = 95 < 100; la mejor es 2 emp + 2 mates = 110 o 1 emp + 4 mates = 100 exacto
Check(HealPlanner.Plan(100, [Mate(10), Emp(2)]).TotalHeal == 100, "con mates de 15 y empanadas de 40 se puede llegar a 100 EXACTO (1 empanada + 4 mates): el planificador lo encuentra");
var exact = HealPlanner.Plan(100, [Mate(10), Emp(2)]);
Check(exact.Foods.Count == 2 && exact.Units == 5 && exact.Overshoot == 0, $"y entre los exactos usa las menos unidades ({exact.Units})");
Check(HealPlanner.Plan(15, [Mate(5)]) is { Covers: true, TotalHeal: 15 } && HealPlanner.Plan(15, [Mate(5)]).Units == 1, "falta justo lo que cura un mate: 1 mate");
Check(HealPlanner.Plan(1, [Mate(5), Asado(2)]) is { Covers: true } one && one.Foods.Single().Food.Name == "Mate Amargo" && one.Foods.Single().Quantity == 1, "una herida de 1 HP se cura con lo que MENOS desperdicia (un mate, no un asado)");
var notEnough = HealPlanner.Plan(500, [Mate(3), Emp(1)]);
Check(!notEnough.Covers && notEnough.TotalHeal == 85 && notEnough.Units == 4 && notEnough.Missing == 500, "si la comida no alcanza, el plan es TODA la comida y avisa que no cubre");
var single = HealPlanner.Plan(100, [Mate(20)]);
Check(single.Covers && single.Units == 7 && single.TotalHeal == 105, "con una sola comida elegida: las unidades justas (ceil(100/15) = 7)");
Check(HealPlanner.Plan(0, [Mate(5)]).Foods.Count == 0 && HealPlanner.Plan(-5, [Mate(5)]).Covers && HealPlanner.Plan(50, []).Foods.Count == 0 && !HealPlanner.Plan(50, []).Covers && HealPlanner.Plan(50, [new FoodStock(9, "X", null, 0, 5), new FoodStock(8, "Y", null, 10, 0)]).Foods.Count == 0, "sin nada que curar, sin comida, o con comida que no cura / sin unidades: plan vacío");
// Contra fuerza bruta
var rng = new Random(11);
int mismatches = 0, tested = 0;
for (int t = 0; t < 4000; t++)
{
    int kinds = rng.Next(1, 4);
    var stock = Enumerable.Range(0, kinds).Select(i => new FoodStock(i + 1, $"F{i}", null, rng.Next(3, 40), rng.Next(0, 6))).ToList();
    int missing = rng.Next(1, 160);
    var plan = HealPlanner.Plan(missing, stock);
    long total = stock.Sum(f => (long)f.HealPerUnit * f.Quantity);
    // fuerza bruta: todas las combinaciones
    int bestSum = int.MaxValue, bestUnits = int.MaxValue;
    void Rec(int idx, int sum, int units)
    {
        if (idx == stock.Count) { if (sum >= missing && (sum < bestSum || (sum == bestSum && units < bestUnits))) { bestSum = sum; bestUnits = units; } return; }
        for (int q = 0; q <= stock[idx].Quantity; q++) Rec(idx + 1, sum + q * stock[idx].HealPerUnit, units + q);
    }

    Rec(0, 0, 0);
    tested++;
    bool ok = total < missing
        ? !plan.Covers && plan.TotalHeal == total && plan.Units == stock.Sum(f => f.Quantity)
        : plan.Covers && plan.TotalHeal == bestSum && plan.Units == bestUnits && plan.Foods.All(f => f.Quantity <= f.Food.Quantity) && plan.Foods.Sum(f => f.Quantity * f.Food.HealPerUnit) == plan.TotalHeal;
    if (!ok) mismatches++;
}

Check(mismatches == 0, $"{tested} casos al azar contra fuerza bruta: siempre el MENOR sobrante y, a igual sobrante, las menos unidades ({mismatches} diferencias)");
var sw = Stopwatch.StartNew();
var big = HealPlanner.Plan(5000, [Mate(900), Emp(900), Asado(900), new FoodStock(4, "C", null, 150, 900), new FoodStock(5, "D", null, 250, 900), new FoodStock(6, "E", null, 300, 900)]);
sw.Stop();
Check(big.Covers && big.TotalHeal >= 5000 && big.TotalHeal < 5000 + 300 && sw.ElapsedMilliseconds < 500, $"un caso enorme (5000 HP, 6 comidas, 900 de cada una) se resuelve en {sw.ElapsedMilliseconds} ms");

Console.WriteLine("\n== 3. /heal con la base real ==");
await Make(U, hp: 10, maxHp: 100);
await Give(U, "Mate Amargo", 10); await Give(U, "Empanada de Carne", 2);
var heal1 = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
int hp1 = await HpOf(U);
Check(hp1 == 100 && heal1.Title == "🍖 ¡Buen provecho!" && heal1.Description.Contains("Comiste:") && heal1.Description.Contains($"{HpDisplay.Scale(100, ClassPassives.For("Guerrero").MaxHpMultiplier)}/{HpDisplay.Scale(100, ClassPassives.For("Guerrero").MaxHpMultiplier)}"), $"(v0.25.3: la vida se ve con el factor de la clase, un Guerrero de 100 se ve de 120) cura toda la vida de una (HP 10 -> {hp1}): «{heal1.Title}»");
int mates = await Have(U, "Mate Amargo"), emps = await Have(U, "Empanada de Carne");
int eaten = (10 - mates) * 15 + (2 - emps) * 40;
Check(eaten >= 90 && eaten < 90 + 40 && eaten == 90 + 0 || eaten >= 90, $"comió lo necesario ({10 - mates} mates y {2 - emps} empanadas = {eaten} HP de comida para 90 que faltaban)");
Console.WriteLine("   " + heal1.Description.Replace("\n", " / "));
var full = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(full.Title.Contains("máximo") && await Have(U, "Mate Amargo") == mates, "con la vida llena no come nada");

await Make(U, hp: 10, maxHp: 500); await Give(U, "Mate Amargo", 3); await Give(U, "Empanada de Carne", 1);
var short1 = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(short1.Title.Contains("todo lo que tenías") && await HpOf(U) == 10 + 85 && await Have(U, "Mate Amargo") == 0 && await Have(U, "Empanada de Carne") == 0 && short1.Description.Contains("te faltan") && short1.Description.Contains("/taberna"), $"si la comida no alcanza: se la come toda, cura lo que puede y dice cuánto falta (HP {await HpOf(U)}/500)");
var none = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(none.Title.Contains("nada para comer"), "sin comida: lo manda a comprar");

await Make(U, hp: 10, maxHp: 100); await Give(U, "Mate Amargo", 20); await Give(U, "Asado de Tira", 2);
var named = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U, "mate amargo");
Check(await HpOf(U) == 100 && await Have(U, "Mate Amargo") == 20 - 6 && await Have(U, "Asado de Tira") == 2, $"eligiendo la comida (sin importar mayúsculas): solo come de esa, las unidades justas (6 mates = 90) y el asado queda ({named.Title})");
await Make(U, hp: 10, maxHp: 100); await Give(U, "Mate Amargo", 2);
var notOwned = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U, "Cordero Patagónico");
Check(notOwned.Title.Contains("No tenés eso") && await HpOf(U) == 10 && await Have(U, "Mate Amargo") == 2, "pedir una comida que no tenés: lo dice y no cambia nada");
await Give(U, "Asado Completo del Domingo en Familia", 1);
var banquet = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U, "Asado Completo del Domingo en Familia");
Check(banquet.Title.Contains("banquete") && banquet.Description.Contains("/use") && await Have(U, "Asado Completo del Domingo en Familia") == 1, "un banquete no se come para curarse (dan un buff): manda a /use y no lo gasta");
await Make(U, hp: 10, maxHp: 100); await Give(U, "Asado Completo del Domingo en Familia", 1); await Give(U, "Mate Amargo", 2);
await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(await Have(U, "Asado Completo del Domingo en Familia") == 1 && await Have(U, "Mate Amargo") == 0 && await HpOf(U) == 10 + 30, "al elegir solo, nunca gasta el banquete (come los mates y cura lo que puede)");

// En combate no
await Make(U, hp: 10, maxHp: 100); await Give(U, "Mate Amargo", 8);
var inCombatState = new CombatState("hunt", "Toro", "🐂", 100, 100, 10, [], 0, 0, null, 100, 50, 50, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
combat.TryStart(U, inCombatState, new FakeTarget());
var blocked = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(blocked.Title.Contains("combate") && await Have(U, "Mate Amargo") == 8, "en pleno combate no se cura (para eso está /use) y no gasta comida");
combat.TryAdvance(U, combat.Peek(U)!, null, null);

// Atomicidad
await Make(U, hp: 10, maxHp: 100); await Give(U, "Mate Amargo", 2);
var mateId = (await items.GetByNameAsync("Mate Amargo"))!.ItemId; var empId = (await items.GetByNameAsync("Empanada de Carne"))!.ItemId;
var rolled = await inventory.EatAndHealAsync(U, [(mateId, 2), (empId, 1)], 70);
Check(rolled is null && await Have(U, "Mate Amargo") == 2 && await HpOf(U) == 10, "si una comida del plan ya no está, se revierte TODO (los mates no se gastan ni se cura)");
var okEat = await inventory.EatAndHealAsync(U, [(mateId, 2)], 30);
Check(okEat is not null && okEat.CurrentHp == 40 && await Have(U, "Mate Amargo") == 0 && await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory WHERE discord_id = @id AND quantity <= 0", new { id = (long)U }) == 0, "una comida completa: se gasta, se cura y no quedan filas en cero");
// Carrera: dos /heal a la vez con comida para uno solo
await Make(U, hp: 1, maxHp: 100); await Give(U, "Mate Amargo", 7);
var raced = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U))));
int matesLeft = await Have(U, "Mate Amargo"); int hpRace = await HpOf(U);
Check(matesLeft >= 0 && 7 - matesLeft >= 0 && hpRace == Math.Min(100, 1 + (7 - matesLeft) * 15) && raced.Count(e => e.Title.StartsWith("🍖")) >= 1, $"6 /heal a la vez: la comida nunca queda en negativo y la vida coincide con lo comido ({7 - matesLeft} mates -> HP {hpRace})");

Console.WriteLine("\n== 4. La taberna: 'Curarme del todo' ==");
await Make(U, gold: 500, hp: 40, maxHp: 100); await Give(U, "Mate Amargo", 6); await Give(U, "Asado Completo del Domingo en Familia", 1);
var scene = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, U, null);
var eatMenu = scene.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId.StartsWith("taberna_eat"));
var eatOpts = eatMenu.Options.ToList();
Check(eatOpts[0].Label == "🍖 Curarme del todo" && eatOpts[0].Description.Contains("60 HP") && eatOpts[0].Value == "__heal_full__" && eatOpts.Skip(1).Select(o => o.Value).Contains("Mate Amargo") && eatOpts.All(o => o.Label.Length <= 100 && o.Description.Length <= 100), $"herido y con comida: la primera opción es 'Curarme del todo' (te faltan 60 HP) y después la comida de a una ({string.Join(" | ", eatOpts.Select(o => o.Label))})");
string talk = await TabernaModule.ExecuteActionAsync("taberna_eat", users, items, inventory, shop, buffs, combat, events, U, "__heal_full__");
Check(await HpOf(U) == 100 && talk.Contains("Comiste:") && await Have(U, "Asado Completo del Domingo en Familia") == 1, "elegirla cura toda la vida y no toca el banquete");
var sceneFull = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, U, null);
var eatFull = sceneFull.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId.StartsWith("taberna_eat")).Options.Select(o => o.Value).ToList();
Check(!eatFull.Contains("__heal_full__"), "con la vida llena la opción no aparece");
await Make(U, gold: 500, hp: 40, maxHp: 100); await Give(U, "Asado Completo del Domingo en Familia", 1);
var onlyBanquet = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, U, null);
Check(!onlyBanquet.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId.StartsWith("taberna_eat")).Options.Any(o => o.Value == "__heal_full__"), "si solo tenés banquetes tampoco se ofrece (no se gastan para curarse)");

Console.WriteLine("\n== 5. El cooldown del raid ==");
Check(CooldownCatalog.Raid.CommandName == CooldownCatalog.Boss.CommandName && CooldownCatalog.Raid.Duration == CooldownCatalog.Boss.Duration && CooldownCatalog.All.Count(d => d.CommandName == "boss") == 1 && CooldownCatalog.Boss.Duration == TimeSpan.FromHours(5) && CooldownCatalog.Boss.RetryAfterFailure == TimeSpan.FromMinutes(30), "el raid usa el MISMO cooldown que el jefe (mismo nombre y duración), de 5 HORAS (30 min si perdés o escapás), y /cd lo lista UNA sola vez");
await Make(U, level: 10);
var cd0 = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U);
Check(cd0.Description.Contains("👑 **Jefe**: **¡Listo!** ✅") && !cd0.Description.Contains("🛡️") && cd0.Description.Split('\n').Count(l => l.Contains("Jefe")) == 1, "/cd muestra UNA sola línea 'Jefe' (lista)");
Console.WriteLine("   " + cd0.Description.Replace("\n", " | "));
bool startNoCd = await RaidModule.ValidateStartAsync(users, items, monsters, zones, combat, raids, U, cooldowns) is null;
Check(startNoCd, "sin cooldown ocupado, arrancar un raid se permite");
await adventure.TryClaimCooldownAsync(U, "boss", CooldownCatalog.Boss.Duration);
var cd1 = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U); Console.WriteLine("   CD1: " + cd1.Description.Replace("\n", " | "));
Check(cd1.Description.Contains("👑 **Jefe**:") && !cd1.Description.Contains("👑 **Jefe**: **¡Listo!**") && cd1.Description.Split('\n').Count(l => l.Contains("restantes")) == 1 && (cd1.Description.Contains("4h 59m") || cd1.Description.Contains("5h 0m")), "después de pelear al jefe, la línea 'Jefe' figura en cooldown (5 horas, un solo cooldown para los dos)");
var rejStart = await RaidModule.ValidateStartAsync(users, items, monsters, zones, combat, raids, U, cooldowns);
Check(rejStart is { PlainMessage: null, Embed: not null } && rejStart.Embed.Title.Contains("Jefe: todavía no podés") && rejStart.Embed.Description.Contains("Te falta **"), $"con el cooldown ocupado NO se puede arrancar un raid: «{rejStart?.Embed?.Title}» / {rejStart?.Embed?.Description}");
Check(rejStart!.Embed!.Description.Contains("/boss") && rejStart.Embed.Description.Contains("mismo cooldown"), "el aviso explica que en equipo o solo es el mismo cooldown del jefe");
Check(await RaidModule.ValidateStartAsync(users, items, monsters, zones, combat, raids, U) is null, "los llamadores viejos (sin el repositorio de cooldowns) siguen andando como antes");
// Unirse
ulong V = U + 1; await Make(V, level: 10);
await Make(U, level: 10);
var session = await RaidModule.BuildSessionAsync(users, items, monsters, zones, buffs, U, "Starter");
var partyOk = await RaidModule.PreparePartyAsync(users, items, monsters, zones, combat, raids, cooldowns, buffs, null, U, "Starter", [new RaidModule.PartyMember(V, "Vero")]);
Check(partyOk.Session is not null, "sin cooldown, el compañero entra al grupo");
await adventure.TryClaimCooldownAsync(V, "boss", CooldownCatalog.Boss.Duration);
var partyBusy = await RaidModule.PreparePartyAsync(users, items, monsters, zones, combat, raids, cooldowns, buffs, null, U, "Starter", [new RaidModule.PartyMember(V, "Vero")]);
Check(partyBusy.Session is null && partyBusy.Rejection?.PlainMessage is { } busyText && busyText.Contains("Vero") && busyText.Contains("cooldown"), "con el cooldown ocupado el compañero NO entra al grupo (y se dice quién y por qué)");
Check(await adventure.TryClaimCooldownAsync(V, CooldownCatalog.Raid.CommandName, CooldownCatalog.Raid.Duration) == false, "y reclamarlo de nuevo (la guarda atómica de siempre) sigue rechazando");

Console.WriteLine("\n== 6. all en /play y /give ==");
await Make(U, gold: 1000);
var played = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", "all", null, events);
long expected = 1000;
Check(played.Embed is not null && played.Embed.Fields.First(f => f.Name.Contains("Apuesta")).Value == "1000", $"/play slots all apuesta TODO el oro (apuesta 1000, oro después {await GoldOf(U)})");
await Make(U, gold: 600);
var playedTodo = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "coinflip", "TODO", "heads", events);
Check(playedTodo.Embed!.Fields.First(f => f.Name.Contains("Apuesta")).Value == "600", "también 'TODO' y en coinflip");
await Make(U, gold: 5);
var tooPoor = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", "all", null, events);
Check(tooPoor.PlainMessage!.Contains("mínima") && await GoldOf(U) == 5, "all con menos de la apuesta mínima: lo dice y no toca el oro");
await Make(U, gold: 0);
Check((await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", "all", null, events)).PlainMessage!.Contains("0"), "all sin oro: lo dice");
Check((await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", "muchisimo", null, events)).PlainMessage!.Contains("all"), "una apuesta ilegible explica cómo escribirla");
await Make(U, gold: 1000);
var played2 = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", "1.000", null, events);
Check(played2.Embed!.Fields.First(f => f.Name.Contains("Apuesta")).Value == "1000", "'1.000' también vale (punto de miles)");
await Make(U, gold: 100);
var oldWay = await CasinoModule.ExecutePlayAsync(users, casinoRepo, casino, U, "slots", 50, null, events);
Check(oldWay.Embed is not null && oldWay.Embed.Fields.First(f => f.Name.Contains("Apuesta")).Value == "50", "la forma vieja (apuesta numérica) sigue andando");

await Make(U, gold: 800); ulong W = U + 2; await Make(W, gold: 5);
var gave = await GiveModule.ExecuteGiveAsync(users, transfers, events, U, FakeUser((long)W, "Receptor"), "all");
Check(gave.Embed is not null && await GoldOf(U) == 0 && await GoldOf(W) == 805 && gave.Embed.Description.Contains("**800**"), "/give all da TODO el oro (800): el receptor pasa de 5 a 805");
var gaveNothing = await GiveModule.ExecuteGiveAsync(users, transfers, events, U, FakeUser((long)W, "Receptor"), "all");
Check(gaveNothing.PlainMessage!.Contains("0") && await GoldOf(W) == 805, "all sin oro: no transfiere nada y lo dice");
await Make(U, gold: 3000);
var gave2 = await GiveModule.ExecuteGiveAsync(users, transfers, events, U, FakeUser((long)W, "Receptor"), "1.500");
Check(gave2.Embed is not null && await GoldOf(U) == 1500, "/give 1.500 también vale");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, events, U, FakeUser((long)W, "Receptor"), "pila")).PlainMessage!.Contains("all"), "una cantidad ilegible explica cómo escribirla");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, events, (ulong)BaseId + 777, FakeUser((long)W, "Receptor"), "all")).PlainMessage!.Contains("/start"), "all de alguien sin cuenta: lo manda a /start");
Check((await GiveModule.ExecuteGiveAsync(users, transfers, events, U, FakeUser((long)U, "Yo"), "all")).PlainMessage!.Contains("vos mismo") && await GoldOf(U) == 1500, "darse todo a uno mismo se rechaza y no mueve oro");
Check(events.Count(U, GameEventKinds.GoldGiven) >= 2, "las entregas siguen registrando su evento");

Console.WriteLine("\n== 7. Comandos ==");
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var heal = interactions.SlashCommands.Single(c => c.Name == "heal");
var play = interactions.SlashCommands.Single(c => c.Name == "play"); var giveCmd = interactions.SlashCommands.Single(c => c.Name == "give");
Check(heal.Parameters.Count == 1 && !heal.Parameters[0].IsRequired && heal.Parameters[0].Name == "comida" && heal.Parameters[0].AutocompleteHandler is not null, "/heal tiene el parámetro opcional 'comida' con lista");
Check(play.Parameters.First(p => p.Name == "apuesta").ParameterType == typeof(string) && giveCmd.Parameters.First(p => p.Name == "cantidad").ParameterType == typeof(string), "/play apuesta y /give cantidad ahora son texto (número o all)");
var fakeCtx = (ICommandContext)FakeProxy.Create(typeof(ICommandContext), (n, a) => null);
async Task<List<string>> ParsesOk(string input)
{
    var found = texts.Search(input);
    var ok = new List<string>();
    foreach (var m in found.Commands)
    {
        var pr = await m.ParseAsync(fakeCtx, found, Discord.Commands.PreconditionResult.FromSuccess(), sp);
        if (pr.IsSuccess) ok.Add(m.Command.Name);
    }

    return ok;
}

Check((await ParsesOk("play slots all")).SequenceEqual(["play"]) && (await ParsesOk("play coinflip 500 heads")).SequenceEqual(["play"]) && (await ParsesOk("heal")).SequenceEqual(["heal"]) && (await ParsesOk("heal Mate Amargo")).SequenceEqual(["heal"]) && (await ParsesOk("pl slots todo")).SequenceEqual(["play"]), "en texto: 'aa play slots all', 'aa heal' y 'aa heal Mate Amargo' parsean");
var tooLong = interactions.SlashCommands.Where(c => c.Description.Length > 100 || c.Parameters.Any(p => p.Description.Length > 100)).Select(c => c.Name).ToList();
Check(tooLong.Count == 0, $"ninguna descripción de comando pasa los 100 caracteres ({string.Join(",", tooLong)})");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && !x.IsSealed && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

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

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
}

sealed class FakeEvents : IGameEvents
{
    private readonly ConcurrentBag<(ulong Id, string Kind)> _recorded = [];
    public int Count(ulong id, string kind) => _recorded.Count(r => r.Id == id && r.Kind == kind);

    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null)
    {
        _recorded.Add((discordId, kind));
        return Task.CompletedTask;
    }

    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
