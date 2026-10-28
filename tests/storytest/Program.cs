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

// v0.15.0: la historia (Las Crónicas del Fogón) + el ajuste de vida por clase solo contra monstruos (PveTuning). Reglas puras (pesar, platos, desbloqueos), pantalla /story, avisos
// (ProgressNotifier), el capítulo que se abre en la primera victoria sobre un jefe (solo y raid, contra la base real con un usuario descartable), las frases del Asador por vuelta,
// los logros nuevos, el ajuste PvE sin tocar el PvP y los comandos con el InteractionService real.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var monsters = new MonsterRepository(factory);
var adventure = new AdventureRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var zoneBoxes = new ZoneBoxService(factory, zonesRepo);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000017000UL;
var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());

async Task Reset(int fuegoNuevo = 0, int highestCleared = 0, bool gateCleared = false, string cls = "Guerrero")
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U, cls);
    await db.ExecuteAsync(
        "UPDATE users SET level = 30, xp = 0, max_hp = 500, current_hp = 500, fuego_nuevo = @fn, highest_zone_cleared = @hz, gate_cleared = @gc WHERE discord_id = @id",
        new { id = (long)U, fn = fuegoNuevo, hz = highestCleared, gc = gateCleared });
}

Console.WriteLine("== 1. Reglas puras: el pesar y la mesa ==");
Check(Lore.Pesar(0) == 100 && Lore.Pesar(1) == 99 && Lore.Pesar(25) == 75 && Lore.Pesar(99) == 1 && Lore.Pesar(100) == 0 && Lore.Pesar(150) == 0 && Lore.Pesar(-3) == 100, "el pesar baja 1 % por Fuego Nuevo y se acota a 0..100");
Check(Lore.Plates(0) == 40 && Lore.Plates(1) == 40 && Lore.Plates(2) == 39 && Lore.Plates(25) == 30 && Lore.Plates(50) == 20 && Lore.Plates(75) == 10 && Lore.Plates(99) == 0 && Lore.Plates(100) == 0 && Lore.Plates(500) == 0,
    "los platos de la mesa: 40, 30 en la vuelta 25, 20 en la 50, 10 en la 75, ninguno en la 99");
bool mono = true; int prev = 100; for (int f = 0; f <= 120; f++) { int p = Lore.Plates(f); if (p > prev) mono = false; prev = p; }
Check(mono, "los platos nunca suben al sumar vueltas");

Console.WriteLine("\n== 2. Los textos ==");
var allEntries = Lore.Chapters.Select(c => (Key: c.Key, Text: c.Text)).Concat(Lore.Scenes.Select(s => (Key: s.Key, Text: s.Text))).Append((Key: Lore.PrologueKey, Text: Lore.Prologue)).ToList();
Check(allEntries.Select(e => e.Key).Distinct().Count() == allEntries.Count && allEntries.Count == 1 + 6 + 14, $"las claves no se repiten ({allEntries.Count} entradas: prólogo + 6 capítulos + 14 escenas)");
Check(allEntries.All(e => e.Text.Length is > 80 and <= 2000), $"cada texto cabe holgado en un mensaje (máximo {allEntries.Max(e => e.Text.Length)} caracteres)");
Check(Lore.Chapters.Select(c => c.Number).SequenceEqual(Enumerable.Range(1, 6)) && Lore.Chapters.All(c => c.Title.Length is > 3 and <= 60 && c.FirstClearLine.Length > 20), "capítulos 1 a 6, con título y con su escena de la primera victoria");
Check(Lore.Scenes.Zip(Lore.Scenes.Skip(1), (a, b) => a.FuegoNuevo <= b.FuegoNuevo).All(x => x) && Lore.Scenes[^1].FuegoNuevo == Lore.FinalFuegoNuevo && Lore.Scenes.Count(s => s.FuegoNuevo == Lore.FinalFuegoNuevo) == 1,
    "las escenas van ordenadas por vuelta y la única del Fuego Nuevo 100 es la última");
// Los platos que nombran las escenas coinciden con la cuenta real (si se retocan los platos por vuelta, la prosa no puede quedar mentirosa).
var plateWords = new (string Key, string Word)[] { ("fn5", "Treinta y ocho"), ("fn25", "Treinta"), ("fn35", "Veintiséis"), ("fn50", "Veinte"), ("fn75", "Diez"), ("fn90", "Cuatro"), ("fn99", "Ya no queda ninguno") };
int[] expectedPlates = [38, 30, 26, 20, 10, 4, 0];
for (int i = 0; i < plateWords.Length; i++)
{
    var scene = Lore.FindScene(plateWords[i].Key)!;
    Check(scene.Text.Contains(plateWords[i].Word) && Lore.Plates(scene.FuegoNuevo) == expectedPlates[i], $"«{scene.Title}» dice «{plateWords[i].Word}» y en la vuelta {scene.FuegoNuevo} hay {Lore.Plates(scene.FuegoNuevo)} platos");
}

Check(Lore.ChapterForBoss("Rey Jabalí")?.Key == "cap1" && Lore.ChapterForBoss("Soberano de la Escoria")?.Key == "cap5" && Lore.ChapterForBoss(FogonRules.BossName)?.Key == "cap6" && Lore.ChapterForBoss("Gólem del Yunque") is null && Lore.ChapterForBoss(null) is null,
    "cada jefe abre su capítulo (y un monstruo común, ninguno)");
