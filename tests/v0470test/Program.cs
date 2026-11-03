using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.GameData.Dungeons;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.47.0: las Zonas 6 a 10 (el dueño, 2026-11-01: «que se tenga que farmear hasta el FN 5… cada más FN más zonas… que pasar de zona sea una mazmorra tipo acertijo… y el Asador más fuerte»).
// La escalera por Fuego Nuevo (zones.min_fuego_nuevo, ZoneRanking.ForPlayer), la puerta que se corre, los datos de las zonas nuevas, las mazmorras y el Asador escalado. Contra la base LOCAL.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var monsters = new MonsterRepository(factory);
var adventure = new AdventureRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const long Base = 999000000000047000L;
ulong Id(int n) => (ulong)(Base + n);

async Task Make(int n, int level, int fn, int zone, int cleared)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id;", new { id = Base + n });
    await users.GetOrCreateUserAsync(Id(n));
    await db.ExecuteAsync("UPDATE users SET level = @l, fuego_nuevo = @fn, current_zone_id = @z, highest_zone_cleared = @c, gate_cleared = false, in_gate = false, class = 'Guerrero' WHERE discord_id = @id",
        new { id = Base + n, l = level, fn, z = zone, c = cleared });
}

Console.WriteLine("== 1. La escalera por Fuego Nuevo (pura, con las zonas de la base) ==");
var all = await zonesRepo.GetAllAsync();
Check(all.Count == 10 && all.Count(z => z.MinFuegoNuevo > 0) == 5, $"la base tiene 10 zonas normales, 5 de ellas con Fuego Nuevo ({all.Count})");
Check(new[] { (0, 5), (4, 5), (5, 6), (7, 6), (8, 7), (11, 8), (14, 9), (16, 9), (17, 10), (99, 10) }.All(p => ZoneRanking.ForPlayer(all, p.Item1).Count == p.Item2),
    "FN 0/4 → 5 zonas, 5/7 → 6, 8 → 7, 11 → 8, 14/16 → 9, 17 en adelante → las 10");
Check(all.Where(z => z.ZoneId <= 5).All(z => !ZoneRanking.IsDungeonZone(z)) && all.Where(z => z.ZoneId >= 6).All(ZoneRanking.IsDungeonZone), "las zonas 6 a 10 son de mazmorra; las 1 a 5 no");
Check(FogonRules.IsGateOpen(ZoneRanking.ForPlayer(all, 0), 5) && !FogonRules.IsGateOpen(ZoneRanking.OrderByDifficulty(all), 5),
    "con FN 0 la Zona 5 abre el Fogón con la escalera DEL JUGADOR (con la lista entera no se abriría nunca: por eso todo pasa por ForPlayer)");
Check(ZoneRanking.MaxUnlockedRank(ZoneRanking.ForPlayer(all, 0), 60, 5) == 5 && ZoneRanking.MaxUnlockedRank(ZoneRanking.ForPlayer(all, 5), 60, 5) == 6 && ZoneRanking.MaxUnlockedRank(ZoneRanking.ForPlayer(all, 8), 60, 5) == 6,
    "un nivel 60 con FN 0 llega hasta la 5; con FN 5 y la 5 vencida, a la 6; con FN 8 sigue en la 6 hasta pasar su mazmorra");
Check(ZoneRanking.TopZone(all, 5)?.ZoneId == 6 && ZoneRanking.TopZone(all, 0)?.ZoneId == 5 && ZoneRanking.NextLockedByFuegoNuevo(all, 5)?.ZoneId == 7 && ZoneRanking.NextLockedByFuegoNuevo(all, 17) is null,
    "la zona tope y la próxima que se abre");
Check(MissionRewards.GoldUnit(6) == 560 && MissionRewards.GoldUnit(10) == 2840 && MissionRewards.GoldUnit(11) == 2840, "la unidad de oro de las zonas nuevas (560 … 2840)");

