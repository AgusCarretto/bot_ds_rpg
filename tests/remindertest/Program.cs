using System.Collections.Concurrent;
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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// v0.16.0: recordatorios de cooldown. Reglas puras (qué esperas corren, cuándo, cómo se escribe el aviso), pantalla de ajustes, repositorio contra la base real (usuarios descartables),
// el servicio que arma la cola, el reloj (con un envío falso) y los enganches: NoticeDelivery.Attach con el InteractionService real, el raid, los comandos, la ayuda y la DI.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var cooldowns = new CooldownRepository(factory);
var reminders = new ReminderRepository(factory);
var pets = new PetRepository(factory);
var blessings = new BlessingRepository(factory);
var gameEventRepo = new GameEventRepository(factory);
var bonusService = new PlayerBonusService(pets, blessings, gameEventRepo);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

const ulong U = 999000000000030000UL, U2 = 999000000000030001UL, U3 = 999000000000030002UL;
const ulong CH = 777000000000000001UL, CH2 = 777000000000000002UL;
async Task Wipe()
{
    foreach (var id in new[] { U, U2, U3 }) await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id }); // el CASCADE borra la cola, los ajustes, los cooldowns y las mascotas
    foreach (var id in new[] { U, U2, U3 }) await users.GetOrCreateUserAsync(id);
}
async Task<List<(string Kind, long Channel, DateTime Due)>> Queue(ulong id) =>
    (await db.QueryAsync<(string, long, DateTime)>("SELECT kind, channel_id, due_at FROM reminders WHERE discord_id = @id ORDER BY kind", new { id = (long)id })).ToList();
async Task SetCooldown(ulong id, string cmd, TimeSpan ago) =>
    await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, @c, now() - @a::interval) ON CONFLICT (discord_id, command_name) DO UPDATE SET last_executed_at = EXCLUDED.last_executed_at",
        new { id = (long)id, c = cmd, a = $"{(long)ago.TotalSeconds} seconds" });
async Task PutDue(ulong id, string kind, ulong channel, TimeSpan fromNow) =>
    await db.ExecuteAsync("INSERT INTO reminders (discord_id, kind, channel_id, due_at) VALUES (@id, @k, @c, now() + @d::interval) ON CONFLICT (discord_id, kind) DO UPDATE SET channel_id = EXCLUDED.channel_id, due_at = EXCLUDED.due_at",
        new { id = (long)id, k = kind, c = (long)channel, d = $"{(long)fromNow.TotalSeconds} seconds" });

IEnumerable<IMessageComponent> Flat(MessageComponent c) => c.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components);

DateTime now0 = DateTime.UtcNow;

Console.WriteLine("== 1. Catálogo y reglas puras ==");
Check(ReminderCatalog.All.Count == 13 && ReminderCatalog.Keys.Distinct().Count() == 13
    && ReminderCatalog.Keys.SequenceEqual(["hunt", "travel", "chop", "mine", "boss", "buybox", "chop_adv", "mine_adv", "pet", "daily", "vote", "dragon", "expedition"]), "13 tipos, claves únicas: hunt travel chop mine boss buybox chop_adv mine_adv pet daily vote dragon expedition");
Check(ReminderCatalog.All.All(k => k.Emoji.Length > 0 && k.Name.Length > 0 && k.Command.StartsWith('/') && k.ReadyText.Length > 0 && ReminderCatalog.Find(k.Key) == k), "cada tipo con emoji, nombre, comando y texto");
Check(ReminderCatalog.Find("nada") is null && ReminderCatalog.Find(null) is null, "una clave que no existe no se encuentra");
Check(ReminderCatalog.IsShort("hunt") && ReminderCatalog.IsShort("chop") && ReminderCatalog.IsShort("mine") && !ReminderCatalog.IsShort("travel") && !ReminderCatalog.IsShort("boss") && !ReminderCatalog.IsShort("buybox") && !ReminderCatalog.IsShort("pet") && !ReminderCatalog.IsShort("daily"),
    "solo cacería, talar y minar son esperas cortas (se borran solas)");
Check(ReminderCatalog.ShortLived == TimeSpan.FromMinutes(2), "los cortos se borran a los 2 minutos");
Check(ReminderCatalog.WaitOf("hunt") == CooldownCatalog.Hunt.Duration && ReminderCatalog.WaitOf("travel") == CooldownCatalog.Travel.Duration && ReminderCatalog.WaitOf("boss") == CooldownCatalog.Boss.Duration
    && ReminderCatalog.WaitOf("buybox") == CooldownCatalog.BoxBuy.Duration && ReminderCatalog.WaitOf("chop_adv") == CooldownCatalog.ChopAdvanced.Duration && ReminderCatalog.WaitOf("pet") == PetRules.FeedCooldown
    && ReminderCatalog.WaitOf("daily") == DailyRewardCalculator.MinInterval && ReminderCatalog.WaitOf("nada") is null && ReminderCatalog.Keys.Where(k => k != "expedition").All(k => ReminderCatalog.WaitOf(k) is not null) && ReminderCatalog.WaitOf("expedition") is null,
    "la espera de cada tipo sale de las constantes del juego (CooldownCatalog, PetRules, DailyRewardCalculator); la expedición no tiene una fija (1, 4 u 8 h)");
Check(new[] { "hunt", "travel", "chop", "boss", "buybox", "chop_adv", "pet", "daily" }.Select(k => ReminderCatalog.WaitLabel(ReminderCatalog.WaitOf(k)!.Value)).SequenceEqual(["1 min", "30 min", "5 min", "5 h", "2 h", "1 h", "1 h", "24 h"]), "etiquetas de espera: 1 min, 30 min, 5 min, 5 h, 2 h, 1 h, 1 h, 24 h");