var bossNames = new[] { "Rey Jabalí", "Lobisón Alfa", "Capataz de Hierro", "Señor del Volcán", "Soberano de la Escoria" };
var dbBosses = new List<string>();
foreach (var z in zones) dbBosses.Add((await monsters.GetBossByZoneAsync(z.ZoneId))?.Name ?? "(sin jefe)");
Check(dbBosses.SequenceEqual(bossNames) && Lore.Chapters.Take(5).Select(c => c.BossName).SequenceEqual(dbBosses), $"los jefes de la base, en el orden de la escalera, son los de los capítulos 1 a 5 ({string.Join(", ", dbBosses)})");
Check(NpcDialogue.KnownBosses.Contains(FogonRules.BossName) && (await monsters.GetBossByZoneAsync(0))?.Name == FogonRules.BossName, "el Asador sigue siendo el jefe de la zona 0 con el nombre de siempre");

Console.WriteLine("\n== 3. Qué se abre con qué ==");
Check(Lore.Chapters.All(c => !Lore.IsUnlocked(c, new StoryProgress(0, false, 0))) && Lore.Scenes.All(s => !Lore.IsUnlocked(s, new StoryProgress(0, false, 0))), "un jugador nuevo no tiene nada abierto (solo el prólogo, que no se sella)");
Check(Lore.Chapters.Where(c => Lore.IsUnlocked(c, new StoryProgress(2, false, 0))).Select(c => c.Number).SequenceEqual([1, 2]), "con 2 jefes vencidos están abiertos el capítulo 1 y el 2");
Check(Lore.Chapters.Where(c => Lore.IsUnlocked(c, new StoryProgress(5, false, 0))).Select(c => c.Number).SequenceEqual([1, 2, 3, 4, 5]) && Lore.IsUnlocked(Lore.Chapters[5], new StoryProgress(5, true, 0)), "con los 5 jefes falta el del Asador, que se abre al vencerlo");
Check(Lore.Chapters.All(c => Lore.IsUnlocked(c, new StoryProgress(0, false, 1))), "con un Fuego Nuevo hecho todo el Acto I está abierto (aunque la zona más alta vuelva a 0)");
Check(Lore.Scenes.Where(s => Lore.IsUnlocked(s, new StoryProgress(0, false, 3))).Select(s => s.Key).SequenceEqual(["fn1", "fn1b", "fn2", "fn3"]) && Lore.Scenes.Count(s => Lore.IsUnlocked(s, new StoryProgress(0, false, 100))) == Lore.Scenes.Count,
    "las escenas se abren al llegar a su vuelta (3 → las de la 1, la 2 y la 3; 100 → todas)");
Check(Lore.ScenesOpenedAt(1).Count == 2 && Lore.ScenesOpenedAt(4).Count == 0 && Lore.ScenesOpenedAt(10).Single().Key == "fn10" && Lore.NextSceneFuegoNuevo(0) == 1 && Lore.NextSceneFuegoNuevo(3) == 5 && Lore.NextSceneFuegoNuevo(100) is null, "qué se abre justo en cada vuelta y cuál es la próxima");

// El progreso sale del jugador real: el rango de highest_zone_cleared en la escalera (nunca el zone_id crudo).
var u1 = new User { HighestZoneCleared = zones[2].ZoneId, GateCleared = false, FuegoNuevo = 0 };
Check(StoryProgress.For(u1, zones) == new StoryProgress(3, false, 0) && StoryProgress.For(new User { HighestZoneCleared = 0, GateCleared = true, FuegoNuevo = 2 }, zones) == new StoryProgress(0, true, 2), "StoryProgress.For lee el rango de la zona vencida, el Asador y los Fuegos Nuevos");

Console.WriteLine("\n== 4. La pantalla /story ==");
var viewNew = StoryModule.Compose(new StoryProgress(0, false, 0), U, null);
string newText = AllText(viewNew.Embed);
Check(newText.Contains("sellado") && Lore.Chapters.All(c => !newText.Contains(c.Title)) && Lore.Scenes.All(s => !newText.Contains($"«{s.Title}»")) && newText.Contains(Lore.PrologueTitle) && newText.Contains("primer **Fuego Nuevo**"),
    "el índice de un jugador nuevo: prólogo sí, todo lo demás 🔒 sellado SIN título");
var optNew = viewNew.Components!.Components.OfType<ActionRowComponent>().Single().Components.OfType<SelectMenuComponent>().Single();
Check(optNew.Options.Count == 2 && optNew.Options.All(o => o.Label.Length <= 100 && o.Value.Length <= 100) && optNew.CustomId == $"story_pick:{U}", "la lista de un jugador nuevo tiene solo Índice y Prólogo, y su id lleva al dueño");
var viewMax = StoryModule.Compose(new StoryProgress(5, true, 100), U, null);
var optMax = viewMax.Components!.Components.OfType<ActionRowComponent>().Single().Components.OfType<SelectMenuComponent>().Single();
Check(optMax.Options.Count == 2 + 6 + 14 && optMax.Options.Count <= 25 && optMax.Options.Select(o => o.Value).Distinct().Count() == optMax.Options.Count && optMax.Options.All(o => o.Label.Length <= 100), $"con todo abierto la lista tiene {optMax.Options.Count} opciones (el tope de Discord es 25), sin repetidas");
Check(viewMax.Embed.Length <= 6000 && viewMax.Embed.Fields.All(f => f.Value.Length <= 1024) && AllText(viewMax.Embed).Contains("El que volvió") && AllText(viewMax.Embed).Contains("Pesar del Asador 0 %"), $"el índice completo entra en los límites ({viewMax.Embed.Length}) y cuenta el pesar en 0");
var midIndex = StoryModule.Compose(new StoryProgress(2, false, 4), U, null);
string midText = AllText(midIndex.Embed);
Check(midText.Contains("Pesar del Asador 96 %") && midText.Contains("«Los nombres»") && !midText.Contains("«El humo cambió»") && midText.Contains("Próxima escena: en el Fuego Nuevo **5**") && midText.Contains($"Se abre en el Fuego Nuevo **{Lore.FinalFuegoNuevo}**"),
    "con 4 vueltas: pesar 96 %, las escenas abiertas por nombre, la próxima solo por número y el final como sellado");
