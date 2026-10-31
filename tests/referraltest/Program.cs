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

// v0.43.0: INVITAR AMIGOS (`aa invitedby @amigo`, `/invitedby`). El dueño (2026-10-31): una vez por cuenta, cualquiera puede; 10k para el invitado, 20k para el que invitó y 80k más cuando el invitado
// llega al nivel 5 (en el momento si ya pasó el nivel 5 o hizo un Fuego Nuevo).
// Lo que se prueba: la regla pura, el repositorio contra la base LOCAL (usuarios descartables: los pagos, una vez por cuenta, el nivel 5 en el momento o después, 10 anotaciones a la vez del mismo invitado,
// 8 subidas de nivel a la vez), el pago del nivel 5 a través del servicio de eventos (con sus avisos), lo que contesta el comando (los dos idiomas), el tutorial, /start, /info y el ruteo con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
const ulong A = 999000000000043001UL, N = 999000000000043002UL, OLD = 999000000000043003UL, FN = 999000000000043004UL, N2 = 999000000000043005UL, NOACC = 999000000000043099UL;
ulong[] race = Enumerable.Range(0, 10).Select(i => 999000000000043010UL + (ulong)i).ToArray();
const ulong RACED = 999000000000043030UL, LVL = 999000000000043031UL;
ulong[] all = new[] { A, N, OLD, FN, N2, RACED, LVL }.Concat(race).ToArray();

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
await db.ExecuteAsync(File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database", "add_referrals.sql")));
var users = new UserRepository(factory);
var referrals = new ReferralRepository(factory);

async Task Clean()
{
    var ids = all.Select(i => (long)i).ToArray();
    await db.ExecuteAsync("DELETE FROM referrals WHERE invitee_id = ANY(@ids) OR inviter_id = ANY(@ids)", new { ids });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = ANY(@ids)", new { ids });
    await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = ANY(@ids)", new { ids });
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = ANY(@ids)", new { ids });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids)", new { ids });
}

async Task Player(ulong id, int level = 1, int fuegoNuevo = 0)
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET gold = 0, level = @l, fuego_nuevo = @f WHERE discord_id = @id", new { id = (long)id, l = level, f = fuegoNuevo });
}

async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
async Task SetLevel(ulong id, int level) => await db.ExecuteAsync("UPDATE users SET level = @l WHERE discord_id = @id", new { id = (long)id, l = level });

await Clean();

Console.WriteLine("== 1. La regla ==");
Check(!ReferralRules.ReachedGoal(1, 0) && !ReferralRules.ReachedGoal(4, 0) && ReferralRules.ReachedGoal(5, 0) && ReferralRules.ReachedGoal(40, 0) && ReferralRules.ReachedGoal(1, 1),
    "llegó = nivel 5 o más, o al menos un Fuego Nuevo (que vuelve el nivel a 1)");
Check(ReferralRules.NewPlayerGold == 10_000 && ReferralRules.InviterStartGold == 20_000 && ReferralRules.InviterLevelGold == 80_000 && ReferralRules.RequiredLevel == 5, "los números del dueño: 10k, 20k y 80k al nivel 5");

Console.WriteLine("\n== 2. El repositorio contra la base ==");
await Player(A, level: 20); await Player(N); await Player(OLD, level: 12); await Player(FN, level: 1, fuegoNuevo: 2);
var r1 = await referrals.RegisterAsync(N, A);
Check(r1.Status == ReferralStatus.Registered && r1.NewPlayerGold == 10_000 && r1.InviterGold == 20_000 && !r1.LevelBonusPaid && await Gold(N) == 10_000 && await Gold(A) == 20_000,
    "una cuenta nueva anota quién la invitó: ella cobra 10k y el que invitó 20k (el nivel 5 queda pendiente)");
var r2 = await referrals.RegisterAsync(N, OLD);
Check(r2.Status == ReferralStatus.AlreadyRegistered && r2.InviterId == A && await Gold(N) == 10_000 && await Gold(OLD) == 0, "una segunda vez no paga nada y dice a quién ya había anotado");
var r3 = await referrals.RegisterAsync(OLD, A);
Check(r3.Status == ReferralStatus.Registered && r3.InviterGold == 100_000 && r3.LevelBonusPaid && await Gold(OLD) == 10_000 && await Gold(A) == 120_000,
    "un jugador que ya pasó el nivel 5 también puede: el que invitó cobra los 20k + 80k en el momento (100k)");
