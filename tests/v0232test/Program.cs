using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;

// v0.23.2 (el dueño, 2026-10-22): «la Arena la gana siempre el de más nivel… quiero que pueda ganarlo alguien de nivel 1 contra el del 20; si no, llega un punto en que se hace imposible… random con textos mientras pasan los duelos».
// Tres capas de azar (GameData/ArenaTuning.cs: poder comprimido, forma del día y milagro) y la pelea contada (GameData/ArenaStory.cs); el anuncio la cuenta golpe por golpe en las semifinales y la final.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

DuelFighter Make(int id, int level, string cls, int weaponStat, int amuletDef)
{
    var user = new User { DiscordId = id, Class = cls, Level = level, MaxHp = 100 + (15 * (level - 1)), CurrentHp = 100 + (15 * (level - 1)) };
    var family = ClassCatalog.All.First(c => c.Name == cls).WeaponType;
    var weapon = weaponStat > 0 ? new Item { ItemId = 1, Name = "W", Type = "Weapon", StatValue = weaponStat, WeaponFamily = family } : null;
    var amulet = amuletDef > 0 ? new Item { ItemId = 2, Name = "A", Type = "Amulet", StatValue = amuletDef } : null;
    var p = PlayerCombatProfileCalculator.Resolve(user, weapon, amulet, 0);
    return new DuelFighter((ulong)id, $"Jugador{id}", level, cls, new CombatantProfile(p.Damage, p.Defense, cls, p.Passives), PvpTuning.MaxHp(p.CombatMaxHp, cls));
}

Console.WriteLine("== 1. La preparación de una pareja ==");
var weak = Make(1, 1, "Guerrero", 0, 0);
var strong = Make(2, 20, "Guerrero", 20, 18);
var plain = ArenaTuning.Prepare(strong, weak, new Random(1), compression: 1.0, swing: 0.0, miracleChance: 0.0);
Check(plain.A.Profile.Damage == strong.Profile.Damage && plain.A.MaxHp == strong.MaxHp && plain.B.Profile.Defense == weak.Profile.Defense && !plain.Miracle && plain.FormA == 1.0 && plain.FormB == 1.0,
    "sin compresión, sin forma y sin milagro la pareja queda exactamente como es (k = 1)");
var squeezed = ArenaTuning.Prepare(strong, weak, new Random(1), compression: 0.5, swing: 0.0, miracleChance: 0.0);
double ratioBefore = (double)strong.Profile.Damage / weak.Profile.Damage, ratioAfter = (double)squeezed.A.Profile.Damage / squeezed.B.Profile.Damage;
Check(Math.Abs(ratioAfter - Math.Sqrt(ratioBefore)) < 0.2 && squeezed.A.Profile.Damage < strong.Profile.Damage && squeezed.B.Profile.Damage > weak.Profile.Damage && squeezed.A.MaxHp < strong.MaxHp && squeezed.B.MaxHp > weak.MaxHp,
    $"con k = 0,5 la razón de daño baja a su raíz cuadrada ({ratioBefore:F2} → {ratioAfter:F2}) y el fuerte sigue siendo más fuerte");
Check(Math.Abs(Math.Sqrt((double)squeezed.A.MaxHp * squeezed.B.MaxHp) - Math.Sqrt((double)strong.MaxHp * weak.MaxHp)) < 2, "la media geométrica de la vida no cambia (se acercan, no se compensan)");
var miracle = ArenaTuning.Prepare(strong, weak, new Random(1), compression: 0.5, swing: 0.0, miracleChance: 1.0);
Check(miracle.Miracle && Math.Abs(miracle.A.Profile.Damage - miracle.B.Profile.Damage) <= 1 && Math.Abs(miracle.A.MaxHp - miracle.B.MaxHp) <= 1 && Math.Abs(miracle.A.Profile.Defense - miracle.B.Profile.Defense) <= 1,
    "con milagro los dos quedan con el mismo poder (daño, defensa y vida)");
