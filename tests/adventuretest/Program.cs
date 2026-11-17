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
Check(guardian.Name == AdventureRules.GuardianName && guardian.MinHp == 200 && guardian.MaxHp == 400 && guardian.MinDamage == 20 && guardian.MaxDamage == 40 && guardian.DropItemNames.Count == 0,
    "el Guardián: el élite ×2,0 de vida (v0.59.0; era ×1,6) y ×2,0 de daño, con su nombre");
// v0.59.0 (el dueño: «el camino difícil… que pegue ×1,3, y el hunt… tipo 1,2», encima del ×1,3 de antes y solo en el daño).
var safeMonster = AdventureRules.SafeMonster(template);
var riskyMonster = AdventureRules.RiskyMonster(template);
Check(safeMonster.MinHp == 130 && safeMonster.MaxHp == 260 && safeMonster.MinDamage == 16 && safeMonster.MaxDamage == 31 && safeMonster.DropItemNames.Count == 0,
    "camino seguro: el común con ×1,3 de vida y ×1,56 de daño (1,3 × 1,2)");
Check(riskyMonster.MinHp == 130 && riskyMonster.MaxHp == 260 && riskyMonster.MinDamage == 17 && riskyMonster.MaxDamage == 34,
    "camino arriesgado (y emboscada): el élite con ×1,3 de vida y ×1,69 de daño (1,3 × 1,3)");
Check(AdventureRules.ParsePath("seguro") == AdventurePath.Safe && AdventureRules.ParsePath("ARRIESGADO") == AdventurePath.Risky && AdventureRules.ParsePath("misterioso") == AdventurePath.Mystery
    && AdventureRules.ParsePath("difícil") == AdventurePath.Risky && AdventureRules.ParsePath("s") == AdventurePath.Safe && AdventureRules.ParsePath("m") == AdventurePath.Mystery
    && AdventureRules.ParsePath("safe") == AdventurePath.Safe && AdventureRules.ParsePath("mystery") == AdventurePath.Mystery && AdventureRules.ParsePath("guardián") == AdventurePath.Risky
    && AdventureRules.ParsePath("pizza") is null && AdventureRules.ParsePath("  ") is null,
    "«aa av seguro / arriesgado / misterioso»: el camino escrito, sin tildes ni mayúsculas, en los dos idiomas y con atajos");
Check(AdventureRules.Pool.Count == 22 && AdventureRules.Pool.Select(s => s.Key).Distinct().Count() == 22 && AdventureRules.Pool.All(s => s.Key != AdventureRules.Final.Key),
    "22 escenas distintas en el pozo (v0.59.0: 10 nuevas)");
var expectedTravel = CombatRewardCalculator.ExpectedTravelReward(20, 100, 50);
Check(expectedTravel.Gold == (10 + 40 + 100) * CombatRewardCalculator.TravelRewardMultiplier && expectedTravel.Xp == (int)Math.Round((14 + 20 + 50) * CombatRewardCalculator.TravelRewardMultiplier * CombatRewardCalculator.FightXpScale),
    "el premio base de la primera escena: un viaje en su punto medio (10 de oro y 14 de EXP de azar)");
Check(MissionCatalog.WeeklyPool.Any(m => m.Key == "w_adv2" && m.Kind == GameEventKinds.AdventureWin && m.Target == 2)
    && AchievementCatalog.Find("aventurero") is { StatKind: GameEventKinds.AdventureWin } aventurero && aventurero.Tiers.Select(t => t.Threshold).SequenceEqual([5L, 25L, 100L]) && aventurero.Tiers[1].Reward.Box != BoxGrant.None
    && AchievementCatalog.Categories.Single(c => c.Name == "Combate").Keys.Contains("aventurero"),
    "misión semanal «Completá 2 aventuras» y logro «Aventurero» (5 / 25 / 100, con cajas como Racha, en Combate)");
var helpTopic = HelpTopics.Find("aventura");
Check(helpTopic?.Key == "adventure" && HelpTopics.Render(helpTopic) is { } helpEmbed && LeakCheck.FitsDiscord(helpEmbed)
    && helpEmbed.Fields.Any(f => f.Value.Contains("no se come")) && helpEmbed.Fields.Any(f => f.Value.Contains("aa av seguro")) && helpEmbed.Fields.Any(f => f.Value.Contains("Cofre") && f.Value.Contains("25 %")),
    "/info tema:aventura explica los caminos, los eventos (con sus chances), el botín, que no se come y cómo jugarla por texto");
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
Check(start.Embed!.Description!.Contains("Zona 1") && start.Embed.Description.Contains("Premio base") && start.Embed.Footer?.Text?.Contains("aa av seguro") == true,
    "la primera escena dice la zona y el premio base (v0.59.0), y el pie cómo elegir por texto");
Check(await cooldowns.GetRemainingAsync(U, "aventura", AdventureRules.Cooldown.Duration) is { } w && w > TimeSpan.FromHours(2.9), "cobró las 3 horas al arrancar");
var again = await AventuraModule.StartAsync(deps, U);
Check(again.Error is null && again.Embed!.Title!.Contains("1/5") && again.Components is not null, "con una aventura abierta no arranca otra: /aventura vuelve a mostrar la escena (v0.58.2)");
var run = adventures.Peek(U)!;
var target = new NullTarget();
Check((await AventuraModule.ChooseAsync(deps, U, "otrotoken", AdventurePath.Safe, target)).Error!.Contains("anterior"), "un botón de otra corrida no hace nada");

