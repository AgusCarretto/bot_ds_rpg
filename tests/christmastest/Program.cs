using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.57.0: Navidad en el Fogón, primera parte (docs/navidad.md). El dueño (2026-11-13): del 1 al 24 de diciembre caen Regalos (hunt 40 % de 1,
// travel 5, jefe 50, daily 10 y 15 con racha de 7), un calendario de adviento de 24 días (20 Regalos por día y días especiales) y del 25 al 6 de
// enero solo tienda; los Regalos sobreviven al FN y desaparecen al terminar. Se prueba: las reglas puras (fechas, Regalos, calendario), el
// repositorio contra la base LOCAL (usuario descartable, 10 cobros a la vez), el servicio con el reloj fijo y las pantallas en los dos idiomas.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000098911UL, NOACC = 999000000000098999UL;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
await db.ExecuteAsync(File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Database", "add_christmas.sql")));
var users = new UserRepository(factory);
var zones = new ZoneRepository(factory);
var repo = new ChristmasRepository(factory);

// Una temporada de prueba propia: nunca pisa la de verdad.
var settings = ChristmasSettings.FromLocalDates(new DateOnly(2031, 12, 1), new DateOnly(2031, 12, 25), new DateOnly(2032, 1, 7));
string season = settings.Season;

async Task Clean()
{
    foreach (var id in new[] { (long)U, (long)NOACC })
    {
        await db.ExecuteAsync("DELETE FROM event_gifts WHERE discord_id = @id", new { id });
        await db.ExecuteAsync("DELETE FROM event_advent_claims WHERE discord_id = @id", new { id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id });
    }
}

await Clean();

Console.WriteLine("== 1. Las fechas ==");
var def = ChristmasSettings.Default;
Check(def.StartUtc == new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc), "arranca el 1 de diciembre de 2026 a la medianoche de Uruguay (03:00 UTC)");
Check(def.FarmEndUtc == new DateTime(2026, 12, 25, 3, 0, 0, DateTimeKind.Utc), "el farmeo termina al empezar el 25");
Check(def.EndUtc == new DateTime(2027, 1, 7, 3, 0, 0, DateTimeKind.Utc) && def.Season == "navidad2026", "cierra el 7 de enero de 2027; temporada navidad2026");
Check(ChristmasRules.PhaseAt(def, def.StartUtc.AddSeconds(-1)) == ChristmasPhase.Off, "un segundo antes: apagado");
Check(ChristmasRules.PhaseAt(def, def.StartUtc) == ChristmasPhase.Farm, "el 1 a la medianoche: farmeo");
Check(ChristmasRules.PhaseAt(def, def.FarmEndUtc) == ChristmasPhase.Shop, "el 25: tienda");
Check(ChristmasRules.PhaseAt(def, def.EndUtc) == ChristmasPhase.Off, "el 7 de enero: apagado");
Check(ChristmasRules.AdventDayAt(def, def.StartUtc) == 1 && ChristmasRules.AdventDayAt(def, def.StartUtc.AddHours(23.9)) == 1, "todo el 1 de diciembre es el día 1");
Check(ChristmasRules.AdventDayAt(def, def.StartUtc.AddDays(23)) == 24 && ChristmasRules.AdventDayAt(def, def.FarmEndUtc) == 0, "el 24 es el día 24; el 25 no hay calendario");
var fromCfg = ChristmasService.LoadSettings(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Christmas:Start"] = "2026-11-14", ["Christmas:FarmEnd"] = "2026-11-20", ["Christmas:End"] = "2026-11-22" }).Build());
Check(fromCfg.StartUtc == new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc) && fromCfg.EndUtc == new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc),
    "las fechas se pueden adelantar por configuración para probar");
Check(ChristmasService.LoadSettings(new ConfigurationBuilder().Build()) == def, "sin configuración: las de 2026");

Console.WriteLine("\n== 2. Cuántos Regalos da cada cosa ==");
Check(ChristmasRules.VictoryGifts(GameEventKinds.HuntWin, 49) == 1 && ChristmasRules.VictoryGifts(GameEventKinds.HuntWin, 50) == 0, "hunt: 1 Regalo con 50 %");
Check(ChristmasRules.VictoryGifts(GameEventKinds.TravelWin, 99) == 5, "travel: 5");
Check(ChristmasRules.VictoryGifts(GameEventKinds.BossWin, 99) == 50 && ChristmasRules.VictoryGifts(GameEventKinds.RaidWin, 0) == 50, "jefe solo o en equipo: 50");
Check(ChristmasRules.VictoryGifts(GameEventKinds.ArenaWin, 0) == 0, "la Arena no da Regalos");
Check(ChristmasRules.DailyClaimGifts(6) == 10 && ChristmasRules.DailyClaimGifts(7) == 15, "daily: 10, y 15 con racha de 7");

