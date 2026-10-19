using System.Globalization;
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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Etapa 3: misiones y logros. Contra la base REAL con usuarios descartables (se borran al final).
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var zones = new ZoneRepository(factory);
ZoneBoxTable live; await using (var czb = new NpgsqlConnection(conn)) { await czb.OpenAsync(); live = await ZoneBoxQueries.LoadAsync(czb, null); }
var missions = new MissionRepository(factory);
var achievements = new AchievementRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var boxes = new BoxRepository(factory);
var notifier = new ProgressNotifier(missions);
var events = new GameEventService(eventsRepo, notifier);
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000003001UL;
const ulong V = 999000000000003002UL;
async Task Cleanup() => await db.ExecuteAsync(
    "DELETE FROM users WHERE discord_id IN (@u, @v); DELETE FROM game_events WHERE discord_id IN (@u, @v);", new { u = (long)U, v = (long)V });
async Task<int> Gold() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task<(int level, int xp, int hp, int maxHp)> Lvl() => await db.QuerySingleAsync<(int, int, int, int)>("SELECT level, xp, current_hp, max_hp FROM users WHERE discord_id = @id", new { id = (long)U });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task InsertEvent(ulong id, string kind, long amount, DateTime? at = null) => await db.ExecuteAsync(
    "INSERT INTO game_events (discord_id, kind, amount, occurred_at) VALUES (@id, @kind, @amount, COALESCE(@at, now()))", new { id = (long)id, kind, amount, at });
async Task<int> Claims(string table) => await db.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table} WHERE discord_id = @id", new { id = (long)U });
async Task ResetPlayer(int level = 1, int xp = 0, int gold = 0, int zone = 1)
{
    await Cleanup();
    await users.GetOrCreateUserAsync(U);
    await users.GetOrCreateUserAsync(V);
    await db.ExecuteAsync("UPDATE users SET level = @level, xp = @xp, gold = @gold, current_zone_id = @zone, current_hp = max_hp WHERE discord_id = @id", new { id = (long)U, level, xp, gold, zone });
}

var now = DateTime.UtcNow;
var dayStart = UruguayCalendar.DayStartUtc(now);
var dayEnd = UruguayCalendar.NextDayStartUtc(now);
var weekStart = UruguayCalendar.WeekStartUtc(now);
var weekEnd = UruguayCalendar.NextWeekStartUtc(now);
DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

Console.WriteLine("== 0. Registro de comandos y dependencias ==");
{
    var client = new DiscordSocketClient();
    var interactions = new InteractionService(client.Rest);
    var texts = new CommandService();
    texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
    var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
    try
    {
        var m = await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
        var t = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
        Check(interactions.SlashCommands.Any(c => c.Name == "missions") && interactions.SlashCommands.Any(c => c.Name == "achievements"), $"/misiones y /logros registrados ({interactions.SlashCommands.Count} comandos de barra)");
        Check(interactions.ComponentCommands.Any(c => c.Name.StartsWith("missions_claim")) && interactions.ComponentCommands.Any(c => c.Name.StartsWith("achievements_claim")), "los botones de reclamar de misiones y logros están registrados");
        Check(t.SelectMany(x => x.Commands).Any(c => c.Name == "missions" && c.Aliases.Contains("missions")) && t.SelectMany(x => x.Commands).Any(c => c.Name == "achievements" && c.Aliases.Contains("achievements")), "aa misiones / aa logros registrados");
        Check(sp.GetService<IProgressNotifier>() is not null && sp.GetService<IMissionRepository>() is not null && sp.GetService<IAchievementRepository>() is not null, "el DI real tiene el notificador y los dos repositorios");
        var broken = new List<string>();
        foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
        {
            try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
        }

        Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
        // El servicio de eventos del DI real YA trae el notificador: se nota porque un evento completa una misión y deja el aviso.
        var realEvents = sp.GetRequiredService<IGameEvents>();
        Check(realEvents is GameEventService, "IGameEvents del DI real es GameEventService");
    }
    catch (Exception ex) { Check(false, "registro de módulos: " + ex.Message); }
}

Console.WriteLine("\n== 1. Calendario de Uruguay (UTC-3, sin horario de verano) ==");
Check(UruguayCalendar.DayStartUtc(Utc(2026, 10, 1, 2, 59, 59)) == Utc(2026, 9, 30, 3), "02:59:59Z del 1/10 todavía es el día 30/9 de Uruguay (el día empezó 03:00Z del 30/9)");
Check(UruguayCalendar.DayStartUtc(Utc(2026, 10, 1, 3)) == Utc(2026, 10, 1, 3) && UruguayCalendar.DayStartUtc(Utc(2026, 10, 1, 3)).Kind == DateTimeKind.Utc, "03:00:00Z del 1/10 ya es el día 1/10 (medianoche de Uruguay) y el resultado es UTC");
Check(UruguayCalendar.NextDayStartUtc(Utc(2026, 10, 1, 14)) == Utc(2026, 10, 2, 3), "el siguiente corte es 03:00Z del día siguiente");
Check(UruguayCalendar.WeekStartUtc(Utc(2026, 10, 1, 14)) == Utc(2026, 9, 28, 3), "el jueves 1/10 pertenece a la semana que empezó el lunes 28/9 a las 03:00Z");
Check(UruguayCalendar.WeekStartUtc(Utc(2026, 10, 4, 20)) == Utc(2026, 9, 28, 3), "el domingo 4/10 por la tarde sigue en esa semana");
Check(UruguayCalendar.WeekStartUtc(Utc(2026, 10, 5, 2, 59, 59)) == Utc(2026, 9, 28, 3), "el lunes 5/10 02:59:59Z (domingo 23:59 en Uruguay) todavía es la semana vieja");
Check(UruguayCalendar.WeekStartUtc(Utc(2026, 10, 5, 3)) == Utc(2026, 10, 5, 3), "el lunes 5/10 03:00Z empieza la semana nueva");
Check(UruguayCalendar.NextWeekStartUtc(Utc(2026, 10, 1)) == Utc(2026, 10, 5, 3), "la semana siguiente empieza el lunes 5/10 a las 03:00Z");
for (int d = 0; d < 730; d++)
{
    var t = Utc(2026, 1, 1, 5).AddDays(d).AddMinutes(d * 7);
    var s = UruguayCalendar.DayStartUtc(t);
    var w = UruguayCalendar.WeekStartUtc(t);
    if (!(s <= t && t < s.AddDays(1) && s.Hour == 3 && s.Minute == 0 && w <= t && t < w.AddDays(7) && w.Hour == 3 && UruguayCalendar.ToLocal(w).DayOfWeek == DayOfWeek.Monday))
    {
        Check(false, $"invariantes del calendario rotos en {t:o}");
        break;
    }
}

