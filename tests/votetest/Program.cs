using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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

// v0.18.0: /vote (aa vote). Reglas puras y el lector de la respuesta de top.gg, el cliente contra un servidor falso (ruta, token, cache, caídas, timeout, que el token no se imprima),
// el cobro atómico contra la base real (usuario descartable, 20 cobros a la vez), la pantalla, /cd, los recordatorios, los comandos con el InteractionService real, la ayuda y la DI.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var cooldowns = new CooldownRepository(factory);
var voteRepo = new VoteRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000050000UL, U2 = 999000000000050001UL;
var eventsRepo = new GameEventRepository(factory);
var rec = new GameEventService(eventsRepo);
const string Box = "Baúl de Roble";

async Task Reset(ulong id = U)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    await users.GetOrCreateUserAsync(id);
}
async Task<int> Have(string name, ulong id = U) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = name });
async Task<long> Stat(string kind, ulong id = U) => (await eventsRepo.GetStatsAsync(id)).GetValueOrDefault(kind);
async Task<bool> HasRow(string cmd, ulong id = U) => await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM cooldowns WHERE discord_id = @id AND command_name = @c)", new { id = (long)id, c = cmd });
async Task SetBoss(TimeSpan ago, ulong id = U) => await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'boss', now() - @ago) ON CONFLICT (discord_id, command_name) DO UPDATE SET last_executed_at = EXCLUDED.last_executed_at", new { id = (long)id, ago });
DateTime Ms(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

Console.WriteLine("== 1. Reglas puras y el lector de top.gg ==");
Check(VoteRules.RewardBoxName == Box && VoteRules.RewardBoxes == 1 && VoteRules.Interval == TimeSpan.FromHours(12), "el premio es 1 Baúl de Roble (lo que pidió el dueño) y se vota cada 12 horas");
Check(CooldownCatalog.Vote.CommandName == "vote" && CooldownCatalog.Vote.Duration == VoteRules.Interval && !CooldownCatalog.All.Contains(CooldownCatalog.Vote), "el cooldown 'vote' dura lo mismo y NO está en la lista fija de /cd (solo aparece con el voto habilitado)");
var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
var ok = TopGgVoteParser.Parse(200, """{"created_at":"2026-10-16T10:00:00.123Z","expires_at":"2026-10-16T22:00:00.123Z","weight":2}""", now);
Check(ok.Status == VoteCheckStatus.Active && ok.CreatedUtc == new DateTime(2026, 10, 16, 10, 0, 0, 123, DateTimeKind.Utc) && ok.ExpiresUtc == new DateTime(2026, 10, 16, 22, 0, 0, 123, DateTimeKind.Utc) && ok.Weight == 2 && ok.CreatedUtc!.Value.Kind == DateTimeKind.Utc, "200 con un voto vigente: lee las fechas (UTC, con milisegundos) y el peso");
var off = TopGgVoteParser.Parse(200, """{"created_at":"2026-10-16T07:00:00-03:00","expires_at":"2026-10-16T19:00:00-03:00"}""", now);
Check(off.Status == VoteCheckStatus.Active && off.CreatedUtc == new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc) && off.Weight == 1, "una fecha con zona horaria se pasa a UTC y sin peso vale 1");
var noExp = TopGgVoteParser.Parse(200, """{"created_at":"2026-10-16T10:00:00Z"}""", now);
Check(noExp.Status == VoteCheckStatus.Active && noExp.ExpiresUtc is null, "sin expires_at igual es un voto vigente (el vencimiento se calcula)");
Check(TopGgVoteParser.Parse(200, """{"created_at":"2026-10-16T01:00:00Z","expires_at":"2026-10-16T11:59:59Z"}""", now).Status == VoteCheckStatus.NoActiveVote, "con el vencimiento ya pasado no es un voto vigente");
Check(TopGgVoteParser.Parse(200, """{"created_at":"2026-10-16T10:00:00Z","weight":0}""", now).Weight == 1, "un peso 0 o raro se lee como 1");
Check(TopGgVoteParser.Parse(404, "{}", now).Status == VoteCheckStatus.NoActiveVote && TopGgVoteParser.Parse(404, null, now).Status == VoteCheckStatus.NoActiveVote, "404 = nunca votó o el voto venció");
Check(new[] { 400, 401, 403, 429, 500, 502, 503, 0 }.All(c => TopGgVoteParser.Parse(c, """{"created_at":"2026-10-16T10:00:00Z"}""", now).Status == VoteCheckStatus.Unavailable), "cualquier otro código (token malo, límite, caída) = no se pudo saber");
Check(new string?[] { null, "", "   ", "no es json", "[]", "{}", """{"expires_at":"2026-10-16T22:00:00Z"}""", """{"created_at":"cuando sea"}""", """{"created_at":123}""" }.All(b => TopGgVoteParser.Parse(200, b, now).Status == VoteCheckStatus.Unavailable), "un 200 con cuerpo vacío, roto o sin created_at no se cobra (Unavailable)");
Check(VoteRules.NextVoteIn(now, now.AddHours(-2), null) == TimeSpan.FromHours(10) && VoteRules.NextVoteIn(now, now.AddHours(-2), now.AddHours(7)) == TimeSpan.FromHours(7) && VoteRules.NextVoteIn(now, now.AddHours(-13), null) is null && VoteRules.NextVoteIn(now, now.AddHours(-1), now.AddMinutes(-1)) is null,
    "cuánto falta para votar de nuevo: lo que dice top.gg, o 12 h después del voto; null si ya se puede");

