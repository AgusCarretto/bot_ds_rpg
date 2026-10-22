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

// v0.23.0 (el dueño, 2026-10-22): títulos. «Creá muchísimos, 50 mínimo, en páginas en aa titles, y que se equipen al perfil: nombre - título (clase).» «Título nuevo me gusta, que busque cuál es.»
// 113 títulos en 9 páginas; lo único que se guarda es cuál tenés puesto (users.equipped_title); qué tenés se calcula de los contadores y eventos que el juego ya lleva.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
IEnumerable<IMessageComponent> Flat(MessageComponent? c) => c?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components) ?? [];
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

TitleContext Ctx(Dictionary<string, long>? stats = null, int level = 1, int[]? bosses = null, string[]? flags = null, int color = 0) =>
    new(stats ?? new Dictionary<string, long>(), level, new HashSet<int>(bosses ?? []), new HashSet<string>(flags ?? []), color);
TitleDefinition T(string key) => TitleCatalog.Find(key) ?? throw new InvalidOperationException($"no existe el título {key}");

Console.WriteLine("== 1. El catálogo ==");
var all = TitleCatalog.All;
Check(all.Count == 113 && all.Count >= 50, $"hay {all.Count} títulos (el pedido era 50 como mínimo)");
Check(all.Select(t => t.Key).Distinct().Count() == all.Count && all.Select(t => t.Name.ToLowerInvariant()).Distinct().Count() == all.Count, "las claves y los nombres no se repiten");
Check(TitleCatalog.Pages.Count == 9 && TitleCatalog.Pages.Select(p => p.Key).Distinct().Count() == 9 && all.All(t => TitleCatalog.Pages.Any(p => p.Key == t.PageKey)), "9 páginas y cada título está en una que existe");
var sizes = TitleCatalog.Pages.ToDictionary(p => p.Key, p => TitleCatalog.OfPage(p.Key).Count);
Check(sizes.Values.All(n => n is >= 1 and <= 25) && sizes.Values.Sum() == all.Count, $"cada página tiene entre 1 y 25 títulos (el desplegable de equipar admite 25): {string.Join(", ", sizes.Select(kv => $"{kv.Key} {kv.Value}"))}");
Check(all.All(t => t.Name.Length is >= 3 and <= 40 && t.Hint.Length > 3 && t.Hint.Length <= 100), "nombres cortos y una pista de cómo conseguirlo en todos (entran en una opción de desplegable)");
var fromAch = all.Where(t => t.FromAchievement).ToList();
Check(fromAch.Count == 69 && AchievementCatalog.All.All(a => a.Tiers.Select((tier, i) => fromAch.Any(t => t.AchievementKey == a.Key && t.AchievementTier == i + 1 && t.Threshold == tier.Threshold && t.Source == a.StatKind)).All(x => x)),
    "69 títulos vienen de los logros: los 23 logros por sus 3 tramos, con la MISMA meta y el mismo contador que el tramo (una sola fuente de verdad)");
Check(all.Count(t => t.Rule == TitleRule.BossRank) == 5 && all.Count(t => t.Rule == TitleRule.Level) == 4 && all.Count(t => t.Rule == TitleRule.BlessingColor) == 5 && all.Count(t => t.Rule == TitleRule.Flag) == 3 && all.Count(t => t.Rule == TitleRule.Always) == 1,
    "especiales: 5 jefes de la escalera, 4 niveles, 5 de la Brasa de Color, 3 hallazgos y el título inicial");
Check(Enumerable.Range(1, 5).All(l => T($"color_{l}").Name == BlessingCatalog.ColorTitle(l)), "los 5 títulos de la Brasa de Color se llaman igual que los textos de antes (no se pierde ninguno)");
Check(TitleCatalog.Default.Key == TitleCatalog.DefaultKey && TitleCatalog.Default.Name == "Recién Llegado" && TitleCatalog.IsUnlocked(TitleCatalog.Default, TitleContext.Empty), "«Recién Llegado» es el título inicial y siempre está desbloqueado");
Check(TitleCatalog.UnlockedCount(TitleContext.Empty) == 1, "un jugador sin nada tiene exactamente 1 título (el inicial)");
Check(TitleCatalog.DisplayName(null) == "Recién Llegado" && TitleCatalog.DisplayName("cazador_1") == "Rastreador de Pradera" && TitleCatalog.DisplayName("ya_no_existe") == "Recién Llegado", "el nombre del perfil: el puesto, o el inicial si no hay o si el título se retiró");

