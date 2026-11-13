using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.56.0: el bestiario (/bestiary, aa bestiario [zona]). El dueño (2026-11-13): se desbloquea VENCIENDO al monstruo, todo desde cero,
// cada uno con cara, vida, daño, drop y cuántas veces lo venciste, un logro «Cazador de Bestias», y la vista por zonas (abre la Zona 1,
// con un menú para ver otras, como /drops). Se prueba: el repositorio contra la base LOCAL (usuario descartable, 10 victorias a la vez),
// el enganche con las victorias (GameEventService), las pantallas en los dos idiomas, el logro y el ruteo con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000098811UL, NOACC = 999000000000098899UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var zones = new ZoneRepository(factory);
var monsters = new MonsterRepository(factory);
var bestiary = new BestiaryRepository(factory);

async Task Clean()
{
    foreach (var id in new[] { (long)U, (long)NOACC })
    {
        await db.ExecuteAsync("DELETE FROM player_bestiary WHERE discord_id = @id", new { id });
        await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id });
        await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id });
    }
}

await Clean();
await users.GetOrCreateUserAsync(U);
var all = await monsters.GetAllAsync();
var z1 = all.Where(m => m.ZoneId == 1).ToList();
string hunt1 = z1.First(m => m.Kind == MonsterKind.Hunt).Monster.Name;
string boss1 = z1.Single(m => m.Kind == MonsterKind.Boss).Monster.Name;

Console.WriteLine("== 1. El repositorio ==");
Check(await bestiary.RecordKillAsync(U, hunt1) && !await bestiary.RecordKillAsync(U, hunt1), $"la primera victoria sobre {hunt1} es nueva; la segunda no");
Check((await bestiary.GetKillsAsync(U))[hunt1] == 2, "cuenta las dos");
Check(!await bestiary.RecordKillAsync(U, "Jefe Mundial Inventado"), "un monstruo que no está en la tabla (el jefe mundial) no suma nada");
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => bestiary.RecordKillAsync(U, boss1)));
Check(race.Count(x => x) == 1 && (await bestiary.GetKillsAsync(U))[boss1] == 10, $"10 victorias a la vez sobre {boss1}: una sola es «nueva» y suman 10");

Console.WriteLine("\n== 2. El enganche con las victorias ==");
await Clean();
await users.GetOrCreateUserAsync(U);
var events = new GameEventService(new GameEventRepository(factory), null, null, bestiary);
await events.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: hunt1);
await events.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: hunt1);
await events.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: boss1);
await events.RecordAsync(U, GameEventKinds.EnemyDefeated, 1);
long newOnes = await db.ExecuteScalarAsync<long>("SELECT COALESCE(value, 0) FROM player_stats WHERE discord_id = @id AND stat_key = @k", new { id = (long)U, k = GameEventKinds.BestiaryNew });
Check(newOnes == 2 && (await bestiary.GetKillsAsync(U))[hunt1] == 2, $"cada enemigo vencido con su nombre suma al bestiario; «bestiary_new» cuenta monstruos distintos ({newOnes})");
var noTable = new GameEventService(new GameEventRepository(factory), null, null, new ThrowingBestiary());
await noTable.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: hunt1);
Check(await db.ExecuteScalarAsync<long>("SELECT value FROM player_stats WHERE discord_id = @id AND stat_key = @k", new { id = (long)U, k = GameEventKinds.EnemyDefeated }) == 5,
    "si el bestiario falla (sin la tabla), la victoria se anota igual");
var ext = typeof(GameEventExtensions).GetMethod("RecordVictoryAsync")!;
Check(ext.GetParameters().Any(p => p.Name == "monsterName" && p.IsOptional), "RecordVictoryAsync recibe el nombre del monstruo");

Console.WriteLine("\n== 3. Las pantallas ==");
var view = await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U);
string text = LeakCheck.AllText(view.Embed!);
Check(view.Embed!.Title!.Contains("Zona 1") && view.Embed.Fields.Any(f => f.Name.EndsWith(hunt1)) && view.Embed.Fields.Any(f => f.Name.EndsWith(boss1)) && view.Embed.Fields.Any(f => f.Name == "❓ ???"),
    "abre en la Zona 1: un bloque por monstruo, lo vencido con nombre y lo que falta como ???");
