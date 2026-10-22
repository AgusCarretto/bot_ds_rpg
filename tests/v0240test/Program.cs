using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.24.0 (el dueño, 2026-10-22): «aa miniboss y que todos peleen contra él, o worldboss; mientras más gente mejor; que anotarse gaste el cooldown del jefe; que dé entre 30 y 50 cacerías de esa zona».
// El jefe mundial: se anotan durante el día (sin pedir nivel; gasta el cooldown del jefe) y a las 00:00 de Uruguay lo pelean entre todos. Reglas puras, la base real (anotarse, cooldown, pago, carreras), el anuncio,
// el reloj y los comandos con el InteractionService / CommandService reales.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

int Typical(int zoneRank, int minLevel) => WorldBossRules.ReferenceAttack(minLevel, zoneRank);
WorldBossFighter F(ulong id, int rank = 1, double strength = 1.0, int minLevel = 1, int level = 5) =>
    new(id, $"Jugador{id}", level, rank, minLevel, (int)Math.Round(strength * WorldBossRules.ReferenceAttack(minLevel, rank)));

Console.WriteLine("== 1. El jefe del día ==");
Check(WorldBossRules.Bosses.Count == 7 && WorldBossRules.Bosses.Select(b => b.Key).Distinct().Count() == 7 && WorldBossRules.Bosses.Select(b => b.Name).Distinct().Count() == 7 && WorldBossRules.Bosses.All(b => b.Intro.Length > 20), "hay 7 jefes distintos, cada uno con su frase");
var d0 = new DateOnly(2026, 10, 22);
Check(WorldBossRules.BossOf(d0) == WorldBossRules.BossOf(d0) && Enumerable.Range(0, 7).Select(i => WorldBossRules.BossOf(d0.AddDays(i)).Key).Distinct().Count() == 7 && WorldBossRules.BossOf(d0).Key != WorldBossRules.BossOf(d0.AddDays(1)).Key,
    "el jefe sale solo de la fecha: el mismo para todos ese día, y cambia día a día (los 7 en una semana)");

Console.WriteLine("\n== 2. Cuánto aporta cada uno (el mismo orden en todas las zonas) ==");
int[] minLevels = [1, 5, 10, 15, 20];
var refs = Enumerable.Range(1, 5).Select(r => WorldBossRules.ReferenceAttack(minLevels[r - 1], r)).ToList();
Check(refs.Zip(refs.Skip(1)).All(p => p.First < p.Second), $"lo que se espera de alguien equipado sube de zona en zona ({string.Join(", ", refs)})");
Check(Enumerable.Range(1, 5).All(r => Math.Abs(WorldBossRules.Strength(WorldBossRules.ReferenceAttack(minLevels[r - 1], r), minLevels[r - 1], r) - 1.0) < 1e-9), "alguien con justo lo esperado de SU zona aporta 1,0 en cualquiera de las cinco");
double rookie = WorldBossRules.Strength(CombatStats.BaseAttack(1), 1, 1);
double veteran = WorldBossRules.Strength(CombatStats.BaseAttack(60) + 120, 20, 5);
Check(rookie is > 0.25 and < 0.5 && veteran is > 1.2 and <= WorldBossRules.MaxStrength, $"un nivel 1 sin arma en la Zona 1 aporta {rookie:F2} y un nivel 60 equipado en la Zona 5, {veteran:F2}: del mismo orden, el equipado un poco más");
Check(WorldBossRules.Strength(0, 1, 1) == WorldBossRules.MinStrength && WorldBossRules.Strength(1_000_000, 1, 1) == WorldBossRules.MaxStrength && WorldBossRules.Strength(-50, 1, 1) == WorldBossRules.MinStrength, "la fuerza se acota (ni cero, ni un monstruo que lo resuelve solo)");

