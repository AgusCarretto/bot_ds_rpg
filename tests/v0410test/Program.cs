using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.41.0: las HAZAÑAS (el dueño, 2026-10-29: «agregar logros más difíciles, pero bastante más difíciles… siento que se consiguen muy rápido»). Una página nueva de /achievements con 8 logros de tres tramos, con
// metas de 10 a 30 veces las de los logros comunes, premio SOLO de oro y EXP (nunca cajas) y 24 títulos propios en la página «Hazañas». Reglas puras y un reclamo de punta a punta contra la base LOCAL.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var events = new GameEventService(eventsRepo);
var achRepo = new AchievementRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000041000UL;

async Task Reset()
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
    await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET gold = 0, xp = 0, level = 1, current_zone_id = (SELECT zone_id FROM zones WHERE kind = 'normal' ORDER BY min_level LIMIT 1) WHERE discord_id = @id", new { id = (long)U });
}
async Task SetStat(string kind, long value) => await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, @k, @v) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = @v", new { id = (long)U, k = kind, v = value });
async Task<(int Gold, int Xp)> Me() => await db.QuerySingleAsync<(int, int)>("SELECT gold, xp FROM users WHERE discord_id = @id", new { id = (long)U });

var feats = new[] { "hz_cazador", "hz_viajero", "hz_jefes", "hz_recolector", "hz_herrero", "hz_magnate", "hz_encantador", "hz_explorador" };
// El logro común que cuenta lo mismo (para medir cuánto más difícil es la hazaña).
var commonOf = new Dictionary<string, string> { ["hz_cazador"] = "cazador", ["hz_viajero"] = "viajero", ["hz_jefes"] = "matajefes", ["hz_recolector"] = "recolector", ["hz_herrero"] = "herrero", ["hz_magnate"] = "comerciante", ["hz_encantador"] = "encantador" };

Console.WriteLine("== 1. El catálogo (puro) ==");
Check(AchievementCatalog.All.Count == 31 && AchievementCatalog.All.Select(a => a.Key).Distinct().Count() == 31 && feats.All(k => AchievementCatalog.Find(k) is not null), "31 logros con claves distintas: los 23 de siempre y las 8 hazañas");
var page = AchievementCatalog.Categories[^1];
Check(page.Name == "Hazañas" && page.Keys.SequenceEqual(feats) && AchievementCatalog.Categories.Count == 5 && AchievementCatalog.Categories.SelectMany(c => c.Keys).Distinct().Count() == 31, "las hazañas tienen su PÁGINA propia (la última) y cada logro sigue estando en exactamente una");
Check(feats.All(k => AchievementCatalog.Find(k)!.Tiers.Count == 3 && AchievementCatalog.Find(k)!.Tiers.Zip(AchievementCatalog.Find(k)!.Tiers.Skip(1), (a, b) => b.Threshold > a.Threshold).All(x => x)), "cada hazaña tiene tres tramos y cada uno pide más que el anterior");
Check(commonOf.All(kv => AchievementCatalog.Find(kv.Key)!.StatKind == AchievementCatalog.Find(kv.Value)!.StatKind), "cada hazaña cuenta lo mismo que su logro común (los mismos contadores: no hay nada nuevo que registrar)");
Check(commonOf.All(kv => AchievementCatalog.Find(kv.Key)!.Tiers[0].Threshold >= AchievementCatalog.Find(kv.Value)!.Tiers[2].Threshold), "el PRIMER tramo de la hazaña ya pide más que el tramo más alto del logro común (bastante más difícil)");
Check(commonOf.All(kv => AchievementCatalog.Find(kv.Key)!.Tiers[2].Threshold >= 10 * AchievementCatalog.Find(kv.Value)!.Tiers[2].Threshold), "y el último tramo pide 10 veces o más que el del logro común");
Check(AchievementCatalog.Find("hz_explorador")!.StatKind == GameEventKinds.PetExpeditionClaim && AchievementCatalog.Find("hz_explorador")!.Tiers.Select(t => t.Threshold).SequenceEqual([300L, 1_500, 5_000]), "«Gran Explorador» cuenta las horas de expedición de las mascotas (300 / 1.500 / 5.000)");
Check(feats.All(k => AchievementCatalog.Find(k)!.Tiers.All(t => t.Reward.Box == BoxGrant.None)), "premian SOLO oro y EXP: ningún tramo da caja (los contadores se podrían farmear)");
var cazador3 = AchievementCatalog.Find("cazador")!.Tiers[2].Reward;
Check(AchievementCatalog.Find("hz_cazador")!.Tiers.Select(t => t.Reward.GoldUnits).SequenceEqual([40, 120, 400]) && AchievementCatalog.Find("hz_cazador")!.Tiers.Select(t => t.Reward.XpPercent).SequenceEqual([10, 25, 60]) && AchievementCatalog.Find("hz_cazador")!.Tiers[2].Reward.GoldUnits > cazador3.GoldUnits,
    "el premio es 40 / 120 / 400 cacerías de oro y 10 / 25 / 60 % de un nivel, más que el del logro común más alto");