var read1 = StoryModule.Compose(new StoryProgress(1, false, 0), U, "cap1");
Check(read1.Embed.Title.Contains("Capítulo 1") && read1.Embed.Description == Lore.Chapters[0].Text && read1.Embed.Description.Length <= 4096, "elegir un capítulo abierto lo muestra completo");
var sealedTry = StoryModule.Compose(new StoryProgress(1, false, 0), U, "cap3");
Check(sealedTry.Embed.Title == "📖 Las Crónicas del Fogón" && !AllText(sealedTry.Embed).Contains(Lore.Chapters[2].Text[..40]), "armar a mano el id de un capítulo sellado no lo muestra: vuelve al índice");
Check(StoryModule.Compose(new StoryProgress(0, false, 1), U, "fn5").Embed.Title.Contains("Crónicas") && StoryModule.Compose(new StoryProgress(0, false, 5), U, "fn5").Embed.Title.Contains("humo cambió"), "lo mismo con las escenas: sellada vuelve al índice, abierta se lee");
var readFinal = StoryModule.Compose(new StoryProgress(5, true, 100), U, "fn100");
Check(readFinal.Embed.Footer!.Value.Text.Contains("Acto III") && readFinal.Embed.Description.Contains("Esto no termina acá"), "el final se lee en el Acto III y deja la puerta abierta");
await Reset();
var dbView = await StoryModule.BuildViewAsync(users, zonesRepo, U, null);
Check(dbView.Embed.Fields.Any(f => f.Name.Contains("Acto I")) && (await StoryModule.BuildViewAsync(users, zonesRepo, U + 1, null)).Embed.Description.Contains("/start"), "con la base real: el índice del jugador, y sin cuenta manda a /start");

Console.WriteLine("\n== 5. Avisos (ProgressNotifier) ==");
var notifier = new ProgressNotifier(new MissionRepository(factory), zonesRepo);
var now = DateTime.UtcNow;
var nChapter = await notifier.OnEventAsync(U, GameEventKinds.StoryChapter, 1, 1, now, "cap3");
Check(nChapter.Count >= 1 && nChapter.Any(n => n.Text.Contains("Capítulo desbloqueado") && n.Text.Contains("El pedido que no se cancela") && !n.Public && n.ExpiresUtc > now) && !nChapter.Any(n => n.Text.Contains("Capataz")),
    "un capítulo nuevo avisa su título (y no cuenta nada), en privado y con vencimiento");
Check((await notifier.OnEventAsync(U, GameEventKinds.StoryChapter, 1, 1, now, "no-existe")).All(n => !n.Text.Contains("Capítulo desbloqueado")), "una clave que no existe no avisa nada");
var nFn1 = await notifier.OnEventAsync(U, GameEventKinds.FuegoNuevo, 1, 1, now, "Ninja");
Check(nFn1.Any(n => n.Text.Contains("Escenas nuevas del fuego") && n.Text.Contains("«Otra vuelta»") && n.Text.Contains("«Alcanza»") && !n.Public), "el Fuego Nuevo 1 avisa sus DOS escenas");
Check((await notifier.OnEventAsync(U, GameEventKinds.FuegoNuevo, 1, 4, now, "Ninja")).All(n => !n.Text.Contains("Escena")), "en una vuelta sin escena no avisa nada de historia");
var nFn10 = await notifier.OnEventAsync(U, GameEventKinds.FuegoNuevo, 1, 10, now, "Ninja");
Check(nFn10.Any(n => n.Text.Contains("Escena nueva del fuego") && n.Text.Contains("«Te habla»")) && nFn10.Any(n => n.Text.Contains("Compañero del Asador I")), "en la vuelta 10 sale la escena y también el logro Compañero del Asador I");
Check((await notifier.OnEventAsync(U, GameEventKinds.PetHatched, 1, 1, now)).Any(n => n.Text == Lore.FirstPetLine) && (await notifier.OnEventAsync(U, GameEventKinds.PetHatched, 1, 2, now)).All(n => n.Text != Lore.FirstPetLine),
    "la frase de la primera mascota sale una sola vez (al cruzar de 0 a 1)");
Check((await notifier.OnEventAsync(U, GameEventKinds.Craft, 1, 1, now, "Espada")).Any(n => n.Text == Lore.FirstForgeLine) && (await notifier.OnEventAsync(U, GameEventKinds.Craft, 1, 5, now, "Espada")).All(n => n.Text != Lore.FirstForgeLine),
    "la frase de la primera pieza forjada sale una sola vez");
var nRenacido = await notifier.OnEventAsync(U, GameEventKinds.FuegoNuevo, 1, 1, now, "Ninja");
Check(nRenacido.Any(n => n.Text.Contains("Renacido I")), "(el logro Renacido sigue avisando igual)");

Console.WriteLine("\n== 6. Logros nuevos ==");
var comp = AchievementCatalog.Find("companero")!; var vol = AchievementCatalog.Find("volvio")!;
Check(AchievementCatalog.All.Count == 23 && comp.StatKind == GameEventKinds.FuegoNuevo && vol.StatKind == GameEventKinds.FuegoNuevo
        && comp.Tiers.Select(t => t.Threshold).SequenceEqual([10L, 25, 50]) && vol.Tiers.Select(t => t.Threshold).SequenceEqual([75L, 90, 100]),
    "22 logros; Compañero del Asador (10/25/50) y El que volvió (75/90/100) cuentan los Fuegos Nuevos");
