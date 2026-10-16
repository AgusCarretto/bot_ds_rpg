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

// v0.18.2: /daily escala con la zona y el nivel, con hitos de racha y cajas (día 6 diaria, día 7 y cada 7 la semanal, y después del 7 la diaria otra vez). Reglas puras, el pago atómico contra la base real
// (usuario descartable: oro, EXP, caja y racha en UNA transacción, 12 cobros a la vez, rollback si falta la caja), la pantalla, los comandos con el InteractionService real, la ayuda y la DI.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var progression = new ProgressionRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000060000UL;

// Reinicia a U: nivel, racha y hace cuántas horas reclamó (null = nunca).
async Task Reset(int level = 10, int streak = 0, double? hoursAgo = null, int xp = 0, int gold = 0)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync(
        "UPDATE users SET level = @l, xp = @xp, gold = @g, daily_streak = @s, last_daily_claim = CASE WHEN @h::float8 IS NULL THEN NULL ELSE now() - make_interval(secs => @h::float8 * 3600) END WHERE discord_id = @id",
        new { id = (long)U, l = level, xp, g = gold, s = streak, h = hoursAgo });
}
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task<int> GoldNow() => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task<(int Level, int Xp, int Streak)> State() => await db.QuerySingleAsync<(int, int, int)>("SELECT level, xp, daily_streak FROM users WHERE discord_id = @id", new { id = (long)U });
async Task<int> BoxesTotal() => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.type = 'Caja'", new { id = (long)U });

// Qué cajas dice zone_boxes para cada posición de zona (1..5): rol daily (Iron) y weekly (Gold).
async Task<string[]> BoxesOf(string role) => (await db.QueryAsync<string>(
    "SELECT i.name FROM zone_boxes zb JOIN items i ON i.item_id = zb.box_item_id JOIN zones z ON z.zone_id = zb.zone_id WHERE zb.role = @r AND z.zone_id > 0 ORDER BY z.min_level", new { r = role })).ToArray();
var iron = await BoxesOf("daily");
var gold = await BoxesOf("weekly");

Console.WriteLine("== 1. Reglas puras ==");
Check(Enumerable.Range(1, 7).Select(DailyRewardCalculator.BaseGoldUnitsFor).SequenceEqual([6, 8, 10, 12, 14, 16, 20]) && DailyRewardCalculator.BaseGoldUnitsFor(8) == 20 && DailyRewardCalculator.BaseGoldUnitsFor(100) == 20, "el oro base sube del día 1 al 7 (6 → 20 cacerías) y desde el 8 queda en el del día 7");
Check(Enumerable.Range(1, 8).Select(DailyRewardCalculator.XpPercentFor).SequenceEqual([2, 2, 3, 3, 4, 4, 6, 6]) && DailyRewardCalculator.XpPercentFor(50) == 6, "la EXP sube de 2 % a 6 % del nivel del día 1 al 7 y desde el 8 queda en 6 %");
Check(DailyRewardCalculator.GoldUnitsFor(3) == 10 + 15 && DailyRewardCalculator.GoldUnitsFor(7) == 20 + 30 && DailyRewardCalculator.GoldUnitsFor(14) == 50 && DailyRewardCalculator.GoldUnitsFor(21) == 50 && DailyRewardCalculator.GoldUnitsFor(8) == 20 && DailyRewardCalculator.GoldUnitsFor(10) == 20,
    "hitos de oro: el día 3 suma 15 cacerías, y el 7, 14, 21... suman 30; los demás días no");
Check(Enumerable.Range(1, 5).All(d => DailyRewardCalculator.BoxFor(d) == DailyBox.None) && DailyRewardCalculator.BoxFor(6) == DailyBox.Iron && DailyRewardCalculator.BoxFor(7) == DailyBox.Gold
      && Enumerable.Range(8, 6).All(d => DailyRewardCalculator.BoxFor(d) == DailyBox.Iron) && DailyRewardCalculator.BoxFor(14) == DailyBox.Gold && DailyRewardCalculator.BoxFor(15) == DailyBox.Iron && DailyRewardCalculator.BoxFor(21) == DailyBox.Gold && DailyRewardCalculator.BoxFor(28) == DailyBox.Gold,
    "cajas: nada del día 1 al 5, la diaria el 6, la de oro el 7, la diaria del 8 al 13, la de oro el 14, y así cada 7 días");