Console.WriteLine("\n== 2. Cuándo se desbloquea cada uno ==");
Check(!TitleCatalog.IsUnlocked(T("cazador_1"), Ctx(new() { ["hunt_win"] = 24 })) && TitleCatalog.IsUnlocked(T("cazador_1"), Ctx(new() { ["hunt_win"] = 25 })), "una meta de contador: 24 cacerías no, 25 sí (la meta del logro Cazador I)");
var hunts149 = Ctx(new() { ["hunt_win"] = 149 });
Check(TitleCatalog.IsUnlocked(T("cazador_1"), hunts149) && !TitleCatalog.IsUnlocked(T("cazador_2"), hunts149) && TitleCatalog.IsUnlocked(T("cazador_2"), Ctx(new() { ["hunt_win"] = 150 })), "los tramos se desbloquean de a uno: 149 tiene I y no II, 150 tiene II");
Check(TitleCatalog.IsUnlocked(T("nivel_10"), Ctx(level: 25)) && TitleCatalog.IsUnlocked(T("nivel_25"), Ctx(level: 25)) && !TitleCatalog.IsUnlocked(T("nivel_40"), Ctx(level: 25)), "el nivel máximo alcanzado abre los títulos de nivel hasta ese número");
Check(TitleCatalog.IsUnlocked(T("jefe_1"), Ctx(bosses: [1, 3])) && TitleCatalog.IsUnlocked(T("jefe_3"), Ctx(bosses: [1, 3])) && !TitleCatalog.IsUnlocked(T("jefe_2"), Ctx(bosses: [1, 3])), "el título de un jefe de la escalera se abre con la victoria sobre ESE jefe");
Check(TitleCatalog.IsUnlocked(T("hallazgo_corteza"), Ctx(flags: [TitleCatalog.FlagCorteza])) && !TitleCatalog.IsUnlocked(T("hallazgo_meteorito"), Ctx(flags: [TitleCatalog.FlagCorteza])) && TitleCatalog.IsUnlocked(T("encanto_v"), Ctx(flags: [TitleCatalog.FlagEnchantV])), "los hallazgos únicos (Corteza, Meteorito, tier V) se abren de a uno");
Check(new[] { 1, 2, 3 }.All(l => TitleCatalog.IsUnlocked(T($"color_{l}"), Ctx(color: 3))) && !TitleCatalog.IsUnlocked(T("color_4"), Ctx(color: 3)) && !TitleCatalog.IsUnlocked(T("color_1"), Ctx()), "la Brasa de Color nivel 3 abre sus títulos I a III y no el IV");
long woodcutter50 = ProfessionRules.XpToReach(50);
var woodCtx = Ctx(new() { [GameEventKinds.ChopXp] = woodcutter50 });
Check(TitleCatalog.IsUnlocked(T("woodcutter_25"), woodCtx) && TitleCatalog.IsUnlocked(T("woodcutter_50"), woodCtx) && !TitleCatalog.IsUnlocked(T("woodcutter_100"), woodCtx) && !TitleCatalog.IsUnlocked(T("woodcutter_50"), Ctx(new() { [GameEventKinds.ChopXp] = woodcutter50 - 1 })),
    $"el oficio de Leñador a nivel 50 (XP {woodcutter50}) abre «Hachero» y «Leñador Mayor»; con 1 de XP menos no abre el segundo");
long enchantNeeded = T("enchanter_25").Threshold;
Check(ProfessionRules.LevelFor(enchantNeeded * 25) >= 25 && ProfessionRules.LevelFor((enchantNeeded - 1) * 25) < 25, $"el Encantador nivel 25 son {enchantNeeded} intentos (25 de XP cada uno): con uno menos no llega");
Check(all.Where(t => t.Rule == TitleRule.Stat).All(t => t.Threshold >= 1 && t.Source.Length > 0), "toda meta de contador es de 1 para arriba y nombra su contador");