Console.WriteLine("\n== 2. La puerta se corre (base real) ==");
await Make(1, 30, 0, 5, 4);
var v0 = await adventure.ApplyBossVictoryAsync(Id(1), 10, 10, 0, null, 0, 5);
Check(v0.Gate == GateEvent.Opened, "FN 0: ganarle al jefe de la Zona 5 abre el Fogón como siempre");
await Make(2, 30, 5, 5, 4);
var v5 = await adventure.ApplyBossVictoryAsync(Id(2), 10, 10, 0, null, 0, 5);
Check(v5.Gate == GateEvent.None && v5.Player.HighestZoneCleared == 5, "FN 5: ganarle al jefe de la Zona 5 ya NO abre el Fogón: sube la escalera (se abre la Zona 6)");
await Make(3, 35, 5, 6, 5);
var asador = await adventure.ApplyBossVictoryAsync(Id(3), 10, 10, 0, null, 0, FogonRules.GateZoneId);
Check(asador.Gate == GateEvent.Cleared && asador.Player.CurrentZoneId == 6 && asador.Player.GateCleared, "FN 5: ganarle al Asador te devuelve a TU zona tope (la 6), no a la 10");

Console.WriteLine("\n== 3. /zona y /zonas ==");
await Make(4, 40, 4, 5, 5);
var (msg4, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(4), 6);
Check(msg4 is not null && msg4.Contains("Fuego Nuevo 5"), $"/zona 6 con FN 4: «se abre con el Fuego Nuevo 5» ({msg4})");
await Make(5, 40, 5, 5, 5);
var (msg5, emb5) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(5), 6);
Check(msg5 is null && emb5 is not null && (await users.GetByDiscordIdAsync(Id(5)))!.CurrentZoneId == 6, "/zona 6 con FN 5, la 5 vencida y nivel 40: viaja");
await Make(6, 40, 5, 5, 4);
var (msg6, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(6), 6);
Check(msg6 is not null && msg6.Contains("Soberano"), "/zona 6 sin la Zona 5 vencida: pide su jefe");
await Make(7, 50, 8, 6, 5);
var (msg7, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(7), 7);
Check(msg7 is not null && msg7.Contains("mazmorra") && msg7.Contains("Salinas"), $"/zona 7 con FN 8 sin pasar la mazmorra de la 6: la pide ({msg7})");
var (gateMsg, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(7), 0);
Check(gateMsg is not null && gateMsg.Contains("mazmorra"), "/zona 0 con la zona tope de mazmorra: se llega pasando la mazmorra, no caminando");
var list0 = AllText(await ZoneModule.BuildZoneListEmbedAsync(users, zonesRepo, Id(4)));
var list5 = AllText(await ZoneModule.BuildZoneListEmbedAsync(users, zonesRepo, Id(5)));
Check(list0.Contains("Fuego Nuevo 5") && !list0.Contains("mazmorra") && list5.Contains("Salinas") && list5.Contains("Selva de Humo") && list5.Contains("Fuego Nuevo 8") && list5.Contains("mazmorra"),
    "/zonas con FN 4 solo avisa que la 6 se abre en el FN 5; con FN 5 muestra la 6 (sin jefe, con mazmorra) y avisa que la 7 se abre en el FN 8");

Console.WriteLine("\n== 4. Los datos de las zonas 6 a 10 ==");
var mons = await monsters.GetAllAsync();
foreach (var z in all.Where(ZoneRanking.IsDungeonZone))
{
    var mine = mons.Where(m => m.ZoneId == z.ZoneId).ToList();
    Check(mine.Count(m => m.Kind == MonsterKind.Hunt) == 1 && mine.Count(m => m.Kind == MonsterKind.Travel) == 1 && mine.Count(m => m.Kind == MonsterKind.Boss) == 0 && mine.Single(m => m.Kind == MonsterKind.Hunt).Monster.DropItemNames.Count == 1,
        $"Zona {z.ZoneId}: un monstruo de caza con UN drop, uno de viaje y ningún jefe");
}