Check(comp.Tiers.Concat(vol.Tiers).All(t => t.Reward.Box == BoxGrant.None), "pagan solo oro y XP (nunca una caja: el contador es de pocas vueltas pero cada una pide vencer al Asador)");
var keys = AchievementCatalog.Categories.SelectMany(c => c.Keys).ToList();
Check(keys.Count == AchievementCatalog.All.Count && keys.Distinct().Count() == keys.Count && keys.All(k => AchievementCatalog.Find(k) is not null) && AchievementCatalog.Categories.Single(c => c.Name == "Constancia").Keys.Contains("volvio"), "cada logro está en exactamente una página");
Check(AchievementCatalog.Crossed(GameEventKinds.FuegoNuevo, 9, 10).Any(c => c.Achievement.Key == "companero" && c.Tier == 1) && AchievementCatalog.Crossed(GameEventKinds.FuegoNuevo, 99, 100).Any(c => c.Achievement.Key == "volvio" && c.Tier == 3), "se cruzan al llegar a su vuelta");

Console.WriteLine("\n== 7. El capítulo se abre en la primera victoria sobre un jefe (solo) ==");
CombatState Boss(string name, int zoneId, string cls = "Guerrero") =>
    new("boss", name, "👑", 1, 1, 1, [], 0, 0, zoneId, 500, 500, 500, 1000, 0, 30, cls, ClassPassives.For(cls));
async Task<(Rec Events, FakeOutput Out)> Win(CombatState st)
{
    var rec = new Rec(); var output = new FakeOutput(); var combat = new CombatSessionService(users);
    combat.TryStart(U, st, new FakeTarget());
    await AdventureModule.ResolveTurnCoreAsync(combat, users, items, adventure, inventory, buffs, rec, zoneBoxes, U, false, false, output);
    return (rec, output);
}
string BossField(FakeOutput o) => o.Shown.Single().Embed.Fields.Where(f => f.Name.Contains("derrotado") || f.Name.Contains("cayó")).Select(f => f.Value).DefaultIfEmpty(string.Empty).First();
await Reset();
var z1 = zones[0]; var z2 = zones[1];
var first = await Win(Boss("Rey Jabalí", z1.ZoneId));
Check(first.Out.Shown.Count == 1 && first.Events.Items.Contains((GameEventKinds.StoryChapter, "cap1")), "la primera victoria sobre el Rey Jabalí registra el capítulo 1");
Check(BossField(first.Out).Contains(Lore.Chapters[0].FirstClearLine) && !BossField(first.Out).Contains("Capítulo desbloqueado"), "y la pantalla cuenta su escena (el aviso sale aparte, no dentro del embed)");
var again = await Win(Boss("Rey Jabalí", z1.ZoneId));
Check(!again.Events.Items.Any(e => e.Kind == GameEventKinds.StoryChapter) && !BossField(again.Out).Contains(Lore.Chapters[0].FirstClearLine), "la segunda vez no registra nada ni repite la escena");
var other = await Win(Boss("Lobisón Alfa", z2.ZoneId));
Check(other.Events.Items.Contains((GameEventKinds.StoryChapter, "cap2")), "el jefe de la zona siguiente abre SU capítulo");
await Reset(highestCleared: 0, gateCleared: false);
var common = await Win(Boss("Gólem del Yunque", z1.ZoneId));
Check(!common.Events.Items.Any(e => e.Kind == GameEventKinds.StoryChapter), "un monstruo que no es jefe de zona no abre nada");
await Reset(fuegoNuevo: 1, highestCleared: 0);
var lap2 = await Win(Boss("Rey Jabalí", z1.ZoneId));
Check(!lap2.Events.Items.Any(e => e.Kind == GameEventKinds.StoryChapter) && !BossField(lap2.Out).Contains(Lore.Chapters[0].FirstClearLine), "en la segunda vuelta (aunque sea la primera vez DE ESA vuelta) no se repite un capítulo que ya tenía abierto");
await Reset(highestCleared: zones[^1].ZoneId, gateCleared: false);
var asador = await Win(Boss(FogonRules.BossName, FogonRules.GateZoneId));
Check(asador.Events.Items.Contains((GameEventKinds.StoryChapter, "cap6")) && BossField(asador.Out).Contains(Lore.Chapters[5].FirstClearLine), "ganarle al Asador por primera vez abre el capítulo 6 y cuenta su escena");

Console.WriteLine("\n== 8. RecordStoryChapterAsync (la regla sola) ==");
var recX = new Rec();
var outcomeFn0 = new LevelUpOutcome(new User { FuegoNuevo = 0, CurrentZoneId = 1 }, 0);
var outcomeFn2 = new LevelUpOutcome(new User { FuegoNuevo = 2, CurrentZoneId = 1 }, 0);
Check(await recX.RecordStoryChapterAsync(U, "Señor del Volcán", true, outcomeFn0) is { Key: "cap4" } && recX.Items.Count == 1, "primera vez + vuelta 1 → abre y devuelve el capítulo");
Check(await recX.RecordStoryChapterAsync(U, "Señor del Volcán", false, outcomeFn0) is null && await recX.RecordStoryChapterAsync(U, "Señor del Volcán", true, outcomeFn2) is null && await recX.RecordStoryChapterAsync(U, null, true, outcomeFn0) is null && recX.Items.Count == 1,
    "no es primera vez / ya hizo un Fuego Nuevo / no es un jefe → no hace nada");

