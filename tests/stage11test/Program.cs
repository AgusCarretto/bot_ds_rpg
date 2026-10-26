using System.Globalization;
using System.Reflection;
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

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var recipesRepo = sp.GetRequiredService<IRecipeRepository>();
var zonesRepo = sp.GetRequiredService<IZoneRepository>();
var monsters = sp.GetRequiredService<IMonsterRepository>();
var missions = sp.GetRequiredService<IMissionRepository>();
var events = sp.GetRequiredService<IGameEvents>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
const ulong U = 999000000000011001UL, GHOST = 999000000000011999UL;
async Task Cleanup() => await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id;", new { id = (long)U });
async Task Give(string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + @q", new { id = (long)U, n = name, q });
async Task Reset(int gold, int level, int zone, string cls = "Guerrero")
{
    await Cleanup();
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET gold = @g, level = @l, current_zone_id = @z, class = @c, current_hp = max_hp WHERE discord_id = @id", new { id = (long)U, g = gold, l = level, z = zone, c = cls });
}

List<SelectMenuComponent> Menus(BlacksmithScene s) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().ToList() ?? [];
Task<BlacksmithScene> Forge(bool book = false, ulong? id = null) => BlacksmithModule.BuildSceneAsync(users, recipesRepo, zonesRepo, inventory, id ?? U, book);

var zones = await zonesRepo.GetAllAsync();
var zonesByRank = ZoneRanking.OrderByDifficulty(zones);

Console.WriteLine("== 1. Comandos ==");
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var slash = interactions.SlashCommands.Select(c => c.Name).ToList();
Check(slash.Contains("tips") && slash.Contains("forge") && slash.Contains("taberna"), "existen /tips, /forge y /taberna");
Check(interactions.ComponentCommands.Any(c => c.Name.StartsWith("blacksmith_book")) && interactions.ComponentCommands.Any(c => c.Name.StartsWith("blacksmith_home")) && interactions.ComponentCommands.Any(c => c.Name.StartsWith("blacksmith_pick")) && !interactions.ComponentCommands.Any(c => c.Name.StartsWith("blacksmith_zone")), "los botones del recetario (ver todo / volver) tienen su handler, el de forjar sigue y el selector de zonas ya no existe");
Check(texts.Commands.Any(c => c.Name == "tips" && c.Aliases.Contains("consejo")) && texts.Commands.Any(c => c.Name == "forge recipes" && c.Parameters.Count == 1 && c.Parameters[0].IsOptional), "en texto: 'aa tips' (alias consejo) y 'aa forge recipes [zona]' con la zona opcional");
var fakeCtx = (ICommandContext)FakeProxy.Create(typeof(ICommandContext), (n, a) => null);
async Task<List<string>> ParsesOk(string input)
{
    var found = texts.Search(input);
    var ok = new List<string>();
    foreach (var m in found.Commands)
    {
        var pr = await m.ParseAsync(fakeCtx, found, Discord.Commands.PreconditionResult.FromSuccess(), sp);
        if (pr.IsSuccess) ok.Add(m.Command.Name);
    }

    return ok;
}

var pR0 = await ParsesOk("forge recipes"); var pR3 = await ParsesOk("forge recipes 3"); var pFr = await ParsesOk("fr 2");
var pMake = await ParsesOk("forge make Espada de Madera"); var pAlone = await ParsesOk("forge"); var pTips = await ParsesOk("tips"); var pCons = await ParsesOk("consejo");
Check(pR0.SequenceEqual(["forge recipes"]) && pR3.SequenceEqual(["forge recipes"]) && pFr.SequenceEqual(["forge recipes"]), $"'aa forge recipes', 'aa forge recipes 3' y 'aa fr 2' parsean SOLO como recetas ({string.Join(",", pR0)} | {string.Join(",", pR3)} | {string.Join(",", pFr)})");
Check(pMake.Contains("forge make") && !pMake.Contains("blacksmith") && pAlone.SequenceEqual(["blacksmith"]) && pTips.SequenceEqual(["tips"]) && pCons.SequenceEqual(["tips"]), "lo demás sigue parseando como antes: forge make, forge solo (herrería), tips y consejo");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && !x.IsSealed && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");

Console.WriteLine("\n== 2. El jefe paga más que un /travel ==");
Check(CombatRewardCalculator.BossRewardMultiplier == 15 && CombatRewardCalculator.TravelRewardMultiplier == 30, "multiplicadores: jefe x15 (cooldown de 1 h), viaje x30 (cooldown de 30 min); el jefe suma su bono grande, ver seed_zone_bosses.sql");
var plain = Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollBossReward(10, 0, 0, false)).ToList();
Check(plain.All(r => r.Gold is >= 15 * 25 and <= 15 * 35 && r.Xp is >= 135 and <= 225), "jefe sin bono a nivel 10: oro 375-525 y XP 135-225 (la fórmula de /hunt x15, con la EXP a la mitad desde la v0.18.1)");
var hunt = Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollHuntReward(10)).ToList();
Check(hunt.All(r => r.Gold is >= 25 and <= 35 && r.Xp is >= 9 and <= 15), "/hunt: oro 25-35 y XP 9-15 a nivel 10 (la EXP a la mitad desde la v0.18.1)");
Check(CombatRewardCalculator.FightXpScale == 0.5 && Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollTravelReward(10)).All(r => r.Xp is >= 270 and <= 450 && r.Gold is >= 30 * 25 and <= 30 * 35) && Enumerable.Range(0, 2000).Select(_ => CombatRewardCalculator.RollHuntReward(10, 0, 100)).All(r => r.Xp is >= 59 and <= 65), "la EXP de las peleas vale la mitad (0,5): viaje x30 a nivel 10 = 270-450, y un monstruo con bono 100 da 59-65 (el oro no cambia)");
foreach (var zone in zonesByRank)
{
    var boss = await monsters.GetBossByZoneAsync(zone.ZoneId); var travel = await monsters.GetTravelMonsterByZoneAsync(zone.ZoneId);
    if (boss is null || travel is null) { Check(false, $"zona {zone.ZoneId} sin jefe o sin monstruo de viaje"); continue; }
    int level = zone.MinLevel + 3;
    var b = Enumerable.Range(0, 3000).Select(_ => CombatRewardCalculator.RollBossReward(level, boss.GoldBonus, boss.XpBonus, false)).ToList();
    var t = Enumerable.Range(0, 3000).Select(_ => CombatRewardCalculator.RollTravelReward(level, travel.GoldBonus, travel.XpBonus)).ToList();
    double xpRatio = b.Average(r => r.Xp) / t.Average(r => r.Xp), goldRatio = b.Average(r => r.Gold) / t.Average(r => r.Gold);
    Check(b.Min(r => r.Xp) > t.Max(r => r.Xp) && b.Min(r => r.Gold) > t.Max(r => r.Gold) && xpRatio is > 2.5 and < 6.5,
        $"Zona {zone.ZoneId} (nivel {level}): el PEOR jefe paga más que el MEJOR viaje — XP {b.Average(r => r.Xp):0} vs {t.Average(r => r.Xp):0} ({xpRatio:0.0}x), oro {b.Average(r => r.Gold):0} vs {t.Average(r => r.Gold):0} ({goldRatio:0.0}x)");
}