Console.WriteLine("\n== 3. El calendario ==");
Check(Enumerable.Range(1, 24).All(d => ChristmasRules.Advent(d).Gifts >= 20), "todos los días dan 20 Regalos o más");
Check(new[] { 5, 12, 19 }.All(d => ChristmasRules.Advent(d).Gifts == 40), "5, 12 y 19: doble");
Check(ChristmasRules.Advent(24).Gifts == 150 && ChristmasRules.Advent(24).Reward?.Box == BoxGrant.Weekly, "el 24: 150 Regalos y caja grande");
Check(ChristmasRules.Advent(21).Reward?.Box == BoxGrant.Weekly && ChristmasRules.Advent(7).Reward?.Box == BoxGrant.Daily && ChristmasRules.Advent(14).Reward?.Box == BoxGrant.Daily, "7 y 14 caja de la zona; 21 caja grande");
Check(new[] { 3, 10, 17 }.All(d => ChristmasRules.Advent(d).Reward?.GoldUnits > 0) && new[] { 9, 16, 23 }.All(d => ChristmasRules.Advent(d).Dust > 0), "oro el 3/10/17, Polvo el 9/16/23");
Check(ChristmasRules.AdventTotalGifts == 20 * 20 + 3 * 40 + 150, $"el calendario completo suma 670 Regalos ({ChristmasRules.AdventTotalGifts})");

Console.WriteLine("\n== 4. El repositorio ==");
await users.GetOrCreateUserAsync(U);
Check((await repo.GetAsync(U, season)).Balance == 0, "sin Regalos de entrada");
await repo.GrantAsync(U, season, 5);
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => repo.GrantAsync(U, season, 1)));
var bal = await repo.GetAsync(U, season);
Check(bal.Balance == 15 && bal.Earned == 15, $"5 + 10 cobros a la vez de 1 = 15 ({bal.Balance})");
Check((await repo.GetAsync(U, "navidad2099")).Balance == 0, "otra temporada no ve esos Regalos (así «desaparecen» al terminar)");

int goldBefore = (await users.GetByDiscordIdAsync(U))!.Gold;
var claim3 = await repo.ClaimAdventAsync(U, season, ChristmasRules.Advent(3), zoneRank: 1);
Check(claim3.Status == AdventClaimStatus.Ok && claim3.GiftsAfter == 35, "día 3: +20 Regalos");
Check(claim3.Receipt?.Reward.Gold > 0 && (await users.GetByDiscordIdAsync(U))!.Gold == goldBefore + claim3.Receipt!.Reward.Gold, "día 3: el oro se pagó");
var again = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => repo.ClaimAdventAsync(U, season, ChristmasRules.Advent(3), 1)));
Check(again.All(a => a.Status == AdventClaimStatus.AlreadyClaimed) && (await repo.GetAsync(U, season)).Balance == 35, "reclamarlo 5 veces más no paga nada");
int dustBefore = (await users.GetByDiscordIdAsync(U))!.Dust;
var claim9 = await repo.ClaimAdventAsync(U, season, ChristmasRules.Advent(9), 1);
Check(claim9.Status == AdventClaimStatus.Ok && (await users.GetByDiscordIdAsync(U))!.Dust == dustBefore + ChristmasRules.AdventDust, "día 9: el Polvo se pagó");
var claim7 = await repo.ClaimAdventAsync(U, season, ChristmasRules.Advent(7), 1);
Check(claim7.Receipt?.Reward.BoxName is not null, $"día 7: caja de la zona ({claim7.Receipt?.Reward.BoxName})");
Check((await repo.GetClaimedDaysAsync(U, season)).SetEquals([3, 7, 9]), "los días abiertos: 3, 7 y 9");
Check((await repo.ClaimAdventAsync(NOACC, season, ChristmasRules.Advent(1), 1)).Status == AdventClaimStatus.NoAccount, "sin cuenta no se paga nada");