Console.WriteLine("\n== 9. El Asador habla distinto con las vueltas ==");
string[] Said(int fn, NpcDialogue.BossLine line) => Enumerable.Range(0, 60).Select(i => NpcDialogue.Monster(FogonRules.BossName, "🔥", line, true, new Random(i), fn)).Distinct().ToArray();
var stage0 = Said(0, NpcDialogue.BossLine.Intro);
Check(stage0.Length == 2 && stage0.All(s => s.StartsWith("🔥 **El Asador Eterno:** «")) && Said(9, NpcDialogue.BossLine.Intro).SequenceEqual(stage0), "hasta la vuelta 9 dice lo de la tragedia (2 frases)");
var stage10 = Said(10, NpcDialogue.BossLine.Intro);
Check(stage10.Length == 2 && stage10.Any(s => s.Contains("Siempre volvés vos")) && Said(24, NpcDialogue.BossLine.Intro).SequenceEqual(stage10) && !stage10.SequenceEqual(stage0), "de la 10 a la 24: «Siempre volvés vos»");
Check(Said(25, NpcDialogue.BossLine.Defeated).Any(s => s.Contains("Ramona")) && Said(49, NpcDialogue.BossLine.Defeated).Any(s => s.Contains("Ramona")) && Said(50, NpcDialogue.BossLine.Intro).Any(s => s.Contains("se mantiene solo")) && Said(89, NpcDialogue.BossLine.Intro).Any(s => s.Contains("se mantiene solo")), "escalones 25 y 50 con sus bordes");
Check(Said(90, NpcDialogue.BossLine.Victory).Any(s => s.Contains("Siempre hubo tiempo")) && Said(500, NpcDialogue.BossLine.Victory).Any(s => s.Contains("Siempre hubo tiempo")), "de la 90 en adelante, el último escalón");
Check(Enum.GetValues<NpcDialogue.BossLine>().All(l => new[] { 0, 10, 25, 50, 90 }.All(fn => Said(fn, l).Length == 2)), "cada situación de cada escalón tiene 2 frases distintas");
Check(NpcDialogue.AllTables().Where(t => t.Key.Contains("@FN")).All(t => t.Lines.Distinct().Count() >= 2) && NpcDialogue.AllTables().Count(t => t.Key.StartsWith(FogonRules.BossName + "@FN")) == 12, "las tablas nuevas están en AllTables (que revisa las 2 frases distintas) — 4 escalones × 3 situaciones");
Check(Enumerable.Range(0, 30).Select(i => NpcDialogue.Monster("Rey Jabalí", "🐗", NpcDialogue.BossLine.Intro, true, new Random(i), 99)).Distinct().All(s => NpcDialogue.AllTables().Any(t => t.Key == "Rey Jabalí:Intro" && t.Lines.Any(l => s.Contains(l)))), "los demás jefes ignoran las vueltas");
Check(Enumerable.Range(0, 30).Select(i => NpcDialogue.Blacksmith(BlacksmithLine.Greeting, new Random(i))).Distinct().All(s => !s.Contains("duermo mejor")) && Enumerable.Range(0, 60).Select(i => NpcDialogue.Blacksmith(BlacksmithLine.Greeting, new Random(i), 1)).Distinct().Count() == 3
        && NpcDialogue.Blacksmith(BlacksmithLine.Success, new Random(1), 5) == NpcDialogue.Blacksmith(BlacksmithLine.Success, new Random(1), 0), "el Herrero saluda distinto desde la vuelta 1 (solo el saludo)");
Check(Enumerable.Range(0, 60).Select(i => NpcDialogue.Shopkeeper(ShopkeeperLine.Greeting, new Random(i), 1)).Distinct().Count() == 3 && Enumerable.Range(0, 60).Select(i => NpcDialogue.Shopkeeper(ShopkeeperLine.Greeting, new Random(i))).Distinct().All(s => !s.Contains("el otro es para nadie")) && NpcDialogue.Shopkeeper(ShopkeeperLine.NoGold, new Random(2), 7) == NpcDialogue.Shopkeeper(ShopkeeperLine.NoGold, new Random(2)),
    "lo mismo con el Tabernero");

Console.WriteLine("\n== 10. La escena del Herrero y la taberna con vueltas hechas ==");
await Reset(fuegoNuevo: 1);
var invRepo = inventory;
var tavern1 = await TabernaModule.BuildSceneAsync(users, items, invRepo, buffs, U, null);
Check(tavern1.Embed.Description.Contains("**El Tabernero:**") && (new[] { "el otro es para nadie", "Siempre volvés vos", "más tranquilo" }).Any(s => tavern1.Embed.Description.Contains(s)), "la taberna de quien volvió saluda con las frases de la vuelta");
await Reset(fuegoNuevo: 0);
var scene0 = await TabernaModule.BuildSceneAsync(users, items, invRepo, buffs, U, null);
Check(!(new[] { "el otro es para nadie", "Siempre volvés vos", "más tranquilo" }).Any(s => scene0.Embed.Description.Contains(s)), "y la de un jugador nuevo, la de siempre");

Console.WriteLine("\n== 11. El Fuego Nuevo cuenta el pesar ==");
var preview = new RenewPreview("Ninja", 30, 4, false, 0, 0, false, 0, null, 0, null, 0, new Dictionary<string, long>(), 0, 0, 0, 0);
string statusText;
try { statusText = AllText(FuegoNuevoModule.BuildStatusEmbed(preview, null, new Dictionary<string, int>())); } catch (Exception ex) { statusText = "ERROR " + ex.Message; }
Check(statusText.Contains("Pesar del Asador 96 %") && statusText.Contains("/story"), "/fuegonuevo con 4 vueltas muestra el pesar (96 %)");
Check(!AllText(FuegoNuevoModule.BuildStatusEmbed(preview with { FuegoNuevo = 0 }, null, new Dictionary<string, int>())).Contains("Pesar del Asador"), "sin vueltas no muestra nada de la historia");

