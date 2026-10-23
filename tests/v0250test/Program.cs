using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.25.0 (el dueño, 2026-10-22): «armate el bj entonces. aa bj 10k por ejemplo.» El blackjack del casino: reglas puras con un mazo guionado, la plata contra la base real (cobrar al repartir, doblar, pagar, carreras,
// plantar por tiempo), lo que se ve (mano con la carta tapada y botones), el retorno medido con la estrategia básica y los comandos con el InteractionService / CommandService reales.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

// Un mazo guionado: las cartas salen en el orden dado (A = 1, J/Q/K = 11/12/13). Si el guion se acaba, tira (así se nota si el juego pidió una carta de más).
Func<Card> Script(params int[] ranks) { var queue = new Queue<int>(ranks); return () => new Card(queue.Dequeue(), 0); }
// Reparte: jugador, jugador, banca (la que se ve), banca.
BlackjackState Deal(int stake, int p1, int p2, int d1, int d2, out Queue<int> rest, params int[] next)
{
    rest = new Queue<int>(next);
    var q = new Queue<int>([p1, p2, d1, d2, .. next]);
    var state = Blackjack.Start(stake, () => new Card(q.Dequeue(), 0));
    rest = new Queue<int>(q);
    return state;
}

Console.WriteLine("== 1. Valor de las manos ==");
Card C(int rank) => new(rank, 0);
Check(Blackjack.Evaluate([C(1), C(13)]) == (21, true) && Blackjack.Evaluate([C(1), C(1)]) == (12, true) && Blackjack.Evaluate([C(1), C(1), C(9)]) == (21, true) && Blackjack.Evaluate([C(1), C(6), C(13)]) == (17, false)
      && Blackjack.Evaluate([C(13), C(12), C(5)]) == (25, false) && Blackjack.Evaluate([C(1), C(5)]) == (16, true) && Blackjack.Evaluate([C(10), C(11), C(12), C(13)]).Total == 40, "el As vale 11 mientras no se pase (mano blanda) y 1 si no; J, Q y K valen 10");
Check(Blackjack.IsBlackjack([C(1), C(13)]) && Blackjack.IsBlackjack([C(10), C(1)]) && !Blackjack.IsBlackjack([C(1), C(5), C(5)]) && !Blackjack.IsBlackjack([C(10), C(13)]) && !Blackjack.IsBlackjack([C(1), C(1)]), "blackjack natural = exactamente dos cartas que suman 21");
Check(new Card(1, 0).Label == "A♠" && new Card(13, 1).Label == "K♥" && new Card(10, 2).Label == "10♦" && new Card(7, 3).Label == "7♣" && new Card(12, 9).Label.EndsWith('♣'), "las cartas se dibujan con su valor y palo");

Console.WriteLine("\n== 2. La mano, jugada con un mazo guionado ==");
var nat = Deal(100, 1, 13, 9, 7, out _);
Check(nat.Outcome == BlackjackOutcome.PlayerBlackjack && Blackjack.Payout(nat) == 250 && Blackjack.Net(nat) == 150 && nat.Finished, "blackjack natural: paga 3 a 2 (apostó 100, cobra 250) y la mano termina al repartir");
Check(Deal(15, 1, 13, 9, 7, out _) is { } n15 && Blackjack.Payout(n15) == 37 && Blackjack.Payout(Deal(1, 1, 13, 9, 7, out _)) == 2, "el 3 a 2 se redondea hacia abajo (15 → 37; 1 → 2)");
var bothNat = Deal(100, 1, 13, 1, 12, out _);
Check(bothNat.Outcome == BlackjackOutcome.Push && Blackjack.Payout(bothNat) == 100, "los dos con blackjack: empate, devuelve la apuesta");
var dealerNat = Deal(100, 10, 9, 1, 13, out _);
Check(dealerNat.Outcome == BlackjackOutcome.DealerWin && Blackjack.Payout(dealerNat) == 0 && dealerNat.Finished, "blackjack de la banca solamente: pierde al instante, antes de jugar");
var s1 = Deal(100, 13, 9, 13, 7, out var r1);
Check(s1.Outcome == BlackjackOutcome.InProgress && s1.CanDouble, "una mano normal queda en juego y se puede doblar");
var won = Blackjack.Stand(s1, () => new Card(r1.Dequeue(), 0));
Check(won.Outcome == BlackjackOutcome.PlayerWin && Blackjack.Payout(won) == 200 && Blackjack.Net(won) == 100 && won.Dealer.Count == 2, "19 contra 17 de la banca: gana 1 a 1 (cobra 200) y la banca se planta en 17 sin pedir");
var s2 = Deal(100, 13, 8, 6, 5, out var r2, 13);
var lost = Blackjack.Stand(s2, () => new Card(r2.Dequeue(), 0));
Check(lost.Outcome == BlackjackOutcome.DealerWin && Evaluate(lost.Dealer) == 21 && lost.Dealer.Count == 3 && Blackjack.Payout(lost) == 0, "la banca con 11 pide hasta 17 o más (pidió un 10: 21) y le gana al 18");
int Evaluate(IReadOnlyList<Card> hand) => Blackjack.Evaluate(hand).Total;
var s3 = Deal(100, 13, 8, 13, 6, out var r3, 9);
var busted = Blackjack.Stand(s3, () => new Card(r3.Dequeue(), 0));
Check(busted.Outcome == BlackjackOutcome.PlayerWin && Evaluate(busted.Dealer) == 25, "la banca se pasa (16 + 9): gana el jugador");
var s4 = Deal(100, 13, 8, 1, 6, out var r4);
var soft17 = Blackjack.Stand(s4, () => new Card(r4.Dequeue(), 0));
Check(soft17.Outcome == BlackjackOutcome.PlayerWin && soft17.Dealer.Count == 2, "la banca se planta también en el 17 BLANDO (A + 6): el 18 le gana sin que pida");
var s5 = Deal(100, 13, 8, 13, 8, out var r5);
var pushState = Blackjack.Stand(s5, () => new Card(r5.Dequeue(), 0));
Check(pushState.Outcome == BlackjackOutcome.Push && Blackjack.Payout(pushState) == 100, "18 contra 18: empate, devuelve la apuesta");
var s6 = Deal(100, 13, 6, 13, 7, out var r6, 13);
var bust = Blackjack.Hit(s6, () => new Card(r6.Dequeue(), 0));
Check(bust.Outcome == BlackjackOutcome.PlayerBust && bust.Dealer.Count == 2 && Blackjack.Payout(bust) == 0 && r6.Count == 0 && bust.Finished, "pedir y pasarse: pierde y la banca ni juega");
var s7 = Deal(100, 5, 6, 13, 8, out var r7, 13);
var auto21 = Blackjack.Hit(s7, () => new Card(r7.Dequeue(), 0));
Check(auto21.Outcome == BlackjackOutcome.PlayerWin && Evaluate(auto21.Player) == 21 && auto21.Finished, "llegar a 21 pidiendo se planta solo: 21 contra 18, gana");
var s8 = Deal(100, 5, 6, 13, 7, out var r8, 13);
var doubled = Blackjack.Double(s8, () => new Card(r8.Dequeue(), 0));
Check(doubled.Doubled && doubled.Stake == 200 && doubled.Player.Count == 3 && doubled.Outcome == BlackjackOutcome.PlayerWin && Blackjack.Payout(doubled) == 400 && Blackjack.Net(doubled) == 200, "doblar con 11: una carta más (un 10 = 21), la apuesta pasa a 200 y gana 400");
var s9 = Deal(100, 13, 6, 13, 7, out var r9, 13);
var doubleBust = Blackjack.Double(s9, () => new Card(r9.Dequeue(), 0));
Check(doubleBust.Outcome == BlackjackOutcome.PlayerBust && doubleBust.Stake == 200 && Blackjack.Net(doubleBust) == -200, "doblar y pasarse: pierde la apuesta doble");
var s10 = Deal(100, 5, 6, 13, 7, out var r10, 2, 13);
var afterHit = Blackjack.Hit(s10, () => new Card(r10.Dequeue(), 0));
Check(!afterHit.CanDouble && Blackjack.Double(afterHit, () => throw new InvalidOperationException("no tiene que pedir carta")) == afterHit && Blackjack.Hit(won, () => throw new InvalidOperationException("ya terminó")) == won && !won.CanDouble, "doblar solo con las dos primeras cartas; una mano terminada no se toca");

Console.WriteLine("\n== 3. El retorno (la casa no regala) ==");
int BasicStrategy(BlackjackState st)
{
    var (total, soft) = Blackjack.Evaluate(st.Player);
    int up = st.Dealer[0].Rank == 1 ? 11 : st.Dealer[0].Value;
    if (st.CanDouble)
    {
        if (soft && ((total is 13 or 14 && up is >= 5 and <= 6) || (total is 15 or 16 && up is >= 4 and <= 6) || (total is 17 or 18 && up is >= 3 and <= 6))) return 2;
        if (!soft && ((total == 9 && up is >= 3 and <= 6) || (total == 10 && up is >= 2 and <= 9) || (total == 11 && up is >= 2 and <= 10))) return 2;
    }

    if (soft) return total >= 19 ? 1 : total == 18 ? (up is 2 or 7 or 8 ? 1 : 0) : 0;
    return total >= 17 ? 1 : total >= 13 ? (up <= 6 ? 1 : 0) : total == 12 ? (up is >= 4 and <= 6 ? 1 : 0) : 0;
}

double Rtp(Func<BlackjackState, int> policy, int hands, int seed)
{
    var rng = new Random(seed);
    long staked = 0, returned = 0;
    Func<Card> draw = () => Blackjack.RandomCard(rng);
    for (int i = 0; i < hands; i++)
    {
        var st = Blackjack.Start(10, draw);
        while (!st.Finished)
        {
            st = policy(st) switch { 2 => Blackjack.Double(st, draw), 1 => Blackjack.Stand(st, draw), _ => Blackjack.Hit(st, draw) };
        }

        staked += st.Stake;
        returned += Blackjack.Payout(st);
    }

    return (double)returned / staked;
}

double rtpBasic = Rtp(BasicStrategy, 1_000_000, 1);
double rtpNaive = Rtp(st => Blackjack.Evaluate(st.Player).Total >= 12 ? 1 : 0, 500_000, 2);
Check(rtpBasic is > 0.975 and < 1.0, $"jugando con la estrategia básica el retorno es {rtpBasic:F4} (la casa se queda con ~{(1 - rtpBasic) * 100:F1} %): siempre por debajo de 1, como las slots");
Check(rtpNaive < rtpBasic && rtpNaive < 0.97, $"jugando peor (plantarse siempre desde 12) es peor: {rtpNaive:F4}");
double natRate = Enumerable.Range(0, 200_000).Count(i => { var r = new Random(i); return Blackjack.Start(10, () => Blackjack.RandomCard(r)).Outcome == BlackjackOutcome.PlayerBlackjack; }) / 200_000.0;
Check(natRate is > 0.04 and < 0.055, $"el blackjack natural sale ~4,7 % de las manos ({natRate * 100:F2} %): el mazo infinito reparte bien");

Console.WriteLine("\n== 4. La plata, contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var casinoRepo = new CasinoRepository(factory);
var events = new GameEventService(new GameEventRepository(factory));
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const long Base = 999000000000099000L;
async Task Wipe() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id BETWEEN @a AND @b; DELETE FROM game_events WHERE discord_id BETWEEN @a AND @b", new { a = Base, b = Base + 99 });
await Wipe();
ulong U(int n) => (ulong)(Base + n);
async Task Reset(int n, int gold) { await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = Base + n }); await users.GetOrCreateUserAsync(U(n)); await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = Base + n, g = gold }); }
async Task<int> Gold(int n) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = Base + n });
async Task<(string Kind, long Amount)[]> Events(int n) => (await db.QueryAsync<(string, long)>("SELECT kind, amount FROM game_events WHERE discord_id = @id AND detail = 'blackjack' ORDER BY event_id", new { id = Base + n })).ToArray();
BlackjackService Svc(params int[] ranks) => new(users, casinoRepo, events, Script(ranks));