Console.WriteLine("\n== 5. El servicio (reloj fijo) ==");
var before = new ChristmasService(repo, settings, () => settings.StartUtc.AddDays(-1));
Check(await before.GrantAsync(U, GameEventKinds.TravelWin) == 0, "antes del evento no da nada");
var during = new ChristmasService(repo, settings, () => settings.StartUtc.AddDays(2));
long b0 = (await repo.GetAsync(U, season)).Balance;
Check(await during.GrantAsync(U, GameEventKinds.TravelWin) == 5 && (await repo.GetAsync(U, season)).Balance == b0 + 5, "durante el farmeo, un viaje suma 5");
Check(await during.GrantAsync(U, GameEventKinds.DailyClaim, 8) == 15, "el daily con racha 8: 15");
int hunts = 0;
for (int i = 0; i < 400; i++) hunts += await during.GrantAsync(U, GameEventKinds.HuntWin);
Check(hunts is > 160 and < 240, $"400 cacerías dan ~200 Regalos ({hunts})");
var shop = new ChristmasService(repo, settings, () => settings.FarmEndUtc.AddDays(1));
Check(await shop.GrantAsync(U, GameEventKinds.BossWin) == 0, "en la etapa de tienda ya no caen");
var gameEvents = new GameEventService(new GameEventRepository(factory), christmas: during);
Check(await gameEvents.GrantEventGiftsAsync(U, GameEventKinds.BossWin) == 50, "el enganche de IGameEvents cobra el jefe: 50");
Check(await new GameEventService(new GameEventRepository(factory)).GrantEventGiftsAsync(U, GameEventKinds.BossWin) == 0, "sin el servicio de Navidad, no da nada");

Console.WriteLine("\n== 6. Las pantallas ==");
var claimedNow = await repo.GetClaimedDaysAsync(U, season);
var view = await ChristmasModule.BuildAsync(repo, during, U, settings.StartUtc.AddDays(1));
string grid = view.Embed.Fields.Single(f => f.Name.Contains("📅")).Value;
var rows = grid.Split('\n');
Check(rows.Length == 4 && rows[0] == "❌🎁✅⬜⬜⬜", $"la grilla: 4 filas; el 1 perdido, el 2 hoy, el 3 abierto («{rows[0]}»)");
bool HasClaim(MessageComponent? c) => c is not null && c.Components.SelectMany(row => ((ActionRowComponent)row).Components).OfType<ButtonComponent>().Any(x => x.CustomId?.StartsWith("adv_claim") == true);
Check(HasClaim(view.Components), "con el día de hoy sin abrir, hay botón para abrirlo");
var view7 = await ChristmasModule.BuildAsync(repo, during, U, settings.StartUtc.AddDays(6));
Check(!HasClaim(view7.Components) && view7.Embed.Fields.Any(f => f.Value.Contains("Ya abriste")), "el día 7 ya abierto: sin botón para abrir (sí el de la tienda)");
var off = await ChristmasModule.BuildAsync(repo, before, U, settings.StartUtc.AddDays(-1));
Check(off.Embed.Description.Contains("01/12") && off.Embed.Fields.Length == 0, "antes del evento: cuándo arranca");
var shopView = await ChristmasModule.BuildAsync(repo, shop, U, settings.FarmEndUtc.AddDays(1));
Check(shopView.Embed.Description.Contains("06/01") && !HasClaim(shopView.Components), "en la tienda: hasta cuándo gastarlos (el 6 de enero)");
var withGifts = ChristmasDisplay.WithGifts(view.Embed, 5);
Check(withGifts.Fields.Length == view.Embed.Fields.Length + 1 && ChristmasDisplay.WithGifts(view.Embed, 0) == view.Embed, "la línea de Regalos se suma solo si hubo");
var claimed24 = ChristmasModule.BuildClaimEmbed(new AdventClaimOutcome(AdventClaimStatus.Ok, ChristmasRules.Advent(24), 500, null));
Check(claimed24.Title.Contains("Nochebuena"), "el 24 es Nochebuena");
Check(LeakCheck.FitsDiscord(view.Embed) && LeakCheck.FitsDiscord(withGifts), "entra en los límites de Discord");
using (Loc.Use("en"))
{
    var en = await ChristmasModule.BuildAsync(repo, during, U, settings.StartUtc.AddDays(2));
    var enClaim = ChristmasModule.BuildClaimEmbed(claim3);
    var enGifts = ChristmasDisplay.WithGifts(en.Embed, 5);
    string enText = LeakCheck.AllText(en.Embed) + "\n" + LeakCheck.AllText(enClaim) + "\n" + LeakCheck.AllText(enGifts) + "\n" + ChristmasModule.Describe(ChristmasRules.Advent(24));
    var leak = LeakCheck.NoSpanish(enText, ["Fogón", "Calcetín", claim3.Receipt?.Reward.BoxName ?? "", claim7.Receipt?.Reward.BoxName ?? ""]);
    Check(leak.Ok, $"en inglés no queda español ({leak.Found})");
    Check(enText.Contains("Gifts"), "en inglés son «Gifts»");
}