Console.WriteLine("\n== 12. PveTuning: vida solo contra monstruos ==");
var plain = new User { Class = "Ninja", Level = 1, MaxHp = 100, CurrentHp = 100 };
int Hp(string cls, PlayerBonuses? b) => PlayerCombatProfileCalculator.Resolve(new User { Class = cls, Level = 1, MaxHp = 100, CurrentHp = 100 }, null, null, 0, b).CombatMaxHp;
Check(Hp("Ninja", PlayerBonuses.None) == 95 && Hp("Arquero", PlayerBonuses.None) == 115 && Hp("Guerrero", PlayerBonuses.None) == 120 && Hp("Hechicero", PlayerBonuses.None) == 100, "contra monstruos: Ninja 95, Arquero 115, Guerrero 120 (el de siempre), Hechicero 100");
Check(Hp("Ninja", null) == 100 && Hp("Arquero", null) == 100 && Hp("Guerrero", null) == 120 && Hp("Hechicero", null) == 100, "sin bonificaciones (duelos y Arena) no cambia nada");
Check(PvpTuning.MaxHp(Hp("Arquero", null), "Arquero") == 125 && PvpTuning.MaxHp(Hp("Ninja", null), "Ninja") == 100 && PvpTuning.MaxHp(Hp("Guerrero", null), "Guerrero") == 108, "el PvP queda exactamente como estaba medido (Arquero 125, Ninja 100, Guerrero 108)");
var ap = PlayerCombatProfileCalculator.Resolve(new User { Class = "Arquero", Level = 10, MaxHp = 235, CurrentHp = 235 }, null, null, 0, PlayerBonuses.None);
var arch = new CombatState("boss", "X", "x", 100, 100, 1, [], 0, 0, null, ap.CombatMaxHp, ap.CombatCurrentHp, ap.CombatCurrentHp, ap.Damage, ap.Defense, 10, "Arquero", ap.Passives);
Check(ap.CombatMaxHp == 270 && arch.ToDbHpDelta(-27) == -23 && arch.ToDbHpDelta(-270) == -235, "el HP de combate se destraduce a vida real (270 de combate = 235 reales): lo que se pierde en la base es lo justo");
var blessed = PlayerCombatProfileCalculator.Resolve(new User { Class = "Ninja", Level = 1, MaxHp = 100, CurrentHp = 100 }, null, null, 0, new PlayerBonuses(PetBonuses.None, 0, new Dictionary<string, int> { [BlessingCatalog.HpKey] = 5 }));
Check(Math.Abs(blessed.CombatMaxHp - (100 * 0.95 * (1 + (BlessingCatalog.HpPerLevel * 5)))) <= 1.0, $"se multiplica con la bendición de vida, igual que las demás fuentes ({blessed.CombatMaxHp})");

Console.WriteLine("\n== 13. Comandos con el InteractionService real y DI ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var storyCmd = interactions.SlashCommands.Single(c => c.Name == "story");
Check(storyCmd.Module.SlashGroupName is null && validName(storyCmd.Name) && storyCmd.Description.Length is >= 1 and <= 100 && storyCmd.Parameters.Count == 0, "/story existe, sin grupo ni parámetros y dentro de los límites de Discord");
Discord.IComponentInteraction Click(string id) => FakeProxy.Make<Discord.IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<Discord.IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"story_pick:{U}"));
Check(routed.IsSuccess && routed.Command.Module.Name == "StoryModule" && routed.Command.MethodName == "HandlePickAsync" && routed.RegexCaptureGroups is { Length: 1 } g && g[0] == U.ToString(), $"el desplegable «story_pick:{{dueño}}» llega a su método ({(routed.IsSuccess ? routed.Command.Module.Name + "." + routed.Command.MethodName : "(ninguno)")})");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "story", "historia", "cronicas", "crónicas", "lore" }.All(a => textNames.Count(n => n == a) == 1), "aa story / historia / cronicas / lore están registrados, sin repetirse");
Check(textNames.Count(n => n == "history") == 1 && textNames.Count(n => n == "historial") == 1, "y aa history / historial siguen siendo suyos");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");
Check(interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, "siguen entrando en el tope de 100 comandos de nivel superior");

Console.WriteLine("\n== 14. Ayuda ==");
var topic = HelpTopics.Find("historia");
Check(topic?.Key == "story" && new[] { "story", "cronicas", "crónicas", "lore", "cuento" }.All(a => HelpTopics.Find(a)?.Key == "story") && HelpTopics.Find("fuego")?.Key == "fuego" && HelpTopics.Find("history") is null or { Key: not "story" }, "«info historia / story / lore» abren el tema nuevo y los demás siguen en su lugar");
var te = topic!.Build(); string tt = AllText(te);
Check(te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024) && tt.Contains("/story") && tt.Contains("sellado") && tt.Contains($"{Lore.FinalFuegoNuevo}") && !tt.Contains("Ramona"), $"el tema entra en los límites ({te.Length}), explica los actos y no spoilea");
var slashNames = interactions.SlashCommands.Select(c => c.Name).Concat(interactions.SlashCommands.Select(c => c.Module.SlashGroupName)).Where(n => n is not null).ToHashSet();
var mentioned = System.Text.RegularExpressions.Regex.Matches(tt, @"/([a-z]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
Check(mentioned.All(m => slashNames.Contains(m)), $"todo comando que el tema nombra existe ({string.Join(", ", mentioned.Where(m => !slashNames.Contains(m)))})");
var allTopicsOk = HelpTopics.All.All(t => { var e = t.Build(); return e.Length <= 6000 && e.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256) && (e.Description?.Length ?? 0) <= 4096; });
var idx = HelpTopics.BuildIndexEmbed();
Check(allTopicsOk && idx.Length <= 6000 && idx.Fields.All(f => f.Value.Length <= 1024) && HelpTopics.All.Count == 27, $"los 27 temas y el índice entran en Discord (índice {idx.Length})");
var infoE = HelpModule.BuildInfoEmbed();
Check(AllText(infoE).Contains("/story") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024), $"/info lista /story, dentro de los límites ({infoE.Length})");
var welcome = OnboardingModule_Confirmation();
Check(welcome.Contains("/story"), "la bienvenida de /start nombra /story");