Console.WriteLine("\n== 3. La tarjeta de subida de nivel (pura) ==");
var seeded = new Random(1);
var one = LevelUpCard.Compose(U, 7, 1, zones, seeded);
Check(one.Title == "🎉 ¡SUBISTE DE NIVEL! 🎉" && one.Description.Contains($"<@{U}>") && one.Description.Contains("nivel 7") && one.Description.Contains("Nivel 6  ➜  **Nivel 7**"), $"un nivel: título, mención, 6 ➜ 7 ({one.Description.Replace("\n", " / ")})");
Check(one.Fields.Count == 4 && one.Fields.Take(3).All(f => f.Inline) && !one.Fields[3].Inline && one.Fields[0].Value == "**+15**" && one.Fields[1].Value == "**+2**" && one.Fields[2].Value == "**+1**" && one.Fields[3].Value.Contains("máximo") && one.Footer.Length > 5, "vida máxima +15, ATQ +2 y DEF +1 en tres columnas y .curada al máximo. abajo, y una frase al pie (sin zona nueva a nivel 7)");
var multi = LevelUpCard.Compose(U, 12, 3, zones, seeded);
Check(multi.Title.Contains("SUBISTE 3 NIVELES") && multi.Description.Contains("Nivel 9  ➜  **Nivel 12**") && multi.Fields[0].Value == "**+45**", "tres niveles de una: lo dice, 9 ➜ 12 y +45 de vida");
var z2 = zonesByRank[1];
var unlock = LevelUpCard.Compose(U, z2.MinLevel, 1, zones, seeded);
Check(unlock.Fields.Count == 5 && !unlock.Fields[4].Inline && unlock.Fields[4].Value.Contains($"Zona {z2.ZoneId}: {z2.Name}") && unlock.Fields[4].Value.Contains("/zonas"), $"llegar a nivel {z2.MinLevel}: avisa que se acerca la zona «{z2.Name}»");
var none = LevelUpCard.Compose(U, z2.MinLevel + 1, 1, zones, seeded);
Check(none.Fields.Count == 4, "al nivel siguiente ya no repite el aviso de zona");
var two = LevelUpCard.Compose(U, zonesByRank[2].MinLevel, zonesByRank[2].MinLevel - 1, zones, seeded);
Check(two.Fields.Count == 5 && two.Fields[4].Name.Contains("Nuevas zonas") && two.Fields[4].Value.Contains(z2.Name) && two.Fields[4].Value.Contains(zonesByRank[2].Name), "si cruza dos zonas de una (muchos niveles), las avisa a las dos");
var odd = LevelUpCard.Compose(U, 1, 0, zones, seeded);
Check(odd.Title.Contains("SUBISTE DE NIVEL") && odd.Description.Contains("Nivel 1"), "datos raros (cero niveles, nivel 1) no rompen nada");
var cheers = Enumerable.Range(0, 200).Select(i => LevelUpCard.Compose(U, 3, 1, zones, new Random(i)).Footer).Distinct().Count();
Check(cheers >= 5, $"la frase del pie varía ({cheers} distintas)");