// v0.59.0: «aa av arriesgado» elige por texto en la escena abierta (la misma jugada que el botón). La pelea la cierra el bucle de abajo.
var byText = await AventuraModule.ChooseByTextAsync(deps, U, AdventurePath.Risky, target);
Check(byText.Embed is not null && combat.Peek(U) is { } textFight && textFight.State.CommandName == AdventureRules.CommandName && adventures.Peek(U)!.PendingRiskyBonus,
    "«aa av arriesgado» arranca la pelea contra el élite de la escena abierta");
Check((await AventuraModule.ChooseByTextAsync(deps, U, AdventurePath.Safe, target)).Error!.Contains("aa attack"), "en plena pelea, elegir otro camino por texto dice que se sigue con aa attack");

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
            Check(embed.Description!.Contains("Peleas ganadas**: 5") && embed.Description.Contains("Botín**: +20 %") && LeakCheck.FitsDiscord(embed),
                "el resumen del final: 5 peleas ganadas y el botín total (+20 %: la 1.ª escena fue arriesgada, por texto)");
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
// v0.59.0: el resumen del final cuenta cada evento que tocó (y la emboscada también como pelea ganada).
var eventsSeen = trapped.Events.ToList();
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
await AventuraModule.ChooseByTextAsync(deps, U, AdventurePath.Mystery, target);
if (combat.Peek(U) is { } guardFight)
{
    Check(guardFight.State.MonsterName == AdventureRules.GuardianName, "en la última escena cualquier camino escrito es el Guardián");
    combat.TryAdvance(U, guardFight, null, null);
    var (final, _) = await AventuraModule.AfterWinAsync(deps, U, guardFight.State);
    var names = new Dictionary<AdventureEvent, string> { [AdventureEvent.Chest] = "Cofre", [AdventureEvent.Campfire] = "Fogata", [AdventureEvent.Trap] = "Trampa", [AdventureEvent.Shrine] = "Santuario", [AdventureEvent.Ambush] = "Emboscada" };
    Check(eventsSeen.Count == 4 && eventsSeen.GroupBy(e => e).All(g => final.Description!.Contains($"**{names[g.Key]}**: {g.Count()}"))
        && final.Description!.Contains($"Peleas ganadas**: {1 + eventsSeen.Count(e => e == AdventureEvent.Ambush)}"),
        $"el resumen lista los eventos del camino ({string.Join(", ", eventsSeen)}) y las peleas ganadas");
}
else
{
    Check(false, "el Guardián tendría que haber arrancado por texto");
}

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

Console.WriteLine("\n== 5b. La escena que no llegó a verse (v0.58.2, el caso de Matt) ==");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
await AventuraModule.StartAsync(deps, U);
await AventuraModule.ChooseAsync(deps, U, adventures.Find(U)!.Token, AdventurePath.Safe, target);
var lostFight = combat.Peek(U)!;
// Se gana la pelea y la aventura pasa a la escena 2, pero la pantalla nunca se muestra (como si la edición del mensaje hubiera fallado).
combat.TryAdvance(U, lostFight, null, null);
await AventuraModule.AfterWinAsync(deps, U, lostFight.State);
var stuck = adventures.Peek(U)!;
stuck.LastActivityUtc = DateTime.UtcNow - TimeSpan.FromMinutes(2);
Check(adventures.IsInAdventure(U) && FightLock.AdventureMessage("aa pet").Contains("/aventura"), "trabado en la aventura: el aviso del bloqueo dice cómo volver a ver la escena");
var click = new CaptureOutput();
await AdventureModule.ResolveTurnCoreAsync(combat, users, items, adventureRepo, new InventoryRepository(factory), new BuffRepository(factory), deps.Events, deps.ZoneBoxes, U, false, false, click, deps);
Check(click.Shown?.Title?.Contains("2/5") == true && click.Note is null && DateTime.UtcNow - stuck.LastActivityUtc < TimeSpan.FromSeconds(5),
    "el botón viejo de la pelea muestra la escena 2 con sus caminos (y le da 3 minutos nuevos) en vez de «ya terminó»");
long startsBefore = (await eventsRepo.GetStatsAsync(U)).GetValueOrDefault(GameEventKinds.AdventureStart);
var resumed = await AventuraModule.StartAsync(deps, U);
Check(resumed.Embed!.Title!.Contains("2/5") && (await eventsRepo.GetStatsAsync(U)).GetValueOrDefault(GameEventKinds.AdventureStart) == startsBefore, "/aventura también la vuelve a mostrar, sin cobrar otra aventura");
adventures.End(U);
var noAdv = new CaptureOutput();
await AdventureModule.ResolveTurnCoreAsync(combat, users, items, adventureRepo, new InventoryRepository(factory), new BuffRepository(factory), deps.Events, deps.ZoneBoxes, U, false, false, noAdv, deps);
Check(noAdv.Shown is null && noAdv.Note is not null, "sin aventura abierta, el botón viejo sigue diciendo que no hay combate");

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

sealed class CaptureOutput : ICombatTurnOutput
{
    public Embed? Shown { get; private set; }
    public string? Note { get; private set; }

    public Task NoteAsync(string text)
    {
        Note = text;
        return Task.CompletedTask;
    }

    public ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous) => previous;

    public Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous)
    {
        Shown = embed;
        return Task.CompletedTask;
    }
}

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