Console.WriteLine("\n== 5. Los acertijos (puros) ==");
Check(DungeonRules.ForZoneRank(6) == DungeonKind.FirePattern && DungeonRules.ForZoneRank(7) == DungeonKind.SmokeMaze && DungeonRules.ForZoneRank(8) == DungeonKind.Embers && DungeonRules.ForZoneRank(9) == DungeonKind.Lock && DungeonRules.ForZoneRank(10) == DungeonKind.Rhythm && DungeonRules.ForZoneRank(5) is null,
    "cada zona de la 6 a la 10 tiene su mazmorra (y la 5 ninguna)");
{
    var fp = new FirePattern(new Random(3));
    var s1 = fp.CurrentSeries.ToArray();
    Check(fp.Watching && s1.Length == 3 && fp.Apply("W") == DungeonStep.Ignored && fp.Apply(FirePattern.Ready) == DungeonStep.Ok && !fp.Watching, "patrón del fuego: arranca mirando una serie de 3; hasta «Listo» no se contesta");
    string wrong = new[] { "F", "W", "S" }.First(x => x != FirePattern.Counter(s1[0]));
    Check(fp.Apply(wrong) == DungeonStep.Mistake && fp.Watching && fp.Floor == 0, "un contraataque equivocado es un error y vuelve a mostrar la serie");
    bool grows = true; DungeonStep last = DungeonStep.Ok;
    for (int floor = 0; floor < FirePattern.Floors; floor++)
    {
        var series = fp.CurrentSeries.ToArray();
        grows &= series.Length == 3 + floor && series.Take(s1.Length).SequenceEqual(s1);
        fp.Apply(FirePattern.Ready);
        foreach (var attack in series) last = fp.Apply(FirePattern.Counter(attack));
    }
    Check(grows && last == DungeonStep.Solved && fp.Solved && fp.Buttons().Count == 0, "cada piso repite la serie anterior con un golpe más (3, 4, 5, 6) y contestar los 4 pisos la resuelve");
    Check(FirePattern.Counter("F") == "W" && FirePattern.Counter("W") == "S" && FirePattern.Counter("S") == "F", "💧 apaga 🔥, 🪨 frena 💧, 🔥 derrite 🪨");
}
{
    bool allOk = true; int traps = 0;
    for (int seed = 0; seed < 40; seed++)
    {
        var mz = new SmokeMaze(new Random(seed));
        var prev = new Dictionary<int, (int From, int Dir)>(); var q = new Queue<int>(); q.Enqueue(0); prev[0] = (-1, -1);
        while (q.Count > 0) { int c = q.Dequeue(); for (int d = 0; d < 4; d++) { if (!mz.IsOpen(c, d)) continue; int n = d switch { 0 => c - 5, 1 => c + 5, 2 => c - 1, _ => c + 1 }; if (prev.ContainsKey(n)) continue; prev[n] = (c, d); q.Enqueue(n); } }
        var dirs = new List<int>(); for (int c = mz.Exit; prev[c].From != -1; c = prev[c].From) dirs.Insert(0, prev[c].Dir);
        DungeonStep st = DungeonStep.Ok; bool mistake = false;
        foreach (int d in dirs) { st = mz.Apply(d switch { 0 => SmokeMaze.Up, 1 => SmokeMaze.Down, 2 => SmokeMaze.Left, _ => SmokeMaze.Right }); mistake |= st == DungeonStep.Mistake; }
        traps += Enumerable.Range(0, 25).Count(mz.IsTrap);
        allOk &= prev.Count == 25 && mz.Exit != 0 && dirs.Count == mz.ShortestPath && st == DungeonStep.Solved && !mistake && mz.StepLimit == mz.ShortestPath + SmokeMaze.StepSlack;
    }
    Check(allOk && traps > 40 && traps <= 40 * SmokeMaze.Traps, $"laberinto: en 40 sorteos llega a las 25 celdas, la salida nunca es la entrada y el camino corto la resuelve SIN pisar brasas (hasta 3 por laberinto, fuera del camino corto: {traps})");
    var mz2 = new SmokeMaze(new Random(1));
    int blocked = Enumerable.Range(0, 4).First(d => !mz2.CanMove(d));
    Check(mz2.Apply(blocked switch { 0 => SmokeMaze.Up, 1 => SmokeMaze.Down, 2 => SmokeMaze.Left, _ => SmokeMaze.Right }) == DungeonStep.Ignored && mz2.Steps == 0 && mz2.Buttons()[blocked].Disabled,
        "contra una pared no se camina (y esa flecha sale apagada)");
}
{
    bool Solvable(Embers e)
    {
        for (int mask = 0; mask < 1 << 16; mask++)
        {
            var lit = Enumerable.Range(0, 16).Select(e.IsLit).ToArray();
            for (int c = 0; c < 16; c++) if ((mask & (1 << c)) != 0) { int r = c / 4, k = c % 4; foreach (var (rr, kk) in new[] { (r, k), (r - 1, k), (r + 1, k), (r, k - 1), (r, k + 1) }) if (rr is >= 0 and < 4 && kk is >= 0 and < 4) lit[rr * 4 + kk] = !lit[rr * 4 + kk]; }
            if (lit.All(x => x)) return true;
        }
        return false;
    }
    var em = new Embers(new Random(5));
    bool startOk = !Enumerable.Range(0, 16).All(em.IsLit) && Solvable(em);
    var wind = DungeonStep.Ok; for (int i = 0; i < 3; i++) wind = em.Apply("e0");
    Check(startOk && (wind == DungeonStep.Mistake || em.Solved) && (em.Solved || Solvable(em)), "brasas: arranca desordenado pero con solución, el tercer toque trae el viento (quema) y el tablero sigue teniendo solución");
    Check(em.Buttons().Count == 16 && em.Buttons().All(b => b.Row is >= 0 and <= 3) && DungeonActions.Parse(DungeonKind.Embers, "B3") == "e9" && DungeonActions.Parse(DungeonKind.Embers, "e5") is null,
        "16 botones en 4 filas; «aa d b3» es la brasa de la columna B, fila 3");
}
{
    var lk = new FireLock(new Random(2));
    var code = lk.Code.ToArray();
    Check(FireLock.Score([0, 1, 2, 3], [0, 1, 2, 3]) == (4, 0) && FireLock.Score([0, 1, 2, 3], [1, 0, 3, 2]) == (0, 4) && FireLock.Score([0, 1, 2, 3], [0, 2, 4, 1]) == (1, 2), "candado: cuenta bien «en su lugar» y «en otro lugar»");
    var wrongGuess = Enumerable.Range(0, 5).Where(i => i != code[0]).Take(4).ToArray();
    foreach (int s in wrongGuess) lk.Apply($"s{s}");
    Check(lk.Apply(FireLock.Try) == DungeonStep.Mistake && lk.Attempts == 1, "un intento que no abre es un error");
    foreach (int s in code) lk.Apply($"s{s}");
    Check(lk.Apply(FireLock.Try) == DungeonStep.Solved && lk.Solved, "la clave abre el candado");
    var lk2 = new FireLock(new Random(9));
    int fails2 = 0;
    for (int t = 0; t < FireLock.MaxAttempts; t++) { int[] g = [0, 1, 2, 3]; if (g.SequenceEqual(lk2.Code)) g = [3, 2, 1, 0]; foreach (int s in g) lk2.Apply($"s{s}"); if (lk2.Apply(FireLock.Try) == DungeonStep.Mistake) fails2++; }
    Check(fails2 == FireLock.MaxAttempts && lk2.Failed && lk2.Apply("s0") == DungeonStep.Ignored, "con 8 intentos fallidos se traba y la mazmorra se pierde");
}
{
    var rh = new Rhythm(new Random(4));
    Check(rh.Cycle.Count == Rhythm.CycleLength && rh.Cycle.Distinct().Count() == 3, "ritmo: un ciclo de 5 con los tres golpes");
    string wrong = new[] { Rhythm.Flame, Rhythm.Axe, Rhythm.Gust }.First(a => a != rh.NextAttack);
    Check(rh.Apply(wrong) == DungeonStep.Mistake && rh.Streak == 0, "cubrirse del golpe equivocado es un error");
    DungeonStep st = DungeonStep.Ok; for (int i = 0; i < Rhythm.StreakToWin; i++) st = rh.Apply(rh.NextAttack);
    Check(st == DungeonStep.Solved && rh.Solved, "7 aciertos seguidos la resuelven");
}
Check(DungeonActions.Parse(DungeonKind.FirePattern, "Agua") == "W" && DungeonActions.Parse(DungeonKind.SmokeMaze, "izquierda") == "l" && DungeonActions.Parse(DungeonKind.Lock, "probar") == FireLock.Try && DungeonActions.Parse(DungeonKind.Rhythm, "ráfaga") == Rhythm.Gust && DungeonActions.Parse(DungeonKind.FirePattern, "banana") is null,
    "las jugadas escritas («aa d agua», «aa d izquierda», «aa d probar», «aa d ráfaga»)");