Console.WriteLine("\n== 4. El aviso de nivel (notificador y cola reales) ==");
var notifier = new ProgressNotifier(missions, zonesRepo);
DateTime now = DateTime.UtcNow;
var n1 = await notifier.OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, now, z2.MinLevel.ToString());
var lvl = n1.FirstOrDefault(n => n.Embed is not null);
Check(lvl is not null && lvl.Public && lvl.Text == "" && lvl.ExpiresUtc is { } exp && exp > now.AddMinutes(1) && exp <= now.AddMinutes(3) && lvl.Embed!.Fields.Length == 5 && lvl.Embed.Color == Color.Gold,
    "level_up: un aviso PÚBLICO con embed dorado (sin texto), con zona nueva, que vence en ~2 min");
Check(lvl!.Embed!.Length < 6000 && lvl.Embed.Title.Contains("SUBISTE") && lvl.Embed.Footer is not null, "el embed entra en los límites de Discord");
Console.WriteLine("   " + string.Join("\n   ", new[] { lvl.Embed.Title, lvl.Embed.Description }.Concat(lvl.Embed.Fields.Select(f => f.Name + " -> " + f.Value)).Concat([lvl.Embed.Footer!.Value.Text]).Select(x => x.Replace("\n", " / "))));
var noDetail = await notifier.OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, now);
var badDetail = await notifier.OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, now, "abc");
var zeroAmt = await notifier.OnEventAsync(U, GameEventKinds.LevelUp, 0, 1, now, "5");
Check(!noDetail.Any(n => n.Embed is not null) && !badDetail.Any(n => n.Embed is not null) && !zeroAmt.Any(n => n.Embed is not null), "sin nivel en el detalle (o ilegible, o 0 niveles) no inventa un aviso");
var otherKind = await notifier.OnEventAsync(U, GameEventKinds.HuntWin, 1, 1, now, "5");
Check(!otherKind.Any(n => n.Embed is not null), "otros eventos (cazar) no generan la tarjeta de nivel");
var plainNotifier = await new ProgressNotifier(missions).OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, now, z2.MinLevel.ToString());
Check(plainNotifier.Single(n => n.Embed is not null).Embed!.Fields.Length == 4, "sin repositorio de zonas igual festeja el nivel (solo sin el aviso de zona)");
var throwingZones = (IZoneRepository)FakeProxy.Create(typeof(IZoneRepository), (n, a) => throw new InvalidOperationException("la base se cayó"));
var tolerant = await new ProgressNotifier(missions, throwingZones).OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, now, z2.MinLevel.ToString());
Check(tolerant.Any(n => n.Embed is not null && n.Embed.Fields.Length == 4), "si las zonas fallan, el nivel igual se festeja");