Console.WriteLine("\n== 3. El premio: de 30 a 50, y mejor mientras más gente ==");
Check(WorldBossRules.HuntsFor(0, false) == 30 && WorldBossRules.HuntsFor(50, false) == 40 && WorldBossRules.HuntsFor(100, false) == 50 && WorldBossRules.HuntsFor(0, true) == 50 && WorldBossRules.HuntsFor(500, false) == 50 && WorldBossRules.HuntsFor(-20, false) == 30, "0 % de vida = 30 cacerías, 50 % = 40, 100 % o cayó = 50 (y nunca sale del rango)");
var rngA = new Random(42);
(double Total, double DefeatedRate, double Hunts) Crowd(int players, int trials = 3000)
{
    int defeated = 0; double total = 0, hunts = 0;
    for (int t = 0; t < trials; t++)
    {
        var o = WorldBossRules.Resolve(Enumerable.Range(0, players).Select(i => F((ulong)(i + 1))).ToList(), rngA);
        total += o.TotalPercent; hunts += o.Hunts; if (o.Defeated) defeated++;
    }

    return (total / trials, (double)defeated / trials, hunts / trials);
}

var c1 = Crowd(1); var c5 = Crowd(5); var c10 = Crowd(10); var c12 = Crowd(12); var c40 = Crowd(40);
Check(c1.Hunts is >= 31 and <= 33 && c5.Hunts is > 38.5 and < 41.5 && c12.Hunts > 49 && c40.Hunts == 50, $"premio promedio: 1 jugador {c1.Hunts:F1}, 5 jugadores {c5.Hunts:F1}, 12 jugadores {c12.Hunts:F1}, 40 jugadores {c40.Hunts:F1}");
Check(c1.DefeatedRate == 0 && c5.DefeatedRate < 0.01 && c10.DefeatedRate is > 0.3 and < 0.7 && c12.DefeatedRate > 0.95 && c40.DefeatedRate == 1.0, $"el jefe cae: con 1 y 5 nunca, con 10 la mitad de las veces ({c10.DefeatedRate * 100:F0} %), con 12 casi siempre ({c12.DefeatedRate * 100:F0} %) y con 40 siempre");
Check(c1.Hunts < c5.Hunts && c5.Hunts < c12.Hunts, "mientras más gente, más cobra cada uno");
var outcome = WorldBossRules.Resolve([F(1, strength: 0.5), F(2, strength: 1.0), F(3, strength: 2.0)], new Random(7));
Check(outcome.Hits.Count == 3 && outcome.Hits.Zip(outcome.Hits.Skip(1)).All(p => p.First.DamagePercent >= p.Second.DamagePercent) && outcome.Hits[0].DiscordId == 3 && Math.Abs(outcome.TotalPercent - outcome.Hits.Sum(h => h.DamagePercent)) < 0.02,
    "los golpes salen de mayor a menor y el total es la suma");
Check(outcome.Hits.All(h => h.Form is >= 1 - WorldBossRules.FormSwing - 1e-9 and <= 1 + WorldBossRules.FormSwing + 1e-9 && Math.Abs(h.DamagePercent - (100.0 * h.Strength * h.Form / WorldBossRules.FullCrowd)) < 0.01), "cada golpe es fuerza × forma del día × (100 / jugadores normales que lo tumban)");
var sameRank = Enumerable.Range(1, 5).Select(r => Crowd2(r)).ToList();
double Crowd2(int rank) { var rr = new Random(rank); return Enumerable.Range(0, 400).Average(_ => WorldBossRules.Resolve(Enumerable.Range(0, 6).Select(i => F((ulong)(i + 1), rank, 1.0, minLevels[rank - 1])).ToList(), rr).TotalPercent); }
Check(sameRank.Max() - sameRank.Min() < 4, $"6 jugadores equipados para su zona suman lo mismo en cualquier zona ({string.Join(", ", sameRank.Select(x => x.ToString("F1")))} %)");
var emptyOutcome = WorldBossRules.Resolve([], new Random(1));
Check(emptyOutcome.Hits.Count == 0 && emptyOutcome.TotalPercent == 0 && !emptyOutcome.Defeated && emptyOutcome.Hunts == 30, "sin nadie: 0 %, no cae y el premio mínimo (no tira)");
var lineBoss = WorldBossRules.Bosses[0];
Check(WorldBossRules.HitLine(outcome.Hits[0], lineBoss, new Random(1)).Contains("Jugador3") && WorldBossRules.HitLine(outcome.Hits[0], lineBoss, new Random(1)) == WorldBossRules.HitLine(outcome.Hits[0], lineBoss, new Random(1))
      && WorldBossRules.HitLine(new WorldBossHit(1, new string('Z', 60), 9, 1, 1, 1, 9.5), lineBoss, new Random(2)).Length < 200, "la línea de cada golpe nombra al jugador, es determinista con la misma semilla y recorta nombres largos");