Console.WriteLine("\n== 2. Configuración (variables de entorno) ==");
void SetEnv(string? token, string? url, string? api) { Environment.SetEnvironmentVariable("TopGg__Token", token); Environment.SetEnvironmentVariable("TopGg__VoteUrl", url); Environment.SetEnvironmentVariable("TopGg__ApiBase", api); }
SetEnv(null, null, null);
var s0 = VoteSettings.FromEnvironment();
Check(!s0.Enabled && s0.VoteUrl is null && s0.ApiBase == "https://top.gg/api/v1", "sin variables: dormido, sin link y con la API real por defecto");
SetEnv("   ", "", null);
Check(!VoteSettings.FromEnvironment().Enabled && VoteSettings.FromEnvironment().VoteUrl is null, "un token o link en blanco cuenta como no puesto");
SetEnv(" tok-123 ", " https://top.gg/bot/1/vote ", "http://localhost:9/api/");
var s1 = VoteSettings.FromEnvironment();
Check(s1.Enabled && s1.Token == "tok-123" && s1.VoteUrl == "https://top.gg/bot/1/vote" && s1.ApiBase == "http://localhost:9/api", "con variables: habilitado, valores recortados y sin barra final en la base");
SetEnv(null, null, null);

Console.WriteLine("\n== 3. El cliente contra un servidor falso ==");
int port; using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
var requests = new List<(string Url, string? Auth, string? Accept)>();
Func<string, (int Status, string Body, int DelayMs)> handler = _ => (404, "{}", 0);
var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
_ = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync(); } catch { break; }
        _ = Task.Run(async () =>
        {
            lock (requests) requests.Add((ctx.Request.RawUrl ?? "", ctx.Request.Headers["Authorization"], ctx.Request.Headers["Accept"]));
            var (status, body, delay) = handler(ctx.Request.RawUrl ?? "");
            if (delay > 0) await Task.Delay(delay);
            try
            {
                ctx.Response.StatusCode = status; var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.ContentType = "application/json"; ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
            }
            catch { }
        });
    }
});
int Count() { lock (requests) return requests.Count; }
const string Secret = "SECRET-TOKEN-XYZ";
var clock = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
var http = new HttpClient();
var client1 = new TopGgVoteClient(new VoteSettings(Secret, "https://top.gg/bot/1/vote", $"http://127.0.0.1:{port}"), http, () => clock);
Check(client1.Enabled && client1.VoteUrl == "https://top.gg/bot/1/vote", "habilitado y con su link");

handler = _ => (200, """{"created_at":"2026-10-16T10:00:00.000Z","expires_at":"2026-10-16T22:00:00.000Z","weight":1}""", 0);
var c1 = await client1.CheckAsync(123456789012345678UL);
(string Url, string? Auth, string? Accept) first; lock (requests) first = requests[0];
Check(c1.Status == VoteCheckStatus.Active && c1.CreatedUtc == new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc), "200 → un voto vigente");
Check(first.Url == "/projects/@me/votes/123456789012345678?source=discord", $"pide GET /projects/@me/votes/{{id}}?source=discord ({first.Url})");
Check(first.Auth == $"Bearer {Secret}" && first.Accept!.Contains("application/json"), "con «Authorization: Bearer <token>»");
await client1.CheckAsync(123456789012345678UL);
Check(Count() == 1, "dos preguntas seguidas por la misma persona hacen UN solo pedido (cache de 5 s)");
await client1.CheckAsync(111UL);
Check(Count() == 2, "otra persona sí pregunta");
clock = clock.AddSeconds(6);
await client1.CheckAsync(123456789012345678UL);
Check(Count() == 3, "pasados los 5 s vuelve a preguntar");

