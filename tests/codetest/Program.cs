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

// v0.37.0: los CÓDIGOS CANJEABLES (`aa code`, `/code`, el panel `aa admincode`). El dueño (2026-10-28): «vamos a ir trabajando en code, `aa code`: un comando nuevo que tenga códigos que se puedan canjear por cosas».
// Lo que se prueba: cómo se escribe un código y cómo se leen los argumentos de create, el freno de intentos, el repositorio contra la base LOCAL (usuarios y códigos descartables: oro, Polvo, EXP con subida de nivel,
// ítems, una vez por persona, tope de personas con 12 canjes a la vez, vencido, apagado, sin cuenta, sin premios), lo que contesta el jugador (los dos idiomas), el panel del administrador (seguridad incluida) y el ruteo
// con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong ADMIN = 999000000000099701UL, P1 = 999000000000099711UL, P2 = 999000000000099712UL, NOACC = 999000000000099799UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] race = Enumerable.Range(0, 12).Select(i => 999000000000099720UL + (ulong)i).ToArray();

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var codes = new RedeemCodeRepository(factory);
var adminRepo = new AdminRepository(factory);

async Task Clean()
{
    await db.ExecuteAsync("DELETE FROM redeem_codes WHERE code LIKE 'TESTV37%'");
    foreach (var id in new[] { ADMIN, P1, P2 }.Concat(race))
    {
        await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

async Task Player(ulong id, int gold = 0, int dust = 0, int level = 1, int xp = 0)
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET gold = @g, dust = @d, level = @l, xp = @x WHERE discord_id = @id", new { id = (long)id, g = gold, d = dust, l = level, x = xp });
}

async Task<int> Have(ulong id, string item) => await db.ExecuteScalarAsync<int>(
    "SELECT COALESCE(SUM(inv.quantity), 0)::int FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = item });
async Task<(int Gold, int Dust, int Level)> Stats(ulong id) => await db.QuerySingleAsync<(int, int, int)>("SELECT gold, dust, level FROM users WHERE discord_id = @id", new { id = (long)id });

RedeemCodeSpec Spec(string code, int? uses, TimeSpan? duration, params RedeemReward[] rewards) => new(code, uses, duration, rewards);
RedeemReward Gold(int n) => new(RedeemRewardKind.Gold, n);
RedeemReward Item(string name, int n) => new(RedeemRewardKind.Item, n, name);

await Clean();

Console.WriteLine("== 1. Cómo se escribe un código ==");
Check(RedeemCodeRules.Normalize("  bienvenida ") == "BIENVENIDA" && RedeemCodeRules.Normalize("Año-Nuevo_2026") == "ANO-NUEVO_2026" && RedeemCodeRules.Normalize("ab") is null && RedeemCodeRules.Normalize(new string('A', 33)) is null
    && RedeemCodeRules.Normalize("con espacio") is null && RedeemCodeRules.Normalize("raro!") is null && RedeemCodeRules.Normalize("") is null && RedeemCodeRules.Normalize(null) is null && RedeemCodeRules.Normalize(new string('A', 32)) is not null,
    "mayúsculas, sin acentos ni espacios alrededor; de 3 a 32 caracteres de A-Z, 0-9, «_» y «-»");

Console.WriteLine("\n== 2. Los argumentos de «aa admincode create» ==");
var ok1 = RedeemCodeRules.ParseCreate("bienvenida 100 7d | gold 5k | item Cofre de Oro 2 | dust 200 | xp 500");
Check(ok1.Spec is { Code: "BIENVENIDA", MaxUses: 100 } s1 && s1.Duration == TimeSpan.FromDays(7) && s1.Rewards.Count == 4
    && s1.Rewards[0] is { Kind: RedeemRewardKind.Gold, Amount: 5000 } && s1.Rewards[1] is { Kind: RedeemRewardKind.Item, Amount: 2, ItemName: "Cofre de Oro" } && s1.Rewards[2] is { Kind: RedeemRewardKind.Dust, Amount: 200 } && s1.Rewards[3] is { Kind: RedeemRewardKind.Xp, Amount: 500 },
    "código + personas + duración + cuatro premios (oro 5k, 2 Cofres de Oro, Polvo, EXP)");
var ok2 = RedeemCodeRules.ParseCreate("SOLOUNO | gold 1m");
Check(ok2.Spec is { MaxUses: null, Duration: null } s2 && s2.Rewards.Single().Amount == 1_000_000, "sin personas ni duración: sin tope y no vence");
var ok3 = RedeemCodeRules.ParseCreate("HORAS 12h 50 | item Mate Amargo");
Check(ok3.Spec is { MaxUses: 50 } s3 && s3.Duration == TimeSpan.FromHours(12) && s3.Rewards.Single() is { ItemName: "Mate Amargo", Amount: 1 }, "la duración y las personas van en cualquier orden; un ítem sin cantidad es 1");
string?[] bad =
[
    RedeemCodeRules.ParseCreate("").Error, RedeemCodeRules.ParseCreate("ab | gold 5").Error, RedeemCodeRules.ParseCreate("CODIGO").Error, RedeemCodeRules.ParseCreate("CODIGO |").Error,
    RedeemCodeRules.ParseCreate("CODIGO raro | gold 5").Error, RedeemCodeRules.ParseCreate("CODIGO 0 | gold 5").Error, RedeemCodeRules.ParseCreate("CODIGO 400d | gold 5").Error, RedeemCodeRules.ParseCreate("CODIGO 2000000 | gold 5").Error,
    RedeemCodeRules.ParseCreate("CODIGO | gold").Error, RedeemCodeRules.ParseCreate("CODIGO | cosa 5").Error, RedeemCodeRules.ParseCreate("CODIGO | gold 5 <@123>").Error,
    RedeemCodeRules.ParseCreate("CODIGO | " + string.Join(" | ", Enumerable.Repeat("gold 1", 7))).Error,
];
Check(bad.All(e => !string.IsNullOrWhiteSpace(e)), $"{bad.Length} entradas inválidas (vacío, código corto, sin premios, símbolo raro, 0 personas, más de un año, premio que no se entiende, con mención, 7 premios) se rechazan con un motivo");

Console.WriteLine("\n== 3. El freno de intentos ==");
var clock = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
var limiter = new RedeemAttemptLimiter(() => clock);
for (int i = 0; i < 4; i++) limiter.RegisterFailure(P1);
Check(limiter.LockedFor(P1) is null, "4 fallos todavía no bloquean");
limiter.RegisterFailure(P1);
Check(limiter.LockedFor(P1) is { } w && w > TimeSpan.FromMinutes(9) && limiter.LockedFor(P2) is null, "el 5.º bloquea 10 minutos (y a otro jugador no le pasa nada)");
clock += TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);
Check(limiter.LockedFor(P1) is null, "pasados los 10 minutos puede volver a probar");
for (int i = 0; i < 4; i++) limiter.RegisterFailure(P2);
clock += TimeSpan.FromMinutes(11);
limiter.RegisterFailure(P2);
Check(limiter.LockedFor(P2) is null, "los fallos viejos (fuera de la ventana de 10 minutos) no suman");
limiter.RegisterFailure(P2); limiter.RegisterFailure(P2); limiter.RegisterFailure(P2); limiter.Clear(P2); limiter.RegisterFailure(P2);
Check(limiter.LockedFor(P2) is null, "un canje que salió bien borra los fallos");

Console.WriteLine("\n== 4. El repositorio: crear ==");
var created = await codes.CreateAsync(Spec("TESTV37A", 3, null, Gold(500), Item("Cajón de Pino", 2)), ADMIN);
var dup = await codes.CreateAsync(Spec("TESTV37A", 3, null, Gold(1)), ADMIN);
var missing = await codes.CreateAsync(Spec("TESTV37B", null, null, Item("Ítem que no existe", 1)), ADMIN);
Check(created.Status == CreateCodeStatus.Created && dup.Status == CreateCodeStatus.Duplicate && missing.Status == CreateCodeStatus.ItemMissing && await codes.GetAsync("TESTV37B") is null, "se crea; el repetido y el ítem que no existe se rechazan sin dejar nada a medias");
var info = await codes.GetAsync("TESTV37A");
Check(info is { MaxUses: 3, Uses: 0, Active: true, ExpiresAt: null } && info.Rewards.Count == 2 && info.CreatedBy == ADMIN, "se lee con sus dos premios, sin canjes y activo");

Console.WriteLine("\n== 5. El repositorio: canjear ==");
await Player(P1, gold: 100, dust: 5);
var r1 = await codes.RedeemAsync(P1, "TESTV37A");
var st = await Stats(P1);
Check(r1.Status == RedeemStatus.Redeemed && r1.Rewards.Count == 2 && st.Gold == 600 && await Have(P1, "Cajón de Pino") == 2, "paga el oro (100 + 500) y los 2 Cajones de Pino a la mochila");
Check((await codes.RedeemAsync(P1, "TESTV37A")).Status == RedeemStatus.AlreadyRedeemed && (await Stats(P1)).Gold == 600 && (await codes.GetAsync("TESTV37A"))!.Uses == 1, "la misma persona no lo canjea dos veces (y no se cobra otro uso)");
Check((await codes.RedeemAsync(P1, "TESTV37NOEXISTE")).Status == RedeemStatus.UnknownCode && (await codes.RedeemAsync(NOACC, "TESTV37A")).Status == RedeemStatus.NoAccount
    && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM users WHERE discord_id = @id", new { id = (long)NOACC }) == 0, "un código que no existe y un jugador sin cuenta: se rechaza (y no se crea la cuenta)");

await Player(P2, level: 1, xp: 0);
await codes.CreateAsync(Spec("TESTV37MIX", null, null, Gold(1_000), new RedeemReward(RedeemRewardKind.Dust, 70), new RedeemReward(RedeemRewardKind.Xp, 5_000)), ADMIN);
var mix = await codes.RedeemAsync(P2, "TESTV37MIX");
var st2 = await Stats(P2);
Check(mix.Status == RedeemStatus.Redeemed && st2.Gold == 1_000 && st2.Dust == 70 && mix.LevelsGained > 0 && st2.Level == mix.NewLevel && st2.Level > 1, $"oro, Polvo y EXP en un mismo código: la EXP sube de nivel ({mix.LevelsGained} niveles, nivel {st2.Level})");

await Player(P1, gold: int.MaxValue - 10);
await codes.CreateAsync(Spec("TESTV37TOPE", null, null, Gold(1_000)), ADMIN);
var capped = await codes.RedeemAsync(P1, "TESTV37TOPE");
Check(capped.Status == RedeemStatus.Redeemed && capped.Capped && (await Stats(P1)).Gold == int.MaxValue, "un total enorme se recorta en el tope de la columna en vez de fallar");

await codes.CreateAsync(Spec("TESTV37OFF", null, null, Gold(5)), ADMIN);
await codes.SetActiveAsync("TESTV37OFF", false);
await codes.CreateAsync(Spec("TESTV37VENCE", null, TimeSpan.FromHours(1), Gold(5)), ADMIN);
await db.ExecuteAsync("UPDATE redeem_codes SET expires_at = now() - interval '1 minute' WHERE code = 'TESTV37VENCE'");
await codes.CreateAsync(Spec("TESTV37UNO", 1, null, Gold(5)), ADMIN);
await codes.CreateAsync(Spec("TESTV37VACIO", null, null, Item("Cajón de Pino", 1)), ADMIN);
await db.ExecuteAsync("DELETE FROM redeem_code_rewards WHERE code_id = (SELECT code_id FROM redeem_codes WHERE code = 'TESTV37VACIO')");
await Player(P1, gold: 0); await Player(P2, gold: 0);
Check((await codes.RedeemAsync(P1, "TESTV37OFF")).Status == RedeemStatus.Inactive && (await codes.RedeemAsync(P1, "TESTV37VENCE")).Status == RedeemStatus.Expired && (await codes.RedeemAsync(P1, "TESTV37VACIO")).Status == RedeemStatus.NoRewards,
    "apagado, vencido y sin premios: cada uno con su estado");
Check((await codes.RedeemAsync(P1, "TESTV37UNO")).Status == RedeemStatus.Redeemed && (await codes.RedeemAsync(P2, "TESTV37UNO")).Status == RedeemStatus.Exhausted && (await Stats(P2)).Gold == 0, "con un tope de 1 persona, la segunda se queda sin nada (agotado)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM redeem_code_claims cl JOIN redeem_codes c ON c.code_id = cl.code_id WHERE c.code IN ('TESTV37OFF', 'TESTV37VENCE', 'TESTV37VACIO')") == 0, "los intentos rechazados no dejan canje anotado (el sin premios no quema el canje de nadie)");
await codes.SetActiveAsync("TESTV37OFF", true);
await Player(P1, gold: 0);
Check((await codes.RedeemAsync(P1, "TESTV37OFF")).Status == RedeemStatus.Redeemed && !await codes.SetActiveAsync("TESTV37NOEXISTE", true), "prenderlo de nuevo lo deja canjeable; prender uno que no existe da falso");

Console.WriteLine("\n== 6. Carreras ==");
await codes.CreateAsync(Spec("TESTV37CUPO", 5, null, Gold(100)), ADMIN);
foreach (var id in race) await Player(id, gold: 0);
var results = await Task.WhenAll(race.Select(id => codes.RedeemAsync(id, "TESTV37CUPO")));
int paid = 0; foreach (var id in race) paid += (await Stats(id)).Gold;
Check(results.Count(r => r.Status == RedeemStatus.Redeemed) == 5 && results.Count(r => r.Status == RedeemStatus.Exhausted) == 7 && paid == 500 && (await codes.GetAsync("TESTV37CUPO"))!.Uses == 5,
    "12 personas a la vez con un cupo de 5: canjean exactamente 5 (se pagan 500 de oro en total, ni uno más) y las otras 7 ven «agotado»");
await codes.CreateAsync(Spec("TESTV37DOBLE", null, null, Gold(100)), ADMIN);
await Player(P1, gold: 0);
var twice = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => codes.RedeemAsync(P1, "TESTV37DOBLE")));
Check(twice.Count(r => r.Status == RedeemStatus.Redeemed) == 1 && twice.Count(r => r.Status == RedeemStatus.AlreadyRedeemed) == 7 && (await Stats(P1)).Gold == 100, "8 pedidos de la MISMA persona a la vez: se paga una sola vez");