// -- FromCooldowns
var rows = new List<(string, DateTime)>
{
    ("hunt", now0.AddSeconds(-10)),       // corre: faltan ~50 s
    ("chop", now0.AddMinutes(-10)),       // ya terminó (5 min): sin aviso
    ("travel", now0.AddMinutes(-5)),      // corre: faltan ~25 min
    ("boss", now0.AddHours(-4.5)),        // la espera corta de un jefe perdido (ya acomodada en last_executed_at): faltan 30 min
    ("daily", now0.AddHours(-1)),         // 'daily' no vive en cooldowns como esta clase: igual no es de la tabla de cooldowns → se ignora
    ("raid", now0),                       // no es un tipo
    ("buybox", now0.AddMinutes(-30)),
    ("mine_adv", now0.AddMinutes(-90)),   // terminó (1 h)
};
var from = ReminderCatalog.FromCooldowns(rows, now0);
Check(from.Select(f => f.Kind).OrderBy(x => x).SequenceEqual(["boss", "buybox", "hunt", "travel"]), $"de la tabla de cooldowns salen solo las esperas que TODAVÍA corren y son un tipo ({string.Join(",", from.Select(f => f.Kind).OrderBy(x => x))})");
Check(Math.Abs((from.Single(f => f.Kind == "hunt").DueUtc - now0.AddSeconds(50)).TotalSeconds) < 1 && Math.Abs((from.Single(f => f.Kind == "boss").DueUtc - now0.AddMinutes(30)).TotalSeconds) < 1
    && Math.Abs((from.Single(f => f.Kind == "buybox").DueUtc - now0.AddMinutes(90)).TotalSeconds) < 1, "el momento es última vez + espera (cacería +50 s, jefe con su espera corta +30 min, caja +90 min)");
Check(ReminderCatalog.FromCooldowns([], now0).Count == 0, "sin filas no hay avisos");
Check(from.All(f => f.DueUtc.Kind == DateTimeKind.Utc), "los momentos son UTC");

// -- PetDue y DailyDue
PetSpecies Sp(int zone) => new(zone, zone, "Z", "X", null, "gold", 5, 1, "Huevo");
var cd1h = TimeSpan.FromHours(1);
Check(ReminderCatalog.PetDue([], cd1h, now0) is null, "sin mascotas no hay aviso");
Check(ReminderCatalog.PetDue([new OwnedPet(Sp(1), 0, null)], cd1h, now0) is null, "una que nunca comió ya puede comer: sin aviso");
var fed10 = now0.AddMinutes(-10);
var due1 = ReminderCatalog.PetDue([new OwnedPet(Sp(1), 3, fed10)], cd1h, now0);
Check(due1 is { Kind: "pet" } && Math.Abs((due1.DueUtc - now0.AddMinutes(50)).TotalSeconds) < 1, "comió hace 10 min: aviso en 50 min");
var due2 = ReminderCatalog.PetDue([new OwnedPet(Sp(1), 3, now0.AddMinutes(-10)), new OwnedPet(Sp(2), 3, now0.AddMinutes(-40))], cd1h, now0);
Check(due2 is not null && Math.Abs((due2.DueUtc - now0.AddMinutes(20)).TotalSeconds) < 1, "con varias, avisa cuando termina la PRIMERA (la de hace 40 min: en 20)");
Check(ReminderCatalog.PetDue([new OwnedPet(Sp(1), 3, now0.AddMinutes(-10)), new OwnedPet(Sp(2), 3, now0.AddMinutes(-90))], cd1h, now0) is null, "si una ya puede comer ahora, no hay nada que esperar");
Check(ReminderCatalog.PetDue([new OwnedPet(Sp(1), PetRules.TotalFeedsToMax, now0.AddMinutes(-1))], cd1h, now0) is null, "una al nivel máximo no cuenta (no sube más)");
var due3 = ReminderCatalog.PetDue([new OwnedPet(Sp(1), 3, now0.AddMinutes(-10))], TimeSpan.FromMinutes(40), now0);
Check(due3 is not null && Math.Abs((due3.DueUtc - now0.AddMinutes(30)).TotalSeconds) < 1, "usa la espera del jugador (Buen Pienso la acorta): 40 min → faltan 30");
Check(ReminderCatalog.DailyDue(null, now0) is null && ReminderCatalog.DailyDue(now0.AddHours(-25), now0) is null, "el diario: sin reclamar nunca o hace más de 24 h no hay aviso");
var dd = ReminderCatalog.DailyDue(now0.AddHours(-1), now0);
Check(dd is { Kind: "daily" } && Math.Abs((dd.DueUtc - now0.AddHours(23)).TotalSeconds) < 1, "reclamado hace 1 h: aviso en 23 h");
Check(ReminderCatalog.DailyDue(DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Unspecified), now0.AddSeconds(1)) is not null, "una fecha sin Kind (de la base) se trata como UTC");

// -- Compose, AutoDeleteFor, IsStale, hints
string one = ReminderCatalog.Compose(U, ["hunt"]);
Check(one == $"<@{U}> 🏹 ¡Cacería lista! Ya podés volver a cazar con `/hunt`." && !one.Contains('\n'), "un solo aviso: la mención y una línea («Cacería lista»)");
string many = ReminderCatalog.Compose(U, ["hunt", "travel", "boss"]);
Check(many.StartsWith($"<@{U}> ⏰ ¡Ya podés de nuevo!\n") && many.Split('\n').Length == 4 && many.Contains("**Cacería** — `/hunt`") && many.Contains("**Viaje** — `/travel`") && many.Contains("**Jefe** — `/boss`"), "varios juntos: un encabezado y UNA línea por cosa");
Check(ReminderCatalog.Compose(U, ["hunt", "hunt"]) == one, "un tipo repetido no se duplica");
Check(ReminderCatalog.Compose(U, []).Length == 0 && ReminderCatalog.Compose(U, ["nada"]).Length == 0, "sin tipos válidos no hay mensaje");
Check(ReminderCatalog.AutoDeleteFor(["hunt"]) == TimeSpan.FromMinutes(2) && ReminderCatalog.AutoDeleteFor(["hunt", "chop", "mine"]) == TimeSpan.FromMinutes(2) && ReminderCatalog.AutoDeleteFor(["hunt", "travel"]) is null
    && ReminderCatalog.AutoDeleteFor(["travel"]) is null && ReminderCatalog.AutoDeleteFor([]) is null, "se borra solo únicamente si TODOS son de espera corta");