Check(DailyRewardCalculator.SpecFor(1) == new RewardSpec(6, 2) && DailyRewardCalculator.SpecFor(6) == new RewardSpec(16, 4, BoxGrant.Daily) && DailyRewardCalculator.SpecFor(7) == new RewardSpec(50, 6, BoxGrant.Weekly) && DailyRewardCalculator.SpecFor(9) == new RewardSpec(20, 6, BoxGrant.Daily),
    "el premio del día sale como RewardSpec (día 1, 6, 7 y 9) y la caja diaria/semanal usa los roles daily y weekly de zone_boxes");
var t0 = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
Check(DailyRewardCalculator.Evaluate(null, 0, t0) == new DailyClaimCalculation(DailyClaimStatus.FirstClaim, 1, null)
      && DailyRewardCalculator.Evaluate(t0.AddHours(-23.99), 4, t0).Status == DailyClaimStatus.TooSoon && DailyRewardCalculator.Evaluate(t0.AddHours(-23.99), 4, t0).NewStreak == 4 && DailyRewardCalculator.Evaluate(t0.AddHours(-23.99), 4, t0).RemainingCooldown > TimeSpan.Zero
      && DailyRewardCalculator.Evaluate(t0.AddHours(-24), 4, t0) == new DailyClaimCalculation(DailyClaimStatus.StreakContinued, 5, null)
      && DailyRewardCalculator.Evaluate(t0.AddHours(-48), 4, t0).Status == DailyClaimStatus.StreakContinued
      && DailyRewardCalculator.Evaluate(t0.AddHours(-48.01), 9, t0) == new DailyClaimCalculation(DailyClaimStatus.StreakReset, 1, null),
    "la racha sigue como siempre: antes de 24 h no se puede, entre 24 y 48 h suma 1, pasadas 48 h vuelve al día 1");
Check(DailyRewardCalculator.NextDayHint(2)!.Contains("día 3") && DailyRewardCalculator.NextDayHint(5)!.Contains("día 6") && DailyRewardCalculator.NextDayHint(6)!.Contains("día 7") && DailyRewardCalculator.NextDayHint(13)!.Contains("día 14")
      && DailyRewardCalculator.NextDayHint(1) is null && DailyRewardCalculator.NextDayHint(7) is null && DailyRewardCalculator.NextDayHint(9) is null, "el aviso de «mañana viene algo especial» sale antes del día 3, 6, 7, 14... y no los días comunes");
Check(!DailyRewardCalculator.IsWeekMilestone(0) && !DailyRewardCalculator.IsWeekMilestone(6) && DailyRewardCalculator.IsWeekMilestone(7) && DailyRewardCalculator.IsWeekMilestone(70) && DailyRewardCalculator.IsThreeDayBonus(3) && !DailyRewardCalculator.IsThreeDayBonus(10), "los hitos se reconocen bien (7, 14, 70...; el 3 solo una vez)");

Console.WriteLine("\n== 2. Las cajas de cada zona (zone_boxes) ==");
Check(iron.Length == 5 && gold.Length == 5, $"hay caja diaria y semanal para las 5 zonas ({string.Join("/", iron)} | {string.Join("/", gold)})");
Check(iron[1] == "Baúl de Roble" && gold[1] == "Arcón de Hierro" && iron[4] == "Arcón de Hierro" && gold[4] == "Cofre de Oro", "zona 2: Roble / Arcón de Hierro; zona 5: Arcón de Hierro / Cofre de Oro (lo que se le dijo al dueño)");