Console.WriteLine("\n== 2. Los títulos (puros) ==");
var featTitles = TitleCatalog.All.Where(t => t.AchievementKey is not null && feats.Contains(t.AchievementKey)).ToList();
Check(featTitles.Count == 24 && featTitles.All(t => t.PageKey == "hazanas" && t.Rule == TitleRule.Stat) && TitleCatalog.All.Count == 137 && TitleCatalog.Pages.Count == 10 && TitleCatalog.Pages.Any(p => p.Key == "hazanas" && p.Name == "Hazañas"), "24 títulos nuevos en la página «Hazañas» (137 en total, 10 páginas)");
Check(featTitles.All(t => AchievementCatalog.Find(t.AchievementKey!)!.Tiers[t.AchievementTier - 1].Threshold == t.Threshold && t.Source == AchievementCatalog.Find(t.AchievementKey!)!.StatKind), "cada título se gana con la MISMA meta y el mismo contador que su tramo");
Check(TitleCatalog.All.Select(t => t.Name.ToLowerInvariant()).Distinct().Count() == TitleCatalog.All.Count && TitleCatalog.All.Count(t => t.PageKey == "hazanas") <= 25 && TitleCatalog.ParsePage("hazanas") == 7, "ningún nombre se repite, la página entra en las 25 opciones del desplegable y se pide por su clave");
var ctxLow = new TitleContext(new Dictionary<string, long> { [GameEventKinds.HuntWin] = 2_499 }, 1, new HashSet<int>(), new HashSet<string>(), 0);
var ctxHigh = new TitleContext(new Dictionary<string, long> { [GameEventKinds.HuntWin] = 2_500 }, 1, new HashSet<int>(), new HashSet<string>(), 0);
Check(!TitleCatalog.IsUnlocked(TitleCatalog.All.Single(t => t.Key == "hz_cazador_1"), ctxLow) && TitleCatalog.IsUnlocked(TitleCatalog.All.Single(t => t.Key == "hz_cazador_1"), ctxHigh), "el título «Pesadilla de la Pradera» se desbloquea justo con las 2.500 cacerías");

Console.WriteLine("\n== 3. Verlas y reclamarlas (base local) ==");
await Reset();
var view = await AchievementsModule.BuildViewAsync(eventsRepo, achRepo, U, 4);
string vt = AllText(view.Embed);
Check(vt.Contains("Hazañas") && vt.Contains("Cazador de Leyenda") && vt.Contains("Gran Explorador") && vt.Contains("Magnate") && vt.Contains("Página 5/5") && view.Embed.Length <= 6000 && view.Embed.Fields.All(f => f.Value.Length <= 1024), $"la página 5 muestra las hazañas, dentro de los límites de Discord ({view.Embed.Length})");
await SetStat(GameEventKinds.PetExpeditionClaim, 300);
var claim = await AchievementsModule.ExecuteClaimAsync(users, zonesRepo, eventsRepo, achRepo, events, U);
var me = await Me();
int boxes = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.type = 'Caja'", new { id = (long)U });
Check(claim.Embed is not null && AllText(claim.Embed).Contains("Gran Explorador") && me.Gold == 40 * MissionRewards.GoldUnit(1) && me.Xp == (int)(LevelingCalculator.RequiredXpForLevel(1) * 10 / 100.0) && boxes == 0,
    $"con 300 horas de expedición reclamás el tramo I: {me.Gold} de oro (40 cacerías de la Zona 1) y {me.Xp} de EXP (10 % del nivel 1), sin ninguna caja");
Check((await AchievementsModule.ExecuteClaimAsync(users, zonesRepo, eventsRepo, achRepo, events, U)).PlainMessage!.Contains("No tenés logros") && (await Me()).Gold == me.Gold, "reclamar de nuevo no paga otra vez");
await SetStat(GameEventKinds.PetExpeditionClaim, 1_500);
await AchievementsModule.ExecuteClaimAsync(users, zonesRepo, eventsRepo, achRepo, events, U);
Check((await Me()).Gold == (40 + 120) * MissionRewards.GoldUnit(1), "con 1.500 horas se suma el tramo II (120 cacerías de oro): 160 en total");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