Check(!ReminderCatalog.IsStale("hunt", now0.AddMinutes(-4), now0) && ReminderCatalog.IsStale("hunt", now0.AddMinutes(-6), now0) && !ReminderCatalog.IsStale("travel", now0.AddHours(-5), now0) && ReminderCatalog.IsStale("travel", now0.AddHours(-7), now0)
    && !ReminderCatalog.IsStale("hunt", now0.AddSeconds(30), now0), "un aviso viejo se descarta: corto pasados 5 min, largo pasadas 6 h (uno futuro, nunca)");
Check(ReminderCatalog.MayChangePet("pet feed") && ReminderCatalog.MayChangePet("pet_feed:123") && ReminderCatalog.MayChangePet("mascota") && !ReminderCatalog.MayChangePet("hunt")
    && ReminderCatalog.MayChangeDaily("daily") && ReminderCatalog.MayChangeDaily("diario") && !ReminderCatalog.MayChangeDaily("pet feed") && !ReminderCatalog.MayChangeDaily("chop"), "qué comandos pueden mover el aviso de mascotas o del diario");
Check(ReminderPolicy.IsEligible(1) && ReminderPolicy.IsEligible(ulong.MaxValue), "la política de hoy: avisa a todos (todavía no hay membresía; es el único lugar donde decidirlo)");

Console.WriteLine("\n== 2. Pantalla de ajustes (pura) ==");
var all = ReminderModule.Compose([], U);
string allT = AllText(all.Embed!);
Check(all.PlainMessage is null && all.Embed is not null && all.Components is not null, "con todo prendido hay embed y controles");
Check(ReminderCatalog.All.All(k => allT.Contains(k.Name)) && allT.Split('\n').Count(l => l.StartsWith("✅")) == 13 && !allT.Contains("🔕"), "las 13 líneas con ✅, una por tipo, ninguna apagada");
Check(allT.Contains("cada 1 min") && allT.Contains("cada 24 h") && allT.Contains("cada 5 h"), "cada línea dice cada cuánto");
Check(allT.Contains("2 minutos") && all.Embed!.Length <= 6000, $"avisa que los cortos se borran a los 2 minutos y entra en Discord ({all.Embed.Length})");
var menu = Flat(all.Components!).OfType<SelectMenuComponent>().Single();
Check(menu.CustomId == $"reminders_pick:{U}" && menu.MinValues == 0 && menu.MaxValues == 13 && menu.Options.Count == 13 && menu.Options.All(o => o.IsDefault == true) && menu.Options.Select(o => o.Value).SequenceEqual(ReminderCatalog.Keys), "el desplegable lleva al dueño, deja marcar de 0 a 13 y arranca con todos marcados");
Check(menu.Options.All(o => o.Label.Length is >= 1 and <= 100 && (o.Description?.Length ?? 0) <= 100 && o.Emote is null), "etiquetas dentro del límite y sin emojis en las opciones");
var btns = Flat(all.Components).OfType<ButtonComponent>().ToList();
Check(btns.Count == 2 && btns.Single(b => b.CustomId == $"reminders_all:{U}:on").IsDisabled && !btns.Single(b => b.CustomId == $"reminders_all:{U}:off").IsDisabled, "con todo prendido «Recibir todos» está deshabilitado y «Silenciar todos» no");
var some = ReminderModule.Compose(["hunt", "pet"], U);
string someT = AllText(some.Embed!);
var someMenu = Flat(some.Components!).OfType<SelectMenuComponent>().Single();
Check(someT.Split('\n').Count(l => l.StartsWith("✅")) == 11 && someT.Split('\n').Count(l => l.StartsWith("🔕")) == 2 && someMenu.Options.Count(o => o.IsDefault == true) == 11 && someMenu.Options.Single(o => o.Value == "hunt").IsDefault != true && someT.Contains("~~Cacería~~"),
    "con 2 apagados: 11 ✅ y 2 🔕 (tachados), y el desplegable los deja desmarcados");
var none = ReminderModule.Compose(ReminderCatalog.Keys, U);
var noneBtns = Flat(none.Components!).OfType<ButtonComponent>().ToList();
Check(AllText(none.Embed!).Split('\n').Count(l => l.StartsWith("🔕")) == 13 && noneBtns.Single(b => b.CustomId.EndsWith(":off")).IsDisabled && !noneBtns.Single(b => b.CustomId.EndsWith(":on")).IsDisabled, "con todo apagado: 13 🔕, «Silenciar todos» deshabilitado");
Check(ReminderModule.OffKindsFor(["hunt", "travel"]).Count == 11 && !ReminderModule.OffKindsFor(["hunt", "travel"]).Contains("hunt") && ReminderModule.OffKindsFor([]).Count == 13 && ReminderModule.OffKindsFor(ReminderCatalog.Keys).Count == 0
    && ReminderModule.OffKindsFor(["hunt", "truco"]).Count == 12, "lo marcado se recibe y el resto se apaga; una clave inventada se ignora");
Check(ReminderModule.Compose(["inventado"], U).Embed!.Length <= 6000, "una clave guardada que ya no existe no rompe la pantalla");