Console.WriteLine("\n== 6. La mazmorra de punta a punta (base real) ==");
var cooldowns = new CooldownRepository(factory);
var dungeonRepo = new DungeonRepository(factory);
var zoneBoxes = new ZoneBoxService(factory, zonesRepo);
var itemsRepo = new ItemRepository(factory);
var buffRepo = new BuffRepository(factory);
var bonusSvc = new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), new GameEventRepository(factory));
var combat = new CombatSessionService(users);
var raids = new RaidSessionService();
var starter = new AdventureCombatStarter(cooldowns, adventure, users, itemsRepo, monsters, zonesRepo, buffRepo, bonusSvc, combat, raids);
var gameEvents = new GameEventService(new GameEventRepository(factory));
DungeonService NewDungeons(TimeSpan? timeout = null) => new(users, zonesRepo, adventure, cooldowns, dungeonRepo, zoneBoxes, starter, combat, raids, gameEvents, bonusSvc)
{
    RandomFactory = () => new Random(7),
    Timeout = timeout ?? TimeSpan.FromMinutes(5),
};
var dungeons = NewDungeons();
var relic6 = (await dungeonRepo.GetRelicAsync(6))!;
Check(relic6.Name == "Reliquia de Sal" && (await dungeonRepo.GetRelicAsync(7))!.Name == "Reliquia de Humo" && await dungeonRepo.GetRelicAsync(5) is null, "cada zona de mazmorra tiene su Reliquia (la Zona 5, ninguna)");
async Task GiveRelic(int n, RelicInfo relic, int qty = 1) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@id, @item, @q) ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = Base + n, item = relic.ItemId, q = qty });
async Task<int> RelicCount(int n, RelicInfo relic) => await dungeonRepo.CountAsync(Id(n), relic.ItemId);
async Task<int> GoldOf(int n) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = Base + n });
async Task<bool> HasBossCooldown(int n) => await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM cooldowns WHERE discord_id = @id AND command_name = 'boss')", new { id = Base + n });
async Task<DungeonActResult> SolveFire(DungeonService svc, int n)
{
    var fp = (FirePattern)svc.Peek(Id(n))!.Puzzle;
    DungeonActResult r = null!;
    for (int floor = 0; floor < FirePattern.Floors; floor++)
    {
        var series = fp.CurrentSeries.ToArray();
        r = await svc.ActAsync(Id(n), null, FirePattern.Ready);
        foreach (var attack in series) r = await svc.ActAsync(Id(n), null, FirePattern.Counter(attack));
    }
    return r;
}