Console.WriteLine("\n== 7. Lo que ve el jugador ==");
var events = new RecordingEvents();
var lim2 = new RedeemAttemptLimiter();
await codes.CreateAsync(Spec("TESTV37PREMIO", null, null, Gold(2_500), Item("Cajón de Pino", 3), new RedeemReward(RedeemRewardKind.Xp, 5_000)), ADMIN);
await Player(P2, level: 1, xp: 0);
var hit = await CodeHandler.RedeemAsync(codes, lim2, events, P2, " testv37premio ");
string embedText = hit.Embed is null ? "" : hit.Embed.Title + "\n" + hit.Embed.Description + "\n" + (hit.Embed.Footer?.Text ?? "");
Check(hit.PlainMessage is null && hit.Embed is not null && hit.Embed.Title == "🎟️ ¡Código canjeado!" && embedText.Contains("TESTV37PREMIO") && embedText.Contains("**2.500** de oro") && embedText.Contains("3×") && embedText.Contains("Cajón de Pino")
    && embedText.Contains("de EXP") && embedText.Contains("¡Subiste de nivel!") && hit.Embed.Footer?.Text == "📦 Abrí tus cajas con /open", "el cartel: título, el código, oro, la caja con su cantidad, la EXP, la subida de nivel y el pie de /open");