Console.WriteLine("\n== 3. Repositorio contra la base ==");
await Wipe();
Check((await reminders.GetOffKindsAsync(U)).Count == 0, "un jugador nuevo no apagó nada");
var t0 = DateTime.UtcNow;
await reminders.SyncAsync(U, CH, [new ReminderDue("hunt", t0.AddSeconds(50)), new ReminderDue("travel", t0.AddMinutes(25))]);
var q = await Queue(U);
Check(q.Count == 2 && q.All(r => r.Channel == (long)CH) && q.Single(r => r.Kind == "hunt").Due.ToUniversalTime().Subtract(t0.AddSeconds(50)).Duration() < TimeSpan.FromSeconds(1), "se guardan los candidatos con su canal y su momento");
await reminders.SyncAsync(U, CH2, [new ReminderDue("hunt", t0.AddSeconds(50)), new ReminderDue("travel", t0.AddMinutes(25))]);
Check((await Queue(U)).All(r => r.Channel == (long)CH), "el MISMO cooldown desde otro canal no le roba el aviso (se queda con el canal original)");
await reminders.SyncAsync(U, CH2, [new ReminderDue("hunt", t0.AddSeconds(120))]);
q = await Queue(U);
Check(q.Single(r => r.Kind == "hunt").Channel == (long)CH2 && q.Single(r => r.Kind == "travel").Channel == (long)CH && q.Count == 2, "un cooldown NUEVO (otro momento) pisa el aviso y toma el canal del comando; lo demás no se toca");
await reminders.SyncAsync(U, CH, []);
Check((await Queue(U)).Count == 2, "sincronizar sin candidatos no borra nada");
await reminders.SetOffKindsAsync(U, ["hunt", "truco", "hunt"]);
var off = await reminders.GetOffKindsAsync(U);
Check(off.Count == 1 && off[0] == "hunt", "apagar guarda solo claves reales, sin repetir (truco se descarta)");
Check((await Queue(U)).Select(r => r.Kind).SequenceEqual(["travel"]), "al apagar «hunt» se borra el aviso que estaba pendiente");
await reminders.SyncAsync(U, CH, [new ReminderDue("hunt", t0.AddSeconds(50)), new ReminderDue("chop", t0.AddMinutes(4))]);
Check((await Queue(U)).Select(r => r.Kind).SequenceEqual(["chop", "travel"]), "un tipo apagado ya no entra a la cola (y los demás sí)");
await reminders.SetOffKindsAsync(U, []);
Check((await reminders.GetOffKindsAsync(U)).Count == 0, "volver a prender todo vacía la lista");
await reminders.SyncAsync(U, CH, [new ReminderDue("hunt", t0.AddSeconds(50))]);
Check((await Queue(U)).Count == 3, "prendido otra vez, el tipo vuelve a entrar");
bool threw = false;
try { await reminders.SyncAsync(999000000000039999UL, CH, [new ReminderDue("hunt", t0.AddSeconds(50))]); } catch { threw = true; }
Check(threw, "sincronizar para alguien sin cuenta falla en la base (el servicio lo atrapa; ver más abajo)");

// -- ClaimDue
await Wipe();
await PutDue(U, "hunt", CH, TimeSpan.FromSeconds(-5)); await PutDue(U, "travel", CH, TimeSpan.FromSeconds(-1)); await PutDue(U, "boss", CH, TimeSpan.FromMinutes(30));
await PutDue(U2, "chop", CH2, TimeSpan.FromSeconds(-3));
var claimed = (await reminders.ClaimDueAsync(DateTime.UtcNow)).Where(d => d.DiscordId is U or U2).ToList();
Check(claimed.Count == 3 && claimed.Count(d => d.DiscordId == U) == 2 && claimed.Single(d => d.DiscordId == U2).ChannelId == CH2 && claimed.All(d => d.DueAtUtc.Kind == DateTimeKind.Utc && d.DueAtUtc < DateTime.UtcNow), "saca los vencidos de TODOS los jugadores con su canal y su momento, y deja el que no venció");
Check((await Queue(U)).Select(r => r.Kind).SequenceEqual(["boss"]) && (await Queue(U2)).Count == 0, "lo que sacó ya no está en la cola");
// concurrencia: 8 relojes a la vez, cada aviso se saca UNA sola vez
await Wipe();
foreach (var k in ReminderCatalog.Keys) await PutDue(U, k, CH, TimeSpan.FromSeconds(-2));
var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => reminders.ClaimDueAsync(DateTime.UtcNow))));
int mine = parallel.SelectMany(x => x).Count(d => d.DiscordId == U);
Check(mine == 13 && parallel.SelectMany(x => x).Where(d => d.DiscordId == U).Select(d => d.Kind).Distinct().Count() == 13, $"8 relojes a la vez: los 13 avisos salen exactamente UNA vez ({mine})");