await Make(10, 35, 5, 6, 5);
var noRelic = await dungeons.StartAsync(Id(10), 1);
Check(noRelic.Status == DungeonStartStatus.NoRelic && noRelic.Relic?.Name == "Reliquia de Sal" && !await HasBossCooldown(10), "sin la Reliquia: la puerta la pide y no se cobra el cooldown");
var (relicMsg, _, _, _) = await DungeonModule.ExecuteStartAsync(dungeons, Id(10), 1);
Check(relicMsg is not null && relicMsg.Contains("Reliquia de Sal") && relicMsg.Contains("Cofre de Escoria"), "y el mensaje dice qué Reliquia y cómo se forja");
await GiveRelic(10, relic6);
var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => dungeons.StartAsync(Id(10), 1))));
Check(starts.Count(s => s.Status == DungeonStartStatus.Started) == 1 && dungeons.IsInDungeon(Id(10)) && await HasBossCooldown(10), "6 /boss a la vez: entra UNA vez y se cobra el cooldown del jefe");
Check(FightGuard.IsInFight(combat, raids, null, Id(10), dungeons) && !FightLock.IsAllowedInDungeon("use") && FightLock.IsAllowedInDungeon("dungeon") && FightLock.IsAllowedInDungeon("profile") && FightLock.IsAllowed("dungeon"),
    "adentro cuenta como pelea: no se puede comer (/use) y sí jugar con «aa d» o mirar el perfil");
var session10 = dungeons.Peek(Id(10))!;
Check((await dungeons.ActAsync(Id(10), "zzzzzzzz", FirePattern.Ready)).Step == DungeonStep.Ignored && ((FirePattern)session10.Puzzle).Watching, "un botón de otra corrida (otro código) no juega");
var runEmbed = DungeonModule.BuildRunEmbed(session10, null);
var runButtons = DungeonModule.BuildButtons(session10).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(AllText(runEmbed).Contains("patrón del fuego") && AllText(runEmbed).Contains("Piso 1 de 4") && runButtons.Count == 1 && runButtons[0].CustomId == $"dg:{Id(10)}:{session10.Token}:ready" && runEmbed.Length < 6000,
    "la pantalla: el acertijo, la vida y un botón con el dueño y la corrida en el id");
