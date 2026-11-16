using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.57.0: la Aventura (/aventura, aa aventura; docs/aventura.md). El dueño (2026-11-16): una sola aventura para todas las zonas, más difícil según la zona (no el nivel),
// cada 3 horas, sin comida adentro, el premio de 3 viajes con el botín de las decisiones, y si morís la penalidad de siempre y sin premio. Se prueba: las reglas puras,
// el servicio de corridas (relojes y cierre), el recorrido entero contra la base LOCAL (arranque y cooldown, eventos, peleas cerradas a mano, el pago al final), el
// bloqueo de comandos y de la comida, y las pantallas en los dos idiomas.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000097711UL;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var zones = new ZoneRepository(factory);
var items = new ItemRepository(factory);
var monsters = new MonsterRepository(factory);
var adventureRepo = new AdventureRepository(factory);
var cooldowns = new CooldownRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var bonus = new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), eventsRepo);
var combat = new CombatSessionService(users);
var raids = new RaidSessionService();
var starter = new AdventureCombatStarter(cooldowns, adventureRepo, users, items, monsters, zones, new BuffRepository(factory), bonus, combat, raids);
var adventures = new AdventureService(combat);
var deps = new AdventureDeps(users, adventureRepo, cooldowns, monsters, starter, combat, raids, FakeProxy.Make<IDungeonService>(new()), adventures,
    new ZoneBoxService(factory, zones), items, new GameEventService(eventsRepo), bonus, zones);

async Task Clean()
{
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
}

await Clean();

Console.WriteLine("== 1. Las reglas ==");
var rolls = Enumerable.Range(0, 100).Select(AdventureRules.RollEvent).ToList();
Check(rolls.Count(e => e == AdventureEvent.Chest) == 25 && rolls.Count(e => e == AdventureEvent.Campfire) == 20 && rolls.Count(e => e == AdventureEvent.Trap) == 20
    && rolls.Count(e => e == AdventureEvent.Shrine) == 15 && rolls.Count(e => e == AdventureEvent.Ambush) == 20, "eventos: cofre 25, fogata 20, trampa 20, santuario 15, emboscada 20");
var template = new MonsterTemplate("Lobo", "🐺", 100, 200, 10, 20, ["Colmillo"]);
var scaled = AdventureRules.Scale(template, AdventureRules.MonsterMultiplier);
Check(scaled.MinHp == 130 && scaled.MaxHp == 260 && scaled.MinDamage == 13 && scaled.MaxDamage == 26 && scaled.DropItemNames.Count == 0, "los monstruos de la aventura: ×1,3 y sin drops");
var guardian = AdventureRules.Guardian(template);
Check(guardian.Name == AdventureRules.GuardianName && guardian.MaxHp == 320 && guardian.MaxDamage == 32, "el Guardián: el élite ×1,6 con su nombre");
Check(AdventureRules.ApplyLoot(1000, 0) == 1000 && AdventureRules.ApplyLoot(1000, 60) == 1600, "el botín multiplica el premio (+60 % = ×1,6)");
// v0.57.2: un viaje de la aventura lleva el bono del monstruo de viaje de la zona, como /travel (hasta la v0.57.1 pagaba solo el nivel: ~13 % en la Zona 5).
foreach (var zone in await zones.GetAllAsync())
{
    var travelMonster = await monsters.GetTravelMonsterByZoneAsync(zone.ZoneId);
    if (travelMonster is null || travelMonster.GoldBonus == 0) continue;
    var adv = Enumerable.Range(0, 200).Select(_ => AventuraModule.RewardTravel(zone.MinLevel, travelMonster, null)).ToList();
    int minGold = (5 + 2 * zone.MinLevel + travelMonster.GoldBonus) * CombatRewardCalculator.TravelRewardMultiplier;
    int noBonusMax = (15 + 2 * zone.MinLevel) * CombatRewardCalculator.TravelRewardMultiplier;
    Check(adv.All(r => r.Gold >= minGold && r.Gold > noBonusMax), $"Zona {zone.ZoneId}: un viaje de la aventura paga el bono del {travelMonster.Name} (oro ≥ {minGold}, más que los {noBonusMax} del nivel solo)");
}