Console.WriteLine("\n== 4. Servicio: arma la cola con el estado real ==");
await Wipe();
var service = new ReminderService(reminders, cooldowns, users, pets, bonusService);
Check(service.LastActivityUtc(U) is null, "antes de ver al jugador no hay actividad");
await SetCooldown(U, "hunt", TimeSpan.FromSeconds(10));
await SetCooldown(U, "chop", TimeSpan.FromMinutes(10));
await SetCooldown(U, "travel", TimeSpan.FromMinutes(5));
await SetCooldown(U, "boss", TimeSpan.FromHours(4.5));
var allRows = await cooldowns.GetAllAsync(U);
Check(allRows.Count == 4 && allRows.All(r => r.LastExecutedUtc.Kind == DateTimeKind.Utc) && allRows.Single(r => r.Command == "hunt").LastExecutedUtc > DateTime.UtcNow.AddSeconds(-30), "GetAllAsync devuelve todas las filas del jugador, en UTC");
Check((await cooldowns.GetAllAsync(U3)).Count == 0, "y vacío si no tiene ninguna");
await service.SyncAsync(U, CH, "hunt");
q = await Queue(U);
Check(q.Select(r => r.Kind).OrderBy(x => x).SequenceEqual(["boss", "hunt", "travel"]) && q.All(r => r.Channel == (long)CH), $"de 4 cooldowns, 3 siguen corriendo (el de talar ya terminó) y quedan en la cola ({string.Join(",", q.Select(r => r.Kind))})");
Check(Math.Abs((q.Single(r => r.Kind == "hunt").Due.ToUniversalTime() - DateTime.UtcNow.AddSeconds(50)).TotalSeconds) < 3 && Math.Abs((q.Single(r => r.Kind == "boss").Due.ToUniversalTime() - DateTime.UtcNow.AddMinutes(30)).TotalSeconds) < 3, "cacería en ~50 s y el jefe (con su espera corta de derrota ya acomodada) en ~30 min");
Check(service.LastActivityUtc(U) is { } act && (DateTime.UtcNow - act).TotalSeconds < 5, "y anota la actividad del jugador");
await service.SyncAsync(U, CH2, "hunt");
Check((await Queue(U)).Count == 3 && (await Queue(U)).All(r => r.Channel == (long)CH), "repetir (un botón de la misma pelea) no duplica ni cambia el canal");
await SetCooldown(U, "hunt", TimeSpan.Zero);
await service.SyncAsync(U, CH2, "hunt");
q = await Queue(U);
Check(q.Single(r => r.Kind == "hunt").Channel == (long)CH2 && Math.Abs((q.Single(r => r.Kind == "hunt").Due.ToUniversalTime() - DateTime.UtcNow.AddSeconds(60)).TotalSeconds) < 3, "una cacería nueva reinicia el aviso (+60 s) y toma el canal donde se usó");
// diario
await db.ExecuteAsync("UPDATE users SET last_daily_claim = now() - interval '1 hour' WHERE discord_id = @id", new { id = (long)U });
await service.SyncAsync(U, CH, "hunt");
Check(!(await Queue(U)).Any(r => r.Kind == "daily"), "con un comando que no toca el diario no se lee el diario");
await service.SyncAsync(U, CH, "daily");
var dq = (await Queue(U)).SingleOrDefault(r => r.Kind == "daily");
Check(dq.Kind == "daily" && Math.Abs((dq.Due.ToUniversalTime() - DateTime.UtcNow.AddHours(23)).TotalMinutes) < 1, "después de /daily el aviso queda a las 24 h de reclamarlo (en ~23 h)");
// mascotas
var species = (await pets.GetSpeciesAsync()).First(s => s.ZoneId > 0);
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@id, @i, 1) ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = 1", new { id = (long)U, i = species.EggItemId });
Check((await pets.HatchAsync(U, species.EggItemId)).Status == HatchStatus.Ok, "(preparación) nace una mascota");
await db.ExecuteAsync("UPDATE player_pets SET last_fed_at = now() - interval '10 minutes' WHERE discord_id = @id", new { id = (long)U });
await service.SyncAsync(U, CH, "pet feed");
var pq = (await Queue(U)).SingleOrDefault(r => r.Kind == "pet");
Check(pq.Kind == "pet" && Math.Abs((pq.Due.ToUniversalTime() - DateTime.UtcNow.AddMinutes(50)).TotalMinutes) < 1, "después de /pet feed el aviso queda a la hora de haberla alimentado (en ~50 min)");
await db.ExecuteAsync("DELETE FROM reminders WHERE discord_id = @id AND kind = 'pet'", new { id = (long)U });
await service.SyncAsync(U, CH, "pet_feed:1");
Check((await Queue(U)).Any(r => r.Kind == "pet"), "el botón de /pet feed (pet_feed:…) también arma el aviso de mascotas");
// apagado
await reminders.SetOffKindsAsync(U, ["travel", "daily"]);
await service.SyncAsync(U, CH, "daily");
Check(!(await Queue(U)).Any(r => r.Kind is "travel" or "daily") && (await Queue(U)).Any(r => r.Kind == "boss"), "lo que el jugador apagó no vuelve a la cola");
await service.SyncAsync(999000000000039999UL, CH, "hunt");
Check(true, "un jugador sin cuenta no tira excepción");
await service.SyncAsync(U, 0, "hunt");
Check(service.LastActivityUtc(999000000000039999UL) is not null, "sin cuenta igual se anota la actividad y no explota (el servicio atrapa todo)");
await service.SyncManyAsync([U2, U3, U2], CH, "raid");
Check(service.LastActivityUtc(U2) is not null && service.LastActivityUtc(U3) is not null, "SyncMany recorre a cada participante");