Console.WriteLine("\n== 4. Anotarse y pelear, contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var repo = new WorldBossRepository(factory);
var adventure = new AdventureRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var events = new GameEventService(eventsRepo);
var service = new WorldBossService(repo, users, items, zonesRepo, events);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const long Base = 999000000000098000L;
async Task Wipe() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id BETWEEN @a AND @b; DELETE FROM game_events WHERE discord_id BETWEEN @a AND @b; DELETE FROM cooldowns WHERE discord_id BETWEEN @a AND @b; DELETE FROM worldboss_days WHERE day BETWEEN '2020-06-01' AND '2020-06-30'", new { a = Base, b = Base + 999 });
await Wipe();
async Task Make(int n, int level = 5, int zone = 1, int gold = 0) { await users.GetOrCreateUserAsync((ulong)(Base + n)); await db.ExecuteAsync("UPDATE users SET level = @l, current_zone_id = @z, gold = @g, class = 'Guerrero' WHERE discord_id = @id", new { id = Base + n, l = level, z = zone, g = gold }); }
ulong Id(int n) => (ulong)(Base + n);
async Task<int> Gold(int n) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = Base + n });
async Task<DateTime?> BossStamp(int n) => await db.ExecuteScalarAsync<DateTime?>("SELECT last_executed_at FROM cooldowns WHERE discord_id = @id AND command_name = 'boss'", new { id = Base + n });
var day1 = new DateOnly(2020, 6, 1);
var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());

var ghost = await repo.JoinAsync(day1, Id(999), "Nadie", 555);
Check(ghost.Status == WorldBossJoinStatus.NoAccount && await db.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM worldboss_days WHERE day = '2020-06-01'") == 0, "sin cuenta no se anota y no deja nada");
await Make(1, level: 1);
var j1 = await repo.JoinAsync(day1, Id(1), "Uno", 555);
var stamp1 = await BossStamp(1);
Check(j1.Status == WorldBossJoinStatus.Joined && stamp1 is not null && (DateTime.UtcNow - DateTime.SpecifyKind(stamp1.Value, DateTimeKind.Utc)).TotalSeconds is > -5 and < 30, "un nivel 1 se anota (no pide nivel) y se gasta su cooldown del jefe");
Check(!await adventure.TryClaimCooldownAsync(Id(1), CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Duration), "y ahora /boss ve ese cooldown corriendo: es el mismo");
var j1b = await repo.JoinAsync(day1, Id(1), "Uno", 555);
Check(j1b.Status == WorldBossJoinStatus.AlreadyJoined && await BossStamp(1) == stamp1, "anotarse otra vez dice «ya estás» y NO toca el cooldown");
var info1 = await repo.GetDayAsync(day1);
Check(info1 is { Status: "open", ChannelId: 555 } && (await repo.GetEntriesAsync(day1)).Count == 1, "el día queda abierto y guarda el canal del primero que se anotó");

await Make(2);
await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'boss', now() - interval '1 hour')", new { id = Base + 2 });
var busy = await repo.JoinAsync(day1, Id(2), "Dos", 555);
Check(busy.Status == WorldBossJoinStatus.OnCooldown && busy.CooldownLeft.TotalHours is > 3.9 and < 4.1 && (await repo.GetEntriesAsync(day1)).Count == 1, $"con el cooldown del jefe corriendo (1 h de 5) no se anota y dice cuánto falta ({busy.CooldownLeft.TotalHours:F2} h)");
await db.ExecuteAsync("UPDATE cooldowns SET last_executed_at = now() - interval '6 hours' WHERE discord_id = @id AND command_name = 'boss'", new { id = Base + 2 });
Check((await repo.JoinAsync(day1, Id(2), "Dos", 555)).Status == WorldBossJoinStatus.Joined, "con el cooldown vencido sí se anota");