Check(true, "730 días seguidos: el día siempre contiene al instante, cada corte cae a las 03:00Z y cada semana arranca un lunes (hora de Uruguay)");
Check(UruguayCalendar.FormatRemaining(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(12)) == "5 h 12 min" && UruguayCalendar.FormatRemaining(TimeSpan.FromDays(2) + TimeSpan.FromHours(4)) == "2 d 4 h"
    && UruguayCalendar.FormatRemaining(TimeSpan.FromMinutes(38)) == "38 min" && UruguayCalendar.FormatRemaining(TimeSpan.FromSeconds(10)) == "1 min" && UruguayCalendar.FormatRemaining(TimeSpan.Zero) == "un instante", "formato del tiempo que falta");

Console.WriteLine("\n== 2. Catálogo de misiones ==");
var allMissions = MissionCatalog.DailyPool.Concat(MissionCatalog.WeeklyPool).ToList();
Check(allMissions.Select(m => m.Key).Distinct().Count() == allMissions.Count, $"las {allMissions.Count} misiones del catálogo tienen claves únicas");
Check(allMissions.All(m => m.Target > 0 && m.Reward.GoldUnits > 0 && m.Reward.XpPercent > 0 && !string.IsNullOrWhiteSpace(m.Title) && m.Kind.Length > 0), "toda misión tiene meta, premio y título");
Check(MissionCatalog.DailyPool.All(m => m.Period == MissionPeriod.Daily && m.Key.StartsWith("d_")) && MissionCatalog.WeeklyPool.All(m => m.Period == MissionPeriod.Weekly && m.Key.StartsWith("w_")), "cada misión está en el pool de su período");
var kindConsts = typeof(GameEventKinds).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
Check(allMissions.All(m => kindConsts.Contains(m.Kind)), "todas las misiones cuentan un evento que existe en GameEventKinds");
// Las misiones tienen que poder cumplirlas todos: ninguna cuenta eventos que dependan de nivel o zona (jefes) ni de una cantidad fija de oro.
var forbidden = new[] { GameEventKinds.BossWin, GameEventKinds.RaidWin, GameEventKinds.ShopGoldSpent, GameEventKinds.GoldGiven };
Check(allMissions.All(m => !forbidden.Contains(m.Kind)), "ninguna misión depende de jefes ni de cantidades de oro (no valen igual en todas las zonas)");

var pickedDaily = new Dictionary<string, int>();
var pickedWeekly = new Dictionary<string, int>();
bool selectionOk = true, deterministic = true, distinctKinds = true;
for (int d = 0; d < 365; d++)
{
    var day = Utc(2026, 1, 1, 6).AddDays(d);
    var ds = UruguayCalendar.DayStartUtc(day);
    var ws = UruguayCalendar.WeekStartUtc(day);
    var dailySet = MissionCatalog.ForPeriod(MissionPeriod.Daily, ds);
    var weeklySet = MissionCatalog.ForPeriod(MissionPeriod.Weekly, ws);
    selectionOk &= dailySet.Count == MissionCatalog.DailyCount && weeklySet.Count == MissionCatalog.WeeklyCount;
    distinctKinds &= dailySet.Select(m => m.Kind).Distinct().Count() == dailySet.Count && weeklySet.Select(m => m.Kind).Distinct().Count() == weeklySet.Count;
    deterministic &= MissionCatalog.ForPeriod(MissionPeriod.Daily, ds).Select(m => m.Key).SequenceEqual(dailySet.Select(m => m.Key));
    foreach (var m in dailySet) pickedDaily[m.Key] = pickedDaily.GetValueOrDefault(m.Key) + 1;
    if (day.DayOfWeek == DayOfWeek.Monday) foreach (var m in weeklySet) pickedWeekly[m.Key] = pickedWeekly.GetValueOrDefault(m.Key) + 1;
}

Check(selectionOk, "cada día salen exactamente 3 misiones diarias y cada semana 2 semanales");
Check(distinctKinds, "nunca salen dos misiones que cuenten el mismo tipo de evento (no 'ganá 10 cacerías' y 'ganá 20' a la vez)");
Check(deterministic, "las misiones de un día son siempre las mismas (misma entrada, mismo resultado)");
Check(MissionCatalog.DailyPool.All(m => pickedDaily.GetValueOrDefault(m.Key) > 0) && MissionCatalog.WeeklyPool.All(m => pickedWeekly.GetValueOrDefault(m.Key) > 0),
    $"en un año sale cada misión del catálogo al menos una vez (diarias entre {pickedDaily.Values.Min()} y {pickedDaily.Values.Max()} días de 365; semanales entre {pickedWeekly.Values.Min()} y {pickedWeekly.Values.Max()} de ~52)");
var consecutive = 0;
for (int d = 1; d < 365; d++)
{
    var a = MissionCatalog.ForPeriod(MissionPeriod.Daily, UruguayCalendar.DayStartUtc(Utc(2026, 1, 1, 6).AddDays(d - 1))).Select(m => m.Key).OrderBy(x => x);
    var b = MissionCatalog.ForPeriod(MissionPeriod.Daily, UruguayCalendar.DayStartUtc(Utc(2026, 1, 1, 6).AddDays(d))).Select(m => m.Key).OrderBy(x => x);
    if (a.SequenceEqual(b)) consecutive++;
}

Check(consecutive < 12, $"los días seguidos no se repiten (solo {consecutive} de 364 pares de días consecutivos tienen exactamente las mismas 3)");

Console.WriteLine("\n== 3. Premios ==");
Check(new[] { 0, 1, 2, 3, 4, 5, 9 }.Select(MissionRewards.GoldUnit).SequenceEqual([14, 14, 58, 118, 215, 375, 375]), "oro por cacería: zonas 1..5 = 14/58/118/215/375 (zona desconocida = la primera, muy arriba = la última)");
Check(MissionRewards.BoxName(BoxGrant.ZoneTier, 1, live) == "Cajón de Pino" && MissionRewards.BoxName(BoxGrant.ZoneTier, 3, live) == "Arcón de Hierro" && MissionRewards.BoxName(BoxGrant.ZoneTier, 5, live) == "Cofre de Oro"
    && MissionRewards.BoxName(BoxGrant.ZoneTierPlusOne, 1, live) == "Baúl de Roble" && MissionRewards.BoxName(BoxGrant.ZoneTierPlusOne, 4, live) == "Cofre de Oro" && MissionRewards.BoxName(BoxGrant.Mythic, 1, live) == "Arca del Soberano" && MissionRewards.BoxName(BoxGrant.None, 3, live) is null,
    "la caja sigue la zona (Cajón -> Baúl -> Arcón -> Cofre), la 'una más' sube un escalón sin pasarse del Cofre, y la Mítica es solo la Mítica");