// Ganar plantándose.
await Reset(1, 10_000);
var svc = Svc(13, 9, 13, 7);
var started = await svc.StartAsync(U(1), "1000");
Check(started.View is { State.Finished: false } && started.Message is null && await Gold(1) == 9000 && svc.Peek(U(1)) is not null, "repartir cobra la apuesta (10.000 → 9.000) y deja la mano en juego");
var stood = await svc.ActAsync(U(1), started.View!.GameId, BlackjackAction.Stand);
Check(stood.View is { State.Outcome: BlackjackOutcome.PlayerWin } && await Gold(1) == 11_000 && svc.Peek(U(1)) is null && stood.View.Gold == 11_000, "plantarse y ganar paga 2.000 (queda en 11.000) y la mano sale de juego");
Check((await Events(1)).SequenceEqual([("casino_win", 1000L)]), "deja su evento: casino_win por +1.000 con detail «blackjack»");

// Blackjack natural.
await Reset(2, 10_000);
var natural = await Svc(1, 13, 9, 7).StartAsync(U(2), "1k");
Check(natural.View is { State.Outcome: BlackjackOutcome.PlayerBlackjack } && await Gold(2) == 11_500 && (await Events(2)).SequenceEqual([("casino_win", 1500L)]), "blackjack natural con «1k»: cobra 2.500 (queda en 11.500) en el acto");

