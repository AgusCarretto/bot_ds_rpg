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

// v0.55.0 / v0.55.1 (el paño de fichas): la ruleta dentro de /play (/play game:Ruleta apuesta:5k, aa ruleta 5k, aa ruleta rojo 5k, aa ruleta 17 500). El dueño
// (2026-11-13): giro animado con emojis, mesa con botones y, para arrancar, solo las apuestas simples (color, par o impar, un número).
// Se prueba: las reglas (la rueda, los pagos, la devolución exacta 36/37), cómo se lee lo escrito, el cobro contra la base LOCAL (usuarios
// descartables, 10 giros a la vez con oro para 3), la animación, la mesa, el historial, la ayuda, el inglés y el ruteo de Discord.Net.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong A = 999000000000099911UL, NOACC = 999000000000099999UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var casino = new CasinoRepository(factory);

async Task Reset(int gold)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)A, (long)NOACC } });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)A });
    await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)A });
    await users.GetOrCreateUserAsync(A);
    await db.ExecuteAsync("UPDATE users SET gold = @gold WHERE discord_id = @id", new { id = (long)A, gold });
}

async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)A });

Console.WriteLine("== 1. La rueda y los pagos ==");
var order = RouletteRules.WheelOrder;
Check(order.Count == 37 && order.OrderBy(n => n).SequenceEqual(Enumerable.Range(0, 37)) && order[0] == 0, "la rueda europea: 37 casillas, cada número una vez, empieza en el 0");
Check(Enumerable.Range(1, 36).Count(RouletteRules.IsRed) == 18 && !RouletteRules.IsRed(0) && RouletteRules.ColorEmoji(0) == "🟩", "18 rojos, 18 negros y el 0 verde");
Check(order.Zip(order.Skip(1)).All(p => RouletteRules.IsRed(p.First) != RouletteRules.IsRed(p.Second) || p.First == 0 || p.Second == 0), "en la rueda los colores se alternan (salvo junto al 0)");
var red = new RouletteBet(RouletteBetKind.Red);
var black = new RouletteBet(RouletteBetKind.Black);
var even = new RouletteBet(RouletteBetKind.Even);
var odd = new RouletteBet(RouletteBetKind.Odd);
var seventeen = new RouletteBet(RouletteBetKind.Number, 17);
Check(new[] { red, black, even, odd }.All(b => !RouletteRules.Wins(b, 0)) && RouletteRules.Wins(new RouletteBet(RouletteBetKind.Number, 0), 0), "el 0 hace perder color y par/impar, y gana si le apostaste al 0");
foreach (var bet in new[] { red, black, even, odd, seventeen })
{
    long back = Enumerable.Range(0, 37).Sum(n => (long)RouletteRules.Payout(bet, 1000, n));
    Check(back == 36000, $"{bet.Code}: en las 37 casillas devuelve 36.000 de 37.000 apostados (0,973, la casa se queda el 2,7 %)");
}
Check(RouletteRules.Payout(red, 500, 3) == 1000 && RouletteRules.Payout(red, 500, 2) == 0 && RouletteRules.Payout(seventeen, 500, 17) == 18000, "rojo paga ×2, el número ×36");
Check(Math.Abs(RouletteRules.ReturnToPlayer - 36.0 / 37.0) < 1e-12, "la devolución publicada es 36/37");

Console.WriteLine("\n== 2. Lo que se escribe ==");
Check(RouletteRules.Parse("ROJO")?.Kind == RouletteBetKind.Red && RouletteRules.Parse("black")?.Kind == RouletteBetKind.Black && RouletteRules.Parse("par")?.Kind == RouletteBetKind.Even && RouletteRules.Parse("odd")?.Kind == RouletteBetKind.Odd,
    "rojo/negro/par/impar en los dos idiomas, sin importar mayúsculas");