Console.WriteLine("\n== 7. La tienda ==");
var campana = ChristmasShop.Find("campana")!;
var reno = ChristmasShop.Find("reno")!;
Check(campana.Price == 3 && reno.Price == 4500, "precios del dueño: Campana 3, Reno 4.500");
Check(ChristmasShop.Available(ChristmasShop.Find("vela")!, ChristmasPhase.Farm) && !ChristmasShop.Available(ChristmasShop.Find("vela")!, ChristmasPhase.Shop)
    && ChristmasShop.Available(reno, ChristmasPhase.Shop), "la Vela solo mientras caen Regalos; el resto hasta el 6 de enero");
await db.ExecuteAsync("UPDATE event_gifts SET balance = 100 WHERE discord_id = @id AND season = @season", new { id = (long)U, season });
var bells = await repo.BuyAsync(U, season, campana, 10, 1);
Check(bells.Status == ChristmasBuyStatus.Ok && bells.Quantity == 10 && bells.GiftsAfter == 70 && bells.BellsAfter == 10, "10 Campanas = 30 Regalos");
Check((await repo.BuyAsync(U, season, reno, 1, 1)).Status == ChristmasBuyStatus.NotEnoughGifts && (await repo.GetAsync(U, season)).Balance == 70, "el Reno sin Regalos suficientes: no cobra nada");
int dust0 = (await users.GetByDiscordIdAsync(U))!.Dust;
var sock = await repo.BuyAsync(U, season, ChristmasShop.Find("calcetin")!, 1, 1);
Check(sock.Status == ChristmasBuyStatus.Ok && sock.Receipt?.Reward.Gold > 0 && sock.Receipt.Reward.BoxName is not null
    && (await users.GetByDiscordIdAsync(U))!.Dust == dust0 + ChristmasShop.SockDust, "el Calcetín paga oro, EXP, caja y Polvo");

Console.WriteLine("\n== 8. La Vela ==");
await db.ExecuteAsync("UPDATE event_gifts SET balance = 200 WHERE discord_id = @id AND season = @season", new { id = (long)U, season });
var candle = await repo.BuyAsync(U, season, ChristmasShop.Find("vela")!, 1, 1);
Check(candle.CandleUntil is { } until && until > DateTime.UtcNow.AddMinutes(59), "la Vela dura 1 hora");
Check(await repo.GrantAsync(U, season, 1) == 2 && await repo.GrantAsync(U, season, 5) == 8 && await repo.GrantAsync(U, season, 50) == 75, "con la Vela: 1 → 2, 5 → 8, 50 → 75");
var candle2 = await repo.BuyAsync(U, season, ChristmasShop.Find("vela")!, 1, 1);
Check(candle2.CandleUntil > candle.CandleUntil!.Value.AddMinutes(59), "una segunda Vela suma otra hora");
await db.ExecuteAsync("UPDATE event_gifts SET candle_until = NULL WHERE discord_id = @id AND season = @season", new { id = (long)U, season });
Check(await repo.GrantAsync(U, season, 5) == 5, "sin Vela, 5 son 5");

Console.WriteLine("\n== 9. La Campana ==");
var adventure = new AdventureRepository(factory);
Check((await repo.UseBellAsync(U, season, CooldownCatalog.Hunt.Duration)).Status == BellUseStatus.NotNeeded && (await repo.GetAsync(U, season)).Bells == 10, "con /hunt listo no gasta la Campana");
await adventure.TryClaimCooldownAsync(U, CooldownCatalog.Hunt.CommandName, CooldownCatalog.Hunt.Duration, null);
var cds = new CooldownRepository(factory);
Check(await cds.GetRemainingAsync(U, "hunt", CooldownCatalog.Hunt.Duration) is not null, "/hunt quedó esperando");
var used = await repo.UseBellAsync(U, season, CooldownCatalog.Hunt.Duration);
Check(used.Status == BellUseStatus.Ok && used.BellsLeft == 9 && await cds.GetRemainingAsync(U, "hunt", CooldownCatalog.Hunt.Duration) is null, "¡Ding! /hunt listo y quedan 9");
Check((await repo.UseBellAsync(NOACC, season, CooldownCatalog.Hunt.Duration)).Status == BellUseStatus.NoBells, "sin Campanas no hace nada");