var picked = AdventureRules.PickScenes(new Random(1));
Check(picked.Count == 4 && picked.Select(s => s.Key).Distinct().Count() == 4 && !picked.Contains(AdventureRules.Final), "4 escenas distintas del pozo; la 5.ª es siempre el Guardián");
Check(AdventureRules.Cooldown.Duration == TimeSpan.FromHours(3) && CooldownCatalog.All.Contains(AdventureRules.Cooldown) && ReminderCatalog.Find("aventura") is not null,
    "cada 3 horas, con línea en /cd y recordatorio");
Check(!FightLock.IsAllowedInAdventure("use") && !FightLock.IsAllowedInAdventure("heal") && !FightLock.IsAllowedInAdventure("hunt") && FightLock.IsAllowedInAdventure("attack") && FightLock.IsAllowedInAdventure("profile"),
    "en la aventura no se come ni se hace otra cosa (sí atacar y mirar)");
Check(!CombatHeal.HasMenu(AdventureRules.CommandName), "la pelea de la aventura no trae el desplegable de comida");

Console.WriteLine("\n== 2. El servicio de corridas ==");
var run1 = adventures.Start(U, new Random(2));
Check(run1 is not null && adventures.Start(U, new Random(3)) is null && adventures.IsInAdventure(U), "una corrida por jugador");
run1!.LastActivityUtc = DateTime.UtcNow - AdventureRules.ChoiceTimeout - TimeSpan.FromSeconds(1);
Check(!adventures.IsInAdventure(U) && adventures.Find(U) is null, "eligiendo camino, a los 3 minutos sin tocar nada se termina");
var run2 = adventures.Start(U, new Random(4))!;
run2.Phase = AdventurePhase.Fighting;
run2.LastActivityUtc = DateTime.UtcNow - TimeSpan.FromMinutes(1);
Check(!adventures.IsInAdventure(U), "peleando, si la pelea ya no existe (se le venció el reloj), la corrida se termina");
adventures.End(U);