Console.WriteLine("\n== 3. Cuánto llevás ==");
Check(TitleCatalog.Progress(T("cazador_2"), Ctx(new() { ["hunt_win"] = 87 })) == "vas 87", "un contador a medias: «vas 87»");
Check(TitleCatalog.Progress(T("cazador_2"), Ctx(new() { ["hunt_win"] = 400 })) == "vas 149", "nunca muestra de más que la meta (si ya la pasó está desbloqueado)");
Check(TitleCatalog.Progress(T("woodcutter_100"), woodCtx) == "vas en el nivel 50", "un oficio muestra su NIVEL, no la XP");
Check(TitleCatalog.Progress(T("nivel_40"), Ctx(level: 12)) == "tu máximo: nivel 12" && TitleCatalog.Progress(T("jefe_2"), Ctx()) is null && TitleCatalog.Progress(T("encanto_v"), Ctx()) is null, "el nivel muestra tu máximo y los jefes y hallazgos no llevan número");

Console.WriteLine("\n== 4. Los avisos de «¡Título nuevo!» ==");
var tier1 = AchievementCatalog.Crossed("hunt_win", 24, 25).Single();
Check(TitleCatalog.ForAchievementTier(tier1.Achievement.Key, tier1.Tier)?.Name == "Rastreador de Pradera" && TitleCatalog.ForAchievementTier("cazador", 9) is null, "cada tramo de un logro trae su título (y un tramo que no existe, ninguno)");
Check(TitleCatalog.SpecialCrossed("duel_win", 1, 1, null).Select(t => t.Name).SequenceEqual(["Peleador de Barrio"]) && TitleCatalog.SpecialCrossed("duel_win", 1, 2, null).Count == 0, "el primer duelo ganado abre «Peleador de Barrio» y el segundo no avisa nada");
Check(TitleCatalog.SpecialCrossed("duel_win", 5, 12, null).Select(t => t.Name).SequenceEqual(["Duelista"]), "un salto de 7 a 12 duelos cruza solo la meta de 10");
Check(TitleCatalog.SpecialCrossed("level_up", 3, 26, "26").Select(t => t.Name).SequenceEqual(["Parrillero Hecho y Derecho"]) && TitleCatalog.SpecialCrossed("level_up", 1, 10, "10").Select(t => t.Name).SequenceEqual(["Fogonero"]) && TitleCatalog.SpecialCrossed("level_up", 1, 11, "11").Count == 0,
    "subir de nivel avisa solo al cruzar una meta (de 23 a 26 cruza la del 25; de 10 a 11 nada)");
Check(TitleCatalog.SpecialCrossed("story_chapter", 1, 1, "cap3").Select(t => t.Name).SequenceEqual(["Quebrador de Capataces"]) && TitleCatalog.SpecialCrossed("story_chapter", 1, 1, "cap6").Count == 0 && TitleCatalog.SpecialCrossed("story_chapter", 1, 1, "fn1").Count == 0 && TitleCatalog.SpecialCrossed("story_chapter", 1, 1, null).Count == 0,
    "la primera victoria sobre el jefe de la Zona 3 (capítulo cap3) avisa «Quebrador de Capataces»; el Asador y los capítulos que no son de jefe no");
Check(TitleCatalog.SpecialCrossed("hunt_win", 25, 25, null).Count == 0, "los contadores de los logros no avisan dos veces: el aviso del logro ya dice el título");
var chop25 = T("woodcutter_25").Threshold;
Check(TitleCatalog.SpecialCrossed(GameEventKinds.ChopXp, 5, chop25 + 1, null).Select(t => t.Key).SequenceEqual(["woodcutter_25"]), "la XP del Leñador cruzando el nivel 25 avisa «Hachero»");
Check(TitleCatalog.SpecialCrossed("vote", 0, 1, null).Count == 0 && TitleCatalog.SpecialCrossed("", 1, 1, null).Count == 0, "una cantidad de 0 o un evento cualquiera no avisan nada y no tiran");