var r1 = MissionRewards.Resolve(new RewardSpec(6, 4), 1, 1, live);
var r10 = MissionRewards.Resolve(new RewardSpec(6, 4), 5, 10, live);
Check(r1 == new ResolvedReward(84, 4, null, 0) && r10.Gold == 2250 && r10.Xp == (int)(LevelingCalculator.RequiredXpForLevel(10) * 4 / 100.0), $"6 unidades de oro: zona 1 = {r1.Gold}, zona 5 = {r10.Gold}; el XP es 4% del nivel ({r1.Xp} a nivel 1, {r10.Xp} a nivel 10)");
Check(MissionRewards.Resolve(new RewardSpec(0, 1), 1, 1, live).Xp >= 1 && MissionRewards.Resolve(new RewardSpec(0, 0), 1, 7, live).Xp == 0, "el XP nunca queda en 0 si el premio lo tiene, y es 0 si no hay");
Check(MissionRewards.Describe(MissionRewards.Resolve(new RewardSpec(6, 4, BoxGrant.ZoneTier, 2), 2, 5, live)).Contains("💰 348 oro") && MissionRewards.Describe(MissionRewards.Resolve(new RewardSpec(0, 0), 1, 1, live)) == "—", "el texto del premio muestra oro, XP y cajas (y '—' si no hay)");
// Calibración: lo que vale cada período en "cacerías de oro" de la zona del jugador.
int dailyAvg = (int)MissionCatalog.DailyPool.Average(m => m.Reward.GoldUnits) * MissionCatalog.DailyCount;
int weeklyAvg = (int)MissionCatalog.WeeklyPool.Average(m => m.Reward.GoldUnits) * MissionCatalog.WeeklyCount;
Check(dailyAvg is >= 10 and <= 30 && weeklyAvg is >= 50 and <= 140, $"calibración: 3 diarias ~{dailyAvg} cacerías de oro (+ una caja de la zona), 2 semanales ~{weeklyAvg} (+ una caja mayor)");
var tot = new RewardTotals();
tot.Add(new RewardReceipt(new ResolvedReward(100, 10, "Cajón de Pino", 1), 500, 0, 3));
tot.Add(new RewardReceipt(new ResolvedReward(50, 5, "Cajón de Pino", 1), 550, 1, 4));
Check(tot.Gold == 150 && tot.Xp == 15 && tot.LevelsGained == 1 && tot.NewLevel == 4 && tot.GoldAfter == 550 && tot.Describe() == "💰 +150 oro · ⭐ +15 XP · 📦 2× Cajón de Pino", $"el recibo suma premios: «{tot.Describe()}»");

Console.WriteLine("\n== 4. Catálogo de logros ==");
var ach = AchievementCatalog.All;
Check(ach.Select(a => a.Key).Distinct().Count() == ach.Count && ach.All(a => a.Tiers.Count == 3), $"{ach.Count} logros con claves únicas y 3 tramos cada uno");
Check(ach.All(a => a.Tiers.Zip(a.Tiers.Skip(1), (x, y) => y.Threshold > x.Threshold && y.Reward.GoldUnits > x.Reward.GoldUnits && y.Reward.XpPercent > x.Reward.XpPercent).All(ok => ok)), "las metas y los premios suben de tramo en tramo");
Check(ach.All(a => kindConsts.Contains(a.StatKind)), "todo logro se alimenta de un evento que existe");
var mythic = ach.Where(a => a.Tiers.Any(t => t.Reward.Box == BoxGrant.Mythic)).Select(a => a.Key + ":" + (a.Tiers.ToList().FindIndex(t => t.Reward.Box == BoxGrant.Mythic) + 1)).ToList();
Check(mythic.Count == 1 && mythic.Contains("matajefes:3"), $"el Arca del Soberano (Mítica) ahora solo sale de 1 tramo III (Coleccionista se retiró con los trofeos): {string.Join(", ", mythic)}");
var crossed = AchievementCatalog.Crossed(GameEventKinds.HuntWin, 24, 25);
Check(crossed.Count == 1 && crossed[0].Achievement.Key == "cazador" && crossed[0].Tier == 1, "cruzar la meta avisa una vez (24 -> 25 cacerías = Cazador I)");
Check(AchievementCatalog.Crossed(GameEventKinds.HuntWin, 25, 26).Count == 0 && AchievementCatalog.Crossed(GameEventKinds.HuntWin, 5, 5).Count == 0, "estar por encima o no moverse no avisa de nuevo");
Check(AchievementCatalog.Crossed(GameEventKinds.HuntWin, 0, 700).Select(c => c.Tier).SequenceEqual([1, 2, 3]) && AchievementCatalog.Crossed(GameEventKinds.Craft, 0, 1).Single().Achievement.Key == "herrero", "un salto grande cruza varios tramos a la vez");
Check(AchievementCatalog.TiersReached(ach[0], 24) == 0 && AchievementCatalog.TiersReached(ach[0], 25) == 1 && AchievementCatalog.TiersReached(ach[0], 150) == 2 && AchievementCatalog.TiersReached(ach[0], 99999) == 3, "tramos alcanzados: 24 -> 0, 25 -> 1, 150 -> 2, muchos -> 3");
int trophies = await db.ExecuteScalarAsync<int>("SELECT count(*) FROM items i WHERE i.type = 'Material' AND NOT EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)");
Check(trophies == 0, $"ya no hay trofeos en el catálogo (materiales que ningún monstruo suelta): la base tiene {trophies} y el logro Coleccionista no existe ({AchievementCatalog.All.Any(a => a.Key == "coleccionista")})");
var boxNamesInDb = (await db.QueryAsync<string>("SELECT name FROM items WHERE type = 'Caja'")).ToHashSet();
Check(live.Rows.Select(r => r.BoxName).Distinct().All(boxNamesInDb.Contains), $"las {live.Rows.Select(r => r.BoxName).Distinct().Count()} cajas que se regalan existen en la base como ítems de tipo Caja");