Console.WriteLine("\n== 3. El pago, contra la base ==");
await Reset(level: 10, streak: 0, hoursAgo: null, gold: 1000);
var p1 = await progression.ClaimDailyAsync(U, zoneRank: 2);
var s1 = await State();
int need10 = LevelingCalculator.RequiredXpForLevel(10);
int xp1 = (int)(need10 * 2 / 100.0);
Check(p1.Calculation.Status == DailyClaimStatus.FirstClaim && p1.Receipt is not null && p1.Receipt.Reward.Gold == 6 * MissionRewards.GoldUnit(2) && p1.Receipt.Reward.Xp == xp1 && p1.Receipt.Reward.BoxName is null,
    $"día 1 en la zona 2, nivel 10: {p1.Receipt?.Reward.Gold} de oro (6 cacerías de 58) y {p1.Receipt?.Reward.Xp} de EXP (2 % de {need10}), sin caja");
Check(await GoldNow() == 1000 + 6 * 58 && s1 == (10, xp1, 1) && await BoxesTotal() == 0, "la base quedó igual que el recibo: oro, EXP, racha en 1 y ninguna caja");
var tooSoon = await progression.ClaimDailyAsync(U, 2);
Check(tooSoon.Calculation.Status == DailyClaimStatus.TooSoon && tooSoon.Result is null && tooSoon.Receipt is null && await GoldNow() == 1000 + 348, "reclamar de nuevo enseguida: «todavía no», sin cobrar nada");

// día 3: monedas extra
await Reset(level: 10, streak: 2, hoursAgo: 30);
var p3 = await progression.ClaimDailyAsync(U, 2);
Check(p3.Calculation.NewStreak == 3 && p3.Receipt!.Reward.Gold == (10 + 15) * 58 && p3.Receipt.Reward.BoxName is null, $"día 3 (racha de 3): {p3.Receipt!.Reward.Gold} de oro = 25 cacerías (10 + 15 del hito), sin caja");

// día 6: caja diaria; zona 2 = Baúl de Roble
await Reset(level: 10, streak: 5, hoursAgo: 30);
var p6 = await progression.ClaimDailyAsync(U, 2);
Check(p6.Calculation.NewStreak == 6 && p6.Receipt!.Reward.BoxName == iron[1] && await Have(iron[1]) == 1 && p6.Receipt.Reward.Gold == 16 * 58, $"día 6 en la zona 2: la caja diaria ({p6.Receipt!.Reward.BoxName}) está en la mochila y paga 16 cacerías");

// día 7: caja de oro; zona 2 = Arcón de Hierro, + 30 cacerías
await Reset(level: 10, streak: 6, hoursAgo: 30);
var p7 = await progression.ClaimDailyAsync(U, 2);
Check(p7.Calculation.NewStreak == 7 && p7.Receipt!.Reward.BoxName == gold[1] && await Have(gold[1]) == 1 && p7.Receipt.Reward.Gold == 50 * 58 && p7.Receipt.Reward.Xp == (int)(need10 * 6 / 100.0), $"día 7 en la zona 2: la caja grande ({p7.Receipt!.Reward.BoxName}), 50 cacerías de oro y 6 % de EXP");

// día 8: vuelve la diaria; día 14: la de oro otra vez; día 15: la diaria
await Reset(level: 10, streak: 7, hoursAgo: 30);
var p8 = await progression.ClaimDailyAsync(U, 2);
Check(p8.Calculation.NewStreak == 8 && p8.Receipt!.Reward.BoxName == iron[1] && p8.Receipt.Reward.Gold == 20 * 58, "día 8 (después del 7, sigue la racha): la caja diaria otra vez y 20 cacerías");
await Reset(level: 10, streak: 13, hoursAgo: 30);
var p14 = await progression.ClaimDailyAsync(U, 2);
Check(p14.Calculation.NewStreak == 14 && p14.Receipt!.Reward.BoxName == gold[1] && p14.Receipt.Reward.Gold == 50 * 58, "día 14: la caja grande otra vez (cada 7 días en racha) y +30 cacerías");
await Reset(level: 10, streak: 14, hoursAgo: 30);
var p15 = await progression.ClaimDailyAsync(U, 2);
Check(p15.Receipt!.Reward.BoxName == iron[1], "día 15: la diaria");