Check(events.Kinds.Contains(GameEventKinds.CodeRedeem) && events.Kinds.Contains(GameEventKinds.LevelUp), "queda el evento code_redeem y, por la subida de nivel, level_up (que dispara el cartel dorado)");
var again = await CodeHandler.RedeemAsync(codes, lim2, events, P2, "TESTV37PREMIO");
var weird = await CodeHandler.RedeemAsync(codes, lim2, events, P2, "ab!");
var noAcct = await CodeHandler.RedeemAsync(codes, lim2, events, NOACC, "TESTV37PREMIO");
Check(again.PlainMessage == "Ya canjeaste ese código." && weird.PlainMessage!.Contains("no parece un código") && noAcct.PlainMessage!.Contains("/start"), "ya canjeado, algo que no parece un código y sin cuenta: cada uno con su mensaje");
for (int i = 0; i < 4; i++) await CodeHandler.RedeemAsync(codes, lim2, events, P1, $"TESTV37FALSO{i}");
var fifth = await CodeHandler.RedeemAsync(codes, lim2, events, P1, "TESTV37FALSO4");
var blocked = await CodeHandler.RedeemAsync(codes, lim2, events, P1, "TESTV37CUPO");
Check(fifth.PlainMessage!.Contains("no existe") && blocked.PlainMessage!.Contains("esperá") && blocked.PlainMessage.Contains("10"), "tras 5 códigos que no existen, ni siquiera uno bueno se prueba: «esperá 10 min»");
var allowed = new[] { "Cajón de Pino", "Pino" };
using (Loc.Use("en"))
{
    await Player(P2, level: 1, xp: 0);
    await codes.CreateAsync(Spec("TESTV37EN", null, null, Gold(1_500), Item("Cajón de Pino", 1)), ADMIN);
    var en = await CodeHandler.RedeemAsync(codes, new RedeemAttemptLimiter(), events, P2, "TESTV37EN");
    string enText = en.Embed is null ? "" : en.Embed.Title + "\n" + en.Embed.Description + "\n" + en.Embed.Footer?.Text;
    var enAgain = await CodeHandler.RedeemAsync(codes, new RedeemAttemptLimiter(), events, P2, "TESTV37EN");
    var enNone = await CodeHandler.RedeemAsync(codes, new RedeemAttemptLimiter(), events, P2, "TESTV37NOPE");
    Check(enText.Contains("Code redeemed!") && enText.Contains("**1,500** gold") && enAgain.PlainMessage == "You already redeemed that code." && enNone.PlainMessage!.StartsWith("That code does not exist")
        && LeakCheck.NoSpanish(enText + "\n" + enAgain.PlainMessage + "\n" + enNone.PlainMessage, allowed).Ok, $"en inglés, sin fugas de español ({LeakCheck.NoSpanish(enText, allowed).Found})");
}