Check(RouletteRules.Parse("0") is { Kind: RouletteBetKind.Number, Number: 0 } && RouletteRules.Parse("36") is { Number: 36 } && RouletteRules.Parse("37") is null && RouletteRules.Parse("-1") is null && RouletteRules.Parse("5k") is null, "números del 0 al 36 y nada más");
Check(new[] { red, black, even, odd, seventeen }.All(b => RouletteRules.Parse(b.Code) == b), "el código del botón ida y vuelta");
Check(TextShortcuts.SplitRouletteArgs("17", "500") is ({ Number: 17 }, "500") && TextShortcuts.SplitRouletteArgs("500", "17") is ({ Number: 17 }, "500"), "«17 500» y «500 17»: 500 al 17");
Check(TextShortcuts.SplitRouletteArgs("rojo", "5k") is ({ Kind: RouletteBetKind.Red }, "5k") && TextShortcuts.SplitRouletteArgs("5k", "par") is ({ Kind: RouletteBetKind.Even }, "5k"), "«rojo 5k» y «5k par»");
Check(TextShortcuts.SplitRouletteArgs("5k", "") is (null, "5k") && TextShortcuts.SplitRouletteArgs("", "") is (null, ""), "una sola palabra es el monto (abre la mesa)");

Console.WriteLine("\n== 3. El giro, contra la base ==");
await Reset(10_000);
var events = new RecordingEvents();
var rng = new Random(7);
var spun = await RouletteModule.ExecuteSpinAsync(users, casino, events, A, 1000, red, rng);
int expected = 10_000 - 1000 + RouletteRules.Payout(red, 1000, spun.Number);
Check(spun.Error is null && await Gold() == expected && spun.Gold == expected, $"cobra y paga en una llamada: salió el {spun.Number}, quedó {await Gold()}");
Check(events.Kinds.Single() == (spun.Payout > 0 ? GameEventKinds.CasinoWin : GameEventKinds.CasinoLoss) && events.Details.Single() == "roulette" && events.Amounts.Single() == (spun.Payout > 0 ? spun.Payout - 1000 : 1000),
    "evento casino_win (neto) o casino_loss (lo apostado) con detail roulette: cuenta para /history y para Afortunado");
Check((await RouletteModule.ExecuteSpinAsync(users, casino, events, A, 5, red, rng)).Error!.Contains("mínima"), $"apuesta mínima {RouletteRules.MinBet}");
Check((await RouletteModule.ExecuteSpinAsync(users, casino, events, A, 1_000_000, red, rng)).Error!.Contains("No te alcanza"), "no se apuesta lo que no tenés");
Check((await RouletteModule.ExecuteSpinAsync(users, casino, events, A, RouletteRules.MaxNumberBet + 1, seventeen, rng)).Error!.Contains("hasta"), "al número no se le apuesta tanto que el premio no entre en un int");
Check((await RouletteModule.ExecuteSpinAsync(users, casino, events, NOACC, 100, red, rng)).Error!.Contains("/start") && await users.GetByDiscordIdAsync(NOACC) is null, "sin cuenta no juega (y el giro no la crea)");

await Reset(3000);
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => RouletteModule.ExecuteSpinAsync(users, casino, new RecordingEvents(), A, 1000, new RouletteBet(RouletteBetKind.Number, 36), new Random(i + 100))));
int okSpins = race.Count(r => r.Error is null);
Check(okSpins >= 3 && await Gold() >= 0, $"10 giros a la vez con oro para 3: nunca queda negativo ({okSpins} pasaron, oro {await Gold()})");