Console.WriteLine("\n== 5. Progreso y cobro de misiones (base real) ==");
await ResetPlayer();
// Eventos: dentro del día (cuentan), de ayer (no), de otro tipo (no) y de otro jugador (no).
await InsertEvent(U, GameEventKinds.HuntWin, 2, dayStart.AddSeconds(1));
await InsertEvent(U, GameEventKinds.HuntWin, 3, dayStart.AddMinutes(30));
await InsertEvent(U, GameEventKinds.HuntWin, 10, dayStart.AddMinutes(-1));
await InsertEvent(U, GameEventKinds.TravelWin, 4, dayStart.AddSeconds(5));
await InsertEvent(V, GameEventKinds.HuntWin, 99, dayStart.AddSeconds(5));
var prog = await missions.GetProgressAsync(U, dayStart, [GameEventKinds.HuntWin, GameEventKinds.TravelWin, GameEventKinds.Chop]);
Check(prog.GetValueOrDefault(GameEventKinds.HuntWin) == 5 && prog.GetValueOrDefault(GameEventKinds.TravelWin) == 4 && !prog.ContainsKey(GameEventKinds.Chop) && prog.Count == 2,
    "el progreso suma solo lo de hoy, del jugador y de ese tipo (5 cacerías, 4 viajes, 0 talas)");
Check((await missions.GetProgressAsync(U, dayStart, [])).Count == 0, "sin tipos pedidos devuelve vacío sin tocar la base");

MissionTemplate T(string key, string kind, long target, RewardSpec reward, MissionPeriod period = MissionPeriod.Daily) => new(key, period, kind, target, "Misión de prueba " + key, reward);
var mA = T("t_a", GameEventKinds.HuntWin, 5, new RewardSpec(6, 4));
var early = await missions.ClaimMissionAsync(U, T("t_big", GameEventKinds.HuntWin, 6, new RewardSpec(6, 4)), dayStart, dayEnd, 1);
Check(early.Status == ClaimStatus.NotCompleted && await Gold() == 0 && await Claims("mission_claims") == 0, "cobrar sin llegar a la meta (5 de 6): NotCompleted, no paga y no deja marca");
var c1 = await missions.ClaimMissionAsync(U, mA, dayStart, dayEnd, 1);
var lv = await Lvl();
Check(c1.Status == ClaimStatus.Claimed && c1.Receipt!.Reward.Gold == 84 && await Gold() == 84 && c1.Receipt.GoldAfter == 84 && lv.xp == 4 && lv.level == 1 && await Claims("mission_claims") == 1,
    $"con la meta cumplida (5 de 5) cobra: +84 oro (6 x 14), +4 XP, una marca. Recibo: {MissionRewards.Describe(c1.Receipt!.Reward)}");
var c2 = await missions.ClaimMissionAsync(U, mA, dayStart, dayEnd, 1);
Check(c2.Status == ClaimStatus.AlreadyClaimed && await Gold() == 84 && (await Lvl()).xp == 4, "cobrarla de nuevo: AlreadyClaimed y no paga otra vez");
var claimedSet = await missions.GetClaimedAsync(U, MissionPeriod.Daily, dayStart);
Check(claimedSet.SetEquals(["t_a"]) && (await missions.GetClaimedAsync(U, MissionPeriod.Weekly, dayStart)).Count == 0 && (await missions.GetClaimedAsync(U, MissionPeriod.Daily, dayStart.AddDays(-1))).Count == 0, "GetClaimed devuelve solo lo cobrado en ese período (no se mezcla con la semana ni con otro día)");
var yesterday = await missions.ClaimMissionAsync(U, T("t_old", GameEventKinds.HuntWin, 1, new RewardSpec(6, 4)), dayStart.AddDays(-1), dayStart, 1);
Check(yesterday.Status == ClaimStatus.Expired && await Gold() == 84, "cobrar una misión de ayer (el período ya cerró): Expired, no paga");
var stranger = await missions.ClaimMissionAsync(999000000000003999UL, mA, dayStart, dayEnd, 1);
Check(stranger.Status == ClaimStatus.NotCompleted, "un jugador que no existe no cobra nada (y no tira excepción)");

// Concurrencia: 20 cobros simultáneos de la misma misión pagan UNA sola vez.
var mC = T("t_conc", GameEventKinds.TravelWin, 4, new RewardSpec(10, 4));
int goldBefore = await Gold();
var race = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
{
    try { return (await missions.ClaimMissionAsync(U, mC, dayStart, dayEnd, 1)).Status; }
    catch (Exception ex) { Console.WriteLine("    excepción en carrera: " + ex.Message.Split((char)10)[0]); return (ClaimStatus?)null; }
})));
Check(race.Count(s => s == ClaimStatus.Claimed) == 1 && race.Count(s => s == ClaimStatus.AlreadyClaimed) == 19 && await Gold() == goldBefore + 140, $"20 cobros a la vez: {race.Count(s => s == ClaimStatus.Claimed)} cobrado, {race.Count(s => s == ClaimStatus.AlreadyClaimed)} ya cobrados, oro +{await Gold() - goldBefore} (esperado +140, UNA vez)");

// Escala con la zona: a Zona 3 la misma misión paga por 118.
var mZ = T("t_z3", GameEventKinds.HuntWin, 5, new RewardSpec(6, 4));
goldBefore = await Gold();
var z3 = await missions.ClaimMissionAsync(U, mZ, dayStart, dayEnd, 3);
Check(z3.Status == ClaimStatus.Claimed && await Gold() == goldBefore + 6 * 118, $"en Zona 3 la misma misión paga 6 x 118 = {6 * 118} (+{await Gold() - goldBefore})");