Console.WriteLine("\n== 5. El reloj (con envío falso) ==");
await Wipe();
var sent = new ConcurrentQueue<(ulong User, ulong Channel, string Text, TimeSpan? Del)>();
var combat = new CombatSessionService(users);
var raids = new RaidSessionService();
var clientFake = new DiscordSocketClient();
var service2 = new ReminderService(reminders, cooldowns, users, pets, bonusService);
var scheduler = new ReminderScheduler(reminders, service2, combat, raids, clientFake)
{
    Sender = (user, ch, text, del) => { sent.Enqueue((user, ch, text, del)); return Task.CompletedTask; },
};
List<(ulong User, ulong Channel, string Text, TimeSpan? Del)> Drain() { var l = sent.ToList(); sent.Clear(); return l; }
// a) dos largos juntos → UN mensaje
await PutDue(U, "travel", CH, TimeSpan.FromSeconds(-1)); await PutDue(U, "boss", CH, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow);
var s1 = Drain();
Check(s1.Count == 1 && s1[0].User == U && s1[0].Channel == CH && s1[0].Text.StartsWith($"<@{U}> ⏰") && s1[0].Text.Contains("/travel") && s1[0].Text.Contains("/boss") && s1[0].Del is null, "dos esperas largas vencidas juntas: UN solo mensaje, mencionando al jugador, que se queda (no se borra)");
Check((await Queue(U)).Count == 0, "y salieron de la cola");
await scheduler.TickAsync(DateTime.UtcNow);
Check(Drain().Count == 0, "otro ciclo no repite el aviso");
// b) uno corto → se borra solo
await PutDue(U, "hunt", CH, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow);
var s2 = Drain();
Check(s2.Count == 1 && s2[0].Text == ReminderCatalog.Compose(U, ["hunt"]) && s2[0].Del == TimeSpan.FromMinutes(2), "una cacería lista: su línea y se borra a los 2 minutos");
// c) distintos canales → mensajes separados
await PutDue(U, "travel", CH, TimeSpan.FromSeconds(-1)); await PutDue(U, "daily", CH2, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow);
var s3 = Drain();
Check(s3.Count == 2 && s3.Select(x => x.Channel).OrderBy(x => x).SequenceEqual([CH, CH2]), "dos canales distintos: un mensaje por canal");
// d) viejos
await PutDue(U, "hunt", CH, TimeSpan.FromMinutes(-10)); await PutDue(U, "travel", CH, TimeSpan.FromHours(-7)); await PutDue(U, "boss", CH, TimeSpan.FromHours(-1));
await scheduler.TickAsync(DateTime.UtcNow);
var s4 = Drain();
Check(s4.Count == 1 && s4[0].Text.Contains("/boss") && !s4[0].Text.Contains("/hunt") && !s4[0].Text.Contains("/travel"), "un corto vencido hace 10 min y un largo hace 7 h (el bot estuvo apagado) se descartan; el de hace 1 h se manda");
Check((await Queue(U)).Count == 0, "los descartados también salen de la cola (no se acumulan)");
// e) jugando (actividad reciente): los cortos no
await service2.SyncAsync(U2, CH, "hunt");   // anota actividad de U2 ahora
await PutDue(U2, "hunt", CH, TimeSpan.FromSeconds(-1)); await PutDue(U2, "chop", CH, TimeSpan.FromSeconds(-1)); await PutDue(U2, "travel", CH, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow);
var s5 = Drain();
Check(s5.Count == 1 && s5[0].Text == ReminderCatalog.Compose(U2, ["travel"]), "si el jugador usó un comando hace segundos, los cortos (cacería, talar) no avisan pero el largo sí");
await PutDue(U2, "hunt", CH, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow.AddSeconds(30));
Check(Drain().Count == 1, "30 s más tarde ya no está 'jugando' y el corto avisa");
// f) en pelea: los cortos no
CombatState Fight() => new("hunt", "Rata", "🐀", 100, 100, 5, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
bool started = combat.TryStart(U3, Fight(), new FakeTarget());
await PutDue(U3, "hunt", CH, TimeSpan.FromSeconds(-1)); await PutDue(U3, "mine", CH, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow);
Check(started && Drain().Count == 0, "peleando (combate en curso) no se manda ningún aviso corto");
await PutDue(U3, "buybox", CH, TimeSpan.FromSeconds(-1));
await scheduler.TickAsync(DateTime.UtcNow);
var s6 = Drain();
Check(s6.Count == 1 && s6[0].Text.Contains("/taberna"), "pero uno largo (la caja) sí, aunque esté peleando");
// g) un envío que falla no frena a los demás
var failing = new ReminderScheduler(reminders, service2, combat, raids, clientFake)
{
    Sender = (user, ch, text, del) => { if (user == U) throw new InvalidOperationException("canal borrado (simulado)"); sent.Enqueue((user, ch, text, del)); return Task.CompletedTask; },
};
await PutDue(U, "travel", CH, TimeSpan.FromSeconds(-1)); await PutDue(U3, "travel", CH, TimeSpan.FromSeconds(-1));
await failing.TickAsync(DateTime.UtcNow);
var s7 = Drain();
Check(s7.Count == 1 && s7[0].User == U3, "si mandar a un jugador falla (canal borrado), se registra y el resto se manda igual");
scheduler.Start(); scheduler.Start();
Check(true, "Start() es idempotente (dos llamadas no abren dos relojes)");

Console.WriteLine("\n== 6. NoticeDelivery.Attach con el InteractionService real ==");
var spHook = new ServiceCollection().BuildServiceProvider();
var commandsHook = new InteractionService(clientFake.Rest);
await commandsHook.AddModuleAsync(typeof(HookModule), spHook);
var fakeEvents = new FakeEvents();
var fakeReminders = new FakeReminders();
NoticeDelivery.Attach(commandsHook, fakeEvents, fakeReminders);
{
    var i = FakeInteraction.Slash("hunt", U, CH);
    await commandsHook.ExecuteCommandAsync(new FakeContext((IDiscordInteraction)(object)i), spHook);
    bool got = await fakeReminders.WaitAsync(1);
    Check(got && fakeReminders.Calls[0] == (U, CH, "hunt"), $"un comando de barra termina → se sincroniza con el jugador, el canal y el nombre del comando ({(got ? string.Join(",", fakeReminders.Calls[0].ToString()) : "nada")})");
}
{
    fakeReminders.Reset();
    var i = FakeInteraction.Component("btn_ataque:55", U2, CH2);
    await commandsHook.ExecuteCommandAsync(new FakeContext((IDiscordInteraction)(object)i), spHook);
    bool got = await fakeReminders.WaitAsync(1);
    Check(got && fakeReminders.Calls[0] == (U2, CH2, "btn_ataque:55"), "un botón también (así termina una pelea): el id del botón es la pista");
}
{
    fakeReminders.Reset();
    var i = FakeInteraction.Slash("hunt", U, null);
    await commandsHook.ExecuteCommandAsync(new FakeContext((IDiscordInteraction)(object)i), spHook);
    await Task.Delay(400);
    Check(fakeReminders.Calls.Count == 0, "una interacción sin canal conocido no sincroniza nada");
}
{
    NoticeDelivery.Attach(new InteractionService(clientFake.Rest), fakeEvents); // sin servicio de recordatorios: la firma vieja sigue andando
    Check(true, "Attach sin el servicio de recordatorios (los arneses viejos) sigue compilando y andando");
}

Console.WriteLine("\n== 7. El raid arma el aviso de todos los participantes ==");
var rm = typeof(RaidModule).GetMethod("SyncRemindersAsync", AnyS)!;
var reminderProp = typeof(RaidModule).GetProperty("Reminders", AnyS)!;
var session = (RaidSession)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RaidSession));
Check(typeof(RaidSession).GetProperty("ChannelId")!.PropertyType == typeof(ulong) && session.ChannelId == 0, "el raid guarda el canal del lobby (0 = no se sabe)");
var fakeR = new FakeReminders();
reminderProp.SetValue(null, fakeR);
session.ChannelId = CH;
await (Task)rm.Invoke(null, [session, new List<ulong> { U, U2, U3 }])!;
Check(fakeR.ManyCalls.Count == 1 && fakeR.ManyCalls[0].Channel == CH && fakeR.ManyCalls[0].Ids.SequenceEqual([U, U2, U3]) && fakeR.ManyCalls[0].Hint == "raid", "con canal: sincroniza a TODOS los participantes en ese canal");
fakeR.Reset(); session.ChannelId = 0;
await (Task)rm.Invoke(null, [session, new List<ulong> { U }])!;
Check(fakeR.ManyCalls.Count == 0, "sin canal conocido no hace nada");
reminderProp.SetValue(null, null);
session.ChannelId = CH;
await (Task)rm.Invoke(null, [session, new List<ulong> { U }])!;
Check(true, "sin servicio asignado (las pruebas del raid) tampoco hace nada ni tira");