handler = _ => (404, """{"message":"not found"}""", 0);
clock = clock.AddMinutes(1);
var c404 = await client1.CheckAsync(222UL);
Check(c404.Status == VoteCheckStatus.NoActiveVote, "404 → sin voto vigente");

int before = Count();
handler = _ => (401, """{"message":"bad token"}""", 0);
var oldOut = Console.Out; var oldErr = Console.Error; var sw = new StringWriter(); Console.SetOut(sw); Console.SetError(sw);
VoteCheck c401, c401b;
try { c401 = await client1.CheckAsync(333UL); c401b = await client1.CheckAsync(333UL); } finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
Check(c401.Status == VoteCheckStatus.Unavailable && c401b.Status == VoteCheckStatus.Unavailable && Count() == before + 2, "401 (token malo) → Unavailable y NO se cachea (el segundo vuelve a preguntar)");
Check(!sw.ToString().Contains(Secret) && sw.ToString().Contains("HTTP 401"), "el log dice el código HTTP pero JAMÁS imprime el token");

handler = _ => (500, "boom", 0);
Check((await client1.CheckAsync(444UL)).Status == VoteCheckStatus.Unavailable, "500 → Unavailable");
handler = _ => (200, "esto no es json", 0);
Check((await client1.CheckAsync(445UL)).Status == VoteCheckStatus.Unavailable, "200 con basura → Unavailable (no tira excepción)");

var sw2 = Stopwatch2.Start();
handler = _ => (200, "{}", 7500);
var cSlow = await client1.CheckAsync(555UL);
Check(cSlow.Status == VoteCheckStatus.Unavailable && sw2.Elapsed.TotalSeconds is > 5 and < 9, $"un top.gg que no contesta se corta a los 6 s ({sw2.Elapsed.TotalSeconds:0.0} s) y devuelve Unavailable");

int closedPort; using (var probe2 = new TcpListener(IPAddress.Loopback, 0)) { probe2.Start(); closedPort = ((IPEndPoint)probe2.LocalEndpoint).Port; probe2.Stop(); }
var clientDown = new TopGgVoteClient(new VoteSettings(Secret, null, $"http://127.0.0.1:{closedPort}"), new HttpClient(), () => clock);
Check((await clientDown.CheckAsync(666UL)).Status == VoteCheckStatus.Unavailable, "sin conexión (puerto cerrado) → Unavailable");

int beforeOff = Count();
var clientOff = new TopGgVoteClient(new VoteSettings(null, null, $"http://127.0.0.1:{port}"), http, () => clock);
Check(!clientOff.Enabled && (await clientOff.CheckAsync(777UL)).Status == VoteCheckStatus.NotConfigured && Count() == beforeOff, "sin token: NotConfigured y no hace NINGÚN pedido");
listener.Stop();