// Carreras: 10 clicks del mismo jugador y 8 jugadores distintos a la vez.
await Make(3);
var burst = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => repo.JoinAsync(day1, Id(3), "Tres", 555))));
Check(burst.Count(r => r.Status == WorldBossJoinStatus.Joined) == 1 && burst.Count(r => r.Status == WorldBossJoinStatus.AlreadyJoined) == 9 && await db.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM worldboss_entries WHERE day = '2020-06-01' AND discord_id = @id", new { id = Base + 3 }) == 1,
    "10 clicks a la vez del mismo jugador: se anota UNA vez");
for (int n = 10; n < 18; n++) await Make(n);
var crowd = await Task.WhenAll(Enumerable.Range(10, 8).Select(n => Task.Run(() => repo.JoinAsync(day1, Id(n), $"P{n}", 555))));
Check(crowd.All(r => r.Status == WorldBossJoinStatus.Joined) && (await repo.GetEntriesAsync(day1)).Count == 11, "8 jugadores distintos a la vez entran todos (11 anotados)");

// Pelear: cada uno cobra SU zona.
await db.ExecuteAsync("UPDATE users SET current_zone_id = @z, level = @l WHERE discord_id = @id", new { id = Base + 1, z = zones[0].ZoneId, l = 1 });
await db.ExecuteAsync("UPDATE users SET current_zone_id = @z, level = @l WHERE discord_id = @id", new { id = Base + 2, z = zones[2].ZoneId, l = 12 });
await db.ExecuteAsync("UPDATE users SET current_zone_id = @z, level = @l WHERE discord_id = @id", new { id = Base + 3, z = zones[4].ZoneId, l = 40 });
var goldBefore = new Dictionary<int, int>();
foreach (int n in new[] { 1, 2, 3, 10, 11 }) goldBefore[n] = await Gold(n);
var res = await service.ResolveDayAsync(day1);
Check(res is { Cancelled: false } && res.Participants == 11 && res.Hits.Count == 11 && res.Paid.Count == 11 && res.Boss.Key == WorldBossRules.BossOf(day1).Key && res.ChannelId == 555, "se pelea con los 11 anotados y se anuncia en el canal del primero");
int hunts = res!.Hunts;
Check(hunts is >= 30 and <= 50 && res.TotalPercent > 0, $"el premio sale del rango: {hunts} cacerías ({res.TotalPercent:F1} % de la vida)");
bool paidRight = true;
foreach (int n in new[] { 1, 2, 3 })
{
    int rank = n switch { 1 => 1, 2 => 3, _ => 5 };
    paidRight &= await Gold(n) - goldBefore[n] == hunts * MissionRewards.GoldUnit(rank);
}

Check(paidRight, $"cada uno cobra {hunts} cacerías de SU zona: zona 1 → {hunts * MissionRewards.GoldUnit(1)} de oro, zona 3 → {hunts * MissionRewards.GoldUnit(3)}, zona 5 → {hunts * MissionRewards.GoldUnit(5)}");
var dayAfter = await repo.GetDayAsync(day1);
Check(dayAfter is { Status: "resolved", Participants: 11, Defeated: not null, ResolvedAt: not null } && dayAfter.Hunts == hunts && dayAfter.BossKey == res.Boss.Key, "el día queda resuelto con el jefe, el total y el premio guardados");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM worldboss_entries WHERE day = '2020-06-01' AND damage_pct IS NOT NULL AND reward_gold > 0") == 11, "cada anotación guarda lo que le sacó al jefe y el oro que cobró");
Check(await service.ResolveDayAsync(day1) is null && await Gold(1) - goldBefore[1] == hunts * MissionRewards.GoldUnit(1), "pelear el mismo día otra vez no hace nada y no paga de nuevo");
Check(await repo.JoinAsync(day1, Id(1), "Uno", 555) is { Status: WorldBossJoinStatus.Closed }, "un jefe ya peleado no admite anotados");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM game_events WHERE kind = 'worldboss_paid' AND discord_id BETWEEN @a AND @b", new { a = Base, b = Base + 999 }) == 11 && await db.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM game_events WHERE kind = 'worldboss_join' AND discord_id BETWEEN @a AND @b", new { a = Base, b = Base + 999 }) == 0,
    "los pagos dejan su evento (los anotados por el repositorio directo, no: el evento lo registra el servicio)");