// Premio por completar todas: exige haber cobrado TODAS las del período.
var set3 = new List<MissionTemplate> { T("t_b1", GameEventKinds.HuntWin, 5, new RewardSpec(1, 1)), T("t_b2", GameEventKinds.TravelWin, 4, new RewardSpec(1, 1)), T("t_b3", GameEventKinds.Chop, 1, new RewardSpec(1, 1)) };
await InsertEvent(U, GameEventKinds.Chop, 1, dayStart.AddSeconds(9));
var bonusEarly = await missions.ClaimBonusAsync(U, MissionPeriod.Daily, set3, dayStart, dayEnd, 1);
Check(bonusEarly.Status == ClaimStatus.NotCompleted && await Have("Cajón de Pino") == 0, "el premio por completar todas sin haber cobrado ninguna: NotCompleted, sin caja");
await missions.ClaimMissionAsync(U, set3[0], dayStart, dayEnd, 1);
await missions.ClaimMissionAsync(U, set3[1], dayStart, dayEnd, 1);
Check((await missions.ClaimBonusAsync(U, MissionPeriod.Daily, set3, dayStart, dayEnd, 1)).Status == ClaimStatus.NotCompleted, "con 2 de 3 cobradas todavía no se puede");
await missions.ClaimMissionAsync(U, set3[2], dayStart, dayEnd, 1);
var bonusRace = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
{
    try { return (await missions.ClaimBonusAsync(U, MissionPeriod.Daily, set3, dayStart, dayEnd, 1)).Status; } catch (Exception ex) { Console.WriteLine("    excepción: " + ex.Message.Split((char)10)[0]); return (ClaimStatus?)null; }
})));
Check(bonusRace.Count(s => s == ClaimStatus.Claimed) == 1 && bonusRace.Count(s => s == ClaimStatus.AlreadyClaimed) == 9 && await Have("Cajón de Pino") == 1, $"las 3 cobradas: el premio mayor se paga UNA sola vez entre 10 intentos simultáneos y llega 1 Cajón de Pino (zona 1)");
var weeklyBonus = await missions.ClaimBonusAsync(U, MissionPeriod.Weekly, [T("t_w", GameEventKinds.HuntWin, 1, new RewardSpec(1, 1), MissionPeriod.Weekly)], weekStart, weekEnd, 1);
Check(weeklyBonus.Status == ClaimStatus.NotCompleted, "el período semanal tiene sus propias marcas (no se mezcla con el diario)");

// Subida de nivel por el XP del premio.
await db.ExecuteAsync("UPDATE users SET level = 1, xp = 99, current_hp = 1 WHERE discord_id = @id", new { id = (long)U });
await InsertEvent(U, GameEventKinds.Mine, 1, dayStart.AddSeconds(12));
var up = await missions.ClaimMissionAsync(U, T("t_lvl", GameEventKinds.Mine, 1, new RewardSpec(1, 4)), dayStart, dayEnd, 1);
lv = await Lvl();
Check(up.Status == ClaimStatus.Claimed && up.Receipt!.LevelsGained == 1 && up.Receipt.NewLevel == 2 && lv.level == 2 && lv.xp == 3 && lv.hp == lv.maxHp && lv.maxHp > 100, $"el XP del premio sube de nivel (nivel 2, xp sobrante {lv.xp}) y cura al máximo ({lv.hp}/{lv.maxHp})");

// Si la caja del premio no existe, NO se cobra nada (ni oro ni la marca): todo o nada.
await db.ExecuteAsync("UPDATE users SET gold = 1000 WHERE discord_id = @id", new { id = (long)U });
await InsertEvent(U, GameEventKinds.DailyClaim, 1, dayStart.AddSeconds(15));
int claimsBefore = await Claims("mission_claims");
await db.ExecuteAsync("UPDATE items SET name = 'Arca del Soberano__prueba' WHERE name = 'Arca del Soberano'");
try
{
    try
    {
        await missions.ClaimMissionAsync(U, T("t_boom", GameEventKinds.DailyClaim, 1, new RewardSpec(9, 4, BoxGrant.Mythic)), dayStart, dayEnd, 1);
        Check(false, "con la caja inexistente debía fallar");
    }
    catch (InvalidOperationException ex)
    {
        Check(ex.Message.Contains("Arca del Soberano") && await Gold() == 1000 && await Claims("mission_claims") == claimsBefore, "premio con una caja que no existe: falla en voz alta y se revierte TODO (oro 1000 intacto, sin marca de cobro)");
    }
}
finally
{
    await db.ExecuteAsync("UPDATE items SET name = 'Arca del Soberano' WHERE name = 'Arca del Soberano__prueba'");
}

Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM items WHERE name = 'Arca del Soberano'") == 1, "la caja Mítica quedó con su nombre (la prueba de recién la renombró un instante)");

Console.WriteLine("\n== 6. Logros: cobro por tramos ==");
await ResetPlayer();
var cazador = AchievementCatalog.Find("cazador")!;
await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, 'hunt_win', 30)", new { id = (long)U });
var a1 = await achievements.ClaimTierAsync(U, cazador, 1, 1);
Check(a1.Status == ClaimStatus.Claimed && await Gold() == 12 * 14 && a1.Receipt!.Reward.Xp == 4, $"Cazador I (30 de 25): cobra 12 x 14 = {12 * 14} de oro y 4 XP");
Check((await achievements.ClaimTierAsync(U, cazador, 2, 1)).Status == ClaimStatus.NotCompleted && (await achievements.ClaimTierAsync(U, cazador, 1, 1)).Status == ClaimStatus.AlreadyClaimed && await Gold() == 168, "el tramo II sin llegar (30 de 150) no cobra, y el I de nuevo no paga otra vez");
Check((await achievements.ClaimTierAsync(U, cazador, 0, 1)).Status == ClaimStatus.NotCompleted && (await achievements.ClaimTierAsync(U, cazador, 4, 1)).Status == ClaimStatus.NotCompleted, "tramos que no existen (0 y 4) no cobran ni tiran excepción");
await db.ExecuteAsync("UPDATE player_stats SET value = 150 WHERE discord_id = @id AND stat_key = 'hunt_win'", new { id = (long)U });
int g0 = await Gold();
var a2 = await achievements.ClaimTierAsync(U, cazador, 2, 1);
Check(a2.Status == ClaimStatus.Claimed && await Gold() == g0 + 40 * 14 && await Have("Cajón de Pino") == 1, "Cazador II (150): 40 x 14 de oro y un Cajón de Pino (la caja de la zona 1)");
await db.ExecuteAsync("UPDATE player_stats SET value = 600 WHERE discord_id = @id AND stat_key = 'hunt_win'", new { id = (long)U });
g0 = await Gold();
var raceA = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
{
    try { return (await achievements.ClaimTierAsync(U, cazador, 3, 1)).Status; } catch (Exception ex) { Console.WriteLine("    excepción: " + ex.Message.Split((char)10)[0]); return (ClaimStatus?)null; }
})));
Check(raceA.Count(s => s == ClaimStatus.Claimed) == 1 && raceA.Count(s => s == ClaimStatus.AlreadyClaimed) == 19 && await Gold() == g0 + 100 * 14 && await Have("Baúl de Roble") == 1, "Cazador III con 20 cobros a la vez: se paga UNA vez (100 x 14 y un Baúl de Roble, la caja 'una más' de la zona 1)");
var claimedTiers = await achievements.GetClaimedTiersAsync(U);
Check(claimedTiers.Count == 1 && claimedTiers["cazador"].SetEquals([1, 2, 3]), "GetClaimedTiers: Cazador con los 3 tramos cobrados");