// Perder pasándose.
await Reset(3, 10_000);
var svc3 = Svc(13, 6, 13, 7, 13);
var h3 = await svc3.StartAsync(U(3), "1000");
var bust3 = await svc3.ActAsync(U(3), h3.View!.GameId, BlackjackAction.Hit);
Check(bust3.View is { State.Outcome: BlackjackOutcome.PlayerBust } && await Gold(3) == 9000 && (await Events(3)).SequenceEqual([("casino_loss", 1000L)]), "pasarse: queda en 9.000 y deja casino_loss por 1.000");

// Empate.
await Reset(4, 10_000);
var svc4 = Svc(13, 8, 13, 8);
var h4 = await svc4.StartAsync(U(4), "1000");
var push4 = await svc4.ActAsync(U(4), h4.View!.GameId, BlackjackAction.Stand);
Check(push4.View is { State.Outcome: BlackjackOutcome.Push } && await Gold(4) == 10_000 && (await Events(4)).Length == 0, "empate: devuelve la apuesta (10.000) y no deja evento de ganada ni perdida");

// Doblar.
await Reset(5, 10_000);
var svc5 = Svc(5, 6, 13, 7, 13);
var h5 = await svc5.StartAsync(U(5), "1000");
Check(h5.View!.CanDouble, "con 11 y oro suficiente se puede doblar");
var d5 = await svc5.ActAsync(U(5), h5.View.GameId, BlackjackAction.Double);
Check(d5.View is { State: { Doubled: true, Stake: 2000, Outcome: BlackjackOutcome.PlayerWin } } && await Gold(5) == 12_000 && (await Events(5)).SequenceEqual([("casino_win", 2000L)]), "doblar cobra otros 1.000 (8.000), saca un 10 (21), gana: 4.000 → queda en 12.000 y el evento es +2.000");