Console.WriteLine("\n== 4. El paño de fichas (v0.55.1) ==");
var boards = new RouletteBoardService();
var bd = boards.Open(A, 1000);
Check(boards.Place(bd, [red], 10_000) == RoulettePlaceStatus.Ok && boards.Place(bd, [red], 10_000) == RoulettePlaceStatus.Ok && bd.Bets["red"] == 2000, "dos fichas al rojo: 2.000");
boards.SetMultiplier(bd, 5);
Check(bd.Chip == 5000 && boards.Place(bd, [seventeen], 10_000) == RoulettePlaceStatus.Ok && bd.Total == 7000, "ficha ×5 (5.000) al 17: total 7.000");
Check(boards.Place(bd, [even], 10_000) == RoulettePlaceStatus.NotEnoughGold && bd.Total == 7000, "no deja pasar el oro que tenés (y no pone nada)");
boards.SetMultiplier(bd, 1);
Check(boards.Place(bd, [new RouletteBet(RouletteBetKind.Number, 1), new RouletteBet(RouletteBetKind.Number, 2), new RouletteBet(RouletteBetKind.Number, 3)], 10_000) == RoulettePlaceStatus.Ok && bd.Total == 10_000, "varios números de una: una ficha en cada uno");
Check(boards.Undo(bd) && bd.Total == 7000 && !bd.Bets.ContainsKey("1"), "Deshacer saca el último toque entero (los tres números)");
Check(boards.Undo(bd) && bd.Total == 2000 && boards.Undo(bd) && boards.Undo(bd) && bd.Total == 0 && !boards.Undo(bd), "y así hasta vaciarlo");
boards.Place(bd, [red], 10_000);
boards.Clear(bd);
Check(bd.Total == 0 && bd.Actions.Count == 0, "Borrar deja el paño vacío");
boards.SetMultiplier(bd, 7);
Check(bd.Multiplier == 1, "solo ×1, ×5 o ×10");
var huge = boards.Open(A, int.MaxValue / 20);
boards.SetMultiplier(huge, 10);
Check(boards.Place(huge, [red], int.MaxValue) == RoulettePlaceStatus.TooMuch && huge.Total == 0, "el total nunca pasa del máximo para que el premio entre en un int");
var placeRace = boards.Open(A, 1000);
await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => boards.Place(placeRace, [red], 10_000))));
Check(placeRace.Total == 10_000, $"30 toques a la vez con oro para 10 fichas: quedan exactamente 10 ({placeRace.Total})");

Console.WriteLine("\n== 5. Girar el paño, contra la base ==");
await Reset(10_000);
var multi = boards.Open(A, 1000);
boards.Place(multi, [red], 10_000);
boards.Place(multi, [seventeen], 10_000);
boards.Place(multi, [even], 10_000);
RouletteModule.FrameDelay = TimeSpan.Zero;
var edits = new List<(Embed Embed, MessageComponent? Components)>();
var errors = new List<string>();
await RouletteModule.SpinBoardAsync(users, casino, new RecordingEvents(), multi, new Random(11), (e, c) => { edits.Add((e, c)); return Task.CompletedTask; }, m => { errors.Add(m); return Task.CompletedTask; });
int landed = int.Parse(System.Text.RegularExpressions.Regex.Match(edits[^1].Embed.Title!, @"(\d+)!").Groups[1].Value);
int expectedPay = RouletteRules.TotalPayout([(red, 1000), (seventeen, 1000), (even, 1000)], landed);
Check(errors.Count == 0 && await Gold() == 10_000 - 3000 + expectedPay, $"las tres apuestas en un giro: salió el {landed}, pagó {expectedPay}, quedó {await Gold()}");
Check(edits.Count == 5 && edits.Take(4).All(x => x.Components is null && x.Embed.Title!.Contains("girando")) && edits[^1].Components is not null && edits[^1].Embed.Description!.Contains("3 apuestas"),
    "4 cuadros girando sin botones y el resultado con el paño de nuevo");