Console.WriteLine("\n== 7. Colección de trofeos ==");
// El mecanismo de la colección quedó DORMIDO (v0.14.4: ya no hay trofeos reales): se prueba con tres materiales temporales que ningún monstruo suelta.
foreach (var tn in new[] { "ZZ Trofeo prueba A", "ZZ Trofeo prueba B", "ZZ Trofeo prueba C" })
{
    await db.ExecuteAsync("INSERT INTO items (name, type, rarity, sell_price, buy_price) VALUES (@n, 'Material', 'Común', 0, 0) ON CONFLICT (name) DO NOTHING", new { n = tn });
}
await ResetPlayer();
await Give("Cajón de Pino", 5);
var pino = await items.GetByNameAsync("Cajón de Pino");
async Task<BoxLootResult> LootOf(params string[] names)
{
    var list = new List<LootedItem>();
    foreach (var n in names)
    {
        var it = (await items.GetByNameAsync(n))!;
        list.Add(new LootedItem(it.ItemId, it.Name, it.Rarity, it.Emoji, 1));
    }

    return new BoxLootResult(0, list);
}

var o1 = await boxes.OpenAsync(U, pino!.ItemId, await LootOf("ZZ Trofeo prueba A", "Garra de Puma Cenizo"));
Check(o1 is not null && o1.NewTrophies!.SequenceEqual(["ZZ Trofeo prueba A"]), $"un trofeo (prueba A) entra a la colección y un material de monstruo (Garra de Puma Cenizo) NO ({string.Join(", ", o1?.NewTrophies ?? [])})");
var o2 = await boxes.OpenAsync(U, pino.ItemId, await LootOf("ZZ Trofeo prueba A"));
Check(o2 is not null && o2.NewTrophies!.Count == 0, "el mismo trofeo por segunda vez no suma (la colección cuenta distintos)");
var o3 = await boxes.OpenAsync(U, pino.ItemId, await LootOf("ZZ Trofeo prueba B", "ZZ Trofeo prueba A", "ZZ Trofeo prueba C"));
Check(o3 is not null && o3.NewTrophies!.SequenceEqual(["ZZ Trofeo prueba B", "ZZ Trofeo prueba C"]) && await Claims("player_collection") == 3, "una apertura con 2 trofeos nuevos y 1 repetido suma solo los nuevos (colección = 3)");
var o4 = await boxes.OpenAsync(U, pino.ItemId, await LootOf("Mate Amargo"));
Check(o4 is not null && o4.NewTrophies!.Count == 0, "comida en el botín no es un trofeo");
var embedT = BoxModule.BuildOpenedEmbed(pino, 1, 1, 0, [], 0, ["ZZ Trofeo prueba A"]);
Check(embedT.Fields.Any(f => f.Name.Contains("colección") && f.Value.Contains("ZZ Trofeo prueba A")) && !BoxModule.BuildOpenedEmbed(pino, 1, 1, 0, [], 0).Fields.Any(f => f.Name.Contains("colección")), "la pantalla de /abrir avisa los trofeos nuevos (y no muestra el campo si no hay)");

await db.ExecuteAsync("DELETE FROM items WHERE name LIKE 'ZZ Trofeo prueba %'");
Console.WriteLine("\n== 8. Avisos: se avisa justo al cruzar la meta ==");
{
    await ResetPlayer();
    var testNow = Utc(2026, 3, 10, 15);
    var tStart = UruguayCalendar.DayStartUtc(testNow);
    var tDaily = MissionCatalog.ForPeriod(MissionPeriod.Daily, tStart);
    var m0 = tDaily[0];
    long total = 0;
    async Task<IReadOnlyList<GameNotice>> Fire(string kind, long amount, DateTime? at = null)
    {
        await InsertEvent(U, kind, amount, at ?? tStart.AddHours(2));
        total += amount;
        return await notifier.OnEventAsync(U, kind, amount, total, testNow);
    }

    bool Mine(GameNotice n) => n.Text.Contains(m0.Title);
    var first = m0.Target > 1 ? await Fire(m0.Kind, m0.Target - 1) : [];
    Check(!first.Any(Mine), $"una meta sin cruzar ({m0.Target - 1} de {m0.Target}, «{m0.Title}») no avisa");
    var cross = await Fire(m0.Kind, 1);
    var noticeM = cross.Where(Mine).ToList();
    Check(noticeM.Count == 1 && !noticeM[0].Public && noticeM[0].Text.Contains("¡Misión completada!") && noticeM[0].Text.Contains("/missions"), $"al cruzar la meta avisa UNA vez, en privado: «{noticeM.FirstOrDefault()?.Text}»");
    Check(!(await Fire(m0.Kind, 1)).Any(Mine), "después de cruzada, otro evento del mismo tipo no vuelve a avisar");

    // Un salto grande (de 0 a más que la meta de una vez, como juntar varias unidades) también avisa.
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    total = 0;
    var jump = await Fire(m0.Kind, m0.Target + 5);
    Check(jump.Count(Mine) == 1, "un solo evento que de golpe pasa la meta (de 0 a meta + 5) avisa igual");
    var other = tDaily.FirstOrDefault(m => m.Kind != m0.Kind);
    if (other is not null)
    {
        var none = await notifier.OnEventAsync(U, other.Kind, 1, 1, testNow);
        Check(!none.Any(n => n.Text.Contains(other.Title)) || other.Target <= 1, "un evento de otro tipo no avisa de misiones que no cuenta");
    }

    // Lo de ayer no cuenta para hoy.
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await InsertEvent(U, m0.Kind, m0.Target + 50, tStart.AddMinutes(-5));
    var stale = await notifier.OnEventAsync(U, m0.Kind, 1, 1, testNow);
    Check(!stale.Any(Mine), "eventos de ayer no completan la misión de hoy");

    // Semana: una misión semanal avisa al cruzar.
    var tWeek = UruguayCalendar.WeekStartUtc(testNow);
    var w0 = MissionCatalog.ForPeriod(MissionPeriod.Weekly, tWeek)[0];
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await InsertEvent(U, w0.Kind, w0.Target - 1, tWeek.AddHours(5));
    var wBefore = await notifier.OnEventAsync(U, w0.Kind, w0.Target - 1, w0.Target - 1, testNow);
    await InsertEvent(U, w0.Kind, 1, tWeek.AddHours(6));
    var wCross = await notifier.OnEventAsync(U, w0.Kind, 1, w0.Target, testNow);
    Check(!wBefore.Any(n => n.Text.Contains(w0.Title)) && wCross.Count(n => n.Text.Contains(w0.Title)) == 1, $"la misión semanal «{w0.Title}» avisa al cruzar la meta de la semana ({w0.Target})");

    // Logros: avisan al cruzar el tramo, en público.
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    var below = await notifier.OnEventAsync(U, GameEventKinds.HuntWin, 1, 24, testNow);
    var atI = await notifier.OnEventAsync(U, GameEventKinds.HuntWin, 1, 25, testNow);
    var above = await notifier.OnEventAsync(U, GameEventKinds.HuntWin, 1, 26, testNow);
    var big = await notifier.OnEventAsync(U, GameEventKinds.HuntWin, 580, 700, testNow);
    Check(!below.Any(n => n.Text.Contains("Logro")) && atI.Count(n => n.Text.Contains("Cazador I") && n.Public) == 1 && !above.Any(n => n.Text.Contains("Logro")), $"Cazador I avisa al llegar a 25 (en público): «{atI.FirstOrDefault(n => n.Text.Contains("Logro"))?.Text}»");
    Check(big.Count(n => n.Text.Contains("Logro")) == 2 && big.Any(n => n.Text.Contains("Cazador II")) && big.Any(n => n.Text.Contains("Cazador III")), "pasar de 120 a 700 de una vez avisa los dos tramos que cruzó (II y III)");

    // El servicio de eventos completo: registra, avanza y deja el aviso en la cola (una sola vez).
    await ResetPlayer();
    var todayMissions = MissionCatalog.ForPeriod(MissionPeriod.Daily, dayStart);
    var tm = todayMissions[0];
    await events.RecordAsync(U, tm.Kind, 1, tm.Target - 1 > 0 ? tm.Target - 1 : 1);
    var drained = tm.Target > 1 ? events.TakeNotices(U) : [];
    if (tm.Target > 1) Check(!drained.Any(n => n.Text.Contains(tm.Title)), "GameEventService: antes de la meta no deja avisos en la cola");
    await events.RecordAsync(U, tm.Kind, 1, tm.Target > 1 ? 1 : 0);
    var queued = events.TakeNotices(U);
    Check(queued.Count(n => n.Text.Contains(tm.Title)) == (tm.Target > 1 ? 1 : 0) || tm.Target <= 1, $"GameEventService: al completar «{tm.Title}» el aviso queda en la cola");
    Check(events.TakeNotices(U).Count == 0, "los avisos se entregan una sola vez (la cola queda vacía)");
}