// Doblar sin oro: la mano sigue.
await Reset(6, 1500);
var svc6 = Svc(5, 6, 13, 7, 2, 13);
var h6 = await svc6.StartAsync(U(6), "1000");
Check(!h6.View!.CanDouble && await Gold(6) == 500, "con 500 de oro después de apostar 1.000 no se ofrece doblar");
var d6 = await svc6.ActAsync(U(6), h6.View.GameId, BlackjackAction.Double);
Check(d6.Message!.Contains("No te alcanza") && d6.View is null && await Gold(6) == 500 && svc6.Peek(U(6)) is { State.Finished: false }, "doblar sin el oro se rechaza sin tocar nada y la mano sigue en juego");

// Rechazos al empezar.
await Reset(7, 500);
var svc7 = Svc(13, 9, 13, 7);
Check((await svc7.StartAsync(U(7), "1000")).Message!.Contains("No tenés suficiente oro") && await Gold(7) == 500 && svc7.Peek(U(7)) is null, "sin oro para la apuesta: se rechaza, no se cobra y no hay mano");
Check((await svc7.StartAsync(U(7), "5")).Message!.Contains("mínima") && (await svc7.StartAsync(U(7), "abc")).Message!.Contains("No entendí") && (await svc7.StartAsync(U(7), "999m")).Message!.Contains("máxima"), "apuesta menor al mínimo, ilegible o por encima del máximo: se rechaza con su motivo");
var all7 = await svc7.StartAsync(U(7), "all");
Check(all7.View is not null && all7.View.State.Stake == 500 && await Gold(7) == 0, "«all» apuesta todo el oro (500 → 0 en juego)");
Check((await svc7.StartAsync(U(7), "10")).Message!.Contains("Ya tenés una mano en juego"), "con una mano en juego no se reparte otra");
await svc7.ActAsync(U(7), all7.View!.GameId, BlackjackAction.Stand);