Console.WriteLine("\n== 3. El recorrido en la base ==");
await users.GetOrCreateUserAsync(U);
await db.ExecuteAsync("UPDATE users SET level = 20, current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
var start = await AventuraModule.StartAsync(deps, U);
Check(start.Embed is not null && start.Embed.Title!.Contains("1/5") && start.Components is not null, "arranca en la escena 1 con los tres caminos");
Check(await cooldowns.GetRemainingAsync(U, "aventura", AdventureRules.Cooldown.Duration) is { } w && w > TimeSpan.FromHours(2.9), "cobró las 3 horas al arrancar");
Check((await AventuraModule.StartAsync(deps, U)).Error is not null, "con una aventura abierta no arranca otra");
var run = adventures.Peek(U)!;
var target = new NullTarget();
Check((await AventuraModule.ChooseAsync(deps, U, "otrotoken", AdventurePath.Safe, target)).Error!.Contains("anterior"), "un botón de otra corrida no hace nada");

int fights = 0;
for (int guard = 0; guard < 20 && adventures.Find(U) is { } r; guard++)
{
    var view = await AventuraModule.ChooseAsync(deps, U, r.Token, AdventurePath.Safe, target);
    if (combat.Peek(U) is { } session)
    {
        fights++;
        Check(session.State.CommandName == AdventureRules.CommandName, $"escena {r.Scene}: arranca una pelea de la aventura");
        await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
        combat.TryAdvance(U, session, null, null);
        var (embed, _) = await AventuraModule.AfterWinAsync(deps, U, session.State);
        if (adventures.Find(U) is null)
        {
            Check(embed.Title!.Contains("completada"), "al vencer al Guardián se termina y paga");
        }
    }
    else
    {
        Check(view.Embed is not null, "un camino sin pelea muestra la escena siguiente");
    }
}

Check(fights == 5, $"por el camino seguro son 5 peleas, la última el Guardián ({fights})");
var stats = await eventsRepo.GetStatsAsync(U);
var player = (await users.GetByDiscordIdAsync(U))!;
Check(stats.GetValueOrDefault(GameEventKinds.AdventureWin) == 1 && stats.GetValueOrDefault(GameEventKinds.AdventureStart) == 1 && player.Gold > 50, $"cobró el oro y quedó anotada ({player.Gold} de oro)");

Console.WriteLine("\n== 4. Eventos del camino misterioso ==");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
await AventuraModule.StartAsync(deps, U);
int events = 0;
for (int i = 0; i < 40 && adventures.Find(U) is { } r2 && !r2.AtGuardian; i++)
{
    await AventuraModule.ChooseAsync(deps, U, r2.Token, AdventurePath.Mystery, target);
    if (combat.Peek(U) is { } s2)
    {
        combat.TryAdvance(U, s2, null, null);
        await AventuraModule.AfterWinAsync(deps, U, s2.State);
    }
    else
    {
        events++;
    }
}

var afterMystery = adventures.Find(U);
Check(afterMystery is { AtGuardian: true } && afterMystery.LastNote is not null, $"los caminos misteriosos llevan al Guardián con su nota ({events} eventos sin pelea)");
await db.ExecuteAsync("UPDATE users SET current_hp = 1 WHERE discord_id = @id", new { id = (long)U });
var trapped = afterMystery!;
var guardView = AventuraModule.BuildSceneView(trapped, (await users.GetByDiscordIdAsync(U))!);
Check(guardView.Components!.Components.SelectMany(c => ((ActionRowComponent)c).Components).Count() == 1 && guardView.Embed!.Title!.Contains("5/5"), "en la escena 5 solo queda «Enfrentar al Guardián»");
adventures.End(U);

Console.WriteLine("\n== 5. Sin comida ==");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
await AventuraModule.StartAsync(deps, U);
await AventuraModule.ChooseAsync(deps, U, adventures.Find(U)!.Token, AdventurePath.Risky, target);
var used = await UseModule.ExecuteUseAsync(users, items, new InventoryRepository(factory), combat, new BuffRepository(factory), U, "Asado");
Check(used.PlainMessage!.Contains("no se come"), "en una pelea de la aventura /use no cura");
if (combat.Peek(U) is { } s3) { combat.TryAdvance(U, s3, null, null); }
adventures.End(U);

Console.WriteLine("\n== 6. Pantallas ==");
var sample = adventures.Start(U, new Random(9))!;
sample.LootPercent = 40;
sample.LastNote = Loc.F($"⚔️ Venciste a **Lobo**.");
var scene = AventuraModule.BuildSceneView(sample, (await users.GetByDiscordIdAsync(U))!);
Check(scene.Embed!.Description!.Contains("+40 %") && scene.Components!.Components.SelectMany(c => ((ActionRowComponent)c).Components).Count() == 3 && LeakCheck.FitsDiscord(scene.Embed), "la escena: botín, vida y los tres caminos");
using (Loc.Use("en"))
{
    sample.LastNote = Loc.F($"⚔️ Venciste a **Lobo**.");
    var en = AventuraModule.BuildSceneView(sample, (await users.GetByDiscordIdAsync(U))!);
    var leak = LeakCheck.NoSpanish(LeakCheck.AllText(en.Embed!), ["Lobo"]);
    Check(leak.Ok && en.Embed!.Description!.Contains("Loot"), $"en inglés sin español ({leak.Found})");
}

adventures.End(U);
await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class NullTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
}

public class FakeProxy : DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var v)) return v;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public static T Make<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)proxy).Values = values;
        return proxy;
    }
}