Console.WriteLine("\n== 4. El cobro, contra la base ==");
await Reset();
var t0 = Ms(DateTime.UtcNow.AddMinutes(-30));
await SetBoss(TimeSpan.FromHours(2));
var r1 = await voteRepo.ClaimAsync(U, t0);
var storedAt = await db.ExecuteScalarAsync<DateTime>("SELECT last_executed_at FROM cooldowns WHERE discord_id = @id AND command_name = 'vote'", new { id = (long)U });
Check(r1.Status == VoteClaimStatus.Claimed && r1.BossReset && await Have(Box) == 1 && !await HasRow("boss") && Ms(storedAt) == t0, "cobrar un voto: +1 Baúl de Roble, el jefe (que corría) se resetea y la fila 'vote' guarda el momento del voto");
var r1b = await voteRepo.ClaimAsync(U, t0);
Check(r1b.Status == VoteClaimStatus.AlreadyClaimed && await Have(Box) == 1, "el MISMO voto otra vez: ya cobrado, no paga de nuevo");
var rOld = await voteRepo.ClaimAsync(U, t0.AddHours(-5));
Check(rOld.Status == VoteClaimStatus.AlreadyClaimed && await Have(Box) == 1, "un voto MÁS VIEJO que el cobrado tampoco paga");
await SetBoss(TimeSpan.FromMinutes(10));
var t1 = Ms(DateTime.UtcNow);
var r2 = await voteRepo.ClaimAsync(U, t1);
Check(r2.Status == VoteClaimStatus.Claimed && r2.BossReset && await Have(Box) == 2 && !await HasRow("boss"), "un voto más nuevo sí paga (2 cajas) y vuelve a resetear el jefe");
var t2 = Ms(DateTime.UtcNow.AddSeconds(1));
var r3 = await voteRepo.ClaimAsync(U, t2);
Check(r3.Status == VoteClaimStatus.Claimed && !r3.BossReset && await Have(Box) == 3, "con el jefe ya listo igual paga la caja, y avisa que no había nada que resetear (BossReset = false)");
await SetBoss(TimeSpan.FromHours(6));
var r4 = await voteRepo.ClaimAsync(U, Ms(DateTime.UtcNow.AddSeconds(2)));
Check(r4.Status == VoteClaimStatus.Claimed && !r4.BossReset && !await HasRow("boss") && await Have(Box) == 4, "un cooldown de jefe que ya había vencido (6 h) no cuenta como reseteo, pero la fila vieja se limpia");
await SetBoss(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(30));
var r5 = await voteRepo.ClaimAsync(U, Ms(DateTime.UtcNow.AddSeconds(3)));
Check(r5.Status == VoteClaimStatus.Claimed && r5.BossReset, "el reintento corto de un jefe perdido (30 min restantes) también se resetea");

await Reset();
var tc = Ms(DateTime.UtcNow.AddMinutes(-1));
var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => voteRepo.ClaimAsync(U, tc)));
Check(results.Count(r => r.Status == VoteClaimStatus.Claimed) == 1 && results.Count(r => r.Status == VoteClaimStatus.AlreadyClaimed) == 19 && await Have(Box) == 1, "20 cobros del mismo voto a la vez: exactamente UNO paga (1 caja)");

await Reset();
await db.ExecuteAsync("UPDATE items SET name = 'Baúl de Roble (temp)' WHERE name = @n", new { n = Box });
VoteClaimOutcome rMiss;
try { rMiss = await voteRepo.ClaimAsync(U, Ms(DateTime.UtcNow)); }
finally { await db.ExecuteAsync("UPDATE items SET name = @n WHERE name = 'Baúl de Roble (temp)'", new { n = Box }); }
Check(rMiss.Status == VoteClaimStatus.BoxMissing && !await HasRow("vote") && await Have(Box) == 0, "si la caja del premio no existe: BoxMissing y NO queda el voto marcado (se revierte todo)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM items WHERE name = @n AND type = 'Caja'", new { n = Box }) == 1, "(el catálogo quedó como estaba)");

Console.WriteLine("\n== 5. /vote de punta a punta (con un top.gg falso) ==");
var fake = new FakeVote();
Task<VoteResult> Vote(ulong id = U) => VoteModule.ExecuteVoteAsync(items, voteRepo, cooldowns, fake, rec, id);
await Reset();
fake.Enabled = false; fake.Next = new VoteCheck(VoteCheckStatus.NotConfigured);
var vOff = await Vote();
string tOff = vOff.Embed is null ? "" : AllText(vOff.Embed);
Check(vOff.Embed is not null && vOff.PlainMessage is null && tOff.Contains("Baúl de Roble") && tOff.Contains("jefe se resetea") && tOff.Contains("se habilita muy pronto") && !tOff.Contains("Votá acá"), "sin token: cuenta qué da votar (la caja y el reseteo), dice que todavía no se puede y no promete un link");
Check(await Have(Box) == 0 && !await HasRow("vote") && await Stat("vote") == 0, "y no toca nada");

fake.Enabled = true;
fake.Next = new VoteCheck(VoteCheckStatus.Unavailable);
var vDown = await Vote();
Check(vDown.Embed is null && vDown.PlainMessage!.Contains("top.gg") && vDown.PlainMessage.Contains("no se perdió nada") && await Have(Box) == 0, "top.gg caído: lo dice, que no se perdió nada, y no cobra");

