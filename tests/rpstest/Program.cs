using System.Reflection;
using BotDsRpg.Data;
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
using Npgsql;

// v0.54.0: Elementos (fuego, hielo y agua) apostando oro entre dos jugadores (/play game:Elementos rival:@x, aa play elementos @x 5k, aa elementos @x 5k).
// Nació como piedra, papel o tijera (/ppt) y el dueño (2026-11-13) lo pasó a /play con elementos. El dueño (2026-11-12): una sola mano,
// sin tope de apuesta y un logro por partidas ganadas (sin títulos). Se prueba: las reglas, el servicio en memoria (aceptar, elegir,
// empate, 3 empates, reloj, carreras), la validación y el cobro contra la base LOCAL (usuarios descartables), las pantallas en los dos
// idiomas, el historial, el logro, la ayuda y el ruteo con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong A = 999000000000099811UL, B = 999000000000099812UL, C = 999000000000099813UL, NOACC = 999000000000099899UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var transfers = new TransferRepository(factory);

async Task Reset()
{
    foreach (var id in new[] { A, B, C, NOACC })
    {
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }

    foreach (var id in new[] { A, B, C })
    {
        await users.GetOrCreateUserAsync(id);
        await db.ExecuteAsync("UPDATE users SET gold = 10000 WHERE discord_id = @id", new { id = (long)id });
    }
}

async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });

IUser Player(ulong id, string name, bool bot = false) => FakeProxy.Make<IUser>(new() { ["Id"] = id, ["Username"] = name, ["Mention"] = $"<@{id}>", ["IsBot"] = bot });

Console.WriteLine("== 1. Reglas ==");
var all = new[] { RpsChoice.Fire, RpsChoice.Ice, RpsChoice.Water };
Check(RpsRules.Compare(RpsChoice.Fire, RpsChoice.Ice) == 1 && RpsRules.Compare(RpsChoice.Ice, RpsChoice.Water) == 1 && RpsRules.Compare(RpsChoice.Water, RpsChoice.Fire) == 1,
    "el fuego derrite el hielo, el hielo congela el agua, el agua apaga el fuego");
Check(all.All(a => all.All(b => RpsRules.Compare(a, b) == -RpsRules.Compare(b, a))) && all.All(a => RpsRules.Compare(a, a) == 0), "las 9 combinaciones son simétricas y el empate es solo con la misma");
Check(all.All(c => RpsRules.FromCode(RpsRules.Code(c)) == c) && RpsRules.FromCode("x") is null, "la letra del botón ida y vuelta");
using (Loc.Use("en")) Check(RpsRules.Name(RpsChoice.Fire) == "Fire" && RpsRules.Name(RpsChoice.Ice) == "Ice" && RpsRules.Name(RpsChoice.Water) == "Water", "en inglés: Fire, Ice, Water");
Check(RpsRules.Name(RpsChoice.Fire) == "Fuego" && RpsRules.Name(RpsChoice.Ice) == "Hielo" && RpsRules.Name(RpsChoice.Water) == "Agua", "en español: Fuego, Hielo, Agua");
Check(RpsRules.WhyWins(RpsChoice.Water).Contains("apaga el fuego"), "la frase de por qué ganó");