string OnboardingModule_Confirmation()
{
    var m = typeof(OnboardingModule).GetMethod("BuildConfirmationEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
    var embed = (Embed)m.Invoke(null, ["Prueba", new User { Class = "Guerrero", Level = 1, MaxHp = 100, CurrentHp = 100, Gold = 50 }, ClassCatalog.All[0], true])!;
    return AllText(embed);
}

Console.WriteLine("\n== 15. La apertura de cajas sin rareza (v0.15.1) ==");
{
    var box = new Item { ItemId = 1, Name = "Cofre de Oro", Type = "Caja", Rarity = "Legendario", BoxMinItems = 25, BoxMaxItems = 60 };
    var opened = BoxModule.BuildOpenedEmbed(box, 1, 1, 0, [new LootedItem(1, "Ceniza del Abismo", "Mítico", null, 3), new LootedItem(2, "Cuero Grueso", "Común", null, 5), new LootedItem(3, "Gema de Zafiro", "Legendario", null, 1)], 100);
    string text = opened.Description;
    Check(text.Contains("3×") && text.Contains("Ceniza del Abismo") && text.Contains("Cuero Grueso") && text.Contains("Gema de Zafiro"), "la lista sigue mostrando cantidad y nombre de cada ítem");
    Check(!new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Any(r => text.Contains(r)) && !text.Contains("_("), "y ya no escribe la rareza al lado de ninguno");
    Check(text.IndexOf("Ceniza del Abismo") < text.IndexOf("Cuero Grueso"), "el orden por rareza del catálogo se mantiene (lo más alto arriba)");
}

Console.WriteLine("\n== 16. Amuletos más fuertes contra monstruos, el PvP con la escalera vieja (v0.15.1) ==");
{
    string[] names = ["Hombreras de Cuero Grueso", "Talismán de Ceniza Bendita", "Peto de Escoria Templada", "Talismán del Volcán", "Corazón de Titán Engarzado", "Brasa del Fogón Eterno"];
    int[] pve = [10, 35, 60, 85, 120, 165], pvp = [10, 18, 30, 46, 75, 120];
    var rows = new List<(string Name, int Stat)>();
    foreach (var n in names) rows.Add((n, await db.ExecuteScalarAsync<int>("SELECT stat_value FROM items WHERE name = @n AND type = 'Amulet'", new { n })));
    Check(rows.Select(r => r.Stat).SequenceEqual(pve), $"en la base los amuletos valen {string.Join('/', rows.Select(r => r.Stat))} (contra monstruos)");
    bool mapOk = true;
    for (int i = 0; i < names.Length; i++) mapOk &= PvpTuning.Amulet(new Item { Name = names[i], Type = "Amulet", StatValue = pve[i] })!.StatValue == pvp[i];
    Check(mapOk && PvpTuning.Amulet(null) is null && PvpTuning.Amulet(new Item { Name = "Amuleto nuevo", StatValue = 77 })!.StatValue == 77, "PvpTuning.Amulet devuelve la DEF vieja de cada amuleto por nombre, deja pasar uno desconocido y null");
    var piece = new Item { ItemId = 5, Name = "Peto de Escoria Templada", Type = "Amulet", Rarity = "Legendario", StatValue = 60, SellPrice = 360, BuyPrice = 468, Emoji = "<:x:1>" };
    var copy = PvpTuning.Amulet(piece)!;
    Check(copy.StatValue == 30 && copy.ItemId == 5 && copy.Emoji == piece.Emoji && copy.SellPrice == 360 && piece.StatValue == 60, "la copia no toca el original y conserva el resto del ítem");

    await Reset();
    await db.ExecuteAsync("UPDATE users SET level = 20, amulet_id = (SELECT item_id FROM items WHERE name = 'Peto de Escoria Templada') WHERE discord_id = @id", new { id = (long)U });
    var fighter = await new DuelFighterFactory(users, items, buffs).BuildAsync(U, "Prueba");
    var monsterProfile = PlayerCombatProfileCalculator.Resolve((await users.GetByDiscordIdAsync(U))!, null, await items.GetByIdAsync(await db.ExecuteScalarAsync<int>("SELECT amulet_id FROM users WHERE discord_id = @id", new { id = (long)U }), default), 0, PlayerBonuses.None);
    Check(fighter is not null && fighter.Profile.Defense == 20 + 30 && monsterProfile.Defense == 20 + 60, $"el mismo jugador defiende {fighter?.Profile.Defense} en un duelo y {monsterProfile.Defense} contra monstruos");

    // la cacería común con el amuleto de la zona casi no cuesta vida (lo que pidió el dueño)
    var z3 = World_Z3();
    double cost = 0; int n2 = 0;
    foreach (var cls in new[] { "Guerrero", "Hechicero", "Ninja", "Arquero" })
        foreach (int level in new[] { 13, 16, 20 })
        {
            var prof = PlayerCombatProfileCalculator.Resolve(new User { Class = cls, Level = level, MaxHp = 100 + (15 * (level - 1)), CurrentHp = 100 + (15 * (level - 1)) }, new Item { StatValue = 32, WeaponFamily = ClassCatalog.All.First(c => c.Name == cls).WeaponType }, new Item { StatValue = 60 }, 0, PlayerBonuses.None);
            for (int k = 0; k < 400; k++)
            {
                var m = z3[k % z3.Length];
                int hp = prof.CombatMaxHp, mHp = Random.Shared.Next(m.min, m.max + 1), mDmg = Random.Shared.Next(m.dmin, m.dmax + 1);
                var st = default(AbilityState);
                var cp = new CombatantProfile(prof.Damage, prof.Defense, cls, prof.Passives);
                while (mHp > 0 && hp > 0) { var r = CombatTurnResolver.ResolveTurn(cp, st, PlayerAction.Attack, hp, prof.CombatMaxHp, mHp, mDmg); hp = r.PlayerHpAfter; mHp = r.MonsterHpAfter; st = r.StatusAfter; }
                cost += (prof.CombatMaxHp - hp) / (double)prof.CombatMaxHp; n2++;
            }
        }

    Check(cost / n2 < 0.05, $"una cacería común de la Zona 3 con el Peto (60) y ataque básico cuesta {100 * cost / n2:F1} % de la vida (autohunt sin perder casi nada)");
}

(int min, int max, int dmin, int dmax)[] World_Z3() =>
[
    (271, 397, 59, 95), (250, 365, 55, 84),
];

Console.WriteLine("\n== 17. El Polvo de los drops de monstruo por su origen (v0.15.1) ==");
{
    Check(Dismantling.HuntDropDust == 17, $"la constante sale de la chance y el cooldown reales: cacería {Dismantling.HuntDropDust} (los drops de viaje se borraron en la v0.36.0)");
    var dropRows = (await db.QueryAsync<(string Name, string Rarity, int Dust, bool Travel)>(
        @"SELECT i.name, i.rarity, i.dust_value, EXISTS (SELECT 1 FROM monster_drops d JOIN monsters m ON m.monster_id = d.monster_id WHERE d.item_id = i.item_id AND m.is_travel) FROM items i WHERE i.dust_value IS NOT NULL")).ToList();
    Check(dropRows.Count == 10 && dropRows.All(r => r.Dust == Dismantling.HuntDropDust) && dropRows.Count(r => r.Travel) == 0,
        "en la base: los 10 drops de monstruo (todos de cacería) traen su Polvo y coincide con la constante del código");
    Check(dropRows.Any(r => r.Rarity == "Mítico" && r.Dust == 17) && dropRows.Any(r => r.Rarity == "Común" && r.Dust == 17), "un drop «Mítico» de cacería da lo mismo que uno «Común» (la etiqueta ya no cuenta)");
    Check(Dismantling.DustFor("Mítico", 1) == 500 && Dismantling.DustFor("Raro", 3) == 18 && Dismantling.DustFor("Mítico", 2, 17) == 34 && Dismantling.DustPerUnit("Común", 38) == 38,
        "lo recolectado sigue pagando por rareza (Mítico 500) y el valor propio del ítem manda cuando existe");
    var inv = new List<InventoryEntry>
    {
        new() { ItemName = "Corazón de Titán", Type = "Material", Rarity = "Mítico", Quantity = 4, SellPrice = 300, DustValue = 17 },
        new() { ItemName = "Madera de Pino", Type = "Madera", Rarity = "Común", Quantity = 10, SellPrice = 2 },
    };
    var dl = ItemChoices.ForDismantle(inv, string.Empty);
    Check(dl.Count == 2 && dl.Any(d => d.Name.Contains("Corazón de Titán") && d.Name.Contains("+17 Polvo") && !d.Name.Contains("Mítico")) && dl.Any(d => d.Name.Contains("Madera de Pino") && d.Name.Contains("+1 Polvo") && d.Name.Contains("(Común)")),
        "la lista de /dismantle: el drop muestra +17 y sin rareza; la madera, +1 con su rareza");
    var sl = ItemChoices.ForSell(inv, string.Empty);
    Check(sl.Any(d => d.Name.Contains("Corazón de Titán") && !d.Name.Contains("Mítico")) && sl.Any(d => d.Name.Contains("Madera de Pino") && d.Name.Contains("(Común)")), "y la de /shop sell: el drop sin rareza, la madera con ella");

    await Reset();
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 5 FROM items WHERE name IN ('Corazón de Titán', 'Madera de Pino', 'Fragmento de Alma')", new { id = (long)U });
    var dust = new DustRepository(factory);
    var d1 = await DustModule.ExecuteDismantleAsync(users, items, dust, new Rec(), U, "Corazón de Titán", 3);
    var d2 = await DustModule.ExecuteDismantleAsync(users, items, dust, new Rec(), U, "Fragmento de Alma", 2);
    var d3 = await DustModule.ExecuteDismantleAsync(users, items, dust, new Rec(), U, "Madera de Pino", 4);
    Check(d1.Embed is not null && AllText(d1.Embed).Contains("+51") && d2.Embed is not null && AllText(d2.Embed).Contains("+34") && d3.Embed is not null && AllText(d3.Embed).Contains("+4"),
        "desmantelar de verdad: 3 Corazón de Titán (Mítico, cacería) = 51, 2 Fragmento de Alma (Mítico, cacería) = 34, 4 Pino = 4");
    Check(await db.ExecuteScalarAsync<int>("SELECT dust FROM users WHERE discord_id = @id", new { id = (long)U }) == 51 + 34 + 4, "y el Polvo de la cuenta es la suma exacta");
    var info = DustModule.BuildOptionsEmbed();
    Check(AllText(info).Contains("17") && !AllText(info).Contains("38") && AllText(info).Contains("no tienen rareza") && info.Length <= 6000, "/enchant info y el tema de ayuda explican el Polvo de los drops");
}

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
    public Task ClearComponentsAsync() => Task.CompletedTask;
}

sealed class FakeOutput : ICombatTurnOutput
{
    public List<string> Notes { get; } = [];
    public List<(Embed Embed, MessageComponent Components)> Shown { get; } = [];
    public Task NoteAsync(string text) { Notes.Add(text); return Task.CompletedTask; }
    public ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous) => new FakeTarget();
    public Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous) { Shown.Add((embed, components)); return Task.CompletedTask; }
}

sealed class Rec : IGameEvents
{
    public List<(string Kind, string? Detail)> Items { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Items.Add((kind, detail)); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
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