Console.WriteLine("\n== 10. Títulos y el Reno ==");
await db.ExecuteAsync("UPDATE event_gifts SET balance = 10000 WHERE discord_id = @id AND season = @season", new { id = (long)U, season });
var renoRace = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => repo.BuyAsync(U, season, reno, 1, 1)));
Check(renoRace.Count(r => r.Status == ChristmasBuyStatus.Ok) == 1 && renoRace.Count(r => r.Status == ChristmasBuyStatus.AlreadyOwned) == 4
    && (await repo.GetAsync(U, season)).Balance == 10000 - 4500, "5 compras del Reno a la vez: una sola cobra");
var stats = await new GameEventRepository(factory).GetStatsAsync(U);
Check(stats.GetValueOrDefault(RenoRules.StatKey) == 1, "el Reno queda en player_stats (para siempre)");
var bonusSvc = new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), new GameEventRepository(factory));
var withReno = await bonusSvc.GetAsync(U, 0);
Check(withReno.HasReno && Math.Abs(withReno.XpMultiplier - 1.15) < 1e-9 && Math.Abs(withReno.GoldMultiplier - 1.15) < 1e-9, "el Reno: +15 % de XP y de oro");
Check(withReno.TravelCooldown == TimeSpan.FromMinutes(25) && PlayerBonuses.None.TravelCooldown == TimeSpan.FromMinutes(30), "/travel: 25 minutos con el Reno, 30 sin");
var cdRows = new[] { ("travel", DateTime.UtcNow.AddMinutes(-27)) };
Check(ReminderCatalog.FromCooldowns(cdRows, DateTime.UtcNow).Count == 1 && ReminderCatalog.FromCooldowns(cdRows, DateTime.UtcNow, withReno.TravelCooldown).Count == 0,
    "el aviso de /travel respeta los 25 minutos del Reno");
Check((await repo.BuyAsync(U, season, ChristmasShop.Find("elfo")!, 1, 1)).Status == ChristmasBuyStatus.Ok, "el título «Elfo del Fogón» se compra");
var titleCtx = new TitleContext(await new GameEventRepository(factory).GetStatsAsync(U), 1, new HashSet<int>(), new HashSet<string>(), 0);
var xmasTitles = TitleCatalog.All.Where(t => t.PageKey == "navidad").ToList();
Check(xmasTitles.Count == 4 && TitleCatalog.IsUnlocked(xmasTitles.Single(t => t.Key == "xmas_elfo"), titleCtx) && !TitleCatalog.IsUnlocked(xmasTitles.Single(t => t.Key == "xmas_espiritu"), titleCtx),
    "página 🎄 Navidad con 4 títulos (2 de la tienda, 2 del Muñeco); el del Elfo desbloqueado");

Console.WriteLine("\n== 11. Calendario: Campanas y Calcetín ==");
int bellsBefore = (await repo.GetAsync(U, season)).Bells;
var day11 = await repo.ClaimAdventAsync(U, season, ChristmasRules.Advent(11), 1);
Check(day11.Status == AdventClaimStatus.Ok && (await repo.GetAsync(U, season)).Bells == bellsBefore + 5, "el día 11 da 5 Campanas");
var day24 = await repo.ClaimAdventAsync(U, season, ChristmasRules.Advent(24), 1);
Check(day24.Sock?.Reward.Gold > 0 && day24.Receipt?.Reward.BoxName is not null && day24.Day!.Gifts == 150, "Nochebuena: 150 Regalos, caja grande y Calcetín");