var won = await SolveFire(dungeons, 10);
Check(won.End == DungeonEnd.Won && won.Finish is { Won: true, RelicSpent: true } && await RelicCount(10, relic6) == 0 && !dungeons.IsInDungeon(Id(10)), "resolverla: gana, la Reliquia se gasta y la corrida termina");
var stats6 = AsadorScaling.StatsFor(6)!.Value;
Check(won.Asador is { FromDungeon: true, HealUsed: true, MonsterSpecialChancePercent: 20, BossZoneId: 0, CommandName: "boss" } a && a.MonsterMaxHp >= stats6.MinHp && a.MonsterMaxHp <= stats6.MaxHp && a.MonsterDamage >= stats6.MinDamage && a.MonsterDamage <= stats6.MaxDamage,
    "en la zona tope (FN 5 → la 6) sale directo el Asador: sin comida, con llamarada, más fuerte (vida y daño de la Zona 6) y su victoria es la de la puerta");
var endTop = AllText((await DungeonModule.RenderAsync(zonesRepo, won)).Embed);
Check(endTop.Contains("Asador") && endTop.Contains("Reliquia de Sal"), "el mensaje del final avisa que te espera el Asador");

await Make(11, 40, 8, 6, 5);
await GiveRelic(11, relic6);
int gold11 = await GoldOf(11);
var s11 = await dungeons.StartAsync(Id(11), 1);
var won11 = await SolveFire(dungeons, 11);
var u11 = (await users.GetByDiscordIdAsync(Id(11)))!;
Check(s11.Status == DungeonStartStatus.Started && won11.End == DungeonEnd.Won && won11.Asador is null && u11.HighestZoneCleared == 6 && won11.Gold == 40 * 560 && await GoldOf(11) == gold11 + 40 * 560 && won11.BoxName == "Arcón de Hierro",
    "FN 8 en la Zona 6 (no es la tope): ganar abre la 7, paga 40 cacerías de oro y una caja; no hay Asador");
var cd11 = await cooldowns.GetRemainingAsync(Id(11), CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Duration);
Check(cd11 is { TotalHours: > 9.9 }, "y completa el cooldown del jefe (10 h, como ganarle a un jefe)");
var (msg11, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(11), 7);
Check(msg11 is null && (await users.GetByDiscordIdAsync(Id(11)))!.CurrentZoneId == 7, "y ahora puede viajar a la Zona 7");

await Make(12, 34, 8, 6, 5);
await GiveRelic(12, relic6);
var low = await dungeons.StartAsync(Id(12), 1);
Check(low.Status == DungeonStartStatus.NotLeveled && low.RequiredLevel == 35 && !await HasBossCooldown(12), "para pasar a la Zona 7 hace falta su nivel (35), como con un jefe");

await Make(13, 35, 5, 6, 5);
await GiveRelic(13, relic6);
await dungeons.StartAsync(Id(13), 1);
var fp13 = (FirePattern)dungeons.Peek(Id(13))!.Puzzle;
DungeonActResult lost = null!;
for (int i = 0; i < 40 && dungeons.IsInDungeon(Id(13)); i++)
{
    lost = await dungeons.ActAsync(Id(13), null, FirePattern.Ready);
    string wrong = new[] { "F", "W", "S" }.First(x => x != FirePattern.Counter(fp13.CurrentSeries[0]));
    lost = await dungeons.ActAsync(Id(13), null, wrong);
}
var u13 = (await users.GetByDiscordIdAsync(Id(13)))!;
Check(lost.End == DungeonEnd.Lost && lost.Session!.Hp == 0 && await RelicCount(13, relic6) == 0 && u13.HighestZoneCleared == 5 && u13.CurrentHp == 0 && lost.Asador is null,
    "errar hasta quedarse sin vida: pierde, la Reliquia se gasta, la zona no sube y la vida queda en 0 (no hay Asador)");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*)::int FROM game_events WHERE discord_id = @id AND kind = 'dungeon_loss'", new { id = Base + 13 }) == 1, "queda el evento de la mazmorra perdida");
var endLost = AllText((await DungeonModule.RenderAsync(zonesRepo, lost)).Embed);
Check(endLost.Contains("Perdiste la") && endLost.Contains("Reliquia de Sal"), "el final dice que se perdió la Reliquia");