Check(squeezed.A.DiscordId == 2 && squeezed.A.PlayerClass == "Guerrero" && squeezed.A.Level == 20 && ReferenceEquals(squeezed.A.Profile.Passives, strong.Profile.Passives) && squeezed.B.Name == weak.Name, "el id, la clase, el nivel y las pasivas no cambian");
var rng = new Random(99);
var many = Enumerable.Range(0, 4000).Select(_ => ArenaTuning.Prepare(strong, weak, rng)).ToList();
Check(many.All(s => s.FormA is >= 1 - ArenaTuning.FormSwing - 1e-9 and <= 1 + ArenaTuning.FormSwing + 1e-9 && s.FormB is >= 1 - ArenaTuning.FormSwing - 1e-9 and <= 1 + ArenaTuning.FormSwing + 1e-9), $"la forma del día siempre queda entre {1 - ArenaTuning.FormSwing:F1} y {1 + ArenaTuning.FormSwing:F1}");
double miracleRate = many.Count(s => s.Miracle) / 4000.0;
Check(Math.Abs(miracleRate - ArenaTuning.MiracleChance) < 0.03, $"el milagro sale en ~{ArenaTuning.MiracleChance * 100:F0} % de las peleas (salió {miracleRate * 100:F1} %)");
Check(many.All(s => s.A.Profile.Damage >= 1 && s.B.Profile.Damage >= 1 && s.A.MaxHp >= 1 && s.B.MaxHp >= 1 && s.A.Profile.Defense >= 0 && s.B.Profile.Defense >= 0), "ningún valor baja de su mínimo (daño 1, vida 1, defensa 0)");
Check(ArenaTuning.Compress(0, 50, 0.5) == 0 && ArenaTuning.Compress(-3, 50, 0.5) == -3 && ArenaTuning.Compress(10, 10, 0.0) == 10 && Math.Abs(ArenaTuning.Compress(4, 100, 0.0) - 20) < 1e-9, "un valor sin sentido se deja como está y con k = 0 los dos quedan en la media geométrica");

Console.WriteLine("\n== 2. Cuánto gana el de MÁS nivel (el motivo de todo esto) ==");
string[] classes = ["Guerrero", "Hechicero", "Arquero", "Ninja"];
double WinRate(int lowLevel, int highLevel, (int w, int a) lowGear, (int w, int a) highGear, int seed, int n = 1600)
{
    var r = new Random(seed);
    int wins = 0, total = 0;
    foreach (var cls in classes)
    {
        var low = Make(1, lowLevel, cls, lowGear.w, lowGear.a);
        var high = Make(2, highLevel, cls, highGear.w, highGear.a);
        for (int i = 0; i < n / classes.Length; i++)
        {
            if (ArenaFightRunner.Play(low, high, r, maxHighlights: 0).Result.Winner.Fighter.DiscordId == 2) wins++;
            total++;
        }
    }

    return 100.0 * wins / total;
}

double even = WinRate(10, 10, (20, 18), (20, 18), 1);
double plus1 = WinRate(10, 11, (20, 18), (20, 18), 2), plus5 = WinRate(10, 15, (20, 18), (20, 18), 3), plus10 = WinRate(10, 20, (20, 18), (20, 18), 4), plus19 = WinRate(10, 29, (20, 18), (20, 18), 5);
Check(even is > 44 and < 56, $"dos iguales: ~50 % ({even:F1} %)");
Check(plus1 is > 46 and < 63 && plus5 is > 54 and < 74 && plus10 is > 60 and < 80 && plus19 is > 68 and < 90, $"el de más nivel con el mismo equipo gana: +1 {plus1:F1} %, +5 {plus5:F1} %, +10 {plus10:F1} %, +19 {plus19:F1} % (antes casi 75 / 98 / 100 / 100 %)");
Check(plus1 < plus5 && plus5 < plus10 && plus10 < plus19 + 3, "subir de nivel sigue sirviendo: cuanto más arriba, más chance");
double oneVsTwenty = WinRate(1, 20, (0, 0), (20, 18), 6, 2400), oneVsTwentyZ4 = WinRate(1, 20, (0, 0), (50, 46), 7, 2400);
Check(oneVsTwenty is > 76 and < 95 && 100 - oneVsTwenty >= 5, $"nivel 1 SIN equipo contra nivel 20 con equipo de zona 2: el de nivel 1 gana {100 - oneVsTwenty:F1} % (antes 0 %)");
Check(oneVsTwentyZ4 is > 76 and < 95 && 100 - oneVsTwentyZ4 >= 5, $"nivel 1 sin equipo contra nivel 20 con equipo de zona 4: el de nivel 1 gana {100 - oneVsTwentyZ4:F1} %");