Console.WriteLine("\n== 12. Las pantallas de la tienda ==");
var shopScreen = await ChristmasModule.BuildShopAsync(repo, during, U, settings.StartUtc.AddDays(2));
var menuOptions = shopScreen.Components!.Components.SelectMany(r => ((ActionRowComponent)r).Components).OfType<SelectMenuComponent>().Single().Options;
Check(menuOptions.Count == ChristmasShop.Items.Count && LeakCheck.FitsDiscord(shopScreen.Embed), "la tienda: todos los artículos en el menú, entra en Discord");
var shopLate = await ChristmasModule.BuildShopAsync(repo, shop, U, settings.FarmEndUtc.AddDays(1));
Check(shopLate.Components!.Components.SelectMany(r => ((ActionRowComponent)r).Components).OfType<SelectMenuComponent>().Single().Options.All(o => o.Value != "vela"), "del 25 en adelante no se vende la Vela");
Check((await ChristmasModule.BuyAsync(users, zones, repo, during, U, "zapato", 1, settings.StartUtc.AddDays(2))).Error!.Contains("campana"), "un artículo que no existe: lista los que hay");
var bellText = await ChristmasModule.UseBellAsync(repo, during, U);
Check(bellText.Contains("/hunt"), "aa campana contesta");
using (Loc.Use("en"))
{
    var enShop = await ChristmasModule.BuildShopAsync(repo, during, U, settings.StartUtc.AddDays(2));
    var enBuy = ChristmasModule.BuildBuyEmbed(renoRace.First(r => r.Status == ChristmasBuyStatus.Ok));
    var enSock = ChristmasModule.BuildBuyEmbed(sock);
    var enDay24 = ChristmasModule.BuildClaimEmbed(day24);
    string enText = LeakCheck.AllText(enShop.Embed) + "\n" + LeakCheck.AllText(enBuy) + "\n" + LeakCheck.AllText(enSock) + "\n" + LeakCheck.AllText(enDay24);
    var shopNames = ChristmasShop.Items.Select(i => i.Name).Concat([sock.Receipt?.Reward.BoxName ?? "", day24.Receipt?.Reward.BoxName ?? "", day24.Sock?.Reward.BoxName ?? "", "Fogón"]).ToList();
    var shopLeak = LeakCheck.NoSpanish(enText, shopNames);
    Check(shopLeak.Ok, $"la tienda en inglés sin español ({shopLeak.Found})");
}

Console.WriteLine("\n== 13. El Krampus ==");
var gifts30to40 = Enumerable.Range(0, 100).Select(r => ChristmasRules.VictoryGifts(GameEventKinds.KrampusWin, r)).ToList();
Check(gifts30to40.Min() == 30 && gifts30to40.Max() == 40, "el Krampus da entre 30 y 40 Regalos");
var travelTemplate = new MonsterTemplate("Élite", "🐺", 100, 200, 10, 20, ["Colmillo"], 5, 5, "retrato");
var krampus = KrampusRules.FromTravel(travelTemplate);
Check(krampus.Name == "Krampus" && krampus.MinHp == 150 && krampus.MaxHp == 300 && krampus.MinDamage == 12 && krampus.MaxDamage == 24 && krampus.DropItemNames.Count == 0,
    "el Krampus es el élite de la zona con ×1,5 de vida, ×1,2 de daño y sin drops");
Check(KrampusRules.Cooldown.Duration == TimeSpan.FromHours(4) && KrampusRules.Cooldown.RetryAfterFailure is null && !CooldownCatalog.All.Contains(KrampusRules.Cooldown),
    "cooldown propio de 4 h (no gasta el del jefe ni aparece en /cd fuera del evento)");
Check(ChristmasModule.KrampusClosedMessage(during, settings.StartUtc.AddDays(3)) is null && ChristmasModule.KrampusClosedMessage(shop, settings.FarmEndUtc.AddDays(1)) is not null,
    "solo se pelea del 1 al 24");
var combatSessions = new CombatSessionService(users);
var starter = new AdventureCombatStarter(new CooldownRepository(factory), new AdventureRepository(factory), users, new ItemRepository(factory), new MonsterRepository(factory), zones,
    new BuffRepository(factory), bonusSvc, combatSessions, new RaidSessionService());
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp WHERE discord_id = @id", new { id = (long)U });
var started = await starter.PrepareKrampusAsync(U);
Check(started.Status == CombatStartStatus.Started && started.State!.CommandName == "krampus" && started.State.MonsterName == "Krampus", "arranca una pelea contra el Krampus");
Check((await starter.PrepareKrampusAsync(U)).Status is CombatStartStatus.OnCooldown or CombatStartStatus.AlreadyInCombat, "no se puede volver a arrancar enseguida");
Check(await cds.GetRemainingAsync(U, "krampus", KrampusRules.Cooldown.Duration) is { } kw && kw > TimeSpan.FromHours(3.9), "quedó esperando 4 h");
Check(await cds.GetRemainingAsync(U, "boss", CooldownCatalog.Boss.Duration) is null, "no tocó el cooldown del jefe");
var encounter = AdventureModule.BuildEncounterEmbed(started.State!);
Check(encounter.Title.Contains("Krampus"), "el encuentro dice que es el Krampus");
var navView = await ChristmasModule.BuildAsync(repo, during, U, settings.StartUtc.AddDays(2), cds);
Check(navView.Embed.Description.Contains("/krampus") && !navView.Embed.Description.Contains("¡listo!"), "/navidad muestra cuánto le falta al Krampus");