var r4 = await referrals.RegisterAsync(FN, A);
Check(r4.Status == ReferralStatus.Registered && r4.InviterGold == 100_000 && r4.LevelBonusPaid && await Gold(A) == 220_000, "uno con Fuego Nuevo (nivel 1 de nuevo) cuenta como que ya llegó: 100k en el momento");
var mutual = await referrals.RegisterAsync(A, N);
Check(mutual.Status == ReferralStatus.Registered && mutual.InviterGold == 100_000 && await Gold(N) == 110_000 && await Gold(A) == 230_000, "cualquiera puede, también al revés (A, de nivel 20, anota a N: N cobra los 100k): cada cuenta lo hace una vez");
Check((await referrals.RegisterAsync(NOACC, A)).Status == ReferralStatus.NoAccount && (await referrals.RegisterAsync(N, NOACC)).Status == ReferralStatus.InviterNoAccount
    && await db.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE discord_id = @id", new { id = (long)NOACC }) == 0, "sin cuenta (el invitado o el que invitó) no anota nada y no crea cuentas");

Check(await referrals.PayLevelBonusAsync(N) is null && await Gold(A) == 230_000, "el invitado en nivel 1: el premio del nivel 5 todavía no se paga");
await SetLevel(N, 5);
var paid = await referrals.PayLevelBonusAsync(N);
Check(paid == A && await Gold(A) == 310_000 && await referrals.PayLevelBonusAsync(N) is null && await Gold(A) == 310_000, "al llegar al nivel 5 el que invitó cobra 80k, y una sola vez");
Check(await referrals.PayLevelBonusAsync(OLD) is null && await referrals.PayLevelBonusAsync(LVL) is null, "al que ya cobró los 100k no se le vuelve a pagar, y alguien sin invitación no paga nada");
var sumA = await referrals.GetSummaryAsync(A);
var sumN = await referrals.GetSummaryAsync(N);
Check(sumA.InvitedBy == N && sumA.Invited == 3 && sumA.ReachedLevel == 3 && sumN.InvitedBy == A && sumN.Invited == 1 && sumN.ReachedLevel == 1, "el resumen: quién te invitó, a cuántos invitaste y cuántos llegaron al nivel 5");

Console.WriteLine("\n== 3. Carreras ==");
await Player(RACED);
foreach (var id in race) await Player(id);
var results = await Task.WhenAll(race.Select(id => referrals.RegisterAsync(RACED, id)));
int winners = results.Count(r => r.Status == ReferralStatus.Registered);
int paidOut = 0; foreach (var id in race) paidOut += await Gold(id);
Check(winners == 1 && results.Count(r => r.Status == ReferralStatus.AlreadyRegistered) == 9 && paidOut == 20_000 && await Gold(RACED) == 10_000, $"10 anotaciones a la vez del mismo invitado: entra una sola y se paga una vez ({winners} y {paidOut} de oro)");
ulong racedInviter = results.Single(r => r.Status == ReferralStatus.Registered).InviterId!.Value;
await SetLevel(RACED, 6);
var pays = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => referrals.PayLevelBonusAsync(RACED)));
Check(pays.Count(p => p is not null) == 1 && await Gold(racedInviter) == 100_000, "8 subidas de nivel a la vez: el premio del nivel 5 se paga una vez");

Console.WriteLine("\n== 4. El nivel 5 a través del servicio de eventos ==");
var events = new GameEventService(new GameEventRepository(factory), null, referrals);
await Player(N2);
await referrals.RegisterAsync(N2, OLD);
int oldBefore = await Gold(OLD);
await events.RecordAsync(N2, GameEventKinds.LevelUp, amount: 1, detail: "2");
Check(await Gold(OLD) == oldBefore && events.TakeNotices(OLD).Count == 0, "subir al nivel 2 no paga nada");
await SetLevel(N2, 5);
await events.RecordAsync(N2, GameEventKinds.LevelUp, amount: 3, detail: "5");
var toInviter = events.TakeNotices(OLD);
var toInvitee = events.TakeNotices(N2);
Check(await Gold(OLD) == oldBefore + 80_000 && toInviter.Count == 1 && toInviter[0].Embed!.Description.Contains($"<@{N2}>") && toInviter[0].Embed!.Description.Contains("80.000")
    && toInvitee.Count == 1 && toInvitee[0].Embed!.Description.Contains($"<@{OLD}>"), "el level_up del nivel 5 le paga 80k al que invitó y les avisa a los dos (al que invitó, con su próximo comando)");
await events.RecordAsync(N2, GameEventKinds.LevelUp, amount: 1, detail: "6");
Check(await Gold(OLD) == oldBefore + 80_000 && events.TakeNotices(OLD).Count == 0, "las subidas siguientes no vuelven a pagar");
Check(await db.ExecuteScalarAsync<long>("SELECT count(*) FROM game_events WHERE discord_id = @id AND kind = 'referral_level' AND detail = @d", new { id = (long)N2, d = OLD.ToString() }) == 1,
    "queda el evento referral_level en el invitado, con el ID del que invitó");