Console.WriteLine("\n== 5. Buscar por nombre y por página ==");
Check(TitleCatalog.ParsePage("jefes") == 5 && TitleCatalog.ParsePage("6") == 5 && TitleCatalog.ParsePage("PvP") == 7 && TitleCatalog.ParsePage("fogon") == 4 && TitleCatalog.ParsePage("Maestros") == 6 && TitleCatalog.ParsePage("mascotas") == 4, "la página se pide por número, por su clave o por el principio del nombre (sin mirar mayúsculas)");
Check(TitleCatalog.ParsePage("99") is null && TitleCatalog.ParsePage("0") is null && TitleCatalog.ParsePage("") is null && TitleCatalog.ParsePage(null) is null && TitleCatalog.ParsePage("xyz") is null, "una página que no existe, vacía o nula devuelve null");
Check(TitleCatalog.FindByName("MATAJEFES").Select(t => t.Key).SequenceEqual(["matajefes_2"]) && TitleCatalog.FindByName("terror de").Select(t => t.Key).SequenceEqual(["matajefes_3"]) && TitleCatalog.FindByName("cabeza de hierro").Count == 1, "el título se busca sin mirar mayúsculas y alcanza con parte del nombre si es una sola coincidencia");
Check(TitleCatalog.FindByName("domador").Select(t => t.Key).SequenceEqual(["domador_2"]) && TitleCatalog.FindByName("DOMADOR DE").Count == 1 && TitleCatalog.FindByName("fuego").Count > 1 && TitleCatalog.FindByName("inexistente").Count == 0 && TitleCatalog.FindByName("   ").Count == 0,
    "si el nombre es exacto gana aunque otros lo contengan; varias coincidencias se devuelven todas; ninguna o vacío, nada");

Console.WriteLine("\n== 6. La base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var buffRepo = new BuffRepository(factory);
var titles = new TitleRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000096001UL, GHOST = 999000000000096999UL;
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids); DELETE FROM game_events WHERE discord_id = ANY(@ids); DELETE FROM player_stats WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)U, (long)GHOST } });
await users.GetOrCreateUserAsync(U);
await db.ExecuteAsync("UPDATE users SET class = 'Guerrero', level = 3 WHERE discord_id = @id", new { id = (long)U });
var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());

var empty = await titles.GetContextAsync(U, zones);
Check(empty.Stats.Count == 0 && empty.MaxLevel == 3 && empty.BossRanksCleared.Count == 0 && empty.Flags.Count == 0 && empty.ColorBlessingLevel == 0 && TitleCatalog.UnlockedCount(empty) == 1, "un jugador nuevo: sin contadores, su nivel de cuenta como máximo y solo el título inicial");

async Task Stat(string key, long value) => await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, @k, @v) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = @v", new { id = (long)U, k = key, v = value });
async Task Event(string kind, int? zone, string? detail) => await db.ExecuteAsync("INSERT INTO game_events (discord_id, kind, zone_id, amount, detail) VALUES (@id, @k, @z, 1, @d)", new { id = (long)U, k = kind, z = zone, d = detail });
await Stat("hunt_win", 160); await Stat("duel_win", 12); await Stat("fight_lost", 3);
await Event("level_up", null, "12"); await Event("level_up", null, "30"); await Event("level_up", null, "basura");
await Event("boss_win", zones[1].ZoneId, null); await Event("raid_win", zones[3].ZoneId, null); await Event("boss_win", 0, null); await Event("boss_win", 987654, null);
await Event("chop", null, "Madera de Pino, Corteza del Árbol de Vida"); await Event("mine", null, "Hierro"); await Event("enchant", null, "weapon:4"); await Event("enchant", null, "amulet:5");
await db.ExecuteAsync("INSERT INTO player_blessings (discord_id, blessing_key, level) VALUES (@id, @k, 3) ON CONFLICT (discord_id, blessing_key) DO UPDATE SET level = 3", new { id = (long)U, k = BlessingCatalog.ColorKey });

var ctx = await titles.GetContextAsync(U, zones);
Check(ctx.Stats["hunt_win"] == 160 && ctx.Stats["duel_win"] == 12, "los contadores salen de player_stats");
Check(ctx.MaxLevel == 30, "el nivel máximo es el más alto de los eventos «level_up» (30), aunque la cuenta esté en el nivel 3 y haya un evento con basura en el detalle");
Check(ctx.BossRanksCleared.SetEquals([2, 4]), $"los jefes vencidos salen de los eventos de victoria por la zona en que estaba (rango por dificultad): el jefe 2 y el 4 ({string.Join(",", ctx.BossRanksCleared)}); una zona 0 o que no existe no cuenta");
Check(ctx.Flags.SetEquals([TitleCatalog.FlagCorteza, TitleCatalog.FlagEnchantV]), $"hallazgos: la Corteza y el tier V sí; el Meteorito no, y un encantamiento tier IV no abre el tier V ({string.Join(",", ctx.Flags)})");
Check(ctx.ColorBlessingLevel == 3, "el nivel de la bendición Brasa de Color");
var owned = all.Where(t => TitleCatalog.IsUnlocked(t, ctx)).Select(t => t.Key).ToHashSet();
Check(owned.SetEquals(["cazador_1", "cazador_2", "duelo_1", "duelo_10", "nivel_10", "nivel_25", "jefe_2", "jefe_4", "hallazgo_corteza", "encanto_v", "color_1", "color_2", "color_3", TitleCatalog.DefaultKey]),
    $"con todo eso, tiene exactamente los 14 que corresponden y ninguno de más ({owned.Count}: {string.Join(", ", owned.OrderBy(k => k))})");