await Make(14, 35, 5, 6, 5);
await GiveRelic(14, relic6, 2);
var quick = NewDungeons(TimeSpan.FromMilliseconds(300));
var startQuick = await quick.StartAsync(Id(14), 1);
DungeonActResult? timedOut = null;
startQuick.Session!.OnTimeout = r => { timedOut = r; return Task.CompletedTask; };
await Task.Delay(1500);
Check(timedOut is { End: DungeonEnd.TimedOut } && !quick.IsInDungeon(Id(14)) && await RelicCount(14, relic6) == 1, "quedarse quieto: la corrida se pierde sola, avisa y gasta UNA Reliquia");

Console.WriteLine("\n== 7. El Asador desde la mazmorra y la puerta vieja ==");
await Make(15, 35, 5, 6, 5);
var fromDungeon = await starter.PrepareAsadorFromDungeonAsync(Id(15), 7, arrivalHp: 50);
var stats7 = AsadorScaling.StatsFor(7)!.Value;
Check(fromDungeon.State is { PlayerCurrentHp: 50, PlayerStartingHp: 50 } st15 && st15.MonsterMaxHp >= stats7.MinHp && st15.MonsterMaxHp <= stats7.MaxHp, "arranca con la vida con la que salió de la mazmorra (aunque haya comido afuera mientras tanto) y con los números de su zona tope");
Check(AsadorScaling.FlareDamage(100) == 175 && AsadorScaling.StatsFor(5) is null && AsadorScaling.StatsFor(11) is null, "la llamarada pega ×1,75; fuera de las zonas 6 a 10, el Asador de siempre");
await Make(16, 40, 5, 6, 5);
await db.ExecuteAsync("UPDATE users SET in_gate = true WHERE discord_id = @id", new { id = Base + 16 });
var oldGate = await starter.PrepareBossAsync(Id(16));
Check(oldGate.Status == CombatStartStatus.GateViaDungeon && !(await users.GetByDiscordIdAsync(Id(16)))!.InGate && !await HasBossCooldown(16), "parado en la puerta vieja con FN 5: /boss no pelea al Asador por el camino de antes, lo saca de la puerta y no cobra el cooldown");
var encounter = AllText(AdventureModule.BuildEncounterEmbed(fromDungeon.State!));
Check(encounter.Contains("no se come") && encounter.Contains("llamarada"), "la pelea avisa que no se come y que cuidado con la llamarada");
Check(AdventureModule.BuildCombatButtons(fromDungeon.State!, [new HealOption("Mate Amargo", 15, 3)]).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().All(m => m.IsDisabled == true),
    "y el desplegable de comida sale apagado");

Console.WriteLine("\n== 8. Ruteo con el InteractionService y el CommandService reales ==");
{
    var client = new Discord.WebSocket.DiscordSocketClient();
    var interactions = new Discord.Interactions.InteractionService(client.Rest);
    var anyStatic = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", anyStatic)!.Invoke(null, [client, interactions, cfg])!;
    await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
    IComponentInteraction Click(string id) => FakeProxy47.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy47.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
    var route = interactions.SearchComponentCommand(Click($"dg:{Id(1)}:abcd1234:W"));
    Check(route.IsSuccess && route.Command.MethodName == "HandleButtonAsync", "el botón «dg:{dueño}:{corrida}:{acción}» llega al handler de la mazmorra");
    var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
    texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
    var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
    var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
    Check(new[] { "dungeon", "dg", "mazmorra" }.All(n => textNames.Count(x => x == n) == 1) && textNames.Count(x => x == "d") == 1, "aa dungeon / aa dg / aa mazmorra registrados una sola vez, y «aa d» sigue siendo solo el del diario");
    Check(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IDungeonService>(sp) is DungeonService
        && Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, typeof(DungeonModule)) is not null
        && Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, typeof(AdventureModule)) is not null, "el contenedor arma el servicio de mazmorras y los módulos que lo usan");
}

for (int n = 1; n <= 16; n++) await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id;", new { id = Base + n });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

public class FakeProxy47 : System.Reflection.DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var v)) return v;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public static T Make<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = Create<T, FakeProxy47>();
        ((FakeProxy47)(object)proxy).Values = values;
        return proxy;
    }
}