var noReferrals = new GameEventService(new GameEventRepository(factory));
await noReferrals.RecordAsync(N2, GameEventKinds.LevelUp, amount: 1, detail: "7");
Check(true, "el servicio sin repositorio de invitaciones (como lo arman las pruebas viejas) sigue andando");

Console.WriteLine("\n== 5. Lo que contesta el comando ==");
await Clean();
await Player(A, level: 20); await Player(N);
var rec = new RecordingEvents();
async Task<ReferralReply> Run(ulong who, ulong? friend, bool bot = false) => await ReferralModule.ExecuteAsync(users, referrals, rec, who, friend, bot);
string Text(ReferralReply r) => LeakCheck.AllText(r.Embed);
Check(Text(await Run(NOACC, A)).Contains("/start") && Text(await Run(N, A, bot: true)).Contains("bot") && Text(await Run(N, N)).Contains("vos mismo") && Text(await Run(N, NOACC)).Contains("todavía no juega"),
    "sin cuenta, un bot, a uno mismo y alguien que no juega: cada uno con su mensaje y sin pagar nada");
Check(await Gold(N) == 0 && await Gold(A) == 0 && rec.Kinds.Count == 0, "ninguno de esos movió oro ni registró eventos");
var empty = await Run(N, null);
Check(empty.Mention is null && Text(empty).Contains("Nadie todavía") && Text(empty).Contains("aa invitedby @amigo"), "sin nombrar a nadie muestra el resumen y cómo se usa");
var ok = await Run(N, A);
Check(ok.Mention == $"<@{A}>" && Text(ok).Contains("10.000") && Text(ok).Contains("20.000") && Text(ok).Contains("80.000") && LeakCheck.FitsDiscord(ok.Embed) && rec.Kinds.SequenceEqual([GameEventKinds.Referral]),
    "anotado: avisa con un ping al que invitó, dice lo que cobró cada uno y lo del nivel 5, y registra el evento referral");
Check(Text(await Run(N, A)).Contains("una sola vez") && await Gold(N) == 10_000, "la segunda vez dice que ya lo anotó y no paga");
var summary = await Run(A, null);
Check(Text(summary).Contains("**1** jugadores") && LeakCheck.FitsDiscord(summary.Embed), "el que invitó ve a cuántos invitó");
await Player(OLD, level: 30);
var oldReply = await Run(OLD, A);
Check(Text(oldReply).Contains("100.000") && Text(oldReply).Contains("ya va incluido"), "si el invitado ya pasó el nivel 5, la pantalla dice que los 80k ya van incluidos (100k)");
using (Loc.Use("en"))
{
    await Player(FN);
    var en = await Run(FN, A);
    var enSum = await Run(FN, null);
    var enAgain = await Run(FN, A);
    string all3 = Text(en) + "\n" + Text(enSum) + "\n" + Text(enAgain) + "\n" + HelpModule.BuildTutorialEmbed().Fields.Last().Value;
    var leak = LeakCheck.NoSpanish(all3, []);
    Check(Text(en).Contains("10,000") && leak.Ok, $"en inglés, sin fugas de español ({leak.Found})");
}

Console.WriteLine("\n== 6. Tutorial, /start, /info y el ruteo ==");
var tutorial = HelpModule.BuildTutorialEmbed();
Check(tutorial.Fields.Any(f => f.Value.Contains("aa invitedby @amigo")) && LeakCheck.FitsDiscord(tutorial), "el tutorial termina con «¿Te invitó un amigo?»");
Check(HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("/invitedby")), "/info nombra /invitedby");
var confirm = (Embed)typeof(OnboardingModule).GetMethod("BuildConfirmationEmbed", AnyS)!.Invoke(null, ["Prueba", (await users.GetByDiscordIdAsync(N))!, ClassCatalog.All[0], true])!;
Check(confirm.Fields.Any(f => f.Value.Contains("aa invitedby")), "la pantalla de /start de una cuenta nueva también lo dice");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(ReferralModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(ReferralModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "invitedby");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Single().Name == "amigo" && !slash.Parameters.Single().IsRequired, $"/invitedby está registrado con «amigo» opcional y su descripción entra en 100 caracteres ({slash.Description.Length})");
var textCommands = new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false });
await textCommands.AddModuleAsync<TextCommandModule>(sp);
bool aliasesOk = new[] { "invitedby", "invited", "invitado", "meinvito", "referido" }.All(a => textCommands.Search($"{a} @x") is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "invitedby");
Check(aliasesOk, "aa invitedby y sus alias llegan a su comando, sin chocar con ningún otro");
Check(sp.GetService(typeof(IGameEvents)) is GameEventService && sp.GetService(typeof(IReferralRepository)) is ReferralRepository, "el bot arma el servicio de eventos y el repositorio de invitaciones");
Check(!FightLock.IsAllowed("invitedby"), "en plena pelea /invitedby se bloquea, como todo lo que no es de la pelea");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