// Carreras.
await Reset(8, 10_000);
var rankCycle = new[] { 13, 9, 13, 7 };
int cycle = 0; object cycleLock = new();
var raceSvc = new BlackjackService(users, casinoRepo, events, () => { lock (cycleLock) { return new Card(rankCycle[cycle++ % rankCycle.Length], 0); } });
var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => raceSvc.StartAsync(U(8), "1000"))));
Check(starts.Count(s => s.View is not null) == 1 && starts.Count(s => s.Message is not null) == 5 && await Gold(8) == 9000, "6 «aa bj 1000» a la vez: se reparte UNA mano y se cobra UNA vez (9.000)");
var gameId8 = starts.First(s => s.View is not null).View!.GameId;
var stands = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => raceSvc.ActAsync(U(8), gameId8, BlackjackAction.Stand))));
Check(await Gold(8) is 9000 or 11_000 && (await Events(8)).Length <= 1 && raceSvc.Peek(U(8)) is null, $"10 clicks a la vez en «Plantarse»: la mano se paga una sola vez (oro final {await Gold(8)}, {(await Events(8)).Length} evento)");
var stale = await raceSvc.ActAsync(U(8), gameId8, BlackjackAction.Hit);
Check(stale.View is { Note: "Esa mano ya había terminado." } && stale.Message is null && await Gold(8) is 9000 or 11_000, "un botón de una mano vieja muestra cómo terminó y no hace nada");
var none = await raceSvc.ActAsync(U(8), null, BlackjackAction.Hit);
var wrongId = await Svc(13, 9, 13, 7).ActAsync(U(9), Guid.NewGuid(), BlackjackAction.Hit);
Check(none.Message!.Contains("No tenés una mano") && wrongId.Message!.Contains("No tenés una mano"), "sin mano en juego dice cómo empezar");