Check(edits[^1].Embed.Description!.Split('\n').Count(l => l.StartsWith("✅") || l.StartsWith("❌")) == 3 && multi.Total == 3000, "cada apuesta con su ✅ o ❌, y las fichas quedan puestas para repetir");
var lossEvents = new RecordingEvents();
await Reset(10_000);
var push = await RouletteModule.ExecuteSpinAsync(users, casino, lossEvents, A, [(red, 1000), (black, 1000)], new Random(2));
Check(push.Error is null && (push.Number == 0 ? lossEvents.Amounts.Single() == 2000 : lossEvents.Kinds.Count == 0), $"rojo y negro a la vez: recuperás lo puesto y no cuenta como ganada ni perdida (salvo el 0) (salió {push.Number})");
Check((await RouletteModule.ExecuteSpinAsync(users, casino, lossEvents, A, [], new Random(1))).Error!.Contains("al menos una ficha"), "sin fichas no gira");
RouletteModule.FrameDelay = TimeSpan.FromMilliseconds(200);
var errs2 = new List<string>();
var first = RouletteModule.SpinBoardAsync(users, casino, new RecordingEvents(), multi, new Random(4), (_, _) => Task.CompletedTask, m => { errs2.Add(m); return Task.CompletedTask; });
await Task.Delay(50);
await RouletteModule.SpinBoardAsync(users, casino, new RecordingEvents(), multi, new Random(5), (_, _) => Task.CompletedTask, m => { errs2.Add(m); return Task.CompletedTask; });
await first;
Check(errs2.Count == 1 && errs2[0].Contains("girando"), "un segundo Girar mientras gira no apuesta otra vez");
RouletteModule.FrameDelay = TimeSpan.Zero;
var quick = new List<MessageComponent?>();
await RouletteModule.PlayAndAnimateAsync(users, casino, new RecordingEvents(), A, 100, odd, new Random(6), (_, c) => { quick.Add(c); return Task.CompletedTask; }, _ => Task.CompletedTask);
Check(quick.Count == 5 && quick[^1] is null, "el atajo de texto («aa ruleta impar 100») gira igual, sin paño al final");

Console.WriteLine("\n== 5b. El paño en pantalla ==");
var view = await RouletteModule.OpenBoardAsync(users, boards, A, "1k");
var rows = view.Components!.Components.OfType<ActionRowComponent>().ToList();
var chips = rows[0].Components.OfType<ButtonComponent>().ToList();
var betButtons = rows[1].Components.OfType<ButtonComponent>().ToList();
var menus = rows.Skip(2).Take(2).SelectMany(r => r.Components).OfType<SelectMenuComponent>().ToList();
var actions = rows[4].Components.OfType<ButtonComponent>().ToList();
Check(rows.Count == 5 && chips.Select(b => b.CustomId).SequenceEqual([$"rb_chip:{A}:1", $"rb_chip:{A}:5", $"rb_chip:{A}:10"]) && chips[0].IsDisabled == true && chips[1].Label!.Contains("5.000"),
    "fila 1: el tamaño de ficha (×1 marcado, ×5 = 5.000, ×10)");
Check(betButtons.Select(b => b.CustomId).SequenceEqual([$"rb_bet:{A}:red", $"rb_bet:{A}:black", $"rb_bet:{A}:even", $"rb_bet:{A}:odd"]), "fila 2: rojo, negro, par, impar");
Check(menus.Count == 2 && menus[0].Options.Count == 19 && menus[0].MaxValues == 19 && menus[1].Options.Count == 18 && menus[1].MaxValues == 18 && menus.SelectMany(m => m.Options).Select(o => int.Parse(o.Value)).SequenceEqual(Enumerable.Range(0, 37)),
    "filas 3 y 4: los 37 números, marcando varios de una");