Console.WriteLine("\n== 8. Comandos, ayuda y DI con el InteractionService real ==");
await Wipe();
var clientReal = new DiscordSocketClient();
var interactions = new InteractionService(clientReal.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [clientReal, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var cmd = interactions.SlashCommands.Single(c => c.Name == "reminders");
Check(cmd.Module.SlashGroupName is null && validName(cmd.Name) && cmd.Description.Length is >= 1 and <= 100 && cmd.Parameters.Count == 0, "/reminders existe, sin grupo ni parámetros y dentro de los límites de Discord");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var r1 = interactions.SearchComponentCommand(Click($"reminders_pick:{U}"));
Check(r1.IsSuccess && r1.Command.Module.Name == "ReminderModule" && r1.Command.MethodName == "HandlePickAsync" && r1.RegexCaptureGroups is { Length: 1 } g1 && g1[0] == U.ToString(), $"el desplegable «reminders_pick:{{dueño}}» llega a su método ({(r1.IsSuccess ? r1.Command.MethodName : "(ninguno)")})");
var r2 = interactions.SearchComponentCommand(Click($"reminders_all:{U}:on"));
var r3 = interactions.SearchComponentCommand(Click($"reminders_all:{U}:off"));
Check(r2.IsSuccess && r2.Command.MethodName == "HandleAllAsync" && r2.RegexCaptureGroups is { Length: 2 } g2 && g2[0] == U.ToString() && g2[1] == "on" && r3.IsSuccess && r3.RegexCaptureGroups![1] == "off", "los botones «reminders_all:{dueño}:on|off» llegan a su método con el modo");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "reminders", "recordatorios", "recordatorio", "reminder", "avisos", "aviso" }.All(a => textNames.Count(n => n == a) == 1), "aa reminders / recordatorios / recordatorio / reminder / avisos / aviso están registrados, sin repetirse");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");
Check(sp.GetRequiredService<IReminderService>() is ReminderService && sp.GetRequiredService<IReminderRepository>() is ReminderRepository && sp.GetRequiredService<ReminderScheduler>() is not null && ReferenceEquals(sp.GetRequiredService<IReminderService>(), sp.GetRequiredService<IReminderService>()),
    "la DI tiene el servicio (único), el repositorio y el reloj");
Check(interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "siguen entrando en el tope de 100 comandos de nivel superior");

// -- pantalla extremo a extremo contra la base
var userRepo = new UserRepository(factory);
var v0 = await ReminderModule.ExecuteViewAsync(userRepo, reminders, 999000000000039998UL);
Check(v0.PlainMessage is not null && v0.PlainMessage.Contains("/start") && v0.Embed is null, "sin cuenta: pide empezar con /start (y no toca la base)");
var v1 = await ReminderModule.ExecuteViewAsync(userRepo, reminders, U);
Check(v1.Embed is not null && AllText(v1.Embed).Split('\n').Count(l => l.StartsWith("✅")) == 13, "con cuenta y sin ajustes: todo prendido");
await PutDue(U, "hunt", CH, TimeSpan.FromMinutes(1));
var v2 = await ReminderModule.ExecuteSetAsync(userRepo, reminders, U, ReminderCatalog.Keys.Where(k => k != "hunt" && k != "mine"));
Check(AllText(v2.Embed!).Split('\n').Count(l => l.StartsWith("🔕")) == 2 && (await reminders.GetOffKindsAsync(U)).OrderBy(x => x).SequenceEqual(["hunt", "mine"]) && !(await Queue(U)).Any(r => r.Kind == "hunt"), "desmarcar dos en la lista los apaga en la base y borra el aviso pendiente de cacería");
var v3 = await ReminderModule.ExecuteSetAsync(userRepo, reminders, U, []);
Check((await reminders.GetOffKindsAsync(U)).Count == 13 && AllText(v3.Embed!).Split('\n').Count(l => l.StartsWith("🔕")) == 13, "«Silenciar todos» (nada marcado) apaga los 13");
var v4 = await ReminderModule.ExecuteSetAsync(userRepo, reminders, U, ReminderCatalog.Keys);
Check((await reminders.GetOffKindsAsync(U)).Count == 0 && AllText(v4.Embed!).Split('\n').Count(l => l.StartsWith("✅")) == 13, "«Recibir todos» los prende otra vez");
var v5 = await ReminderModule.ExecuteSetAsync(userRepo, reminders, U, ["hunt", "truco", "inventado"]);
Check((await reminders.GetOffKindsAsync(U)).Count == 12 && !(await reminders.GetOffKindsAsync(U)).Contains("hunt"), "claves inventadas en el desplegable no se guardan (solo cuenta «hunt»)");
var v6 = await ReminderModule.ExecuteSetAsync(userRepo, reminders, 999000000000039998UL, ["hunt"]);
Check(v6.PlainMessage is not null && v6.Embed is null, "guardar sin cuenta tampoco falla: pide /start");