// Plantar por tiempo.
await Reset(10, 10_000);
var timed = Svc(13, 9, 13, 7);
timed.IdleTimeout = TimeSpan.FromMilliseconds(400);
var h10 = await timed.StartAsync(U(10), "1000");
Check(await Gold(10) == 9000 && timed.Peek(U(10)) is not null, "una mano abandonada arranca cobrada");
await Task.Delay(1800);
var late = await timed.ActAsync(U(10), h10.View!.GameId, BlackjackAction.Hit);
Check(timed.Peek(U(10)) is null && await Gold(10) == 11_000 && late.View is { Note: "Esa mano ya había terminado." } or { Note: not null } && (await Events(10)).SequenceEqual([("casino_win", 1000L)]), "sin jugar durante el tiempo límite la banca te planta: se jugó sola (19 contra 17) y se pagó, no se perdió la apuesta");
Check(late.View!.State.Outcome == BlackjackOutcome.PlayerWin, "y el botón viejo muestra ese resultado");

Console.WriteLine("\n== 5. Lo que se ve ==");
var viewIn = new BlackjackView(Guid.NewGuid(), Deal(500, 1, 6, 13, 7, out _), 2000);
var embedIn = BlackjackModule.BuildEmbed(viewIn);
Check(embedIn.Title!.Contains("apuesta 500") && embedIn.Description.Contains("Tu mano** (17 blando)") && embedIn.Description.Contains("`A♠ 6♠`") && embedIn.Description.Contains("`K♠ ❓`") && !embedIn.Description.Contains("7♠") && embedIn.Description.Contains("aa bj hit") && embedIn.Footer!.Value.Text.Contains("Oro: 2000"),
    "en juego: tus cartas con el total (blando), UNA carta de la banca y la otra tapada, y cómo seguir");
var compsIn = BlackjackModule.BuildComponents(viewIn, 77)!;
var buttons = compsIn.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(buttons.Count == 3 && buttons[0].CustomId == $"bj_hit:77:{viewIn.GameId:N}" && buttons[1].CustomId == $"bj_stand:77:{viewIn.GameId:N}" && buttons[2].CustomId == $"bj_double:77:{viewIn.GameId:N}" && !buttons[2].IsDisabled, "tres botones (Pedir, Plantarse, Doblar) con el dueño y la mano en el id");
var poor = BlackjackModule.BuildComponents(viewIn with { Gold = 100 }, 77)!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Last();
Check(poor.IsDisabled && !BlackjackModule.BuildEmbed(viewIn with { Gold = 100 }).Description.Contains("aa bj double"), "sin oro para igualar la apuesta, Doblar queda deshabilitado y no se ofrece");
var hit1 = Blackjack.Hit(viewIn.State, () => new Card(2, 1));
var viewHit = viewIn with { State = hit1 };
Check(!viewHit.CanDouble && BlackjackModule.BuildComponents(viewHit, 77)!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Last().IsDisabled, "después de pedir una carta ya no se puede doblar");
var viewWin = new BlackjackView(Guid.NewGuid(), won, 11_000);
var embedWin = BlackjackModule.BuildEmbed(viewWin);
Check(BlackjackModule.BuildComponents(viewWin, 77) is null && embedWin.Description.Contains("¡Ganaste!") && embedWin.Description.Contains("(+100)") && embedWin.Description.Contains($"`{string.Join(" ", won.Dealer.Select(c => c.Label))}`") && embedWin.Color == Color.Green, "terminada: sin botones, con la banca entera, el total, el resultado y color verde");
Check(BlackjackModule.BuildEmbed(new BlackjackView(Guid.NewGuid(), nat, 0)).Description.Contains("3 a 2") && BlackjackModule.BuildEmbed(new BlackjackView(Guid.NewGuid(), dealerNat, 0)).Description.Contains("La banca tenía blackjack")
      && BlackjackModule.BuildEmbed(new BlackjackView(Guid.NewGuid(), bust, 0)).Description.Contains("Te pasaste") && BlackjackModule.BuildEmbed(new BlackjackView(Guid.NewGuid(), pushState, 0)).Color == Color.LightGrey
      && BlackjackModule.BuildEmbed(new BlackjackView(Guid.NewGuid(), doubled, 0)).Title!.Contains("doblada"), "cada resultado tiene su texto: blackjack (3 a 2), blackjack de la banca, pasarse, empate y apuesta doblada");