Console.WriteLine("\n== 14. El Muñeco de Nieve Gigante ==");
ulong[] players = [999000000000098921UL, 999000000000098922UL, 999000000000098923UL, 999000000000098924UL];
async Task CleanSnow()
{
    await db.ExecuteAsync("DELETE FROM event_snowman WHERE season = @season", new { season });
    await db.ExecuteAsync("DELETE FROM event_snowman_hits WHERE season = @season", new { season });
    foreach (var p in players)
    {
        await db.ExecuteAsync("DELETE FROM event_gifts WHERE discord_id = @id", new { id = (long)p });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)p });
    }
}

await CleanSnow();
foreach (var p in players)
{
    await users.GetOrCreateUserAsync(p);
    await repo.GrantAsync(p, season, 1);
    await db.ExecuteAsync("UPDATE event_gifts SET balance = 100000 WHERE discord_id = @id AND season = @season", new { id = (long)p, season });
}

var snowRepo = new SnowmanRepository(factory);
var snowSvc = new SnowmanService(snowRepo, during, 3000);
Check(SnowmanRules.Find("bola")!.Damage == 10 && SnowmanRules.Find("trineo")!.Price == 200 && SnowmanRules.Projectiles.Count == 4, "4 proyectiles: Bola, Bastón, Petardo, Trineo");
Check(SnowmanRules.Projectiles.Zip(SnowmanRules.Projectiles.Skip(1)).All(p => (double)p.Second.Damage / p.Second.Price > (double)p.First.Damage / p.First.Price),
    "los caros rinden más daño por Regalo");
var st0 = await snowSvc.GetStateAsync(players[0]);
Check(st0.Round == 1 && st0.Hp == 3000 && st0.MaxHp == 3000 && st0.MyDamage == 0 && st0.MyRank == 0, "abre la primera vuelta con la vida de la configuración");
var poor = await snowRepo.ThrowAsync(U, season, SnowmanRules.Find("trineo")!, 1000, 3000, true);
Check(poor.Status == SnowThrowStatus.NotEnoughGifts && (await snowSvc.GetStateAsync(U)).Hp == 3000, "sin Regalos no pega ni cobra");
// players[0] pega 1.200, players[1] 600, players[2] 110, y después todos a la vez hasta tirarlo.
await snowSvc.ThrowAsync(players[0], "Uno", SnowmanRules.Find("petardo")!, 2);
await snowSvc.ThrowAsync(players[1], "Dos", SnowmanRules.Find("petardo")!, 1);
await snowSvc.ThrowAsync(players[2], "Tres", SnowmanRules.Find("baston")!, 1);
var mid = await snowSvc.GetStateAsync(players[1]);
Check(mid.Hp == 3000 - 1910 && mid.MyDamage == 600 && mid.MyRank == 2 && mid.Participants == 3, $"vida 1.090, el segundo va puesto 2 ({mid.Hp}, puesto {mid.MyRank})");
Check(snowSvc.Feed().Count == 3 && snowSvc.Feed()[0].Name == "Tres", "el panel guarda las últimas tiradas, la más nueva primero");
long goldChest = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity),0) FROM inventory v JOIN items i USING (item_id) WHERE v.discord_id = @id AND i.name = 'Cofre de Oro'", new { id = (long)players[3] });
var race20 = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => snowSvc.ThrowAsync(players[i % 4], "x", SnowmanRules.Find("bola")!, 10)));
var kills = race20.Where(r => r.Kill is not null).ToList();
Check(kills.Count == 1, "20 tiros a la vez: uno solo da el último golpe");
var kill = kills.Single().Kill!;
Check(kill.Round == 1 && kill.Participants == 4 && kill.NextMaxHp == 4500, "cobran los 4 y vuelve con ×1,5 de vida (4.500)");
Check(kill.Top[0].DiscordId == players[0] && kill.Top[0].Rank == 1, "el que más pegó es el primero del ranking");
var hitsSum = await db.ExecuteScalarAsync<long>("SELECT SUM(damage) FROM event_snowman_hits WHERE season = @season AND round = 1", new { season });
Check(hitsSum == 3000, $"el daño anotado suma justo la vida (3.000; {hitsSum}): lo que sobra del último golpe no cuenta");
Check(race20.Count(r => r.Status == SnowThrowStatus.Ok) == 20, "los tiros después de la muerte pegan a la vuelta nueva");
var after = await snowSvc.GetStateAsync(players[0]);
Check(after.Round == 2 && after.MaxHp == 4500 && after.Kills == 1, "vuelta 2, cayó 1 vez");
var gp0 = await repo.GetAsync(players[0], season);
var p3Escoria = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity),0) FROM inventory v JOIN items i USING (item_id) WHERE v.discord_id = @id AND i.name = 'Cofre de Escoria'", new { id = (long)players[3] });
var p0Escoria = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity),0) FROM inventory v JOIN items i USING (item_id) WHERE v.discord_id = @id AND i.name = 'Cofre de Escoria'", new { id = (long)players[0] });
long goldChestAfter = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity),0) FROM inventory v JOIN items i USING (item_id) WHERE v.discord_id = @id AND i.name = 'Cofre de Oro'", new { id = (long)players[3] });
Check(gp0.Earned >= 1 + SnowmanRules.AllGifts + SnowmanRules.Top10Gifts, "el top 10 cobra 150 + 300 Regalos");
Check(p0Escoria == 1 && p3Escoria == 0 && goldChestAfter == goldChest + 1, "Cofre de Oro para todos; Cofre de Escoria solo al top 3");
var killStats = await new GameEventRepository(factory).GetStatsAsync(players[3]);
Check(killStats.GetValueOrDefault(SnowmanRules.KillStat) == 1 && killStats.GetValueOrDefault(SnowmanRules.DamageStat) > 0, "contadores: 1 vuelta ganada y el daño total (para los títulos)");
Check(TitleCatalog.All.Count(t => t.Source == SnowmanRules.KillStat) == 2, "dos títulos del Muñeco (1 y 3 vueltas)");
Check(SnowmanRules.DustFor(5) == 10 && SnowmanRules.DustFor(1200) == 120 && SnowmanRules.DustFor(99999) == 500, "Polvo: 1 cada 10 de daño, entre 10 y 500");