// escala con la zona
await Reset(level: 10, streak: 6, hoursAgo: 30);
var z5 = await progression.ClaimDailyAsync(U, 5);
Check(z5.Receipt!.Reward.BoxName == gold[4] && z5.Receipt.Reward.Gold == 50 * 375 && z5.Receipt.Reward.Gold > 20 * 800, $"el día 7 en la zona 5 paga {z5.Receipt!.Reward.Gold} de oro y un {z5.Receipt.Reward.BoxName} (antes, el tope eran 800-1.000 de oro)");
await Reset(level: 10, streak: 0, hoursAgo: null);
var z1 = await progression.ClaimDailyAsync(U, 1);
await Reset(level: 10, streak: 0, hoursAgo: null);
var z0 = await progression.ClaimDailyAsync(U, 0);
Check(z1.Receipt!.Reward.Gold == 6 * 14 && z0.Receipt!.Reward.Gold == 6 * 14, "zona 1 (o desconocida, rank 0): se toma la primera, nunca queda en cero");

// escala con el nivel: EXP = % de lo que pide el nivel actual
await Reset(level: 60, streak: 6, hoursAgo: 30);
var l60 = await progression.ClaimDailyAsync(U, 5);
int need60 = LevelingCalculator.RequiredXpForLevel(60);
Check(l60.Receipt!.Reward.Xp == (int)(need60 * 6 / 100.0) && l60.Receipt.Reward.Xp > 2000, $"a nivel 60 el día 7 da {l60.Receipt!.Reward.Xp} de EXP (6 % de {need60}); antes el tope eran 500");

// subir de nivel con el daily: el banner sale por el evento level_up
await Reset(level: 3, streak: 6, hoursAgo: 30, xp: LevelingCalculator.RequiredXpForLevel(3) - 1);
var lvl = await progression.ClaimDailyAsync(U, 1);
Check(lvl.Result!.LevelsGained >= 1 && lvl.Receipt!.LevelsGained == lvl.Result.LevelsGained && (await State()).Level >= 4, $"el EXP del daily puede subir de nivel ({lvl.Result.LevelsGained})");

// racha perdida
await Reset(level: 10, streak: 9, hoursAgo: 49);
var lost = await progression.ClaimDailyAsync(U, 2);
Check(lost.Calculation.Status == DailyClaimStatus.StreakReset && lost.Calculation.NewStreak == 1 && lost.Receipt!.Reward.BoxName is null && lost.Receipt.Reward.Gold == 6 * 58 && (await State()).Streak == 1, "pasadas 48 h: la racha vuelve al día 1 y el premio al del día 1");

Console.WriteLine("\n== 4. Concurrencia y reversión ==");
await Reset(level: 10, streak: 5, hoursAgo: 30);
var many = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => progression.ClaimDailyAsync(U, 2)));
Check(many.Count(o => o.Calculation.Status != DailyClaimStatus.TooSoon) == 1 && many.Count(o => o.Calculation.Status == DailyClaimStatus.TooSoon) == 11 && await Have(iron[1]) == 1 && await GoldNow() == 16 * 58 && (await State()).Streak == 6,
    "12 /daily a la vez el día 6: exactamente UNO paga (una caja, 16 cacerías, racha 6)");

await Reset(level: 10, streak: 5, hoursAgo: 30, gold: 500);
await db.ExecuteAsync("UPDATE items SET type = 'Material' WHERE name = @n", new { n = iron[1] });
Exception? thrown = null;
try { await progression.ClaimDailyAsync(U, 2); }
catch (Exception ex) { thrown = ex; }
finally { await db.ExecuteAsync("UPDATE items SET type = 'Caja' WHERE name = @n", new { n = iron[1] }); }
var afterFail = await State();
Check(thrown is InvalidOperationException && await GoldNow() == 500 && afterFail.Streak == 5 && afterFail.Xp == 0 && await BoxesTotal() == 0, "si la caja del día no existe: tira y se revierte TODO (oro, EXP y racha quedan como estaban; se puede reintentar)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM items WHERE name = @n AND type = 'Caja'", new { n = iron[1] }) == 1, "(el catálogo quedó como estaba)");