var me = await users.GetByDiscordIdAsync(U);
Check(me!.EquippedTitle is null, "sin título puesto la columna es NULL");
Check(await titles.SetEquippedAsync(U, "cazador_2") && (await users.GetByDiscordIdAsync(U))!.EquippedTitle == "cazador_2", "ponerse un título lo guarda y se lee de vuelta con el resto del jugador");
Check(await titles.SetEquippedAsync(U, null) && (await users.GetByDiscordIdAsync(U))!.EquippedTitle is null && !await titles.SetEquippedAsync(GHOST, "cazador_1"), "sacarlo vuelve a NULL; sin cuenta no hace nada y lo dice");

var okEquip = await TitlesModule.ExecuteEquipAsync(users, zonesRepo, titles, U, "jefe_2");
Check(okEquip.Ok && okEquip.Message.Contains("Domador de Lobisones") && (await users.GetByDiscordIdAsync(U))!.EquippedTitle == "jefe_2", $"ponerse uno que tiene: «{okEquip.Message}»");
var lockedEquip = await TitlesModule.ExecuteEquipAsync(users, zonesRepo, titles, U, "nivel_60");
Check(!lockedEquip.Ok && lockedEquip.Message.Contains("Maestro Parrillero") && lockedEquip.Message.Contains("Llegá al nivel 60") && (await users.GetByDiscordIdAsync(U))!.EquippedTitle == "jefe_2", $"uno que no tiene: se rechaza diciendo cómo conseguirlo y no cambia el puesto: «{lockedEquip.Message}»");
var unknownEquip = await TitlesModule.ExecuteEquipAsync(users, zonesRepo, titles, U, "inventado");
var ghostEquip = await TitlesModule.ExecuteEquipAsync(users, zonesRepo, titles, GHOST, "cazador_1");
Check(!unknownEquip.Ok && !ghostEquip.Ok && ghostEquip.Message.Contains("/start"), "un título que no existe se rechaza; sin cuenta pide /start");
var cleared = await TitlesModule.ExecuteClearAsync(users, titles, U);
Check(cleared.Ok && cleared.Message.Contains("Recién Llegado") && (await users.GetByDiscordIdAsync(U))!.EquippedTitle is null && !(await TitlesModule.ExecuteClearAsync(users, titles, GHOST)).Ok, "quitarse el título vuelve al inicial; sin cuenta no hace nada");

Console.WriteLine("\n== 7. La pantalla de /titles ==");
var noAccount = await TitlesModule.BuildViewAsync(users, zonesRepo, titles, GHOST);
Check(noAccount.Embed is null && noAccount.Components is null && noAccount.PlainMessage!.Contains("/start"), "sin cuenta: solo el mensaje que pide /start");
await titles.SetEquippedAsync(U, "cazador_1");
var view = await TitlesModule.BuildViewAsync(users, zonesRepo, titles, U, "combate");
var desc = view.Embed!.Description;
Check(view.Embed.Title == "🏷️ Títulos · ⚔️ Combate" && desc.Contains("Puesto: **Rastreador de Pradera**") && desc.Contains("✅ **Rastreador de Pradera** ◀ puesto") && desc.Contains("✅ **Cazador Curtido**") && !desc.Contains("◀ puesto\n✅ **Cazador Curtido** ◀"),
    "la página: lo puesto arriba, ✅ los que tiene (con ◀ en el que lleva puesto)");