Check(view.Embed.Fields.Length == z1.Count && view.Embed.Description is null, "un campo por monstruo (Discord los separa solo)");
var hunter = z1.First(m => m.Monster.Name == hunt1).Monster;
Check(text.Contains("vencido **2**") && !text.Contains("❤️") && !text.Contains("⚔️"), "en la lista: cuántas veces lo venciste y su drop; la vida y el daño quedan para la ficha (v0.56.1)");
Check(view.Embed.Footer!.Value.Text.Contains($"Esta zona: 2 de {z1.Count}"), "el pie dice cuántos de la zona y del total");
var rows = view.Components!.Components.OfType<ActionRowComponent>().ToList();
var zoneMenu = rows[0].Components.OfType<SelectMenuComponent>().Single();
var cardMenu = rows[1].Components.OfType<SelectMenuComponent>().Single();
var pages = await BestiaryModule.PagesAsync(zones, monsters, 0);
Check(zoneMenu.CustomId == $"bes_zone:{U}" && zoneMenu.Options.Count == pages.Count && zoneMenu.Options.Last().Value == "0" && pages.Last().Monsters.Single().Monster.Name == FogonRules.BossName,
    $"el menú de zonas: las {pages.Count - 1} de su escalera y El Fogón al final");
Check(!pages.Any(p => p.ZoneId >= 6), "con Fuego Nuevo 0 las zonas 6 a 10 no aparecen (son sorpresa)");
Check(cardMenu.CustomId == $"bes_card:{U}:1" && cardMenu.Options.Select(o => o.Value).OrderBy(x => x).SequenceEqual(new[] { hunt1, boss1 }.OrderBy(x => x)), "el menú de fichas solo trae los que venció");
var card = await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U, 1, boss1);
var bossT = z1.Single(m => m.Kind == MonsterKind.Boss).Monster;
Check(card.Embed!.Title!.Contains(boss1) && card.Embed.Thumbnail is not null && card.Embed.Description!.Contains("**Lo venciste:** 1") && card.Embed.Description.Contains($"**Vida:** {GameHistory.Number(bossT.MinHp)}-{GameHistory.Number(bossT.MaxHp)}"),
    "la ficha: su cara grande, su vida y su daño, cuántas veces lo venciste y lo que dice");
string locked = z1.First(m => m.Monster.Name != hunt1 && m.Monster.Name != boss1).Monster.Name;
var cheat = await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U, 1, locked);
Check(!cheat.Embed!.Title!.Contains(locked) && cheat.Embed.Title.Contains("Zona 1"), "pedir la ficha de uno que no venció muestra la zona (no lo revela)");
var other = await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U, 2);
Check(other.Embed!.Title!.Contains("Zona 2") && !LeakCheck.AllText(other.Embed).Contains("vencido") && other.Components!.Components.OfType<ActionRowComponent>().Count() == 1, "la Zona 2 sin nada vencido: todo ??? y sin menú de fichas");
Check((await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U, 8)).Embed!.Title!.Contains("Zona 1"), "una zona que no tiene abierta abre la primera");
Check((await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, NOACC)).Error!.Contains("/start") && await users.GetByDiscordIdAsync(NOACC) is null, "sin cuenta no muestra nada y no la crea");
Check(LeakCheck.FitsDiscord(view.Embed) && LeakCheck.FitsDiscord(card.Embed), "entran en Discord");
using (Loc.Use("en"))
{
    var en = await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U);
    var enCard = await BestiaryModule.BuildAsync(users, zones, monsters, bestiary, U, 1, boss1);
    var names = all.Select(m => m.Monster.Name).Concat((await zones.GetAllAsync()).Select(z => z.Name)).Concat(z1.SelectMany(m => m.Monster.DropItemNames)).ToList();
    string enText = LeakCheck.AllText(en.Embed!) + "\n" + LeakCheck.AllText(enCard.Embed!).Split("\n\n").First();
    var leak = LeakCheck.NoSpanish(enText, names);
    Check(leak.Ok && enText.Contains("Bestiary") && enText.Contains("defeated"), $"en inglés sin español ({leak.Found})");
}

Console.WriteLine("\n== 4. El logro ==");
var ach = AchievementCatalog.Find("bestiario");
Check(ach is not null && ach.StatKind == GameEventKinds.BestiaryNew && ach.Tiers.Select(t => t.Threshold).SequenceEqual([10L, 20L, 31L]) && ach.Tiers.All(t => t.Reward.Box == BoxGrant.None)
    && AchievementCatalog.Categories.Single(c => c.Name == "Combate").Keys.Contains("bestiario"), "«Cazador de Bestias» (10/20/31 monstruos distintos), solo oro y EXP, en Combate");