Check(BlackjackModule.BuildEmbed(new BlackjackView(Guid.NewGuid(), busted, 0, "⏰ nota de prueba")).Description.Contains("⏰ nota de prueba"), "la nota (por ejemplo «te plantó la banca») se muestra");

Console.WriteLine("\n== 6. Comandos, historial y DI con el InteractionService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var play = interactions.SlashCommands.Single(c => c.Name == "play");
var gameParam = play.Parameters.Single(p => p.Name == "game");
Check(play.Description.Length <= 100 && play.Description.Contains("Blackjack") && gameParam.Choices.Select(c => c.Value.ToString()).SequenceEqual(["coinflip", "slots", "blackjack"]) && play.Parameters.Single(p => p.Name == "apuesta").Description.Length <= 100, "/play ofrece Blackjack entre los juegos, con sus descripciones dentro de los 100 caracteres");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
string gid = Guid.NewGuid().ToString("N");
var rHit = interactions.SearchComponentCommand(Click($"bj_hit:{U(1)}:{gid}")); var rStand = interactions.SearchComponentCommand(Click($"bj_stand:{U(1)}:{gid}")); var rDouble = interactions.SearchComponentCommand(Click($"bj_double:{U(1)}:{gid}"));
Check(rHit.IsSuccess && rHit.Command.MethodName == "HandleHitAsync" && rHit.RegexCaptureGroups is { Length: 2 } g1 && g1[0] == U(1).ToString() && g1[1] == gid && rStand.IsSuccess && rStand.Command.MethodName == "HandleStandAsync" && rDouble.IsSuccess && rDouble.Command.MethodName == "HandleDoubleAsync",
    "los tres botones llegan a su método con el dueño y la mano");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var aliasCount = textMods.SelectMany(m => m.Commands).SelectMany(c => c.Aliases).GroupBy(a => a.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
Check(new[] { "bj", "blackjack", "21" }.All(a => aliasCount.GetValueOrDefault(a) == 1) && aliasCount.GetValueOrDefault("play") >= 1, "aa bj / blackjack / 21 están registrados sin repetirse, y aa play sigue");
await db.ExecuteAsync("INSERT INTO game_events (discord_id, kind, amount, detail) VALUES (@id, 'casino_win', 300, 'blackjack'), (@id, 'casino_loss', 100, 'blackjack')", new { id = Base + 20 });
var totals = new List<EventTotal> { new("casino_win", "blackjack", 1, 300), new("casino_loss", "blackjack", 1, 100) };
var report = GameHistory.Build(totals);
var bjRecord = report.Games.SingleOrDefault(g => g.Name == "Blackjack");
Check(bjRecord is { IsCasino: true } && bjRecord.Won == 1 && bjRecord.Lost == 1 && bjRecord.GoldWon == 300 && bjRecord.GoldLost == 100, "/history cuenta el blackjack como un juego del casino (con el oro ganado y perdido)");
var playHelp = string.Join("\n", HelpTopics.Find("play")!.Build().Fields.Select(f => f.Name + " " + f.Value));
Check(playHelp.Contains("Blackjack") && playHelp.Contains("3 a 2") && playHelp.Contains($"{BlackjackService.IdleSeconds} segundos") && HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256); }), "/info tema:play explica el blackjack y todos los temas siguen entrando en los límites de Discord");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(x))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split('\n')[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
Check(sp.GetService(typeof(IBlackjackService)) is BlackjackService, "el servicio es uno solo para todo el bot (las manos viven en él)");

await Wipe();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
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