fake.Next = new VoteCheck(VoteCheckStatus.NoActiveVote);
var vNone = await Vote();
string tNone = AllText(vNone.Embed!);
Check(tNone.Contains("Votá acá") && tNone.Contains("https://top.gg/bot/1/vote") && tNone.Contains("/vote") && tNone.Contains("12 horas") && !tNone.Contains("volver a votar"), "sin voto vigente: el link, qué da y que hay que escribir /vote después de votar");
fake.VoteUrl = null;
Check(AllText((await Vote()).Embed!).Contains("Buscá Asado y Acero en **top.gg**"), "sin link configurado: manda a buscarlo en top.gg");
fake.VoteUrl = "https://top.gg/bot/1/vote";

await SetBoss(TimeSpan.FromHours(1));
var votedAt = Ms(DateTime.UtcNow.AddMinutes(-20));
fake.Next = new VoteCheck(VoteCheckStatus.Active, votedAt, votedAt.AddHours(12), 1);
var vOk = await Vote();
string tOk = AllText(vOk.Embed!);
Check(vOk.Embed!.Title.Contains("¡Gracias por votar!") && tOk.Contains("Baúl de Roble") && tOk.Contains("/open") && tOk.Contains("¡Jefe reseteado!") && tOk.Contains("/boss") && tOk.Contains("11h"), $"con un voto vigente: lo cobra, dice la caja, el jefe reseteado y cuánto falta para votar de nuevo");
Check(await Have(Box) == 1 && !await HasRow("boss") && await HasRow("vote") && await Stat("vote") == 1, "la caja está en la mochila, el cooldown del jefe se fue y se registró el evento 'vote'");
var vAgain = await Vote();
string tAgain = AllText(vAgain.Embed!);
Check(tAgain.Contains("Ya cobraste este voto") && tAgain.Contains("volver a votar en") && !vAgain.Embed!.Title.Contains("Gracias") && await Have(Box) == 1 && await Stat("vote") == 1, "pedirlo otra vez con el mismo voto: «ya cobraste este voto», sin pagar ni registrar de nuevo");
fake.Next = new VoteCheck(VoteCheckStatus.NoActiveVote);
var vWait = await Vote();
Check(AllText(vWait.Embed!).Contains("Podés volver a votar en") && !AllText(vWait.Embed!).Contains("Votá acá"), "después de cobrar, sin voto nuevo: dice cuánto falta para votar de nuevo (la fila 'vote' lo sabe)");

var votedAt2 = Ms(DateTime.UtcNow);
fake.Next = new VoteCheck(VoteCheckStatus.Active, votedAt2, votedAt2.AddHours(12), 1);
var vReady = await Vote();
string tReady = AllText(vReady.Embed!);
Check(vReady.Embed!.Title.Contains("Gracias") && tReady.Contains("ya estaba listo") && !tReady.Contains("¡Jefe reseteado!") && await Have(Box) == 2 && await Stat("vote") == 2, "con el jefe ya listo: igual paga la caja y lo explica sin prometer un reseteo");

await Reset(U2);
fake.Next = new VoteCheck(VoteCheckStatus.Active, votedAt2, votedAt2.AddHours(12), 1);
var vOther = await Vote(U2);
Check(vOther.Embed!.Title.Contains("Gracias") && await Have(Box, U2) == 1, "cada persona cobra su propio voto (el voto de U no bloquea a U2)");

await Reset();
await db.ExecuteAsync("UPDATE items SET name = 'Baúl de Roble (temp)' WHERE name = @n", new { n = Box });
VoteResult vMiss;
try { fake.Next = new VoteCheck(VoteCheckStatus.Active, Ms(DateTime.UtcNow), null, 1); var errOld = Console.Error; Console.SetError(new StringWriter()); try { vMiss = await Vote(); } finally { Console.SetError(errOld); } }
finally { await db.ExecuteAsync("UPDATE items SET name = @n WHERE name = 'Baúl de Roble (temp)'", new { n = Box }); }
Check(vMiss.Embed is null && vMiss.PlainMessage!.Contains("No se cobró nada") && !await HasRow("vote"), "si falta la caja en el catálogo: avisa que no se cobró nada (y se puede reintentar)");