Console.WriteLine("\n== 9. Pantallas y cobro completo (módulos) ==");
{
    await ResetPlayer(level: 1, xp: 0, gold: 0, zone: 1);
    var view0 = await MissionsModule.BuildViewAsync(users, zones, missions, new ZoneBoxService(factory, zones), U, now);
    Check(view0.Embed.Fields.Length == 2 && view0.ClaimableCount == 0 && view0.Components is null, "sin progreso: 2 secciones (diarias y semanales), nada para reclamar y sin botón");
    Check(view0.Embed.Fields[0].Name.Contains("Diarias") && view0.Embed.Fields[0].Name.Contains("se reinician en") && view0.Embed.Fields[1].Name.Contains("Semanales"), $"los títulos dicen cuándo se reinician: «{view0.Embed.Fields[0].Name}» / «{view0.Embed.Fields[1].Name}»");
    Check(view0.Embed.Fields[0].Value.Split((char)10).Count(l => l.StartsWith("▫️")) == 3 && view0.Embed.Fields[1].Value.Split((char)10).Count(l => l.StartsWith("▫️")) == 2 && view0.Embed.Fields[0].Value.Contains("🔒"), "3 diarias y 2 semanales en curso, con el premio por completarlas bloqueado");

    var dailies = MissionCatalog.ForPeriod(MissionPeriod.Daily, dayStart);
    foreach (var m in dailies) await InsertEvent(U, m.Kind, m.Target);
    var view1 = await MissionsModule.BuildViewAsync(users, zones, missions, new ZoneBoxService(factory, zones), U, now);
    Check(view1.ClaimableCount == 4 && view1.Components is not null, "las 3 diarias completas: hay 4 para reclamar (3 misiones + el premio por completarlas) y aparece el botón");
    var btn = view1.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Single();
    Check(btn.CustomId == $"missions_claim:{U}" && btn.Label == "Reclamar (4)", $"el botón lleva el dueño en su id ({btn.CustomId}) y la cuenta ({btn.Label})");
    Check(view1.Embed.Fields[0].Value.Split((char)10).Count(l => l.StartsWith("🎁")) == 4, "las 3 misiones y el premio mayor se ven como 'listas para reclamar'");

    int goldBefore2 = await Gold();
    var claim = await MissionsModule.ExecuteClaimAsync(users, zones, missions, events, U, now);
    int expectedGold = dailies.Sum(m => m.Reward.GoldUnits) * 14;
    Check(claim.Embed is not null && claim.Embed.Description.Count(c => c == '✅') == 3 && claim.Embed.Description.Contains("Completaste todas las diarias") && await Gold() == goldBefore2 + expectedGold && await Have("Cajón de Pino") == 1,
        $"reclamar todo: 3 misiones + el premio mayor, oro +{await Gold() - goldBefore2} (esperado {expectedGold}) y 1 Cajón de Pino. Total: {claim.Embed?.Description.Split((char)10).Last()}");
    Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM game_events WHERE discord_id = @id AND kind = 'mission_claimed'", new { id = (long)U }) == 4, "quedaron registrados 4 eventos mission_claimed (3 misiones + el premio mayor)");
    var again = await MissionsModule.ExecuteClaimAsync(users, zones, missions, events, U, now);
    Check(again.Embed is null && again.PlainMessage!.Contains("No tenés misiones listas") && await Gold() == goldBefore2 + expectedGold, "reclamar de nuevo: no hay nada y no paga otra vez");
    var view2 = await MissionsModule.BuildViewAsync(users, zones, missions, new ZoneBoxService(factory, zones), U, now);
    Check(view2.ClaimableCount == 0 && view2.Components is null && view2.Embed.Fields[0].Value.Count(c => c == '~') >= 6 && view2.Embed.Fields[0].Value.Contains("cobrado"), "después de cobrar: sin botón y todo tachado ('cobrada' / 'cobrado')");

    // Semanales: un jugador de Zona 2 cobra con ese oro y la caja mayor.
    await db.ExecuteAsync("UPDATE users SET current_zone_id = (SELECT zone_id FROM zones ORDER BY min_level OFFSET 1 LIMIT 1) WHERE discord_id = @id", new { id = (long)U });
    var weeklies = MissionCatalog.ForPeriod(MissionPeriod.Weekly, weekStart);
    foreach (var m in weeklies) await InsertEvent(U, m.Kind, m.Target);
    goldBefore2 = await Gold();
    var claimW = await MissionsModule.ExecuteClaimAsync(users, zones, missions, events, U, now);
    int expectedW = weeklies.Sum(m => m.Reward.GoldUnits) * 58;
    Check(claimW.Embed is not null && claimW.Embed.Description.Contains("Completaste todas las semanales") && await Gold() == goldBefore2 + expectedW && await Have("Arcón de Hierro") == 1,
        $"semanales en Zona 2: oro +{await Gold() - goldBefore2} (esperado {expectedW}, a 58 por unidad) y un Arcón de Hierro (la caja 'una más' de la zona 2)");

    // La subida de nivel por XP de premios queda registrada.
    await ResetPlayer(level: 1, xp: 95);
    foreach (var m in dailies) await InsertEvent(U, m.Kind, m.Target);
    await MissionsModule.ExecuteClaimAsync(users, zones, missions, events, U, now);
    var lvEvent = await db.QueryFirstOrDefaultAsync<(long amount, string detail)?>("SELECT amount, detail FROM game_events WHERE discord_id = @id AND kind = 'level_up'", new { id = (long)U });
    Check(lvEvent is not null && lvEvent.Value.amount >= 1 && (await Lvl()).level >= 2, $"el XP de los premios sube de nivel y deja el evento level_up (nivel {(await Lvl()).level})");

    // Logros por módulo.
    await ResetPlayer(level: 1, xp: 0, gold: 0, zone: 1);
    var av0 = await AchievementsModule.BuildViewAsync(eventsRepo, achievements, U);
    bool HasButton(AchievementsView v) => v.Components is not null && v.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Any();
    Check(av0.Embed.Fields.Length == 1 && av0.ClaimableCount == 0 && !HasButton(av0) && av0.Embed.Fields.All(f => f.Value.Length <= 1024) && av0.Embed.Length < 6000, $"/logros sin progreso (v0.9.0: una página por tema): 1 campo con los 5 logros de Combate, sin botón de reclamar, largo {av0.Embed.Length} de 6000");
    await eventsRepo.RecordAsync(U, GameEventKinds.HuntWin, null, 30, null);
    await eventsRepo.RecordAsync(U, GameEventKinds.Craft, null, 1, null);
    var av1 = await AchievementsModule.BuildViewAsync(eventsRepo, achievements, U);
    Check(av1.ClaimableCount == 2 && av1.Components is not null && av1.Embed.Fields[0].Value.Contains("Cazador") && av1.Embed.Fields[0].Value.Contains("I 🎁"), "30 cacerías y 1 forja: 2 tramos listos (Cazador I y Herrero I) y botón de reclamar");
    var abtn = av1.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Single();
    Check(abtn.CustomId == $"ach_claim:{U}:0" && abtn.Label == "Reclamar (2)", $"botón de logros (v0.9.0: lleva la página): {abtn.CustomId} / {abtn.Label}");
    int g1 = await Gold();
    var ac = await AchievementsModule.ExecuteClaimAsync(users, zones, eventsRepo, achievements, events, U);
    Check(ac.Embed is not null && ac.Embed.Description.Contains("Cazador I") && ac.Embed.Description.Contains("Herrero I") && await Gold() == g1 + 24 * 14, $"reclamar logros: Cazador I y Herrero I, oro +{await Gold() - g1} (esperado {24 * 14})");
    Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM game_events WHERE discord_id = @id AND kind = 'achievement_unlocked'", new { id = (long)U }) == 2, "quedaron registrados 2 eventos achievement_unlocked");
    var ac2 = await AchievementsModule.ExecuteClaimAsync(users, zones, eventsRepo, achievements, events, U);
    var av2 = await AchievementsModule.BuildViewAsync(eventsRepo, achievements, U);
    Check(ac2.Embed is null && ac2.PlainMessage!.Contains("No tenés logros listos") && av2.ClaimableCount == 0 && !HasButton(av2) && av2.Embed.Fields[0].Value.Contains("I ✅"), "reclamar de nuevo no paga y la pantalla muestra ✅ en lo cobrado");
    // El peor caso de tamaño: todos los logros completos y cobrados.
    foreach (var a in AchievementCatalog.All) await eventsRepo.RecordAsync(U, a.StatKind, null, 1_000_000, null);
    var avFull = await AchievementsModule.BuildViewAsync(eventsRepo, achievements, U);
    bool allPagesFit = true; int worstLen = 0;
    for (int pg = 0; pg < AchievementCatalog.Categories.Count; pg++) { var vp = await AchievementsModule.BuildViewAsync(eventsRepo, achievements, U, pg); allPagesFit &= vp.Embed.Fields.All(f => f.Value.Length <= 1024) && vp.Embed.Length < 6000; worstLen = Math.Max(worstLen, vp.Embed.Length); }
    Check(allPagesFit && avFull.ClaimableCount > 0, $"con todo desbloqueado ninguna de las páginas pasa los límites de Discord (la más larga mide {worstLen})");
    // Pantalla de misiones en el peor caso: todo completo y ninguna cobrada.
    var worst = MissionCatalog.ForPeriod(MissionPeriod.Weekly, weekStart).Concat(MissionCatalog.ForPeriod(MissionPeriod.Daily, dayStart)).ToList();
    Check(worst.Count == 5, "5 misiones activas en total (3 diarias + 2 semanales)");
    var mv = await MissionsModule.BuildViewAsync(users, zones, missions, new ZoneBoxService(factory, zones), U, now);
    Check(mv.Embed.Length < 6000 && mv.Embed.Fields.All(f => f.Value.Length <= 1024), $"la pantalla de misiones mide {mv.Embed.Length} caracteres (tope 6000)");
}

await Cleanup();
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE discord_id IN (@u, @v)", new { u = (long)U, v = (long)V }) == 0
    && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM mission_claims WHERE discord_id IN (@u, @v)", new { u = (long)U, v = (long)V }) == 0
    && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_collection WHERE discord_id IN (@u, @v)", new { u = (long)U, v = (long)V }) == 0, "usuarios de prueba y todo lo suyo borrado");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