// Resolver en paralelo: se paga una sola vez.
var day2 = new DateOnly(2020, 6, 2);
for (int n = 20; n < 26; n++) { await Make(n); await repo.JoinAsync(day2, Id(n), $"Q{n}", 777); }
var before = new Dictionary<int, int>(); foreach (int n in Enumerable.Range(20, 6)) before[n] = await Gold(n);
var race = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => service.ResolveDayAsync(day2))));
int totalPaid = 0; foreach (int n in Enumerable.Range(20, 6)) totalPaid += await Gold(n) - before[n];
Check(race.Count(r => r is not null) == 1 && totalPaid == 6 * race.First(r => r is not null)!.Hunts * MissionRewards.GoldUnit(zones[0].ZoneId == zones[0].ZoneId ? 1 : 1), $"6 intentos de pelear el mismo día a la vez: pelea y paga UNA sola vez ({totalPaid} de oro en total)");

// Un día sin nadie a quien pagar se cancela.
var day3 = new DateOnly(2020, 6, 3);
await Make(30); await repo.JoinAsync(day3, Id(30), "Ya no", 888);
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = Base + 30 });
var info3 = await repo.GetDayAsync(day3);
var cancelled = await service.ResolveDayAsync(day3);
Check(info3 is { Status: "open" } && cancelled is { Cancelled: true } && (await repo.GetDayAsync(day3))!.Status == "cancelled", "si todos borraron su cuenta, el día se cancela sin pagar");

// ResolveDue: pelea solo los días anteriores a hoy.
var day4 = new DateOnly(2020, 6, 4);
await Make(40); await repo.JoinAsync(day4, Id(40), "Cuatro", 999);
var future = ArenaRules.DayOf(DateTime.UtcNow).AddDays(30);
await db.ExecuteAsync("INSERT INTO worldboss_days (day, channel_id) VALUES (@d::date, 1) ON CONFLICT DO NOTHING", new { d = future.ToString("yyyy-MM-dd") });
var due = await service.ResolveDueAsync(DateTime.UtcNow);
Check(due.Any(r => r.Day == day4) && (await repo.GetDayAsync(future))!.Status == "open", "el reloj pelea los días anteriores a hoy que siguen abiertos y no toca el de hoy ni los futuros");
await db.ExecuteAsync("DELETE FROM worldboss_days WHERE day = @d::date", new { d = future.ToString("yyyy-MM-dd") });
Check((await repo.GetLatestResolvedAsync()) is not null && (await repo.GetOpenDaysBeforeAsync(new DateOnly(2020, 6, 30))).Count == 0, "el último peleado se consulta y no quedan días abiertos del pasado");

Console.WriteLine("\n== 5. El anuncio ==");
WorldBossResolution Res(int n, double strength, bool noise = true)
{
    var fighters = Enumerable.Range(0, n).Select(i => F((ulong)(i + 1), strength: strength)).ToList();
    var o = WorldBossRules.Resolve(fighters, new Random(n));
    return new WorldBossResolution(day1, WorldBossRules.BossOf(day1), false, 555, n, o.Defeated, o.TotalPercent, o.Hunts, o.Hits, []);
}

var smallCast = WorldBossModule.BuildBroadcast(Res(3, 1.0));
Check(smallCast.Count == 3 && smallCast[0].Embed.Title!.Contains(WorldBossRules.BossOf(day1).Name) && smallCast[1].Embed.Description.Contains("Vida de") && smallCast[2].Embed.Title!.Contains("sigue en pie") && smallCast[2].Embed.Description.Contains("mientras más gente, mejor") && smallCast[2].Embed.Description.Contains("El que más le pegó"),
    "3 anotados: abre con el jefe y los nombres, cuenta la batalla con la barra de vida y cierra «sigue en pie» con el premio y el MVP");