Console.WriteLine("\n== 2. El servicio en memoria ==");
{
    var svc = new RpsService();
    var g = svc.Create(A, "Ana", B, "Beto", 500)!;
    Check(g is not null && g.Phase == RpsPhase.Offered, "crea la propuesta");
    Check(svc.Accept(g.Id, A) == RpsAcceptStatus.NotYours && svc.Accept(g.Id, C) == RpsAcceptStatus.NotYours, "solo el invitado acepta");
    Check(svc.Pick(g.Id, A, RpsChoice.Fire).Status == RpsPickStatus.NotPicking, "antes de aceptar no se elige");
    Check(svc.Accept(g.Id, B) == RpsAcceptStatus.Ok && g.Phase == RpsPhase.Picking && svc.Accept(g.Id, B) == RpsAcceptStatus.AlreadyStarted, "acepta una vez");
    Check(svc.Pick(g.Id, C, RpsChoice.Fire).Status == RpsPickStatus.NotPlayer, "un tercero no juega");
    Check(svc.Pick(g.Id, A, RpsChoice.Fire).Status == RpsPickStatus.Waiting && svc.Pick(g.Id, A, RpsChoice.Water).Status == RpsPickStatus.AlreadyPicked, "elige y no puede cambiar");
    int round = g.Round;
    var tie = svc.Pick(g.Id, B, RpsChoice.Fire);
    Check(tie.Status == RpsPickStatus.Tie && g.Ties == 1 && g.ChallengerPick is null && g.OpponentPick is null && g.Round == round + 1, "empate: otra mano, sin elecciones y con reloj nuevo");
    Check(svc.Timeout(g.Id, round).Status == RpsPickStatus.NotPicking && g.Phase == RpsPhase.Picking, "el reloj de la mano vieja no hace nada");
    svc.Pick(g.Id, A, RpsChoice.Water);
    var win = svc.Pick(g.Id, B, RpsChoice.Fire);
    Check(win.Status == RpsPickStatus.Decided && win.WinnerId == A && win.LoserId == B && win.ChallengerChoice == RpsChoice.Water && svc.Get(g.Id) is null, "el agua apaga el fuego: gana el retador y la partida se borra");

    var t = svc.Create(A, "Ana", B, "Beto", 500)!;
    svc.Accept(t.Id, B);
    for (int i = 0; i < RpsRules.MaxTies; i++) { svc.Pick(t.Id, A, RpsChoice.Ice); var r = svc.Pick(t.Id, B, RpsChoice.Ice); if (i == RpsRules.MaxTies - 1) Check(r.Status == RpsPickStatus.Cancelled && !r.ByTimeout, $"a los {RpsRules.MaxTies} empates se cancela"); }

    var o = svc.Create(A, "Ana", B, "Beto", 500)!;
    svc.Accept(o.Id, B);
    svc.Pick(o.Id, B, RpsChoice.Water);
    var to = svc.Timeout(o.Id, o.Round);
    Check(to.Status == RpsPickStatus.Decided && to.ByTimeout && to.WinnerId == B && to.LoserId == A, "el reloj: el que no eligió pierde");
    var n = svc.Create(A, "Ana", B, "Beto", 500)!;
    svc.Accept(n.Id, B);
    Check(svc.Timeout(n.Id, n.Round).Status == RpsPickStatus.Cancelled, "el reloj: si ninguno eligió, se cancela");

    var d = svc.Create(A, "Ana", B, "Beto", 500)!;
    Check(!svc.Decline(d.Id, C) && svc.Decline(d.Id, B) && svc.Get(d.Id) is null, "el invitado rechaza (un tercero no puede)");
    var r1 = svc.Create(A, "Ana", B, "Beto", 500)!;
    var r2 = svc.Create(A, "Ana", C, "Ceci", 700)!;
    Check(svc.Get(r1.Id) is null && svc.Get(r2.Id) is not null, "una propuesta nueva del mismo retador reemplaza a la anterior");
    svc.Accept(r2.Id, C);
    Check(svc.Create(B, "Beto", C, "Ceci", 100) is null && svc.Create(A, "Ana", B, "Beto", 100) is null, "con una partida en juego, ninguno de los dos puede armar otra");
    var busy = svc.Create(B, "Beto", NOACC, "X", 100)!;
    Check(busy is not null, "los demás sí pueden");

    // Carrera: el retador ya eligió y el invitado aprieta 20 veces a la vez: se decide UNA vez.
    var race = new RpsService();
    var rg = race.Create(A, "Ana", B, "Beto", 500)!;
    race.Accept(rg.Id, B);
    race.Pick(rg.Id, A, RpsChoice.Fire);
    var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => race.Pick(rg.Id, B, RpsChoice.Ice))));
    Check(results.Count(r => r.Status == RpsPickStatus.Decided) == 1, $"20 clicks a la vez: se decide una sola vez ({results.Count(r => r.Status == RpsPickStatus.Decided)})");
    // Y el reloj contra el último click: uno solo cierra la partida.
    var rt = race.Create(A, "Ana", B, "Beto", 500)!;
    race.Accept(rt.Id, B);
    race.Pick(rt.Id, A, RpsChoice.Fire);
    var mixed = await Task.WhenAll(Task.Run(() => race.Timeout(rt.Id, rt.Round)), Task.Run(() => race.Pick(rt.Id, B, RpsChoice.Water)));
    Check(mixed.Count(r => r.Status is RpsPickStatus.Decided or RpsPickStatus.Cancelled) == 1, "el reloj y el último click a la vez: la partida se cierra una sola vez");
}