Check(desc.Contains("🔒 Azote de las Bestias — Cacerías ganadas: 600 _(vas 160)_") || desc.Contains("🔒 Azote de las Bestias — Cacerías ganadas: 600 _(vas 160"), "los que faltan llevan 🔒, cómo conseguirlos y cuánto llevás");
Check(view.Embed.Footer!.Value.Text.Contains($"de {all.Count}") && view.Embed.Length < 6000 && desc.Length < 4096, "el pie cuenta cuántos tiene de los 113 y entra en los límites de Discord");
var rows = view.Components!.Components.OfType<ActionRowComponent>().ToList();
var pageMenu = Flat(view.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId == $"titles_page:{U}");
var equipMenu = Flat(view.Components).OfType<SelectMenuComponent>().Single(m => m.CustomId == $"titles_equip:{U}:combate");
var clearButton = Flat(view.Components).OfType<ButtonComponent>().Single();
Check(rows.Count == 3 && pageMenu.Options.Count == 9 && pageMenu.Options.Single(o => o.IsDefault == true).Value == "combate" && pageMenu.Options.All(o => o.Emote is null && o.Label.Length <= 100),
    "tres filas: el desplegable de las 9 páginas (marcada la actual), el de equipar y el botón");
Check(equipMenu.Options.Select(o => o.Value).OrderBy(v => v).SequenceEqual(new[] { "cazador_1", "cazador_2" }.OrderBy(v => v)) && equipMenu.Options.Single(o => o.Value == "cazador_1").Description == "Puesto ahora" && equipMenu.Options.Count <= 25,
    "el desplegable de equipar lista SOLO los que tiene de esa página");
Check(clearButton.CustomId == $"titles_clear:{U}:combate" && !clearButton.IsDisabled, "el botón de quitar está activo con un título puesto y su id lleva el dueño y la página");
var withNote = await TitlesModule.BuildViewAsync(users, zonesRepo, titles, U, "maestros", "🏷️ Ahora sos algo");
var pvpRows = withNote.Components!.Components.OfType<ActionRowComponent>().ToList();
Check(withNote.Embed!.Description.StartsWith("🏷️ Ahora sos algo") && pvpRows.Count == 2 && Flat(withNote.Components).OfType<SelectMenuComponent>().All(m => !m.CustomId.StartsWith("titles_equip")) , "una página donde no tiene ninguno no ofrece el desplegable de equipar, y la nota va arriba");
await titles.SetEquippedAsync(U, null);
var noTitleView = await TitlesModule.BuildViewAsync(users, zonesRepo, titles, U, "combate");
Check(Flat(noTitleView.Components).OfType<ButtonComponent>().Single().IsDisabled && noTitleView.Embed!.Description.Contains("Puesto: _ninguno_"), "sin título puesto el botón de quitar está deshabilitado y la pantalla lo dice");
var weirdPage = await TitlesModule.BuildViewAsync(users, zonesRepo, titles, U, "no_existe");
Check(weirdPage.Embed!.Title!.Contains("Combate"), "una página que no existe cae a la primera");
var everything = new TitleContext(all.Where(t => t.Rule == TitleRule.Stat).Select(t => t.Source).Distinct().ToDictionary(s => s, s => 1_000_000_000L, StringComparer.Ordinal), 100, new HashSet<int> { 1, 2, 3, 4, 5 }, new HashSet<string> { "corteza", "meteorito", "encanto_v" }, 5);
Check(TitleCatalog.UnlockedCount(everything) == all.Count && TitleCatalog.Pages.All(p => { var e = TitlesModule.BuildEmbed(p, everything, "cazador_1"); return e.Description.Length < 4096 && e.Length < 6000 && TitlesModule.BuildComponents(U, p, everything, "cazador_1").Components.Count <= 5; }),
    "con los 113 desbloqueados todas las páginas siguen entrando en el embed y en 5 filas");
var nothing = TitlesModule.BuildEmbed(TitleCatalog.PageOf("combate"), TitleContext.Empty, null);
Check(nothing.Description.Split("🔒").Length - 1 == 18 && !nothing.Description.Contains("✅"), "sin nada, los 18 de Combate se ven bloqueados");