var bigCast = WorldBossModule.BuildBroadcast(Res(30, 1.0));
Check(bigCast.Count == 3 && bigCast[2].Embed.Title!.Contains("Cayó") && bigCast[2].Embed.Description.Contains("el máximo") && bigCast[1].Embed.Description.Contains("Y otros **24** más"), "30 anotados: el jefe cae, el cierre lo celebra y la batalla resume a los que no entran en el top 6");
var hugeCast = WorldBossModule.BuildBroadcast(Res(400, 1.0));
Check(hugeCast.All(m => m.Embed.Description.Length <= 4096 && m.Embed.Length <= 6000), "con 400 anotados ningún mensaje pasa de 4096 / 6000");
var nobody = WorldBossModule.BuildBroadcast(new WorldBossResolution(day1, WorldBossRules.BossOf(day1), true, 555, 0, false, 0, 0, [], []));
Check(nobody.Count == 1 && nobody[0].Embed.Title!.Contains("sin pelea"), "un día cancelado es un solo mensaje");
Check(WorldBossModule.BuildBroadcast(Res(3, 1.0)).Select(m => m.Embed.Description).SequenceEqual(smallCast.Select(m => m.Embed.Description)), "contar el mismo día dos veces dice lo mismo (las frases salen de la fecha)");

Console.WriteLine("\n== 6. La pantalla de anotarse ==");
var fakeUser = FakeProxy.Make<IUser>(new() { ["Id"] = 5UL, ["Username"] = "Agus", ["IsBot"] = false });
var boss0 = WorldBossRules.BossOf(day1);
Embed Join(WorldBossJoinStatus s, TimeSpan? left = null) => WorldBossModule.ExecuteJoinAsync(new FakeWorldBoss { Join = new WorldBossJoinResult(s, day1, boss0, 4, TimeSpan.FromHours(3), left ?? TimeSpan.Zero) }, fakeUser, 1, DateTime.UtcNow).GetAwaiter().GetResult();
var joined = Join(WorldBossJoinStatus.Joined);
Check(joined.Title!.Contains(boss0.Name) && joined.Description.Contains("00:00") && joined.Description.Contains("30 a 50") && joined.Description.Contains("cooldown del jefe") && joined.Description.Contains("No hace falta nivel") && joined.Description.Contains("**4** anotado"), "anotarse: dice cuándo se pelea, cuánto paga, que gastó el cooldown del jefe y que no pide nivel");
Check(Join(WorldBossJoinStatus.AlreadyJoined).Title!.Contains("Ya estabas") && Join(WorldBossJoinStatus.OnCooldown, TimeSpan.FromMinutes(95)).Description.Contains("1h 35m") && Join(WorldBossJoinStatus.NoAccount).Description.Contains("/start") && Join(WorldBossJoinStatus.Closed).Title!.Contains("cerró"), "los otros cuatro resultados, cada uno con su mensaje (y el cooldown que falta)");