Check(actions.Select(b => b.CustomId).SequenceEqual([$"rb_spin:{A}", $"rb_undo:{A}", $"rb_clear:{A}"]), "fila 5: girar, deshacer, borrar");
var shown = boards.Get(A)!;
boards.Place(shown, [red, seventeen], 10_000);
var boardEmbed = RouletteModule.BuildBoardEmbed(shown, 10_000);
Check(boardEmbed.Description!.Contains("🟥 Rojo: **1.000**") && boardEmbed.Description.Contains("⬛ 17: **1.000**") && boardEmbed.Description.Contains("Total: **2.000**"), "el paño lista cada apuesta y el total");
Check((await RouletteModule.OpenBoardAsync(users, boards, A, "5")).Error!.Contains("mínima") && (await RouletteModule.OpenBoardAsync(users, boards, A, "9m")).Error!.Contains("No te alcanza"), "abrir revisa la ficha mínima y el oro");
Check(LeakCheck.FitsDiscord(boardEmbed) && LeakCheck.FitsDiscord(edits[^1].Embed), "entran en Discord");
var frames = RouletteRules.Frames(17, new Random(1));
Check(frames.Count == 5 && frames[^1].Contains("**【⬛ 17】**") && frames.Distinct().Count() == 5, "el giro: 5 cuadros distintos y el último deja el 17 en el medio");
using (Loc.Use("en"))
{
    string text = LeakCheck.AllText(RouletteModule.BuildBoardEmbed(shown, 10_000)) + "\n" +
        LeakCheck.AllText(RouletteModule.BuildResultEmbed(frames[^1], [(red, 1000), (seventeen, 1000)], new RouletteModule.SpinOutcome(null, 17, 36000, 45000, 2000))) + "\n" +
        string.Join(" ", RouletteModule.BuildBoardComponents(shown).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label));
    var leak = LeakCheck.NoSpanish(text, []);
    Check(leak.Ok && text.Contains("Red") && text.Contains("You won") && text.Contains("Spin"), $"en inglés sin español ({leak.Found})");
}


Console.WriteLine("\n== 6. Historial y ayuda ==");
var report = GameHistory.Build([new EventTotal(GameEventKinds.CasinoWin, "roulette", 2, 3000), new EventTotal(GameEventKinds.CasinoLoss, "roulette", 5, 2500)]);
var row = report.Games.Single(g => g.Name == "Ruleta");
Check(row.Won == 2 && row.Lost == 5 && row.GoldWon == 3000 && row.GoldLost == 2500 && row.IsCasino, "/history tiene su fila «Ruleta»");
var help = HelpTopics.Render(HelpTopics.Find("play")!);
Check(LeakCheck.AllText(help).Contains("game:Ruleta") && LeakCheck.AllText(help).Contains("2,7") && LeakCheck.FitsDiscord(help), "el tema play explica la ruleta, con el 2,7 % de la casa, y entra");

Console.WriteLine("\n== 7. Ruteo con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(RouletteModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(RouletteModule).Assembly, sp);
var play = interactions.SlashCommands.Single(c => c.Name == "play");
Check(play.Description.Length <= 100 && play.Parameters.Single(p => p.Name == "game").Choices.Any(c => (string)c.Value == RouletteRules.GameKey), $"/play ofrece la Ruleta ({play.Description.Length} caracteres de descripción)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
string? M(string id) => interactions.SearchComponentCommand(Click(id)).Command?.MethodName;
Check(M($"rb_chip:{A}:5") == "HandleChipAsync" && M($"rb_bet:{A}:red") == "HandleBetAsync" && M($"rb_num:{A}:a") == "HandleNumbersAsync" && M($"rb_spin:{A}") == "HandleSpinAsync" && M($"rb_undo:{A}") == "HandleUndoAsync" && M($"rb_clear:{A}") == "HandleClearAsync",
    "cada botón y menú del paño llega a su método");
Check(M($"rlt:{A}:1000:red") == "HandleOldTableAsync" && M($"rlt_n:{A}:1000:a") == "HandleOldNumberAsync", "los botones de la mesa vieja (v0.55.0) contestan cómo abrir el paño");
Check(sp.GetService(typeof(IRouletteBoardService)) is RouletteBoardService, "el servicio del paño está registrado");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "ruleta", "roulette", "rl" }.All(a => texts.Search($"{a} rojo 5k") is { IsSuccess: true } f && f.Commands[0].Command.Name == "ruleta") && texts.Search("play ruleta 5k") is { IsSuccess: true } pl && pl.Commands[0].Command.Name == "play",
    "aa ruleta, sus alias y aa play ruleta llegan");
Check(!FightLock.IsAllowed("ruleta"), "en plena pelea se bloquea");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)A });
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)A });
await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)A });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public List<long> Amounts { get; } = [];
    public List<string?> Details { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); Amounts.Add(amount); Details.Add(detail); return Task.CompletedTask; }
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