Console.WriteLine("\n== 5. La pantalla ==");
var player = (await users.GetByDiscordIdAsync(U))!;
string Day(int day, DailyClaimStatus st, ResolvedReward r) => AllText(DailyModule.BuildResultEmbed(new DailyClaimCalculation(st, day, null), new LevelUpOutcome(player, 0), new RewardReceipt(r, 1000, 0, 10)));
var e3 = Day(3, DailyClaimStatus.StreakContinued, new ResolvedReward(1450, 95, null, 0));
Check(e3.Contains("3 días seguidos") && e3.Contains("1.450") && e3.Contains("Mañana es el día 4") == false && !e3.Contains("📦"), "día 3: festeja los 3 días seguidos con las monedas, sin campo de caja");
var e5 = Day(5, DailyClaimStatus.StreakContinued, new ResolvedReward(812, 120, null, 0));
Check(e5.Contains("Mañana es el día 6") && !e5.Contains("📦"), "día 5: avisa que mañana empiezan las cajas");
var e6 = Day(6, DailyClaimStatus.StreakContinued, new ResolvedReward(928, 150, "Baúl de Roble", 1));
Check(e6.Contains("📦 Caja") && e6.Contains("Baúl de Roble") && e6.Contains("/open") && e6.Contains("Mañana es el día 7"), "día 6: muestra la caja, cómo abrirla y que mañana es el día 7");
var e7 = Day(7, DailyClaimStatus.StreakContinued, new ResolvedReward(2900, 300, "Arcón de Hierro", 1));
var e14 = Day(14, DailyClaimStatus.StreakContinued, new ResolvedReward(2900, 300, "Arcón de Hierro", 1));
Check(e7.Contains("Una semana seguida") && e7.Contains("Arcón de Hierro") && e14.Contains("2 semanas seguidas"), "día 7 y día 14: festejan «una semana» y «2 semanas» seguidas");
var eLost = Day(1, DailyClaimStatus.StreakReset, new ResolvedReward(348, 63, null, 0));
Check(eLost.Contains("Perdiste tu racha") && !eLost.Contains("semana"), "racha perdida: avisa que se vuelve al día 1");
var embedFull = DailyModule.BuildResultEmbed(new DailyClaimCalculation(DailyClaimStatus.StreakContinued, 7, null), new LevelUpOutcome(player, 0), new RewardReceipt(new ResolvedReward(2900, 300, "Arcón de Hierro", 1), 1000, 0, 10));
Check(embedFull.Length <= 6000 && embedFull.Fields.All(f => f.Value.Length <= 1024) && embedFull.Fields.Length == 4, $"el mensaje es corto y entra en Discord ({embedFull.Length} caracteres, {embedFull.Fields.Length} campos)");

Console.WriteLine("\n== 6. Comandos, ayuda y DI con el InteractionService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var cmd = interactions.SlashCommands.Single(c => c.Name == "daily");
Check(cmd.Module.SlashGroupName is null && cmd.Description.Length is >= 1 and <= 100 && cmd.Parameters.Count == 0, $"/daily existe, sin parámetros, y su descripción entra en Discord ({cmd.Description.Length} caracteres)");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "daily", "d" }.All(a => textNames.Count(n => n == a) == 1), "aa daily / aa d siguen registrados, sin repetirse");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");

var topic = HelpTopics.Find("daily")!.Build(); string tt = AllText(topic);
Check(tt.Contains("escala con tu zona") && tt.Contains("6") && tt.Contains("20 cacerías de oro") && tt.Contains("2 % a 6 %") && tt.Contains("Día 6") && tt.Contains("caja diaria") && tt.Contains("caja grande") && tt.Contains("48 h") && topic.Length <= 6000 && topic.Fields.All(f => f.Value.Length <= 1024),
    $"el tema /info daily explica la escala, los hitos y las cajas con los números reales ({topic.Length})");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
Check(HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256); }), "todos los temas de ayuda siguen entrando en Discord");

// /cd sigue mostrando el daily con la misma cuenta
await Reset(level: 10, streak: 3, hoursAgo: 2);
var cdEmbed = await CooldownModule.BuildStatusEmbedAsync(new CooldownRepository(factory), users, U);
Check(AllText(cdEmbed).Contains("🎁 **Diario**:") && AllText(cdEmbed).Contains("restantes"), "/cd sigue contando lo que falta para el daily");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Check(true, "(limpieza) usuario descartable borrado");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