Console.WriteLine("\n== 3. La pelea contada ==");
var played = ArenaFightRunner.Play(Make(1, 5, "Guerrero", 10, 10), Make(2, 5, "Ninja", 10, 10), new Random(3));
Check(played.Result.Winner.Fighter.DiscordId is 1 or 2 && played.Story.P1Id == 1 && played.Story.P2Id == 2 && played.Result.Actions >= 1, "la pelea tiene un ganador entre los dos y su relato dice quiénes son (en el orden de la pareja)");
Check(played.Story.Highlights.Count is >= 1 and <= ArenaStoryteller.DetailedHighlights && played.Story.Highlights.All(l => l.Length > 0), $"cuenta entre 1 y {ArenaStoryteller.DetailedHighlights} golpes ({played.Story.Highlights.Count})");
var silent = ArenaFightRunner.Play(Make(1, 5, "Guerrero", 10, 10), Make(2, 5, "Ninja", 10, 10), new Random(3), maxHighlights: 0);
Check(silent.Story.Highlights.Count == 0, "con 0 golpes cuenta solo la apertura");

// El relato sobre eventos armados a mano: el golpe final siempre está, y las aperturas dependen del milagro y de la forma.
var a = Make(1, 5, "Guerrero", 10, 10); var b = Make(2, 5, "Ninja", 10, 10);
var evs = Enumerable.Range(0, 30).Select(i => new DuelEvent(i % 2 == 0, i % 2 == 0 ? a.Name : b.Name, i % 2 == 0 ? b.Name : a.Name, null, false, true, i % 7 == 3, i == 11 ? 90 : 10 + i, i == 5 ? 1 : 0, 0)).ToList();
var story = ArenaStoryteller.Compose(new ArenaSetup(a, b, 1.25, 0.8, true), evs, 6, new Random(1));
Check(story.Miracle && story.Opening.Count == 3 && story.Opening[0].Contains("Jugador1") && story.Opening[0].Contains("Jugador2") && story.Opening.Any(l => l.Contains("+25 %")) && story.Opening.Any(l => l.Contains("−20 %")), "con milagro y dos formas notables: tres líneas de apertura, con los porcentajes");
Check(story.Highlights.Count == 6 && story.Highlights.Last() == DuelNarrator.Describe(evs[^1]) && story.Highlights.Contains(DuelNarrator.Describe(evs[11])), "elige 6 de los 30 golpes: el último y el más fuerte seguro, en orden");
var calm = ArenaStoryteller.Compose(new ArenaSetup(a, b, 1.05, 0.95, false), evs, 3, new Random(1));
Check(!calm.Miracle && calm.Opening.Count == 0 && calm.Highlights.Count == 3, "sin milagro y con formas parecidas (menos de ±12 %) no hay apertura");
Check(ArenaStoryteller.Compose(new ArenaSetup(a, b, 1, 1, false), [], 8, new Random(1)).Highlights.Count == 0 && ArenaStoryteller.PickHighlights(evs, 0).Count == 0, "sin golpes, o pidiendo 0, no cuenta ninguno y no tira");
Check(ArenaStoryteller.PickHighlights(evs.Take(2).ToList(), 8).Count == 2, "una pelea de 2 golpes los cuenta a los dos");

Console.WriteLine("\n== 4. El anuncio ==");
ArenaMatchRow Row(int round, int slot, ulong p1, string n1, ulong? p2, string? n2, ulong winner) => new(round, slot, p1, n1, p2, n2, winner, 30, 55);
ArenaFightStory St(ulong p1, ulong p2, bool miracle = false, int highlights = 5) =>
    new(p1, p2, miracle, miracle ? [$"🍀 ¡Milagro en la Arena! para {p1} y {p2}."] : ["💪 llegó en un día brillante (+20 %)."], Enumerable.Range(0, highlights).Select(i => $"⚔️ golpe {i} de {p1}").ToList());