Console.WriteLine("\n== 7. El reloj ==");
ArenaScheduler.RoundPause = TimeSpan.Zero;
var sent = new List<(ulong Channel, string? Title)>();
var fakeWb = new FakeWorldBoss { Due = [new WorldBossResolution(day1, boss0, false, 4242, 3, false, 25, 32, Res(3, 1.0).Hits, [])] };
var sched = new ArenaScheduler(new FakeArena(), new DiscordSocketClient(), new ConfigurationBuilder().Build(), fakeWb)
{
    ChannelLookup = id => Task.FromResult<IMessageChannel?>(RecordingChannel.Make(id, sent)),
};
await sched.TickAsync();
Check(sent.Count == 3 && sent.All(s => s.Channel == 4242) && sent[0].Title!.Contains("se despertó") && sent[2].Title!.Contains("sigue en pie"), $"el reloj anuncia el jefe mundial en el canal donde se anotó el primero ({sent.Count} mensajes)");
sent.Clear();
var sched2 = new ArenaScheduler(new FakeArena(), new DiscordSocketClient(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WorldBoss:ChannelId"] = "9090" }).Build(), fakeWb)
{
    ChannelLookup = id => Task.FromResult<IMessageChannel?>(RecordingChannel.Make(id, sent)),
};
await sched2.TickAsync();
Check(sent.Count == 3 && sent.All(s => s.Channel == 9090), "con WorldBoss__ChannelId en la configuración, anuncia ahí");
var sched3 = new ArenaScheduler(new FakeArena(), new DiscordSocketClient(), new ConfigurationBuilder().Build(), new FakeWorldBoss { Throw = true }) { ChannelLookup = id => Task.FromResult<IMessageChannel?>(RecordingChannel.Make(id, sent)) };
await sched3.TickAsync();
await new ArenaScheduler(new FakeArena(), new DiscordSocketClient(), new ConfigurationBuilder().Build()).TickAsync();
Check(true, "un servicio que falla, o un reloj sin jefe mundial (como antes), no tiran el ciclo");

Console.WriteLine("\n== 8. Comandos, ayuda y DI con el InteractionService real ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var cmd = interactions.SlashCommands.SingleOrDefault(c => c.Name == "miniboss");
Check(cmd is not null && cmd.Module.SlashGroupName is null && cmd.Description.Length is >= 1 and <= 100 && cmd.Parameters.Count == 0 && interactions.SlashCommands.Count < 100, $"/miniboss existe, sin grupo ni parámetros, descripción de {cmd?.Description.Length} caracteres ({interactions.SlashCommands.Count} comandos de barra, el tope de Discord es 100)");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var aliasCount = textMods.SelectMany(m => m.Commands).SelectMany(c => c.Aliases).GroupBy(a => a.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
Check(new[] { "miniboss", "worldboss", "wb", "jefemundial" }.All(a => aliasCount.GetValueOrDefault(a) == 1), "aa miniboss / worldboss / wb / jefemundial están registrados, sin repetirse con otro comando");
var bossHelp = string.Join("\n", HelpTopics.Find("boss")!.Build().Fields.Select(f => f.Name + " " + f.Value));
Check(bossHelp.Contains("/miniboss") && bossHelp.Contains("30 a 50") && bossHelp.Contains("Mientras más gente, mejor") && bossHelp.Contains("cooldown del jefe"), "/info tema:boss explica el jefe mundial con los números reales");
Check(HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256); }), "todos los temas de ayuda siguen entrando en los límites de Discord");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(x))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split('\n')[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
Check(sp.GetService(typeof(ArenaScheduler)) is ArenaScheduler, "el reloj se construye con el DI real (con el jefe mundial inyectado)");

await Wipe();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
sealed class FakeWorldBoss : IWorldBossService
{
    public WorldBossJoinResult? Join;
    public IReadOnlyList<WorldBossResolution> Due = [];
    public bool Throw;
    public Task<WorldBossJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default) => Task.FromResult(Join!);
    public Task<WorldBossListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<WorldBossResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) => Throw ? throw new InvalidOperationException("falla") : Task.FromResult(Due);
    public Task<WorldBossResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default) => Task.FromResult<WorldBossResolution?>(null);
}

sealed class FakeArena : IArenaService
{
    public Task<ArenaJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArenaListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArenaResults?> GetLatestResultsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ArenaResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArenaResolution>>([]);
    public Task<ArenaResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default) => Task.FromResult<ArenaResolution?>(null);
}

// Un canal falso que anota cada mensaje que le mandan (el id del canal y el título del embed).
public class RecordingChannel : DispatchProxy
{
    public ulong ChannelId;
    public List<(ulong Channel, string? Title)> Sent = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "SendMessageAsync")
        {
            var embed = args!.OfType<Embed>().FirstOrDefault();
            Sent.Add((ChannelId, embed?.Title));
            return Task.FromResult<IUserMessage>(null!);
        }

        if (method.Name.StartsWith("get_") && method.Name == "get_Id") return ChannelId;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }

    public static IMessageChannel Make(ulong id, List<(ulong Channel, string? Title)> sink)
    {
        var proxy = DispatchProxy.Create<IMessageChannel, RecordingChannel>();
        var rec = (RecordingChannel)(object)proxy;
        rec.ChannelId = id;
        rec.Sent = sink;
        return proxy;
    }
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