Console.WriteLine("\n== 8. El perfil: Nombre - Título (Clase) ==");
async Task<Embed> Profile() => await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, U, "Agus", "https://example.com/a.png");
var p0 = await Profile();
Check(p0.Title == "⚔️ Agus - Recién Llegado (Guerrero)" && p0.Author is null, $"sin título puesto: «{p0.Title}», y ya no hay línea de autor (el nombre está en el título)");
await titles.SetEquippedAsync(U, "jefe_2");
var p1 = await Profile();
Check(p1.Title == "⚔️ Agus - Domador de Lobisones (Guerrero)" && !p1.Description.Contains("🌈"), $"con un título puesto: «{p1.Title}»");
await db.ExecuteAsync("UPDATE users SET equipped_title = 'titulo_retirado' WHERE discord_id = @id", new { id = (long)U });
Check((await Profile()).Title == "⚔️ Agus - Recién Llegado (Guerrero)", "un título que ya no existe en el catálogo cae a «Recién Llegado» en vez de romper el perfil");

Console.WriteLine("\n== 9. El aviso, con el notificador real ==");
var missions = new MissionRepository(factory);
var notifier = new ProgressNotifier(missions, zonesRepo);
var now = DateTime.UtcNow;
var hunt25 = await notifier.OnEventAsync(U, "hunt_win", 1, 25, now);
var huntText = string.Join("\n", hunt25.Select(n => n.Text));
Check(huntText.Contains("¡Logro desbloqueado!") && huntText.Contains("¡Título nuevo!") && huntText.Contains("Rastreador de Pradera") && huntText.Contains("/titles"), $"el aviso del logro dice cuál es el título que trae: «{huntText.Replace("\n", " | ")}»");
var duel1 = await notifier.OnEventAsync(U, "duel_win", 1, 1, now, "Rival");
Check(duel1.Count == 1 && duel1[0].Text.Contains("¡Título nuevo!") && duel1[0].Text.Contains("Peleador de Barrio") && duel1[0].Public && duel1[0].ExpiresUtc is not null, "un título especial avisa solo, público y con vencimiento");
Check((await notifier.OnEventAsync(U, "duel_win", 1, 2, now, "Rival")).Count == 0 && (await notifier.OnEventAsync(U, "hunt_win", 1, 26, now)).Count == 0, "sin cruzar una meta no avisa nada");
var chapter = await notifier.OnEventAsync(U, GameEventKinds.StoryChapter, 1, 1, now, "cap2");
Check(chapter.Any(n => n.Text.Contains("Domador de Lobisones")), "la primera victoria sobre el jefe de la Zona 2 avisa su título junto al capítulo");

Console.WriteLine("\n== 10. Comandos y DI con el InteractionService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var cmd = interactions.SlashCommands.SingleOrDefault(c => c.Name == "titles");
Check(cmd is not null && cmd.Module.SlashGroupName is null && cmd.Description.Length is >= 1 and <= 100 && cmd.Parameters.Count == 0 && interactions.SlashCommands.Count < 100, $"/titles existe, sin grupo ni parámetros, dentro de los 100 caracteres ({interactions.SlashCommands.Count} comandos de barra en total, el tope de Discord es 100)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routedPage = interactions.SearchComponentCommand(Click($"titles_page:{U}"));
var routedEquip = interactions.SearchComponentCommand(Click($"titles_equip:{U}:combate"));
var routedClear = interactions.SearchComponentCommand(Click($"titles_clear:{U}:jefes"));
Check(routedPage.IsSuccess && routedPage.Command.MethodName == "HandlePageAsync" && routedPage.RegexCaptureGroups is { Length: 1 } g1 && g1[0] == U.ToString(), "el desplegable de páginas llega a su método con el dueño");
Check(routedEquip.IsSuccess && routedEquip.Command.MethodName == "HandleEquipAsync" && routedEquip.RegexCaptureGroups is { Length: 2 } g2 && g2[0] == U.ToString() && g2[1] == "combate", "el de equipar llega con el dueño y la página");
Check(routedClear.IsSuccess && routedClear.Command.MethodName == "HandleClearAsync" && routedClear.RegexCaptureGroups is { Length: 2 } g3 && g3[1] == "jefes", "el botón de quitar llega con el dueño y la página");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "titles", "titulos", "títulos", "title", "titulo", "título" }.All(a => textNames.Count(n => n == a) == 1), "aa titles / titulos y aa title / titulo están registrados, sin repetirse con otro comando");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(x))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split('\n')[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = ANY(@ids); DELETE FROM game_events WHERE discord_id = ANY(@ids); DELETE FROM player_stats WHERE discord_id = ANY(@ids)", new { ids = new[] { (long)U, (long)GHOST } });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
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