var rows8 = new List<ArenaMatchRow>();
var stories8 = new List<ArenaFightStory>();
for (int s = 0; s < 4; s++) { rows8.Add(Row(1, s, (ulong)(10 + (2 * s)), $"A{s}", (ulong)(11 + (2 * s)), $"B{s}", (ulong)(10 + (2 * s)))); stories8.Add(St((ulong)(10 + (2 * s)), (ulong)(11 + (2 * s)), miracle: s == 1)); }
for (int s = 0; s < 2; s++) { rows8.Add(Row(2, s, (ulong)(10 + (4 * s)), $"A{2 * s}", (ulong)(12 + (4 * s)), $"A{(2 * s) + 1}", (ulong)(10 + (4 * s)))); stories8.Add(St((ulong)(10 + (4 * s)), (ulong)(12 + (4 * s)))); }
rows8.Add(Row(3, 0, 10, "A0", 18, "A2", 10)); stories8.Add(St(10, 18, miracle: true));
var res8 = new ArenaResolution(new DateOnly(2026, 10, 1), false, 99, 10, "A0", 8, 3, "💰 210 oro", 0, 0, rows8, stories8);
var cast = ArenaModule.BuildBroadcast(res8);
var titles = cast.Select(m => m.Embed.Title ?? string.Empty).ToList();
Check(cast[0].Embed.Description.Contains("milagro") && cast[0].Embed.Description.Contains("forma del día") && cast[0].Embed.Description.Contains($"{(int)(ArenaTuning.MiracleChance * 100)} de cada 100"), "la apertura del torneo avisa que tiene azar (forma del día y milagro)");
Check(titles.Count(t => t.Contains("Cuartos de final")) == 1 && cast.Single(m => (m.Embed.Title ?? string.Empty).Contains("Cuartos")).Embed.Description.Contains("🍀 ¡Milagro"), "la primera ronda (cuartos) va en un solo mensaje y cuenta el milagro de la pelea que lo tuvo");
Check(titles.Count(t => t.Contains("Semifinal")) == 2 && titles.Count(t => t.Contains("Final") && !t.Contains("Semifinal")) == 1, "cada semifinal y la final tienen su propio mensaje (2 + 1)");
var semi = cast.First(m => (m.Embed.Title ?? string.Empty).Contains("Semifinal")).Embed;
Check(semi.Title!.Contains("A0") && semi.Title.Contains("⚔️") && semi.Description.Contains("golpe 0") && semi.Description.Contains("golpe 4") && semi.Description.Contains("llegó en un día brillante"), "una semifinal cuenta cómo llegaron y sus golpes uno por uno");
Check(cast.All(m => m.Embed.Description.Length <= 4096 && m.Embed.Length <= 6000), "todo mensaje entra en los límites de Discord");
var legacy = ArenaModule.BuildBroadcast(new ArenaResolution(new DateOnly(2026, 10, 1), false, 99, 10, "A0", 8, 3, "💰 210 oro", 0, 0, rows8));
Check(legacy.Count == 1 + 3 + 1 && !legacy[0].Embed.Description.Contains("milagro"), "sin relato el anuncio es el de siempre: apertura, un mensaje por ronda y cierre");

// 32 anotados: 16 peleas en la primera ronda con relato; tiene que partirse en varios mensajes y no pasarse de los límites.
var rows32 = new List<ArenaMatchRow>(); var stories32 = new List<ArenaFightStory>(); ulong next = 1000;
int count = 16;
for (int round = 1; round <= 5; round++, count /= 2)
{
    for (int s = 0; s < count; s++)
    {
        ulong p1 = next++, p2 = next++;
        rows32.Add(Row(round, s, p1, $"UnJugadorConNombreLargo{p1}", p2, $"OtroJugadorConNombreLargo{p2}", p1));
        stories32.Add(new ArenaFightStory(p1, p2, true, ["🍀🍀🍀" + " ¡Milagro en la Arena! " + new string('x', 150), "💪 " + new string('y', 120)], Enumerable.Range(0, 8).Select(i => "⚔️ " + new string('z', 150)).ToList()));
    }
}

var cast32 = ArenaModule.BuildBroadcast(new ArenaResolution(new DateOnly(2026, 10, 1), false, 99, 1000, "x", 32, 5, null, 0, 0, rows32, stories32));
Check(cast32.All(m => m.Embed.Description.Length <= 4096 && m.Embed.Length <= 6000), "con 32 anotados y relatos largos ningún mensaje pasa de 4096 / 6000");
Check(cast32.Count(m => (m.Embed.Title ?? string.Empty).StartsWith("Ronda 1")) >= 2 && cast32.Count < 40, $"la primera ronda de 16 peleas se parte en varios mensajes ({cast32.Count} mensajes en total)");

Console.WriteLine("\n== 5. La ayuda ==");
var help = HelpTopics.Find("arena")!.Build();
var helpText = string.Join("\n", help!.Fields.Select(f => f.Name + " " + f.Value));
Check(helpText.Contains("forma del día") && helpText.Contains($"{(int)(ArenaTuning.MiracleChance * 100)} de cada 100") && helpText.Contains("nivel 1 puede ganarle a un nivel 20"), "/info tema:arena explica el azar con los números reales");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