Console.WriteLine("\n== 6. /cd y recordatorios ==");
await Reset();
var cdOff = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, null, false);
var cdOn = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, null, true);
Check(!AllText(cdOff).Contains("Votar") && AllText(cdOn).Contains("🗳️ **Votar**: **¡Listo!** ✅"), "/cd no muestra «Votar» con el voto dormido, y con el voto habilitado lo muestra «¡Listo!»");
await voteRepo.ClaimAsync(U, Ms(DateTime.UtcNow.AddHours(-1)));
var cdWait = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, U, null, null, true);
Check(AllText(cdWait).Contains("🗳️ **Votar**:") && AllText(cdWait).Contains("restantes") && AllText(cdWait).Contains("11h"), "después de cobrar, /cd cuenta lo que falta (≈ 11 h)");
var nowR = DateTime.UtcNow;
var dueList = ReminderCatalog.FromCooldowns([("vote", nowR.AddHours(-1))], nowR);
Check(dueList.Count == 1 && dueList[0].Kind == "vote" && Math.Abs((dueList[0].DueUtc - nowR.AddHours(11)).TotalSeconds) < 2, "el recordatorio 'vote' vence 12 h después del voto cobrado");
Check(ReminderCatalog.FromCooldowns([("vote", nowR.AddHours(-13))], nowR).Count == 0, "pasadas las 12 h ya no hay nada que avisar");
Check(ReminderCatalog.Find("vote") is { } rk && rk.Command == "/vote" && rk.AutoDelete is null && ReminderCatalog.WaitOf("vote") == TimeSpan.FromHours(12) && ReminderCatalog.Compose(U, ["vote"]).Contains("/vote") && ReminderCatalog.All.Count == 13, "el aviso existe (largo: no se borra solo), dura 12 h y nombra /vote");

Console.WriteLine("\n== 7. Comandos, ayuda y DI con el InteractionService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var cmd = interactions.SlashCommands.Single(c => c.Name == "vote");
Check(cmd.Module.SlashGroupName is null && cmd.Name == "vote" && cmd.Description.Length is >= 1 and <= 100 && cmd.Parameters.Count == 0, $"/vote existe, sin grupo ni parámetros, y su descripción entra en Discord ({cmd.Description.Length} caracteres)");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "vote", "votar", "voto" }.All(a => textNames.Count(n => n == a) == 1), "aa vote / votar / voto están registrados, sin repetirse");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");
var realClient = (ITopGgVoteClient)Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(sp, typeof(ITopGgVoteClient));
Check(realClient is TopGgVoteClient && !realClient.Enabled, "el cliente real que arma la DI queda dormido cuando no hay TopGg__Token");
Check(interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "siguen entrando en el tope de 100 comandos de nivel superior");

var topic = HelpTopics.Find("votar")!;
Check(new[] { "vote", "votar", "voto", "votos", "top.gg", "topgg", "Votar en top.gg" }.All(w => HelpTopics.Find(w)?.Key == "vote") && topic.Key == "vote", "el tema de ayuda se encuentra por vote, votar, voto, votos, top.gg y topgg");
var topicE = topic.Build(); string tt = AllText(topicE);
Check(tt.Contains("Baúl de Roble") && tt.Contains("12 h") && tt.Contains("5 h") && tt.Contains("/vote") && tt.Contains("/cd") && topicE.Length <= 6000 && topicE.Fields.All(f => f.Value.Length <= 1024), $"el tema explica el premio y los números reales (12 h, el jefe de 5 h) ({topicE.Length})");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/vote") && infoE.Length <= 6000 && infoE.Fields.Length <= 25 && infoE.Fields.All(f => f.Value.Length <= 1024), $"/info lista /vote, dentro de los límites ({infoE.Length} caracteres, {infoE.Fields.Length} campos)");
Check(HelpTopics.All.Count == 27 && HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256); }), "los 27 temas entran en Discord");
Check(AllText(HelpTopics.Find("reminders")!.Build()).Contains("Votar"), "el tema de recordatorios lista también el aviso de votar");

var vInfo = VoteModule.BuildInfoEmbed(VoteModule.VoteScreen.Ready, "https://top.gg/x", null);
Check(vInfo.Length <= 6000 && AllText(vInfo).Contains("12 h"), "la pantalla de información entra en Discord y dice cada cuánto se vota");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)U, (long)U2 } });
Check(true, "(limpieza) usuarios descartables borrados");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
class FakeVote : ITopGgVoteClient
{
    public bool Enabled { get; set; } = true;
    public string? VoteUrl { get; set; } = "https://top.gg/bot/1/vote";
    public VoteCheck Next { get; set; } = new(VoteCheckStatus.NoActiveVote);
    public Task<VoteCheck> CheckAsync(ulong discordId, CancellationToken cancellationToken = default) => Task.FromResult(Next);
}

static class Stopwatch2 { public static System.Diagnostics.Stopwatch Start() => System.Diagnostics.Stopwatch.StartNew(); }