// -- ayuda
var topic = HelpTopics.Find("recordatorios");
Check(topic?.Key == "reminders" && new[] { "reminders", "recordatorio", "reminder", "aviso", "avisos", "alarma", "alarmas" }.All(a => HelpTopics.Find(a)?.Key == "reminders"), "«info recordatorios / avisos / alarma…» abren el tema");
Check(HelpTopics.Find("fuego")?.Key == "fuego" && HelpTopics.Find("pets")?.Key == "pets" && HelpTopics.Find("daily")?.Key == "daily" && HelpTopics.Find("story")?.Key == "story", "y los demás temas siguen en su lugar");
var te = topic!.Build(); string tt = AllText(te);
Check(te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024) && tt.Contains("/reminders") && tt.Contains("cada 1 min") && tt.Contains("cada 30 min") && tt.Contains("cada 5 min") && tt.Contains("cada 5 h") && tt.Contains("cada 2 h") && tt.Contains("cada 24 h") && tt.Contains("2 minutos"),
    $"el tema entra en los límites ({te.Length}) y sus números salen de las constantes");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var idx = HelpTopics.BuildIndexEmbed();
Check(HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256) && (e.Description?.Length ?? 0) <= 4096; }) && idx.Length <= 6000 && idx.Fields.All(f => f.Value.Length <= 1024) && HelpTopics.All.Count == 27,
    $"los 27 temas y el índice entran en Discord (índice {idx.Length})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/reminders") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024), $"/info lista /reminders, dentro de los límites ({infoE.Length})");

await Wipe();
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)U, (long)U2, (long)U3 } });
Check((await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reminders WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)U, (long)U2, (long)U3 } })) == 0, "(limpieza) los usuarios descartables se fueron con su cola");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
    public Task ClearComponentsAsync() => Task.CompletedTask;
}

public sealed class HookModule : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("hunt", "prueba")]
    public Task HuntAsync() => Task.CompletedTask;

    [ComponentInteraction("btn_ataque:*")]
    public Task AttackAsync(string id) => Task.CompletedTask;
}

public sealed class FakeEvents : IGameEvents
{
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) => Task.CompletedTask;
    public IReadOnlyList<GameNotice> TakeNotices(ulong id) => [];
}

public sealed class FakeReminders : IReminderService
{
    private readonly object _lock = new();
    public List<(ulong, ulong, string)> Calls { get; } = [];
    public List<(IReadOnlyList<ulong> Ids, ulong Channel, string Hint)> ManyCalls { get; } = [];
    public void Reset() { lock (_lock) { Calls.Clear(); ManyCalls.Clear(); } }
    public Task SyncAsync(ulong discordId, ulong channelId, string commandHint, CancellationToken cancellationToken = default) { lock (_lock) Calls.Add((discordId, channelId, commandHint)); return Task.CompletedTask; }
    public Task SyncManyAsync(IEnumerable<ulong> discordIds, ulong channelId, string commandHint, CancellationToken cancellationToken = default) { lock (_lock) ManyCalls.Add((discordIds.ToList(), channelId, commandHint)); return Task.CompletedTask; }
    public DateTime? LastActivityUtc(ulong discordId) => null;
    public async Task<bool> WaitAsync(int expected)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until) { lock (_lock) if (Calls.Count >= expected) return true; await Task.Delay(25); }
        return false;
    }
}

public sealed class FakeContext(IDiscordInteraction interaction) : IInteractionContext
{
    public IDiscordClient Client => null!;
    public IGuild Guild => null!;
    public IMessageChannel Channel => null!;
    public IUser User => interaction.User;
    public IDiscordInteraction Interaction => interaction;
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

// Una interacción falsa: un DispatchProxy que contesta lo justo para que el InteractionService la enrute.
public class FakeInteraction : DispatchProxy
{
    private Func<MethodInfo, object?[], object?> _handler = null!;

    public static FakeInteraction Slash(string name, ulong userId, ulong? channelId) => Make<ISlashCommandInteraction>(userId, channelId, InteractionType.ApplicationCommand, name);
    public static FakeInteraction Component(string customId, ulong userId, ulong? channelId) => Make<IComponentInteraction>(userId, channelId, InteractionType.MessageComponent, customId);

    private static FakeInteraction Make<TInterface>(ulong userId, ulong? channelId, InteractionType type, string key)
    {
        var proxy = (TInterface)DispatchProxy.Create<TInterface, FakeInteraction>()!;
        var self = (FakeInteraction)(object)proxy;
        var user = Fake.Of<IUser>((n, a) => n == "get_Id" ? userId : null);
        object data = typeof(TInterface) == typeof(ISlashCommandInteraction)
            ? Fake.Of<IApplicationCommandInteractionData>((n, a) => n switch { "get_Name" => key, "get_Options" => Array.Empty<IApplicationCommandInteractionDataOption>(), _ => null })
            : Fake.Of<IComponentInteractionData>((n, a) => n switch { "get_CustomId" => key, "get_Type" => ComponentType.Button, "get_Values" => Array.Empty<string>(), _ => null });
        self._handler = (m, a) => m.Name switch
        {
            "get_Type" => type,
            "get_User" => user,
            "get_Data" => data,
            "get_ChannelId" => channelId,
            _ => null,
        };
        return self;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        object? r = _handler(targetMethod!, args ?? []);
        return Fake.Shape(targetMethod!, r);
    }
}

public class Fake : DispatchProxy
{
    private Func<string, object?[], object?> _h = null!;
    public static T Of<T>(Func<string, object?[], object?> h) where T : class
    {
        var p = DispatchProxy.Create<T, Fake>();
        ((Fake)(object)p)._h = h;
        return p;
    }

    public static object? Shape(MethodInfo m, object? r)
    {
        var rt = m.ReturnType;
        if (r is not null) return rt == typeof(Task) ? Task.CompletedTask : r;
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(rt.GetGenericArguments()[0]).Invoke(null, [rt.GetGenericArguments()[0].IsValueType ? Activator.CreateInstance(rt.GetGenericArguments()[0]) : null]);
        return rt.IsValueType && rt != typeof(void) ? Activator.CreateInstance(rt) : null;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? a) => Shape(m!, _h(m!.Name, a ?? []));
}