Console.WriteLine("\n== 5. Cola real: registrar una victoria con subida de nivel ==");
await Reset(100, 7, 1);
var player = (await users.GetByDiscordIdAsync(U))!;
await events.RecordVictoryAsync(U, GameEventKinds.HuntWin, new LevelUpOutcome(player, 2));
var queued = events.TakeNotices(U);
var banner = queued.SingleOrDefault(n => n.Embed is not null);
Check(banner is not null && banner.Embed!.Title.Contains("SUBISTE 2 NIVELES") && banner.Embed.Description.Contains("Nivel 5  ➜  **Nivel 7**") && banner.Embed.Description.Contains($"<@{U}>"),
    $"la victoria que sube 2 niveles deja UNA tarjeta en la cola del jugador (avisos: {queued.Count})");
Check(events.TakeNotices(U).Count == 0, "se entrega una sola vez");
await events.RecordVictoryAsync(U, GameEventKinds.HuntWin, new LevelUpOutcome(player, 0));
Check(!events.TakeNotices(U).Any(n => n.Embed is not null), "una victoria sin subir de nivel no deja tarjeta");
var enqueue = events.GetType().GetMethod("Enqueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
enqueue.Invoke(events, [U, new GameNotice("vencido", false, null, DateTime.UtcNow.AddSeconds(-5))]);
enqueue.Invoke(events, [U, new GameNotice("vigente", false, null, DateTime.UtcNow.AddMinutes(1))]);
enqueue.Invoke(events, [U, new GameNotice("sin vencimiento", false)]);
var taken = events.TakeNotices(U).Select(n => n.Text).ToList();
Check(taken.SequenceEqual(["vigente", "sin vencimiento"]), $"un aviso vencido no se entrega a nadie (llegaron: {string.Join(", ", taken)})");

Console.WriteLine("\n== 6. Los mensajes ya no llevan el nivel ni el consejo ==");
var victory = typeof(AdventureModule).GetMethod("BuildVictoryEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
var turn = CombatTurnResolver.ResolveTurn(new CombatantProfile(100, 0, "Guerrero", ClassPassives.For("Guerrero")), default, PlayerAction.Attack, 100, 100, 1, 5);
var cs = new CombatState("boss", "Lobisón Alfa", "👑🐺", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
var win = (Embed)victory.Invoke(null, [cs, turn, new CombatReward(100, 100, false), null, new LevelUpOutcome(player, 1), true])!;
Check(!win.Fields.Any(f => f.Name.Contains("Subiste") || f.Name.Contains("💡")) && win.Description!.Contains("Oro") && win.Description.Contains("XP"), $"la victoria (aun subiendo de nivel) queda corta, con lo ganado en la descripción (desde la v0.26.2, como el autohunt): {string.Join(" | ", win.Fields.Select(f => f.Name))}");
var gather = GatheringModule.BuildResultEmbed(CooldownCatalog.Chop, (await items.GetByNameAsync("Madera de Pino"))!, 4);
Check(gather.Fields.Length == 1 && gather.Fields[0].Name == "Rareza", "/chop y /mine: solo lo que conseguiste y la rareza");
var daily = DailyModule.BuildResultEmbed(new DailyClaimCalculation(DailyClaimStatus.StreakContinued, 3, null), new LevelUpOutcome(player, 1), new RewardReceipt(new ResolvedReward(100, 100, null, 0), 1000, 1, 5));
Check(!daily.Fields.Any(f => f.Name.Contains("Subiste")) && daily.Fields.Length == 3, "/daily: racha, oro y XP, sin la línea de nivel");

Console.WriteLine("\n== 7. /tips ==");
await Reset(10, 3, 1);
var advisor = sp.GetRequiredService<IFarmAdvisor>();
await Give("Madera de Pino", 2);
var tips = await TipsModule.BuildTipsEmbedAsync(advisor, U);
Check(tips.Title is "💡 Para tu próxima forja" or "💡 ¡Ya podés forjar algo!" && !string.IsNullOrEmpty(tips.Description) && tips.Fields.Length == 0 && tips.Footer is not null && tips.Length < 6000, $"con materiales a medias: embed con el consejo ({tips.Title})");
Console.WriteLine("   " + tips.Description.Replace("\n", "\n   "));
var nothing = (IFarmAdvisor)FakeProxy.Create(typeof(IFarmAdvisor), (n, a) => Task.FromResult<FarmAdvice?>(null));
var empty = await TipsModule.BuildTipsEmbedAsync(nothing, U);
Check(empty.Title == "💡 Consejo" && empty.Description.Contains("/zonas"), "sin nada que aconsejar: lo dice y manda a /zonas");
var ghostTip = await TipsModule.BuildTipsEmbedAsync(advisor, GHOST);
Check(ghostTip.Title == "💡 Consejo", "alguien sin cuenta: no se rompe ni se le crea cuenta");

Console.WriteLine("\n== 8. La herrería: saludo y botón del recetario ==");
await Reset(500, 3, 1);
var home = await Forge();
var hm = Menus(home);
List<ButtonComponent> Buttons(BlacksmithScene s) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList() ?? [];
Check(hm.Count == 1 && hm[0].CustomId == $"blacksmith_pick:{U}" && Buttons(home).Count == 1 && Buttons(home)[0].CustomId == $"blacksmith_book:{U}" && Buttons(home)[0].Label.Contains("Ver todas las recetas"), $"una lista para forjar y un botón «Ver todas las recetas» (v0.27.0: ya no hay selector de zonas) ({string.Join(", ", hm.Select(m => m.CustomId))} + {string.Join(", ", Buttons(home).Select(b => b.CustomId))})");
Check(home.Embed.Title == "⚒️ La Herrería" && home.Embed.Description.Contains("botón de abajo") && home.Embed.Footer is not null && home.Embed.Footer.Value.Text.Contains("abajo"), "el saludo avisa que el botón muestra todas las recetas y que la respuesta sale abajo");
Console.WriteLine("   " + home.Embed.Description.Replace("\n", "\n   "));
Check(hm.All(m => m.Options.Count is >= 1 and <= 25) && home.Components!.Components.OfType<ActionRowComponent>().Count() == 2, "dentro de los límites de Discord (2 filas, hasta 25 opciones en la lista)");

Console.WriteLine("\n== 9. El recetario completo ==");
var book = await Forge(true);
var bm = Menus(book);
string ZW11 = ((char)0x200B).ToString();
Check(book.Embed.Title == "⚒️ Recetas del Herrero" && book.Embed.Length < 6000 && bm.Count == 1 && bm[0].CustomId == $"blacksmith_pick:{U}" && Buttons(book).Single().CustomId == $"blacksmith_home:{U}" && Buttons(book).Single().Label.Contains("Volver"),
    $"el recetario: título propio, la lista para forjar sigue, y el botón vuelve al saludo ({book.Embed.Length} caracteres)");
var zoneFields = book.Embed.Fields.Where(f => f.Name.Contains(" · nivel ")).ToList();
Check(zoneFields.Count == zonesByRank.Count && book.Embed.Fields.Length == zonesByRank.Count + 1 && book.Embed.Fields.Last().Name == "🧰 Sin zona específica" && zoneFields.All(f => f.Value.Split('\n').Length == 2),
    $"un campo por zona (en orden de dificultad) con SUS 2 recetas (el arma de tu clase y el amuleto) y, al final, «Sin zona específica» ({string.Join(" | ", book.Embed.Fields.Select(f => f.Name))})");
foreach (var z in zonesByRank)
{
    bool locked = z.MinLevel > 3;
    var f = book.Embed.Fields.Single(x => x.Name.Contains($"Zona {z.ZoneId} · {z.Name}"));
    Check(f.Name.Contains($"nivel {z.MinLevel}") && f.Name.Contains("🔒") == locked, $"Zona {z.ZoneId}: «{f.Name}» {(locked ? "lleva el 🔒 (sos nivel 3)" : "sin candado")}");
}
Check(book.Embed.Description.Contains("lista de arriba") && !book.Embed.Description.Contains("/forge make"), "la frase de cómo forjar es la de la escena (la lista de arriba), no la del comando viejo");
await Give("Hierro", 5);
var bookWithIron = await Forge(true);
Check(!string.Join(" ", bookWithIron.Embed.Fields.Select(f => f.Value)).Contains("5/") && !string.Join(" ", bookWithIron.Embed.Fields.Select(f => f.Value)).Contains("✅"), "el recetario es una lista simple: sin «tenés X de Y» ni ✅/❌ (eso lo marca la lista para forjar)");
var ghostForge = await Forge(false, GHOST);
Check(ghostForge.Components is null && ghostForge.Embed.Description.Contains("/start"), "sin cuenta: manda a /start");

Console.WriteLine("\n== 10. El texto ('aa forge recipes') con zona ==");
var textAll = await ForgeModule.BuildRecipesEmbed(recipesRepo, zonesRepo, "Guerrero", null, 3);
var textZone3 = await ForgeModule.BuildRecipesEmbed(recipesRepo, zonesRepo, "Guerrero", zonesByRank[2].ZoneId, 3);
var textNone = await ForgeModule.BuildRecipesEmbed(recipesRepo, zonesRepo, "Guerrero", 9999, 3);
Check(textAll.Fields.Length == zonesByRank.Count + 1 && textZone3.Fields.Length == 1 && textZone3.Fields[0].Name.Contains($"Zona {zonesByRank[2].ZoneId}") && textZone3.Fields[0].Name.Contains("🔒") && textAll.Description.Contains("aa forge make") && !textAll.Description.Contains("/forge make"),
    "'aa forge recipes' muestra todas las zonas y «Sin zona específica»; 'aa forge recipes 3' solo esa zona (con 🔒 si no llegás); la frase de cómo forjar es la del comando de texto");
Check(textNone.Fields.Length == 0 && textNone.Description.Contains("9999"), "una zona que no existe no rompe: lo dice");

Console.WriteLine("\n== 11. Respuestas aparte (herrero y tabernero) ==");
var forged = new ForgeModule.ForgeMakeResult(null, new EmbedBuilder().WithTitle("⚒️ ¡Forjado con éxito!").WithDescription("ok").Build());
var refused = new ForgeModule.ForgeMakeResult("El herrero no entiende ese pedido.", null);
Check(BlacksmithModule.BuildAnswerEmbed(forged).Title == "⚒️ ¡Forjado con éxito!" && BlacksmithModule.BuildAnswerEmbed(refused).Description == "El herrero no entiende ese pedido." && BlacksmithModule.BuildAnswerEmbed(new ForgeModule.ForgeMakeResult(null, null)).Description == "⚒️", "herrero: la respuesta es su embed del resultado, o el rechazo simple (y nunca queda vacío)");
var tabAnswer = TabernaModule.BuildAnswerEmbed("🍺 **El Tabernero:** «Dos mates, jefe.»\n\nCompraste **Mate Amargo** x1 por **8** de oro.");
Check(tabAnswer.Description.StartsWith("🍺 **El Tabernero:**") && tabAnswer.Color == Color.Gold && TabernaModule.BuildAnswerEmbed("").Description == "🍺" && TabernaModule.BuildAnswerEmbed(new string('x', 5000)).Description.Length <= 4000, "tabernero: su respuesta es un embed propio (dorado), nunca vacío y siempre dentro del límite de Discord");
var tabScene = await TabernaModule.BuildSceneAsync(users, items, inventory, sp.GetRequiredService<IBuffRepository>(), U, null);
Check(!tabScene.Embed.Description.Contains("Compraste"), "la escena de la taberna ya no guarda la respuesta dentro de su texto");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

public class FakeProxy : DispatchProxy
{
    private Func<string, object?[], object?> _h = null!;
    public static object Create(Type iface, Func<string, object?[], object?> h)
    {
        var create = typeof(DispatchProxy).GetMethod("Create", 2, [])!.MakeGenericMethod(iface, typeof(FakeProxy));
        var proxy = create.Invoke(null, null)!;
        ((FakeProxy)proxy)._h = h;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? a) => _h(m!.Name, a ?? []);
}