Console.WriteLine("\n== 8. El panel del administrador ==");
var adminAccess = new AdminAccess(ADMIN.ToString());
var noAdmins = new AdminAccess(null);
var evA = new RecordingEvents();
Task<string?> Panel(ulong who, string args, IAdminAccess? access = null) => CodeHandler.AdminAsync(access ?? adminAccess, codes, items, adminRepo, evA, who, args);
Check(await Panel(P1, "list") is null && (await Panel(P1, "create ZZZZ | gold 1")) is null && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM redeem_codes WHERE code = 'ZZZZ'") == 0, "quien no es administrador NO recibe respuesta y no crea nada");
Check((await Panel(P1, "list", noAdmins))!.Contains("Admin__UserIds"), "sin administradores configurados dice qué variable falta");
var made = await Panel(ADMIN, "create testv37adm 2 3d | gold 7k | item cajon de pino 2");
Check(made!.Contains("TESTV37ADM") && made.Contains("hasta 2 personas") && made.Contains("7.000 de oro") && made.Contains("2× Cajón de Pino") && made.Contains("aa code TESTV37ADM"), "create: crea el código con el nombre del ítem del catálogo (aunque lo escriba sin acentos) y dice cómo se canjea");
Check((await Panel(ADMIN, "create TESTV37ADM | gold 1"))!.Contains("Ya existe") && (await Panel(ADMIN, "create TESTV37X | item Cosa Rara"))!.Contains("No encontré") && (await Panel(ADMIN, "create TESTV37X | item Espada de Madera"))!.Contains("equipo")
    && (await Panel(ADMIN, "create TESTV37X | item Hacha de Hierro Forjado"))!.Contains("equipo o una herramienta") && (await Panel(ADMIN, "create TESTV37X"))!.Contains("Faltan los premios"), "el repetido, un ítem que no existe, el equipo y las herramientas, y un create sin premios se rechazan con su motivo");
var list = await Panel(ADMIN, "list");
var det = await Panel(ADMIN, "info TESTV37ADM");
Check(list!.Contains("TESTV37ADM") && list.Contains("0/2 canjes") && det!.Contains("7.000 de oro") && det.Contains($"<@{ADMIN}>"), "list e info muestran los canjes, el vencimiento, el estado y los premios");
await Player(P1, gold: 0);
await CodeHandler.RedeemAsync(codes, new RedeemAttemptLimiter(), events, P1, "TESTV37ADM");
Check((await Panel(ADMIN, "info TESTV37ADM"))!.Contains("1/2 canjes"), "después de un canje, info dice 1/2");
Check((await Panel(ADMIN, "off TESTV37ADM"))!.Contains("apagado") && (await CodeHandler.RedeemAsync(codes, new RedeemAttemptLimiter(), events, P2, "TESTV37ADM")).PlainMessage == "Ese código ya no está disponible."
    && (await Panel(ADMIN, "on TESTV37ADM"))!.Contains("activo") && (await Panel(ADMIN, "off NOEXISTE9"))!.Contains("No encontré"), "off lo frena para todos, on lo vuelve a abrir, y uno que no existe lo dice");
Check(evA.Kinds.Count(k => k == GameEventKinds.AdminCode) == 3 && (await Panel(ADMIN, "algo raro"))!.Contains("Uso:"), "queda el rastro admin_code de crear, apagar y prender; un subcomando desconocido muestra el uso");

Console.WriteLine("\n== 9. Ruteo con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(CodeModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(CodeModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "code");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Single().Name == "codigo", $"/code está registrado con su parámetro «codigo» y su descripción entra en 100 caracteres ({slash.Description.Length})");
var textCommands = new CommandService();
await textCommands.AddModuleAsync<TextCommandModule>(sp);
bool aliasesOk = new[] { "code", "codigo", "código", "redeem", "cupon", "cupón" }.All(a => textCommands.Search($"{a} X") is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "code");
Check(aliasesOk && textCommands.Search("canjear sword") is { IsSuccess: true } ex && ex.Commands[0].Command.Name == "exchange" && textCommands.Search("admincode list") is { IsSuccess: true } ad && ad.Commands[0].Command.Name == "admincode"
    && textCommands.Search("admincode list").Commands[0].Command.Summary is null, "aa code y sus alias llegan a su comando, «canjear» sigue siendo del canje de drops y aa admincode existe sin descripción (no figura en ninguna lista)");
Check(!FightLock.IsAllowed("code") && FightLock.IsAllowed("use"), "en plena pelea /code se bloquea (solo valen los comandos de la pelea y los que miran)");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