long totalMonsters = await db.ExecuteScalarAsync<long>("SELECT count(*) FROM monsters");
Check(totalMonsters == 31, $"el tramo III (31) es el total de monstruos de hoy ({totalMonsters})");

Console.WriteLine("\n== 4b. Lo que ya había vencido antes del bestiario ==");
await Clean();
await users.GetOrCreateUserAsync(U);
string travel1 = z1.Single(m => m.Kind == MonsterKind.Travel).Monster.Name;
string hunt1b = z1.Where(m => m.Kind == MonsterKind.Hunt).Select(m => m.Monster.Name).Single(n => n != hunt1);
string boss5 = all.Single(m => m.ZoneId == 5 && m.Kind == MonsterKind.Boss).Monster.Name;
async Task Past(string kind, int zone, int times, string when = "now() - interval '1 day'")
{
    for (int i = 0; i < times; i++)
        await db.ExecuteAsync($"INSERT INTO game_events (discord_id, kind, zone_id, occurred_at) VALUES (@id, @kind, @zone, {when})", new { id = (long)U, kind, zone });
}
await Past(GameEventKinds.HuntWin, 1, 10);
await Past(GameEventKinds.TravelWin, 1, 3);
await Past(GameEventKinds.BossWin, 5, 4);
await Past(GameEventKinds.RaidWin, 5, 1);
await Past(GameEventKinds.GateWin, 5, 2);
await Past(GameEventKinds.HuntWin, 2, 7);
var hist = new GameEventService(new GameEventRepository(factory), null, null, bestiary);
await hist.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: hunt1);
await Past(GameEventKinds.HuntWin, 1, 4, "now() + interval '1 minute'"); // victorias DESPUÉS del corte: ya las cuenta el bestiario, no se suman al pasado
await hist.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: hunt1b);
await hist.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: travel1);
await hist.RecordAsync(U, GameEventKinds.EnemyDefeated, 5, detail: boss5);
await hist.RecordAsync(U, GameEventKinds.EnemyDefeated, 5, detail: FogonRules.BossName);
await hist.RecordAsync(U, GameEventKinds.EnemyDefeated, 1, detail: hunt1);
var k = await bestiary.GetKillsAsync(U);
Check(k[hunt1] == 7 && k[hunt1b] == 6, $"cacería de la Zona 1: las 10 viejas se reparten mitad y mitad (+5 cada uno) y las de después del corte no se suman ({k[hunt1]} y {k[hunt1b]})");
Check(k[travel1] == 4, $"viaje: 1 + las 3 viejas ({k[travel1]})");
Check(k[boss5] == 4, $"jefe de la Zona 5: 1 + 4 de jefe + 1 en equipo − 2 que eran del Asador ({k[boss5]})");
Check(k[FogonRules.BossName] == 3, $"el Asador: 1 + sus 2 victorias viejas ({k[FogonRules.BossName]})");
Check(!k.ContainsKey(all.Where(m => m.ZoneId == 2 && m.Kind == MonsterKind.Hunt).First().Monster.Name), "lo de la Zona 2 sigue en ??? hasta que lo vuelva a vencer (se desbloquea venciendo)");

Console.WriteLine("\n== 5. Ruteo con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(BestiaryModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BestiaryModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "bestiary");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Single().Name == "zona", $"/bestiary registrado con «zona» ({slash.Description.Length} caracteres)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
Check(interactions.SearchComponentCommand(Click($"bes_zone:{U}")).Command?.MethodName == "HandleZoneAsync" && interactions.SearchComponentCommand(Click($"bes_card:{U}:1")).Command?.MethodName == "HandleCardAsync", "los dos menús llegan a su método");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "bestiario", "bestiary", "bestias", "bes" }.All(a => texts.Search($"{a} 2") is { IsSuccess: true } f && f.Commands[0].Command.Name == "bestiario"), "aa bestiario y sus alias llegan");
Check(sp.GetService(typeof(IBestiaryRepository)) is BestiaryRepository && sp.GetService(typeof(IGameEvents)) is GameEventService, "el repositorio está registrado y el servicio de eventos lo recibe");
Check(HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("/bestiary")), "/info lo nombra");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class ThrowingBestiary : IBestiaryRepository
{
    public Task<bool> RecordKillAsync(ulong discordId, string monsterName, CancellationToken cancellationToken = default) => throw new InvalidOperationException("sin tabla");
    public Task<IReadOnlyDictionary<string, long>> GetKillsAsync(ulong discordId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("sin tabla");
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