Console.WriteLine("\n== 3. El desafío contra la base ==");
await Reset();
var game = new RpsService();
var anaUser = Player(A, "Ana");
var beto = Player(B, "Beto");
Check((await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", Player(C, "Bot", bot: true), "100")).Error!.Contains("bots"), "a un bot no");
Check((await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", anaUser, "100")).Error!.Contains("vos mismo"), "a vos mismo no");
Check((await RpsModule.PrepareChallengeAsync(users, game, NOACC, "X", beto, "100")).Error!.Contains("/start"), "sin cuenta no (y no la crea)");
Check(await users.GetByDiscordIdAsync(NOACC) is null, "no se creó ninguna cuenta");
Check((await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", Player(NOACC, "X"), "100")).Error!.Contains("/start"), "a alguien sin cuenta no");
Check((await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", beto, "5")).Error!.Contains("mínima"), $"la apuesta mínima es {RpsRules.MinBet}");
Check((await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", beto, "20k")).Error!.Contains("No te alcanza"), "no se apuesta lo que no tenés");
await db.ExecuteAsync("UPDATE users SET gold = 300 WHERE discord_id = @id", new { id = (long)B });
Check((await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", beto, "5k")).Error!.Contains("no le alcanza"), "ni lo que no tiene el otro");
var allIn = await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", Player(C, "Ceci"), "all");
Check(allIn.Game is { Bet: 10000 }, "«all» es todo tu oro");
var ok = await RpsModule.PrepareChallengeAsync(users, game, A, "Ana", beto, "300");
Check(ok.Game is { Bet: 300 } && ok.Error is null, "300 contra 300: armado");

Console.WriteLine("\n== 4. El cobro ==");
await Reset();
var events = new RecordingEvents();
var paid = game.Create(A, "Ana", B, "Beto", 2500)!;
game.Accept(paid.Id, B);
game.Pick(paid.Id, A, RpsChoice.Ice);
var decided = game.Pick(paid.Id, B, RpsChoice.Water);
string text = await RpsModule.SettleAsync(transfers, events, paid, decided);
Check(await Gold(A) == 12500 && await Gold(B) == 7500, $"el que gana se lleva la apuesta del otro ({await Gold(A)} / {await Gold(B)})");
Check(text.Contains("2.500") && text.Contains($"<@{A}>") && text.Contains("Hielo") && text.Contains("Agua") && text.Contains("congela el agua"), "el texto dice quién eligió qué y cuánto ganó");
Check(events.Kinds.SequenceEqual([GameEventKinds.RpsWin, GameEventKinds.RpsLoss, GameEventKinds.RpsGoldWon, GameEventKinds.RpsGoldLost]) && events.Amounts.SequenceEqual([1L, 1L, 2500L, 2500L]),
    "eventos: ganada y perdida cuentan 1 (para el logro) y el oro va aparte");

var broke = game.Create(A, "Ana", B, "Beto", 2500)!;
game.Accept(broke.Id, B);
game.Pick(broke.Id, A, RpsChoice.Fire);
var lostR = game.Pick(broke.Id, B, RpsChoice.Ice);
await db.ExecuteAsync("UPDATE users SET gold = 100 WHERE discord_id = @id", new { id = (long)B });
var ev2 = new RecordingEvents();
string voided = await RpsModule.SettleAsync(transfers, ev2, broke, lostR);
Check(voided.Contains("se anula") && await Gold(A) == 12500 && await Gold(B) == 100 && ev2.Kinds.Count == 0, "si el perdedor gastó el oro en el medio, se anula y no se mueve nada");
string cancelled = await RpsModule.SettleAsync(transfers, ev2, broke, new RpsPickResult(RpsPickStatus.Cancelled, ByTimeout: true));
Check(cancelled.Contains("nadie pierde") && ev2.Kinds.Count == 0, "cancelada: nadie pierde oro");
Check((await RpsModule.CheckGoldAsync(users, broke))!.Contains("ya no le alcanza"), "al aceptar se vuelve a mirar el oro de los dos");

Console.WriteLine("\n== 5. Pantallas ==");
var view = game.Create(A, "Ana", B, "Beto", 1500)!;
var offered = RpsModule.BuildEmbed(view);
var offeredButtons = RpsModule.BuildComponents(view).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(offered.Description!.Contains("1.500") && offeredButtons.Select(b => b.CustomId).SequenceEqual([$"rps_accept:{view.Id}", $"rps_decline:{view.Id}"]), "la propuesta: la apuesta y Aceptar / Rechazar");
game.Accept(view.Id, B);
game.Pick(view.Id, A, RpsChoice.Fire);
var picking = RpsModule.BuildEmbed(view);
var pickButtons = RpsModule.BuildComponents(view).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(picking.Description!.Contains($"✅ <@{A}> ya eligió") && picking.Description.Contains($"⏳ <@{B}>") && !picking.Description.Contains("Fuego"), "en juego: se ve QUIÉN eligió, nunca QUÉ");
Check(pickButtons.Select(b => b.CustomId).SequenceEqual([$"rps_pick:{view.Id}:f", $"rps_pick:{view.Id}:i", $"rps_pick:{view.Id}:w"]), "los tres botones de elegir");
Check(LeakCheck.FitsDiscord(offered) && LeakCheck.FitsDiscord(picking), "entran en Discord");
using (Loc.Use("en"))
{
    var en = LeakCheck.AllText(RpsModule.BuildEmbed(view)) + "\n" + string.Join("\n", pickButtons.Select(_ => "")) + RpsModule.BuildFinalEmbed(view, await RpsModule.SettleAsync(transfers, new RecordingEvents(), view, new RpsPickResult(RpsPickStatus.Cancelled))).Description;
    var leak = LeakCheck.NoSpanish(en, []);
    Check(leak.Ok && en.Contains("Bet:"), $"en inglés sin español ({leak.Found})");
}

Console.WriteLine("\n== 6. Historial, logro y ayuda ==");
var report = GameHistory.Build([
    new EventTotal(GameEventKinds.RpsWin, "Beto", 3, 3), new EventTotal(GameEventKinds.RpsWin, "Ceci", 1, 1), new EventTotal(GameEventKinds.RpsLoss, "Beto", 2, 2),
    new EventTotal(GameEventKinds.RpsGoldWon, "Beto", 3, 9000), new EventTotal(GameEventKinds.RpsGoldWon, "Ceci", 1, 500), new EventTotal(GameEventKinds.RpsGoldLost, "Beto", 2, 4000)]);
var row = report.Games.Single(g => g.Name == "Elementos");
Check(row.Won == 4 && row.Lost == 2 && row.GoldWon == 9500 && row.GoldLost == 4000 && row.IsCasino, "/history: 4 ganadas, 2 perdidas y el oro sumado de todos los rivales");
Check(GameHistory.Kinds.Contains(GameEventKinds.RpsGoldWon) && GameHistory.Kinds.Contains(GameEventKinds.RpsLoss), "el historial pide los eventos nuevos");
var ach = AchievementCatalog.Find("manoganadora");
Check(ach is not null && ach.StatKind == GameEventKinds.RpsWin && ach.Tiers.Select(x => x.Threshold).SequenceEqual([5L, 25L, 100L]) && ach.Tiers.All(x => x.Reward.Box == BoxGrant.None)
    && AchievementCatalog.Categories.Single(c => c.Name == "Economía").Keys.Contains("manoganadora"), "logro «Señor de los Elementos» (5/25/100 ganadas), solo oro y EXP, en Economía");
Check(!TitleCatalog.All.Any(t => t.Source == GameEventKinds.RpsWin), "sin títulos (el dueño: «logro nomás»)");
var help = HelpTopics.Render(HelpTopics.Find("play")!);
Check(LeakCheck.AllText(help).Contains("game:Elementos") && LeakCheck.FitsDiscord(help), "el tema de ayuda play explica Elementos y entra");

Console.WriteLine("\n== 7. Ruteo con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(RpsModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(RpsModule).Assembly, sp);
Check(!interactions.SlashCommands.Any(c => c.Name == "ppt"), "/ppt ya no existe: el juego está en /play");
var slash = interactions.SlashCommands.Single(c => c.Name == "play");
var gameParam = slash.Parameters.Single(p => p.Name == "game");
Check(slash.Description.Length is > 0 and <= 100 && gameParam.Choices.Any(c => (string)c.Value == RpsRules.GameKey) && slash.Parameters.Any(p => p.Name == "rival" && !p.IsRequired && p.ParameterType == typeof(IUser)) && slash.Parameters.All(p => p.Description.Length <= 100),
    $"/play tiene la opción Elementos y el parámetro opcional rival ({slash.Description.Length} caracteres)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
Check(interactions.SearchComponentCommand(Click($"rps_accept:{Guid.NewGuid()}")).Command?.MethodName == "HandleAcceptAsync"
    && interactions.SearchComponentCommand(Click($"rps_decline:{Guid.NewGuid()}")).Command?.MethodName == "HandleDeclineAsync"
    && interactions.SearchComponentCommand(Click($"rps_pick:{Guid.NewGuid()}:r")).Command?.MethodName == "HandlePickAsync", "los tres botones llegan a su método");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "elementos", "elemento", "elem" }.All(a => texts.Search($"{a} @x 5k") is { IsSuccess: true } f && f.Commands[0].Command.Name == "elementos"), "aa elementos y sus alias llegan al comando");
Check(!texts.Search("ppt @x 5k").IsSuccess, "aa ppt ya no existe");
Check(texts.Search("play elementos @x 5k") is { IsSuccess: true } pl && pl.Commands[0].Command.Name == "play", "aa play elementos llega a /play");
Check(texts.Search("elementos @x 5k").Commands[0].Command.Parameters[0].Type == typeof(IUser), "su primer parámetro es un jugador: «responder = mencionar» lo completa");
Check(!FightLock.IsAllowed("elementos") && !FightLock.IsAllowed("play"), "en plena pelea se bloquea");
Check(sp.GetService(typeof(IRpsService)) is RpsService, "el servicio está registrado");

foreach (var id in new[] { A, B, C }) await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)A, (long)B, (long)C } });
await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)A, (long)B, (long)C } });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public List<long> Amounts { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); Amounts.Add(amount); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
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