// Sin vuelta nueva (se terminó el farmeo): queda en el suelo y no se cobra nada más.
var lastKill = await snowRepo.ThrowAsync(players[0], season, SnowmanRules.Find("trineo")!, 2, 3000, canRespawn: false);
Check(lastKill.Kill is { NextMaxHp: null }, "si cae después del 24, no vuelve");
long before24 = (await repo.GetAsync(players[0], season)).Balance;
var dead = await snowRepo.ThrowAsync(players[0], season, SnowmanRules.Find("bola")!, 1, 3000, true);
Check(dead.Status == SnowThrowStatus.Dead && (await repo.GetAsync(players[0], season)).Balance == before24, "caído: no se puede tirar ni se cobra");

var panel = await SnowmanModule.BuildPanelAsync(repo, during, snowSvc, players[0], settings.StartUtc.AddDays(3));
Check(panel.Components is not null && LeakCheck.FitsDiscord(panel.Embed), "el panel tiene botones y entra en Discord");
var killEmbed = SnowmanModule.BuildKillEmbed(kill);
Check(killEmbed.Description!.Contains($"<@{players[0]}>") && killEmbed.Description.Contains("4.500") && LeakCheck.FitsDiscord(killEmbed), "el anuncio: ranking con menciones y la vida nueva");
Check((await SnowmanModule.ThrowAsync(shop, snowSvc, players[0], "x", "bola", 1, settings.FarmEndUtc.AddDays(1))).Error is not null, "del 25 en adelante no se tira");
using (Loc.Use("en"))
{
    var enPanel = await SnowmanModule.BuildPanelAsync(repo, during, snowSvc, players[1], settings.StartUtc.AddDays(3));
    string enText = LeakCheck.AllText(enPanel.Embed) + "\n" + LeakCheck.AllText(SnowmanModule.BuildKillEmbed(kill));
    var names = SnowmanRules.Projectiles.Select(p => p.Name).Concat([SnowmanRules.AllBox, SnowmanRules.TopBox, "Uno", "Dos", "Tres", "x", "aa muñeco tirar petardo 5"]).ToList();
    var snowLeak = LeakCheck.NoSpanish(enText, names);
    Check(snowLeak.Ok, $"el Muñeco en inglés sin español ({snowLeak.Found})");
}

await CleanSnow();

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
