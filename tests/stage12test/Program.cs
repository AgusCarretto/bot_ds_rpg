using System.Collections.Concurrent;
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
ZoneBoxTable liveBoxes; await using (var czb = new NpgsqlConnection(conn)) { await czb.OpenAsync(); liveBoxes = await ZoneBoxQueries.LoadAsync(czb, null); }
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var arenaRepo = sp.GetRequiredService<IArenaRepository>();
var factory = sp.GetRequiredService<IDuelFighterFactory>();
var zonesRepo = sp.GetRequiredService<IZoneRepository>();
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const long BaseId = 999000000000012000L;
async Task Cleanup()
{
    await db.ExecuteAsync("DELETE FROM arena_days WHERE day < '2021-01-01'");
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
}

async Task MakeUser(int n, string cls = "Guerrero", int level = 1, int zone = 1)
{
    await users.GetOrCreateUserAsync((ulong)(BaseId + n));
    await db.ExecuteAsync("UPDATE users SET class = @c, level = @l, current_zone_id = @z, gold = 0, xp = 0 WHERE discord_id = @id", new { id = BaseId + n, c = cls, l = level, z = zone });
}

DuelFighter Fighter(ulong id, string name, string cls, int level, int weapon, int amulet)
{
    var passives = ClassPassives.For(cls);
    int damage = CombatStats.TotalAttack(level, weapon);
    int defense = CombatStats.TotalDefense(level, amulet);
    int maxHp = PvpTuning.MaxHp((int)Math.Round((100 + 15 * (level - 1)) * passives.MaxHpMultiplier), cls);
    return new DuelFighter(id, name, level, cls, new CombatantProfile(damage, defense, cls, passives), maxHp);
}

Console.WriteLine("== 1. Comandos y piezas ==");
await Cleanup();
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var slash = interactions.SlashCommands.ToList();
Check(slash.Any(c => c.Name == "fight" && c.Parameters.Count == 1), "/fight pide un jugador");
var arenaCmds = slash.Where(c => c.Module.SlashGroupName == "arena").Select(c => c.Name).OrderBy(x => x).ToList();
Check(arenaCmds.SequenceEqual(["join", "listplayers", "results"]), $"/arena tiene join, listplayers y results ({string.Join(", ", arenaCmds)})");
var comps = interactions.ComponentCommands.Select(c => c.Name).ToList();
Check(new[] { "duel_accept", "duel_decline", "duel_attack", "duel_ability", "duel_forfeit" }.All(p => comps.Any(c => c.StartsWith(p))), "los 5 botones del duelo tienen su handler");
Check(texts.Commands.Any(c => c.Name == "fight" && c.Aliases.Contains("duel") && c.Aliases.Contains("duelo")), "en texto: 'aa fight' (alias duel, duelo)");
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

var pJoin = await ParsesOk("arena join"); var pList = await ParsesOk("arena listplayers"); var pPlayers = await ParsesOk("arena players");
var pBare = await ParsesOk("arena"); var pRes = await ParsesOk("arena results"); var pFight = await ParsesOk("duelo");
Check(pJoin.SequenceEqual(["arena join"]) && pList.SequenceEqual(["arena listplayers"]) && pPlayers.SequenceEqual(["arena listplayers"]) && pBare.SequenceEqual(["arena listplayers"]) && pRes.SequenceEqual(["arena results"]),
    $"'aa arena join/listplayers/players/results' y 'aa arena' pelado parsean cada uno a UN comando ({string.Join("|", pJoin)} {string.Join("|", pList)} {string.Join("|", pPlayers)} {string.Join("|", pBare)} {string.Join("|", pRes)})");
Check(pFight.Count == 0, "'aa duelo' sin nombrar a nadie no parsea (pide el jugador)");
var broken = new List<string>();
foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && !x.IsSealed && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
{
    try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
}

Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
Check(sp.GetService<ArenaScheduler>() is not null && sp.GetService<IDuelService>() is not null && sp.GetService<IArenaService>() is not null, "el DI real tiene el reloj, el servicio de duelos y el de la Arena");

Console.WriteLine("\n== 2. Motor del duelo: reglas ==");
var plainA = Fighter(1, "Ana", "Guerrero", 10, 0, 0); var plainB = Fighter(2, "Beto", "Guerrero", 10, 0, 0);
var s0 = DuelEngine.Start(plainA, plainB, aStarts: true);
Check(s0.Actor.Fighter.Name == "Ana" && s0.Target.Fighter.Name == "Beto" && !s0.Finished && s0.A.Hp == plainA.MaxHp && s0.B.Hp == plainB.MaxHp, "arranca con la vida completa y empieza quien salió en la moneda");
var dmgs = new List<int>(); int dodges = 0;
for (int i = 0; i < 20000; i++) { var st = DuelEngine.Step(s0, PlayerAction.Attack); if (st.Event.Dodged) dodges++; else dmgs.Add(st.Event.Damage); }
// Ana: ataque 8+20=28, defensa de Beto 10 => efectivo 18; x0.9 del Guerrero; golpe 80-120%, crítico x2.
int effective = 28 - 10;
Check(dmgs.Min() >= (int)Math.Round(Math.Round(effective * 0.8) * 0.9) - 1 && dmgs.Max() <= (int)Math.Round(Math.Round(effective * 1.2) * 2 * 0.9) + 1, $"el daño sale del ataque MENOS la defensa del rival ({dmgs.Min()}..{dmgs.Max()} con ataque efectivo {effective})");
Check(Math.Abs(dodges / 20000.0 - 0.05) < 0.01, $"esquive base 5% ({dodges / 200.0:0.0}%)");
var afterOne = DuelEngine.Step(s0, PlayerAction.Attack).State;
Check(!afterOne.ATurn && afterOne.Actions == 1 && afterOne.A.Hp == plainA.MaxHp, "después de una acción el turno pasa al otro y el que pegó no pierde vida");
var tinyAttack = Fighter(3, "Debil", "Guerrero", 1, 0, 0) with { Profile = new CombatantProfile(5, 0, "Guerrero", ClassPassives.For("Guerrero")) };
var tank = Fighter(4, "Tanque", "Guerrero", 1, 0, 0) with { Profile = new CombatantProfile(5, 200, "Guerrero", ClassPassives.For("Guerrero")) };
var chipped = Enumerable.Range(0, 2000).Select(_ => DuelEngine.Step(DuelEngine.Start(tinyAttack, tank, true), PlayerAction.Attack).Event).Where(e => !e.Dodged).ToList();
Check(chipped.All(e => e.Damage == 1 || e.Damage == 2), "la defensa nunca anula el golpe: con ataque 5 contra defensa 200 el piso es 1 (2 si es crítico)");
bool threw = false; try { DuelEngine.Step(DuelEngine.Start(Fighter(5, "Ana", "Guerrero", 5, 0, 0), plainB, true) with { A = default! }, PlayerAction.Ability); } catch { threw = true; }
var onCd = DuelEngine.Step(DuelEngine.Start(plainA, plainB, true), PlayerAction.Ability).State;
bool cdThrew = false; var back = onCd with { ATurn = true }; try { DuelEngine.Step(back, PlayerAction.Ability); } catch (InvalidOperationException) { cdThrew = true; }
Check(DuelEngine.CheckAbility(back) == AbilityAvailability.OnCooldown && cdThrew, "pedir la habilidad en enfriamiento tira (el llamador tiene que chequear)");
var dead = DuelEngine.Start(plainA, plainB, true) with { B = DuelEngine.Start(plainA, plainB, true).B with { Hp = 0 } };
bool deadThrew = false; try { DuelEngine.Step(dead, PlayerAction.Attack); } catch (InvalidOperationException) { deadThrew = true; }
Check(dead.Finished && ReferenceEquals(dead.Winner, dead.A) && deadThrew, "un duelo terminado tiene ganador y no admite más acciones");

Console.WriteLine("\n== 3. Habilidades en PvP (mismo efecto que contra monstruos, aun contra defensa) ==");
double AvgDamage(DuelFighter attacker, DuelFighter target, PlayerAction action, int n = 40000)
{
    // Daño medio por uso sobre un objetivo que nunca esquiva (así el cociente no depende del 5% de esquive).
    var noDodge = target with { Profile = target.Profile with { Passives = target.Profile.Passives with { DodgeChanceBonus = -1.0 } } };
    var st = DuelEngine.Start(attacker, noDodge, true);
    long total = 0;
    for (int i = 0; i < n; i++) total += DuelEngine.Step(st, action).Event.Damage;
    return total / (double)n;
}

var wall = Fighter(10, "Muro", "Arquero", 20, 40, 60);
var hech = Fighter(11, "Hechi", "Hechicero", 20, 40, 0); var arq = Fighter(12, "Arq", "Arquero", 20, 40, 0); var gue = Fighter(13, "Gue", "Guerrero", 20, 40, 0);
double hBasic = AvgDamage(hech, wall, PlayerAction.Attack), hAbil = AvgDamage(hech, wall, PlayerAction.Ability);
double aBasic = AvgDamage(arq, wall, PlayerAction.Attack), aAbil = AvgDamage(arq, wall, PlayerAction.Ability);
double gBasic = AvgDamage(gue, wall, PlayerAction.Attack), gAbil = AvgDamage(gue, wall, PlayerAction.Ability);
Check(Math.Abs(hAbil / hBasic - 2.2) < 0.1, $"Bola de Fuego pega 2,2x el golpe común aun contra defensa alta (cociente {hAbil / hBasic:0.00}; restar la defensa al final daría ~4x)");
Check(Math.Abs(aAbil / aBasic - 1.8) < 0.12, $"Lluvia de Flechas pega 1,8x ({aAbil / aBasic:0.00})");
Check(Math.Abs(gAbil / gBasic - 0.7) < 0.06, $"Aguante pega 0,7x ({gAbil / gBasic:0.00})");

// Aguante: el que lo usa recibe menos durante exactamente 2 golpes.
var guerrero = Fighter(20, "Guerrero", "Guerrero", 20, 0, 0); var rival = Fighter(21, "Rival", "Guerrero", 20, 40, 0);
var st1 = DuelEngine.Step(DuelEngine.Start(guerrero, rival, true), PlayerAction.Ability).State;
Check(st1.A.Status.CooldownRemaining == 3 && st1.A.Status.EffectTurnsRemaining == 2 && !st1.ATurn, "Aguante: arranca el enfriamiento (3) y el efecto (2 golpes recibidos)");
var hit1 = DuelEngine.Step(st1, PlayerAction.Attack); var st2 = hit1.State;
var back1 = DuelEngine.Step(st2, PlayerAction.Attack); var hit2 = DuelEngine.Step(back1.State, PlayerAction.Attack).State; // rival pega, Guerrero pega, rival pega
var st3 = DuelEngine.Step(hit2, PlayerAction.Attack).State; var st4 = DuelEngine.Step(st3, PlayerAction.Attack).State;
Check(st2.A.Status.EffectTurnsRemaining is 1 or 2 && hit2.A.Status.EffectTurnsRemaining == 0, $"el efecto se gasta con los golpes que recibe, no con el tiempo ({st1.A.Status.EffectTurnsRemaining} -> {st2.A.Status.EffectTurnsRemaining} -> {hit2.A.Status.EffectTurnsRemaining})");

// Sombra: no ataca, el rival no recibe nada, y después el ataque es una emboscada crítica.
var ninja = Fighter(30, "Ninja", "Ninja", 20, 40, 0);
var sh = DuelEngine.Step(DuelEngine.Start(ninja, rival, true), PlayerAction.Ability);
Check(!sh.Event.Attacked && sh.Event.Damage == 0 && sh.State.B.Hp == rival.MaxHp && sh.State.A.Status.EffectTurnsRemaining == 2, "Sombra: no pega, el rival queda intacto y el Ninja queda invisible 2 golpes");
int dodgedShadow = 0, tries = 8000;
for (int i = 0; i < tries; i++) { var r = DuelEngine.Step(sh.State, PlayerAction.Attack); if (r.Event.Dodged) dodgedShadow++; }
Check(Math.Abs(dodgedShadow / (double)tries - 0.75) < 0.02, $"contra un Ninja escondido el golpe se esquiva ~75% ({dodgedShadow * 100.0 / tries:0.0}%)");
int ambushes = 0;
for (int i = 0; i < 500; i++) { var afterHide = sh.State with { ATurn = true }; var e = DuelEngine.Step(afterHide, PlayerAction.Attack).Event; if (e.Ambush && !e.Dodged && e.Crits == 1) ambushes++; else if (e.Dodged) ambushes++; }
Check(ambushes == 500, "el primer ataque desde las sombras es siempre emboscada crítica (o el rival lo esquiva)");

// Sifón: cura sobre lo que de verdad entró, nunca sobre un golpe esquivado.
var mage = Fighter(40, "Mago", "Hechicero", 20, 40, 0) with { MaxHp = 400 };
var ghost = Fighter(41, "Fantasma", "Guerrero", 20, 0, 0); var ghostDodge = ghost with { Profile = ghost.Profile with { Passives = ghost.Profile.Passives with { DodgeChanceBonus = 5.0 } } };
var hurtMage = DuelEngine.Start(mage, ghostDodge, true) with { A = DuelEngine.Start(mage, ghostDodge, true).A with { Hp = 100 } };
int healsOnDodge = Enumerable.Range(0, 3000).Count(_ => DuelEngine.Step(hurtMage, PlayerAction.Attack).Event.Heal > 0);
var realTarget = Fighter(42, "Real", "Guerrero", 20, 0, 0) with { Profile = Fighter(42, "Real", "Guerrero", 20, 0, 0).Profile with { Passives = ClassPassives.For("Guerrero") with { DodgeChanceBonus = -1.0 } } };
var hurtMage2 = DuelEngine.Start(mage, realTarget, true) with { A = DuelEngine.Start(mage, realTarget, true).A with { Hp = 100 } };
int healed = Enumerable.Range(0, 6000).Count(_ => DuelEngine.Step(hurtMage2, PlayerAction.Attack).Event.Heal > 0);
Check(healsOnDodge == 0 && Math.Abs(healed / 6000.0 - 0.30) < 0.03, $"Sifón de Almas: 0 curas con golpes esquivados; ~30% con golpes que entran ({healed / 60.0:0.0}%)");
var capped = Enumerable.Range(0, 400).Select(_ => DuelEngine.Step(DuelEngine.Start(mage, realTarget, true) with { A = DuelEngine.Start(mage, realTarget, true).A with { Hp = 399 } }, PlayerAction.Attack).State.A.Hp).Max();
Check(capped <= 400, "la vida nunca pasa del máximo por curarse");

Console.WriteLine("\n== 4. Peleas completas ==");
var sims = Enumerable.Range(0, 3000).Select(_ => DuelEngine.Simulate(Fighter(50, "A", "Guerrero", 15, 20, 10), Fighter(51, "B", "Guerrero", 15, 20, 10))).ToList();
double winsA = sims.Count(r => r.Winner.Fighter.DiscordId == 50) / (double)sims.Count;
Check(sims.All(r => r.Actions <= DuelEngine.MaxActions && r.Winner.Hp >= 0 && (r.Capped || r.Loser.Hp <= 0)) && Math.Abs(winsA - 0.5) < 0.04, $"espejo: siempre termina, nadie se va de rango y ~50% cada uno ({winsA * 100:0.0}%); promedio {sims.Average(r => r.Actions):0.0} acciones");
var stall = Simulate(Fighter(60, "Chico", "Guerrero", 1, 0, 0) with { Profile = new CombatantProfile(1, 500, "Guerrero", ClassPassives.For("Guerrero")), MaxHp = 5000 }, Fighter(61, "Otro", "Guerrero", 1, 0, 0) with { Profile = new CombatantProfile(1, 500, "Guerrero", ClassPassives.For("Guerrero")), MaxHp = 5000 });
Check(stall.Capped && stall.Actions == DuelEngine.MaxActions, $"dos tanques que no se pueden herir terminan en el tope de {DuelEngine.MaxActions} acciones (gana por vida; ganó {stall.Winner.Fighter.Name})");
static DuelResult Simulate(DuelFighter a, DuelFighter b) => DuelEngine.Simulate(a, b);

Console.WriteLine("   Balance entre clases (nivel 15, arma +20, amuleto +10; 4000 duelos por par; fila le gana a columna):");
string[] classes = ["Guerrero", "Ninja", "Arquero", "Hechicero"];
Console.WriteLine("            " + string.Join("  ", classes.Select(c => c.PadRight(9))));
double worst = 0.5, best = 0.5; double avgLen = 0; int lenN = 0;
foreach (var cls1 in classes)
{
    var row = new List<string>();
    foreach (var cls2 in classes)
    {
        int w = 0; const int N = 4000;
        for (int i = 0; i < N; i++)
        {
            var r = DuelEngine.Simulate(Fighter(70, cls1, cls1, 15, 20, 10), Fighter(71, cls2, cls2, 15, 20, 10));
            if (r.Winner.Fighter.DiscordId == 70) w++;
            avgLen += r.Actions; lenN++;
        }
        double rate = w / (double)N;
        if (cls1 != cls2) { worst = Math.Min(worst, rate); best = Math.Max(best, rate); }
        row.Add($"{rate * 100,6:0.0}%   ");
    }

    Console.WriteLine($"   {cls1,-10}{string.Join(" ", row)}");
}

Console.WriteLine($"   duelo medio: {avgLen / lenN:0.0} acciones (entre los dos)");
Check(best < 0.70 && worst > 0.30, $"ninguna clase le gana a otra por más de 70/30 (peor {worst * 100:0.0}%, mejor {best * 100:0.0}%)");

Console.WriteLine("\n== 5. La llave (pura) ==");
var okBracket = true; string why = "";
for (int n = 2; n <= 32; n++)
{
    var entrants = Enumerable.Range(1, n).Select(i => new ArenaEntrant((ulong)i, $"P{i}")).ToList();
    var result = ArenaBracket.Run(entrants, new Random(n), (a, b) => new ArenaFightOutcome(Math.Min(a.DiscordId, b.DiscordId), 1));
    int rounds = ArenaBracket.RoundsFor(n); int size = 1 << rounds; int byes = size - n;
    int real = result.Matches.Count(m => m.P2 is not null); int byeMatches = result.Matches.Count(m => m.P2 is null);
    bool first = result.Matches.Where(m => m.Round == 1).SelectMany(m => m.P2 is null ? [m.P1.DiscordId] : new[] { m.P1.DiscordId, m.P2.DiscordId }).OrderBy(x => x).SequenceEqual(entrants.Select(e => e.DiscordId));
    bool progress = Enumerable.Range(2, Math.Max(0, rounds - 1)).All(r =>
        result.Matches.Where(m => m.Round == r).SelectMany(m => new[] { m.P1.DiscordId, m.P2!.DiscordId }).OrderBy(x => x)
            .SequenceEqual(result.Matches.Where(m => m.Round == r - 1).Select(m => m.WinnerId).OrderBy(x => x)));
    bool good = real == n - 1 && byeMatches == byes && result.Rounds == rounds && result.Champion.DiscordId == 1 && first && progress && byes <= size / 2;
    if (!good) { okBracket = false; why += $" n={n}(real {real}, byes {byeMatches}/{byes}, campeón {result.Champion.DiscordId}, r1 {first}, avance {progress})"; }
}

Check(okBracket, "de 2 a 32 anotados: N-1 peleas reales, los pasa-directo completan la llave, todos juegan la primera ronda una vez, cada ronda se arma con los ganadores de la anterior y gana el mejor" + why);
Check(Enumerable.Range(0, 2000).All(i => { var e = Enumerable.Range(1, 5 + i % 20).Select(k => new ArenaEntrant((ulong)k, "x")).ToList(); var r = ArenaBracket.Run(e, new Random(i), (a, b) => new ArenaFightOutcome(Random.Shared.Next(2) == 0 ? a.DiscordId : b.DiscordId, 2)); return e.Any(x => x.DiscordId == r.Champion.DiscordId); }), "con peleas al azar siempre sale un campeón que estaba anotado");
bool oneThrew = false; try { ArenaBracket.Run([new ArenaEntrant(1, "solo")], new Random(1), (a, b) => new ArenaFightOutcome(a.DiscordId, 1)); } catch (ArgumentException) { oneThrew = true; }
bool badThrew = false; try { ArenaBracket.Run([new ArenaEntrant(1, "a"), new ArenaEntrant(2, "b")], new Random(1), (a, b) => new ArenaFightOutcome(99, 1)); } catch (InvalidOperationException) { badThrew = true; }
Check(oneThrew && badThrew, "un solo anotado no es torneo, y una pelea que devuelve un ganador que no peleó se rechaza");
var seedsDiffer = Enumerable.Range(0, 30).Select(i => ArenaBracket.Run(Enumerable.Range(1, 8).Select(k => new ArenaEntrant((ulong)k, "x")).ToList(), new Random(i), (a, b) => new ArenaFightOutcome(a.DiscordId, 1)).Matches.First().P1.DiscordId).Distinct().Count();
Check(seedsDiffer > 3, $"el sorteo cambia el cruce ({seedsDiffer} primeros distintos en 30 sorteos)");
Check(ArenaBracket.RoundName(5, 5) == "🏆 Final" && ArenaBracket.RoundName(4, 5) == "Semifinal" && ArenaBracket.RoundName(3, 5) == "Cuartos de final" && ArenaBracket.RoundName(2, 5) == "Octavos de final" && ArenaBracket.RoundName(1, 5) == "Ronda 1", "nombres de ronda");

Console.WriteLine("\n== 6. Día de Uruguay y premio ==");
Check(ArenaRules.DayOf(new DateTime(2026, 10, 2, 2, 59, 59, DateTimeKind.Utc)) == new DateOnly(2026, 10, 1) && ArenaRules.DayOf(new DateTime(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc)) == new DateOnly(2026, 10, 2), "a las 02:59:59 UTC todavía es el día anterior en Uruguay; a las 03:00:00 UTC ya es el nuevo");
Check(ArenaRules.PlaysAtUtc(new DateOnly(2026, 10, 1)) == new DateTime(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc), "el torneo del 01/10 se juega a las 03:00 UTC del 02/10 (00:00 de Uruguay)");
var rewards = Enumerable.Range(1, 5).Select(r => MissionRewards.Resolve(ArenaRules.ChampionReward, r, 10, liveBoxes)).ToList();
Console.WriteLine("   premio del campeón por zona (nivel 10): " + string.Join(" | ", rewards.Select(MissionRewards.Describe)));
Check(rewards[0].Gold == 210 && rewards[4].Gold == 5625 && rewards.All(r => r.BoxName is not null) && rewards.Zip(rewards.Skip(1), (a, b) => b.Gold > a.Gold).All(x => x), "premio: 15 cacerías de oro de su zona (210 .. 5625) y una caja, creciendo con la zona");

Console.WriteLine("\n== 7. El servicio de duelos ==");
var events = new FakeEvents();
DuelService.TurnTimeout = TimeSpan.FromSeconds(30);
var duels = new DuelService(events);
var c1x = duels.CreateChallenge(1, "Ana", 2, "Beto"); var c2x = duels.CreateChallenge(1, "Ana", 3, "Caro");
Check(duels.PeekChallenge(c1x.Id) is null && duels.PeekChallenge(c2x.Id) is { TargetName: "Caro" }, "un jugador tiene UN desafío abierto: el nuevo reemplaza al anterior");
int takers = 0; Parallel.For(0, 20, _ => { if (duels.TryTakeChallenge(c2x.Id) is not null) Interlocked.Increment(ref takers); });
Check(takers == 1 && duels.PeekChallenge(c2x.Id) is null, "de 20 clicks a la vez en 'Aceptar' solo uno recibe el desafío");

DuelRenderer render = s => DuelModule.Render(s);
var target = new FakeTarget();
var fa = Fighter(101, "Ana", "Guerrero", 10, 0, 0); var fb = Fighter(102, "Beto", "Ninja", 10, 0, 0); var fc = Fighter(103, "Caro", "Arquero", 10, 0, 0);
var ses = duels.TryStart(fa, fb, target, render)!;
Check(ses is not null && duels.IsInDuel(101) && duels.IsInDuel(102) && duels.Find(ses.Id) == ses, "arranca el duelo y los dos quedan adentro");
Check(duels.TryStart(fc, fa, target, render) is null && !duels.IsInDuel(103), "alguien que ya está en un duelo no entra a otro (y el otro tampoco queda trabado)");
ulong firstId = ses.State.Actor.Fighter.DiscordId; ulong secondId = firstId == 101 ? 102UL : 101UL;
Check(duels.TryAct(ses.Id, secondId, PlayerAction.Attack, target).Status == DuelActStatus.NotYourTurn, "pegar sin que sea tu turno se rechaza");
Check(duels.TryAct(ses.Id, 999, PlayerAction.Attack, target).Status == DuelActStatus.NotParticipant && duels.TryAct(Guid.NewGuid(), firstId, PlayerAction.Attack, target).Status == DuelActStatus.NotFound, "un tercero, o un duelo inexistente, se rechazan");
var okAct = duels.TryAct(ses.Id, firstId, PlayerAction.Attack, target);
Check(okAct.Status == DuelActStatus.Ok && ses.State.Actions == 1 && ses.Log.Count == 1 && ses.State.Actor.Fighter.DiscordId == secondId, "una acción válida avanza el turno y deja una línea de registro");
Check(duels.TryAct(ses.Id, firstId, PlayerAction.Attack, target).Status == DuelActStatus.NotYourTurn, "el doble click del mismo jugador no pega dos veces");
// Habilidad en enfriamiento
var cdSes = duels.TryStart(Fighter(111, "Uno", "Hechicero", 10, 0, 0), Fighter(112, "Dos", "Guerrero", 10, 0, 0), target, render)!;
ulong cdFirst = cdSes.State.Actor.Fighter.DiscordId; ulong cdSecond = cdFirst == 111 ? 112UL : 111UL;
string cdFirstClass = cdSes.State.Actor.Fighter.PlayerClass;
Check(duels.TryAct(cdSes.Id, cdFirst, PlayerAction.Ability, target).Status == DuelActStatus.Ok, "usar la habilidad lista funciona");
duels.TryAct(cdSes.Id, cdSecond, PlayerAction.Attack, target);
Check(duels.TryAct(cdSes.Id, cdFirst, PlayerAction.Ability, target).Status == DuelActStatus.AbilityUnavailable, "la misma habilidad enseguida está en enfriamiento");
Check(duels.TryForfeit(cdSes.Id, 999, target).Status == DuelActStatus.NotParticipant, "rendirse es solo de los dos");
var ff = duels.TryForfeit(cdSes.Id, cdSecond, target);
Check(ff.Status == DuelActStatus.Ok && cdSes.End == DuelEnd.Forfeit && cdSes.Winner!.Fighter.DiscordId == cdFirst && !duels.IsInDuel(111) && !duels.IsInDuel(112), "rendirse termina el duelo, gana el rival y los dos quedan libres");
await Task.Delay(150);
Check(events.Kinds(cdFirst, GameEventKinds.DuelWin) == 1 && events.Kinds(cdSecond, GameEventKinds.DuelLoss) == 1, "se registra una victoria y una derrota");
Check(duels.TryForfeit(cdSes.Id, cdFirst, target).Status is DuelActStatus.NotFound or DuelActStatus.AlreadyOver, "rendirse dos veces no hace nada");

// Concurrencia: los dos apretando a la vez hasta que alguien gana.
int bad = 0; int endedTwice = 0;
for (int round = 0; round < 60; round++)
{
    var ev2 = new FakeEvents(); var svc = new DuelService(ev2);
    var s = svc.TryStart(Fighter(201, "P1", "Guerrero", 8, 0, 0), Fighter(202, "P2", "Guerrero", 8, 0, 0), new FakeTarget(), render)!;
    int okActions = 0;
    var tasks = new[] { 201UL, 202UL }.Select(id => Task.Run(() =>
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            var r = svc.TryAct(s.Id, id, PlayerAction.Attack, new FakeTarget());
            if (r.Status == DuelActStatus.Ok) Interlocked.Increment(ref okActions);
            else if (r.Status is DuelActStatus.NotFound or DuelActStatus.AlreadyOver) break;
            else Thread.Yield();
        }
    })).ToArray();
    Task.WaitAll(tasks);
    await Task.Delay(30);
    if (s.End != DuelEnd.Knockout || okActions != s.State.Actions || svc.IsInDuel(201) || svc.IsInDuel(202) || s.State.A.Hp < 0 || s.State.B.Hp < 0 || (s.State.A.Hp > 0 && s.State.B.Hp > 0)) bad++;
    if (ev2.Kinds(201, GameEventKinds.DuelWin) + ev2.Kinds(202, GameEventKinds.DuelWin) != 1 || ev2.Kinds(201, GameEventKinds.DuelLoss) + ev2.Kinds(202, GameEventKinds.DuelLoss) != 1) endedTwice++;
}

Check(bad == 0, "60 duelos con los dos jugadores apretando a la vez: cada acción válida se cuenta una vez, el duelo termina con un solo caído y nadie queda trabado");
Check(endedTwice == 0, "y el resultado se registra exactamente una vez por duelo");

// Timeout del turno
DuelService.TurnTimeout = TimeSpan.FromMilliseconds(400);
var svcT = new DuelService(events); var tgt = new FakeTarget();
var tSes = svcT.TryStart(Fighter(301, "Dormido", "Guerrero", 8, 0, 0), Fighter(302, "Despierto", "Guerrero", 8, 0, 0), tgt, render)!;
ulong sleeper = tSes.State.Actor.Fighter.DiscordId;
await Task.Delay(900);
Check(tSes.End == DuelEnd.TimedOut && tSes.EndedBy == sleeper && tSes.Winner!.Fighter.DiscordId != sleeper && !svcT.IsInDuel(301) && !svcT.IsInDuel(302), "si el que tiene el turno no juega a tiempo, pierde y los dos quedan libres");
Check(tgt.Updates == 1 && tgt.LastEmbed!.Title.Contains("ganó el duelo") && tgt.LastComponents!.Components.Count == 0, "el mensaje se edita solo, sin botones, diciendo quién ganó");
var tSes2 = svcT.TryStart(Fighter(311, "A", "Guerrero", 8, 0, 0), Fighter(312, "B", "Guerrero", 8, 0, 0), new FakeTarget(), render)!;
for (int i = 0; i < 4; i++) { await Task.Delay(250); var r = svcT.TryAct(tSes2.Id, tSes2.State.Actor.Fighter.DiscordId, PlayerAction.Attack, new FakeTarget()); if (r.Status != DuelActStatus.Ok) break; }
Check(tSes2.End == DuelEnd.None || tSes2.End == DuelEnd.Knockout, "jugar a tiempo reinicia el reloj: cuatro acciones separadas por 250 ms con un tope de 400 ms no vencen");
svcT.TryForfeit(tSes2.Id, 311, new FakeTarget());
// Carrera entre el click y el vencimiento: nunca se cierra dos veces.
int raced = 0;
for (int i = 0; i < 25; i++)
{
    DuelService.TurnTimeout = TimeSpan.FromMilliseconds(40);
    var evr = new FakeEvents(); var sv = new DuelService(evr); var rt = new FakeTarget();
    var rs = sv.TryStart(Fighter(321, "A", "Guerrero", 8, 0, 0), Fighter(322, "B", "Guerrero", 8, 0, 0), rt, render)!;
    await Task.Delay(35 + i % 10);
    sv.TryAct(rs.Id, rs.State.Actor.Fighter.DiscordId, PlayerAction.Attack, rt);
    await Task.Delay(200);
    sv.TryForfeit(rs.Id, 321, rt);
    await Task.Delay(50);
    if (evr.Kinds(321, GameEventKinds.DuelWin) + evr.Kinds(322, GameEventKinds.DuelWin) > 1 || sv.IsInDuel(321) || sv.IsInDuel(322)) raced++;
}

Check(raced == 0, "25 carreras entre un click y el vencimiento: nunca queda un duelo abierto ni se registra un ganador de más");
DuelService.TurnTimeout = TimeSpan.FromSeconds(60);

Console.WriteLine("\n== 8. Los mensajes del duelo ==");
var rSes = new DuelService(new FakeEvents());
var rr = rSes.TryStart(Fighter(401, "Ana", "Hechicero", 10, 0, 0), Fighter(402, "Beto", "Ninja", 10, 0, 0), new FakeTarget(), render)!;
var (e0, c0) = DuelModule.Render(rr);
var rows = c0.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
var actorClass = rr.State.Actor.Fighter.PlayerClass;
Check(rows.Count == 3 && rows[0].CustomId == $"duel_attack:{rr.Id}" && rows[1].CustomId == $"duel_ability:{rr.Id}" && rows[2].CustomId == $"duel_forfeit:{rr.Id}" && rows[1].Label == (actorClass == "Hechicero" ? "Bola de Fuego" : "Sombra") && !rows[1].IsDisabled, $"en curso: Atacar, la habilidad DE QUIEN TIENE EL TURNO ({rows[1].Label}) y Rendirse");
Check(e0.Fields.Length == 2 && e0.Fields.All(f => f.Inline && f.Value.Contains("█") || f.Value.Contains("░")) && e0.Description.Contains($"<@{rr.State.Actor.Fighter.DiscordId}>") && e0.Length < 6000, "dos columnas con la barra de vida de cada uno y a quién le toca");
rr.State.Actor.Fighter.ToString();
rSes.TryAct(rr.Id, rr.State.Actor.Fighter.DiscordId, PlayerAction.Ability, new FakeTarget());
var (e1, c1) = DuelModule.Render(rr);
var btn1 = c1.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(btn1.Count == 3 && e1.Description.Contains("**") && btn1[1].CustomId.StartsWith("duel_ability"), "después de actuar los botones pasan al otro jugador");
rSes.TryForfeit(rr.Id, 401, new FakeTarget());
var (e2, c2) = DuelModule.Render(rr);
Check(e2.Title.Contains("ganó el duelo") && c2.Components.Count == 0 && e2.Fields.Any(f => f.Name.Contains("Resumen")) && e2.Footer!.Value.Text.Contains("amistoso") && e2.Description.Contains("se rindió"), "terminado: título con el ganador, resumen, sin botones, aclara que es amistoso");
Console.WriteLine("   " + e2.Title + " | " + e2.Description.Replace("\n", " / ").Substring(0, Math.Min(150, e2.Description.Length)));

var pUsers = sp.GetRequiredService<IUserRepository>();
await MakeUser(1, "Guerrero", 5); await MakeUser(2, "Ninja", 7);
var duelSvc = new DuelService(new FakeEvents());
var uA = FakeUser(BaseId + 1, "Ana", false); var uB = FakeUser(BaseId + 2, "Beto", false); var uBot = FakeUser(BaseId + 3, "Robot", true); var uGhost = FakeUser(BaseId + 4, "Fantasma", false);
var chSelf = await DuelModule.ExecuteChallengeAsync(pUsers, duelSvc, uA, uA);
var chBot = await DuelModule.ExecuteChallengeAsync(pUsers, duelSvc, uA, uBot);
var chGhost = await DuelModule.ExecuteChallengeAsync(pUsers, duelSvc, uA, uGhost);
var chNoAcc = await DuelModule.ExecuteChallengeAsync(pUsers, duelSvc, uGhost, uA);
var chOk = await DuelModule.ExecuteChallengeAsync(pUsers, duelSvc, uA, uB);
Check(chSelf.PlainMessage!.Contains("vos mismo") && chBot.PlainMessage!.Contains("bots") && chGhost.PlainMessage!.Contains("todavía no empezó") && chNoAcc.PlainMessage!.Contains("/start"), "no se puede desafiar a uno mismo, a un bot ni a alguien sin cuenta; sin cuenta propia manda a /start");
var btns = chOk.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(chOk.Embed is not null && chOk.Content == $"<@{BaseId + 2}>" && btns.Count == 2 && btns[0].CustomId!.StartsWith("duel_accept:") && btns[1].CustomId!.StartsWith("duel_decline:") && chOk.Embed.Fields.Length == 2 && chOk.Embed.Description.Contains("amistoso"), "el desafío menciona al desafiado, muestra a los dos (clase y nivel) con Aceptar/Rechazar y aclara que es amistoso");
Console.WriteLine("   " + string.Join(" | ", chOk.Embed!.Fields.Select(f => f.Name + ": " + f.Value)));
var chBusy = new DuelService(new FakeEvents()); chBusy.TryStart(Fighter((ulong)(BaseId + 2), "Beto", "Ninja", 7, 0, 0), Fighter(999, "Otro", "Guerrero", 3, 0, 0), new FakeTarget(), render);
var chTargetBusy = await DuelModule.ExecuteChallengeAsync(pUsers, chBusy, uA, uB);
var chMeBusy = await DuelModule.ExecuteChallengeAsync(pUsers, chBusy, uB, uA);
Check(chTargetBusy.PlainMessage!.Contains("pleno duelo") && chMeBusy.PlainMessage!.Contains("Ya estás en un duelo"), "si alguno ya está en un duelo, no se arma otro");

Console.WriteLine("\n== 9. La arena contra la base real (días sintéticos de 2020) ==");
var events2 = new FakeEvents();
var arena = new ArenaService(arenaRepo, factory, pUsers, zonesRepo, events2);
var day1 = new DateOnly(2020, 3, 1);
for (int n = 10; n <= 12; n++) await MakeUser(n, n % 2 == 0 ? "Guerrero" : "Arquero", 5);
await MakeUser(13, "Ninja", 5); await MakeUser(14, "Hechicero", 5);
var j1 = await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 10), "Uno", 555, ArenaRules.MaxPlayers);
var j1b = await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 10), "Uno", 555, ArenaRules.MaxPlayers);
var jGhost = await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 990), "Nadie", 555, ArenaRules.MaxPlayers);
Check(j1 == ArenaJoinStatus.Joined && j1b == ArenaJoinStatus.AlreadyJoined && jGhost == ArenaJoinStatus.NoAccount, "anotarse: la primera vale, la segunda dice 'ya estás', y sin cuenta no entra");
await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 11), "Dos", 777, ArenaRules.MaxPlayers);
var dayInfo = await arenaRepo.GetDayAsync(day1);
Check(dayInfo is { Status: "open", ChannelId: 555 }, "el día queda abierto y guarda el canal del PRIMERO que se anotó (para anunciar ahí)");
var ents = await arenaRepo.GetEntriesAsync(day1);
Check(ents.Count == 2 && ents[0].DisplayName == "Uno" && ents[0].PlayerClass == "Guerrero" && ents[0].Level == 5 && ents[1].DiscordId == (ulong)(BaseId + 11), "los anotados salen en orden de llegada con su clase y nivel");

// Cupo: 40 a la vez, 32 entran (usa otro día)
var capDay = new DateOnly(2020, 3, 2);
for (int n = 100; n < 140; n++) await MakeUser(n, "Guerrero", 3);
var joins = await Task.WhenAll(Enumerable.Range(100, 40).Select(n => Task.Run(() => arenaRepo.JoinAsync(capDay, (ulong)(BaseId + n), $"P{n}", 1, ArenaRules.MaxPlayers))));
Check(joins.Count(j => j == ArenaJoinStatus.Joined) == 32 && joins.Count(j => j == ArenaJoinStatus.Full) == 8 && (await arenaRepo.GetEntriesAsync(capDay)).Count == 32, $"40 personas anotándose a la vez: entran exactamente {ArenaRules.MaxPlayers} y 8 reciben 'lleno'");

// Resolver un torneo de 4
await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 12), "Tres", 777, ArenaRules.MaxPlayers); await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 13), "Cuatro", 777, ArenaRules.MaxPlayers);
long goldBefore = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(gold),0) FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId + 10, b = BaseId + 13 });
var res = await arena.ResolveDayAsync(day1);
Check(res is { Cancelled: false } && res.Participants == 4 && res.Rounds == 2 && res.Matches.Count == 3 && res.WinnerId is not null && res.ChannelId == 555, "un torneo de 4: 2 rondas, 3 peleas, campeón y el canal del primero");
var champId = (long)res!.WinnerId!.Value;
long goldAfter = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(gold),0) FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId + 10, b = BaseId + 13 });
var champ = (await pUsers.GetByDiscordIdAsync((ulong)champId))!;
int boxes = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity),0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.type = 'Caja'", new { id = champId });
Check(goldAfter - goldBefore == 210 && boxes == 1 && champ.Xp + (champ.Level - 5) * 1 >= 0, $"solo el campeón cobra: +210 de oro (zona 1), una caja ({goldAfter - goldBefore} oro, {boxes} caja)");
var infoAfter = await arenaRepo.GetDayAsync(day1);
Check(infoAfter is { Status: "resolved", Participants: 4, Rounds: 2 } && infoAfter.WinnerId == (ulong)champId && infoAfter.RewardText!.Contains("210") && infoAfter.ResolvedAtUtc is not null, $"el día queda resuelto con campeón y premio guardados ({infoAfter?.RewardText})");
var stored = await arenaRepo.GetMatchesAsync(day1);
Check(stored.Count == 3 && stored.Count(m => m.Round == 1) == 2 && stored.Single(m => m.Round == 2).WinnerId == (ulong)champId, "la llave guardada: 2 peleas en la primera ronda y la final la ganó el campeón");
Check(await arena.ResolveDayAsync(day1) is null, "resolver el mismo día otra vez no hace nada");
Check(await arenaRepo.JoinAsync(day1, (ulong)(BaseId + 14), "Tarde", 1, 32) == ArenaJoinStatus.Closed, "un torneo ya jugado no admite anotados");
Check(events2.Kinds((ulong)champId, GameEventKinds.ArenaWin) == 1, "se registra arena_win una vez");
var latest = await arena.GetLatestResultsAsync();
Check(latest is not null && latest.Info.Day >= day1 && (latest.Info.Day != day1 || latest.Matches.Count == 3), "GetLatestResults devuelve el último torneo jugado con su llave");

// 20 resolviendo a la vez: se paga una sola vez
var raceDay = new DateOnly(2020, 3, 3);
for (int n = 20; n <= 23; n++) { await MakeUser(n, "Guerrero", 5); await arenaRepo.JoinAsync(raceDay, (ulong)(BaseId + n), $"R{n}", 9, ArenaRules.MaxPlayers); }
long gb = await db.ExecuteScalarAsync<long>("SELECT SUM(gold) FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId + 20, b = BaseId + 23 });
var racers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => arena.ResolveDayAsync(raceDay))));
long ga = await db.ExecuteScalarAsync<long>("SELECT SUM(gold) FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId + 20, b = BaseId + 23 });
int boxCount = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity),0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id BETWEEN @a AND @b AND i.type = 'Caja'", new { a = BaseId + 20, b = BaseId + 23 });
int matchRows = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM arena_matches WHERE day = '2020-03-03'");
Check(racers.Count(r => r is not null) == 1 && ga - gb == 210 && boxCount == 1 && matchRows == 3, $"20 cierres a la vez del mismo día: uno solo juega y paga (+{ga - gb} oro, {boxCount} caja, {matchRows} peleas guardadas)");

// Cancelación con un solo anotado
var lonely = new DateOnly(2020, 3, 4);
await MakeUser(30, "Guerrero", 5); await arenaRepo.JoinAsync(lonely, (ulong)(BaseId + 30), "Solo", 3, 32);
var cancelled = await arena.ResolveDayAsync(lonely);
Check(cancelled is { Cancelled: true, Participants: 1, WinnerId: null } && (await arenaRepo.GetDayAsync(lonely))!.Status == "cancelled" && await db.ExecuteScalarAsync<long>("SELECT gold FROM users WHERE discord_id = @id", new { id = BaseId + 30 }) == 0, "con un solo anotado el torneo se cancela y no se paga premio");
Check(await arena.ResolveDayAsync(lonely) is null && await arena.ResolveDayAsync(new DateOnly(2020, 3, 30)) is null, "un día cancelado, o que nunca existió, no se vuelve a resolver");

// Torneo con byes (5 anotados): 3 pasan directo
var byeDay = new DateOnly(2020, 3, 5);
for (int n = 40; n <= 44; n++) { await MakeUser(n, classes[n % 4], 6); await arenaRepo.JoinAsync(byeDay, (ulong)(BaseId + n), $"B{n}", 4, 32); }
var byeRes = await arena.ResolveDayAsync(byeDay);
Check(byeRes is { Participants: 5, Rounds: 3 } && byeRes.Matches.Count(m => m.P2Id is null) == 3 && byeRes.Matches.Count(m => m.P2Id is not null) == 4, $"5 anotados: 3 rondas, 3 pasan directo y se pelean 4 combates ({byeRes?.Matches.Count} filas)");

// ResolveDue: solo los días abiertos anteriores a hoy (con un repo que filtra a mis días de prueba)
var dueDays = new[] { new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 2) };
for (int n = 50; n <= 53; n++) await MakeUser(n, "Guerrero", 4);
foreach (var d in dueDays) { await arenaRepo.JoinAsync(d, (ulong)(BaseId + 50), "A", 8, 32); await arenaRepo.JoinAsync(d, (ulong)(BaseId + 51 + (d.Day - 1)), "B", 8, 32); }
var arenaDue = new ArenaService(new FilteredArenaRepo(arenaRepo, dueDays), factory, pUsers, zonesRepo, new FakeEvents());
var due = await arenaDue.ResolveDueAsync(new DateTime(2026, 10, 2, 3, 0, 5, DateTimeKind.Utc));
Check(due.Count == 2 && due.Select(r => r.Day).SequenceEqual(dueDays) && (await arenaDue.ResolveDueAsync(DateTime.UtcNow)).Count == 0, "ResolveDue juega los días pendientes en orden (el más viejo primero) y la segunda pasada no encuentra nada");
Check((await arenaRepo.GetDayAsync(dueDays[0]))!.Status == "resolved" && (await arenaRepo.GetDayAsync(dueDays[1]))!.Status == "resolved", "los dos días quedan jugados");

// El día de HOY nunca se juega antes de tiempo
var todayDue = await arenaRepo.GetOpenDaysBeforeAsync(new DateOnly(2020, 3, 5));
Check(todayDue.All(d => d < new DateOnly(2020, 3, 5)), "GetOpenDaysBefore(hoy) no devuelve hoy ni el futuro");

// Zona del campeón: el premio escala
var zoneDay = new DateOnly(2020, 5, 1);
await MakeUser(60, "Guerrero", 25, zone: (await zonesRepo.GetAllAsync()).OrderBy(z => z.MinLevel).Last().ZoneId);
await MakeUser(61, "Guerrero", 1);
await arenaRepo.JoinAsync(zoneDay, (ulong)(BaseId + 60), "Veterano", 2, 32); await arenaRepo.JoinAsync(zoneDay, (ulong)(BaseId + 61), "Novato", 2, 32);
var zr = await arena.ResolveDayAsync(zoneDay);
var winnerGold = await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)zr!.WinnerId! });
Check(zr.WinnerId == (ulong)(BaseId + 60) || winnerGold == 210, $"el premio sigue a la zona del campeón (ganó {(zr.WinnerId == (ulong)(BaseId + 60) ? "el veterano de la última zona" : "el novato")}; oro {winnerGold})");
if (zr.WinnerId == (ulong)(BaseId + 60)) Check(winnerGold == 5625, $"campeón de la zona 5: 15 cacerías de oro de zona 5 = 5625 (cobró {winnerGold})");

Console.WriteLine("\n== 10. Mensajes de la arena ==");
var fakeArena = new FakeArena();
var joinedMsg = await ArenaModule.ExecuteJoinAsync(fakeArena.With(new ArenaJoinResult(ArenaJoinStatus.Joined, new DateOnly(2026, 10, 2), 7, TimeSpan.FromHours(5.2))), FakeUser(5, "Ana", false), 1, DateTime.UtcNow);
Check(joinedMsg.Title.Contains("Anotado") && joinedMsg.Description.Contains("02/10") && joinedMsg.Description.Contains("00:00") && joinedMsg.Description.Contains("5 h") && joinedMsg.Description.Contains("7") && joinedMsg.Footer!.Value.Text.Contains("listplayers"), "anotarse: dice el día, la hora, cuánto falta y cuántos van");
foreach (var st in new[] { ArenaJoinStatus.AlreadyJoined, ArenaJoinStatus.Full, ArenaJoinStatus.NoAccount, ArenaJoinStatus.Closed })
{
    var m = await ArenaModule.ExecuteJoinAsync(fakeArena.With(new ArenaJoinResult(st, new DateOnly(2026, 10, 2), 32, TimeSpan.FromHours(1))), FakeUser(5, "Ana", false), 1, DateTime.UtcNow);
    if (string.IsNullOrWhiteSpace(m.Title) || string.IsNullOrWhiteSpace(m.Description)) Check(false, $"el estado {st} no tiene mensaje");
}

Check(true, "los otros 4 estados (ya anotado, lleno, sin cuenta, cerrado) tienen su mensaje");
var emptyList = await ArenaModule.ExecuteListAsync(fakeArena.WithListing(new ArenaListing(new DateOnly(2026, 10, 2), [], TimeSpan.FromHours(3))), DateTime.UtcNow);
Check(emptyList.Description.Contains("nadie") && emptyList.Description.Contains("/arena join"), "lista vacía: invita a ser el primero");
var entries = Enumerable.Range(1, 32).Select(i => new ArenaEntry((ulong)i, $"Jugador número {i} con nombre largo largo", classes[i % 4], i, DateTime.UtcNow)).ToList();
var fullList = await ArenaModule.ExecuteListAsync(fakeArena.WithListing(new ArenaListing(new DateOnly(2026, 10, 2), entries, TimeSpan.FromMinutes(40))), DateTime.UtcNow);
Check(fullList.Description.Contains("32") && fullList.Description.Contains("40 min") && fullList.Length < 6000 && fullList.Description.Length < 4096, $"lista de 32: entra en un mensaje ({fullList.Length} caracteres)");
var listOne = await ArenaModule.ExecuteListAsync(fakeArena.WithListing(new ArenaListing(new DateOnly(2026, 10, 2), entries.Take(1).ToList(), TimeSpan.FromMinutes(40))), DateTime.UtcNow);
Check(listOne.Description.Contains("al menos **2**"), "con un solo anotado avisa que hacen falta 2");
var noResults = await ArenaModule.ExecuteResultsAsync(fakeArena.WithResults(null));
Check(noResults.Title.Contains("Todavía no hubo"), "sin torneos jugados: lo dice");
var big = Enumerable.Range(1, 32).Select(i => new ArenaEntrant((ulong)i, $"Jugador con un nombre bien largo número {i}")).ToList();
var bigBracket = ArenaBracket.Run(big, new Random(3), (a, b) => new ArenaFightOutcome(Math.Min(a.DiscordId, b.DiscordId), 5));
var bigRows = bigBracket.Matches.Select(m => new ArenaMatchRow(m.Round, m.Slot, m.P1.DiscordId, m.P1.Name, m.P2?.DiscordId, m.P2?.Name, m.WinnerId, m.Actions)).ToList();
var bigEmbed = ArenaModule.BuildResultsEmbed(new DateOnly(2026, 10, 1), "Jugador con un nombre bien largo número 1", 32, 5, "💰 3120 oro · ⭐ 40 XP · 📦 Cofre de Oro", bigRows, null);
Check(bigEmbed.Length < 6000 && bigEmbed.Fields.All(f => f.Value.Length <= 1024) && bigEmbed.Fields.Length >= 5 && bigEmbed.Fields[^1].Name.Contains("Final") && bigEmbed.Fields.Sum(f => f.Value.Split('\n').Length) == 31, $"la llave completa de 32 jugadores entra en un embed ({bigEmbed.Length} caracteres, {bigEmbed.Fields.Length} campos, 31 peleas)");
Console.WriteLine("   " + bigEmbed.Description.Replace("\n", " / "));
Console.WriteLine("   " + bigEmbed.Fields[^1].Name + ": " + bigEmbed.Fields[^1].Value + "  |  " + bigEmbed.Fields[^2].Name + ": " + bigEmbed.Fields[^2].Value.Replace("\n", " / "));
var small = ArenaModule.BuildResultsEmbed(new DateOnly(2026, 10, 1), "Ana", 5, 3, null, new[]
{
    new ArenaMatchRow(1, 0, 1, "Ana", null, null, 1, 0), new ArenaMatchRow(1, 1, 2, "Beto", 3, "Caro", 3, 9),
}, null);
Check(small.Fields[0].Value.Contains("pasa directo") && small.Fields[0].Value.Contains("**Caro**") && !small.Fields[0].Value.Contains("**Beto**") && small.Description.Contains("—"), "los pasa-directo se marcan, el ganador va en negrita y sin premio no se rompe");
var annCancel = ArenaModule.BuildAnnouncement(new ArenaResolution(new DateOnly(2026, 10, 1), true, 5, null, null, 1, 0, null, 0, 0, []));
Check(annCancel.Content is null && annCancel.Embed.Title.Contains("canceló") && annCancel.Embed.Description.Contains("2"), "el anuncio de un torneo cancelado dice por qué");
var annWin = ArenaModule.BuildAnnouncement(new ArenaResolution(new DateOnly(2026, 10, 1), false, 5, 77, "Ana", 4, 2, "💰 210 oro", 1, 6, bigRows.Take(3).ToList()));
Check(annWin.Content == "🎉 ¡<@77> es el campeón de la Arena!" && annWin.Embed.Description.Contains("nivel **6**"), "el anuncio menciona al campeón y avisa si subió de nivel");

Console.WriteLine("\n== 11. El reloj ==");
var tickClient = new DiscordSocketClient();
var scheduler = new ArenaScheduler(new FakeArena().WithDue([new ArenaResolution(new DateOnly(2026, 10, 1), false, 123, 1, "Ana", 2, 1, null, 0, 0, [])]), tickClient, new ConfigurationBuilder().Build());
await scheduler.TickAsync();
var throwing = new ArenaScheduler(new FakeArena { Throw = true }, tickClient, new ConfigurationBuilder().Build());
await throwing.TickAsync();
Check(true, "un ciclo con canal inalcanzable, o con el servicio fallando, no tira (se registra y sigue)");

Console.WriteLine("\n== 12. La Arena narrada ==");
ArenaMatchRow M(int round, int slot, ulong w, int hp, int actions) => new(round, slot, 1, "Ana", 2, "Beto", w, actions, hp);
var tight = ArenaNarrator.Describe(M(1, 0, 1, 7, 30)); var crush = ArenaNarrator.Describe(M(1, 0, 2, 96, 12)); var longf = ArenaNarrator.Describe(M(1, 0, 1, 40, 64)); var plain = ArenaNarrator.Describe(M(1, 0, 1, 50, 20));
Check(tight.Contains("7%") && tight.Contains("**Ana**") && crush.Contains("**Beto**") && !crush.Contains("**Ana**") && longf.Contains("64") && plain.Contains("**Ana**"), "cada pelea se cuenta según cómo fue: por un pelo (7%), paliza, batalla larga y la común, siempre con el ganador en negrita");
Check(ArenaNarrator.Describe(M(2, 1, 1, 7, 30)) == ArenaNarrator.Describe(M(2, 1, 1, 7, 30)) && Enumerable.Range(0, 40).Select(i => ArenaNarrator.Describe(M(1, i, 1, 50, 20))).Distinct().Count() >= 3, "es determinista para la misma pelea y varía entre peleas (no es siempre la misma frase)");
var byeLine = ArenaNarrator.Describe(new ArenaMatchRow(1, 0, 1, "Ana", null, null, 1, 0));
Check(byeLine.Contains("pasa directo") && byeLine.Contains("Ana"), "el que no tuvo rival: 'pasa directo'");
var crowning = ArenaNarrator.Crowning(new ArenaMatchRow(3, 0, 1, "Ana", 2, "Beto", 2, 30, 50));
Check(crowning.Contains("**Beto**") && crowning.Contains("Ana") && crowning.Contains("campeón"), "la coronación nombra al campeón y al finalista");

// Transmisión: 8 jugadores => apertura + 3 rondas + cierre
var eight = Enumerable.Range(1, 8).Select(i => new ArenaEntrant((ulong)i, $"Jugador{i}")).ToList();
var br = ArenaBracket.Run(eight, new Random(5), (a, b) => new ArenaFightOutcome(Math.Min(a.DiscordId, b.DiscordId), 20 + (int)a.DiscordId, 30 + (int)b.DiscordId * 5));
var res8 = new ArenaResolution(new DateOnly(2026, 10, 1), false, 99, 1, "Jugador1", 8, 3, "💰 210 oro", 0, 0,
    br.Matches.Select(m => new ArenaMatchRow(m.Round, m.Slot, m.P1.DiscordId, m.P1.Name, m.P2?.DiscordId, m.P2?.Name, m.WinnerId, m.Actions, m.WinnerHpPercent)).ToList());
var cast = ArenaModule.BuildBroadcast(res8);
Check(cast.Count == 5 && cast[0].Embed.Title.Contains("Arranca") && cast[0].Embed.Description.Contains("Jugador1") && cast[1].Embed.Title.Contains("Cuartos") && cast[2].Embed.Title.Contains("Semifinal") && cast[3].Embed.Title.Contains("Final") && cast[4].Content == "🎉 ¡<@1> es el campeón de la Arena!" && cast[4].Embed.Description.Contains("coron"),
    $"8 anotados: apertura, cuartos, semifinal, final y cierre con la mención del campeón ({string.Join(" | ", cast.Select(c => c.Embed.Title))})");
Check(cast.Take(4).All(c => c.Content is null) && cast.All(c => c.Embed.Length < 6000) && cast[1].Embed.Description.Split('\n').Length == 4, "solo el cierre menciona; las rondas muestran un renglón por pelea (4 en cuartos)");
Console.WriteLine("   " + cast[1].Embed.Title + ":\n   " + cast[1].Embed.Description.Replace("\n", "\n   "));
Console.WriteLine("   " + cast[4].Embed.Description.Replace("\n", " / "));
var cast5 = ArenaModule.BuildBroadcast(new ArenaResolution(new DateOnly(2026, 10, 1), false, 99, 1, "x", 32, 5, null, 0, 0,
    ArenaBracket.Run(Enumerable.Range(1, 32).Select(i => new ArenaEntrant((ulong)i, $"Jugador con un nombre bien largo {i}")).ToList(), new Random(2), (a, b) => new ArenaFightOutcome(Math.Min(a.DiscordId, b.DiscordId), 40, 55)).Matches
        .Select(m => new ArenaMatchRow(m.Round, m.Slot, m.P1.DiscordId, m.P1.Name, m.P2?.DiscordId, m.P2?.Name, m.WinnerId, m.Actions, m.WinnerHpPercent)).ToList()));
Check(cast5.Count == 7 && cast5.All(c => c.Embed.Length < 6000 && (c.Embed.Description?.Length ?? 0) < 4096) && cast5[0].Embed.Description.Length < 1200, $"32 anotados: 7 mensajes (apertura, 5 rondas, cierre) y ninguno pasa el límite ({cast5.Max(c => c.Embed.Length)} caracteres el más largo)");
var cancelCast = ArenaModule.BuildBroadcast(new ArenaResolution(new DateOnly(2026, 10, 1), true, 5, null, null, 1, 0, null, 0, 0, []));
Check(cancelCast.Count == 1 && cancelCast[0].Embed.Title.Contains("canceló"), "un torneo cancelado es un solo mensaje");

// El reloj manda la transmisión completa, en orden y con pausa entre mensajes
var sent = new List<(string? Content, string Title)>();
var fakeChannel = (IMessageChannel)FakeProxy.Create(typeof(IMessageChannel), (n, a) =>
{
    if (n == "SendMessageAsync") { lock (sent) sent.Add(((string?)a[0], ((Embed)a[2]).Title)); return Task.FromResult<IUserMessage>(null!); }
    return null;
});
ArenaScheduler.RoundPause = TimeSpan.FromMilliseconds(30);
var bcast = new ArenaScheduler(new FakeArena().WithDue([res8]), tickClient, new ConfigurationBuilder().Build()) { ChannelLookup = id => Task.FromResult<IMessageChannel?>(id == 99 ? fakeChannel : null) };
var t0 = DateTime.UtcNow; await bcast.TickAsync(); var took = DateTime.UtcNow - t0;
Check(sent.Count == 5 && sent[0].Title.Contains("Arranca") && sent[4].Content!.Contains("campeón") && took >= TimeSpan.FromMilliseconds(100), $"el reloj manda los 5 mensajes en orden con pausa entre ellos ({took.TotalMilliseconds:0} ms)");
sent.Clear();
var otherCh = new ArenaScheduler(new FakeArena().WithDue([res8]), tickClient, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Arena:ChannelId"] = "555" }).Build()) { ChannelLookup = id => Task.FromResult<IMessageChannel?>(id == 555 ? fakeChannel : null) };
await otherCh.TickAsync();
Check(sent.Count == 5, "Arena__ChannelId manda la transmisión a otro canal en vez del que se anotó el primero");
sent.Clear();
var noChannel = new ArenaScheduler(new FakeArena().WithDue([res8 with { ChannelId = null }]), tickClient, new ConfigurationBuilder().Build()) { ChannelLookup = id => Task.FromResult<IMessageChannel?>(fakeChannel) };
await noChannel.TickAsync();
Check(sent.Count == 0, "sin canal conocido no se manda nada (y no se rompe)");
ArenaScheduler.RoundPause = TimeSpan.Zero;

// El porcentaje de vida del ganador viaja por la base
await MakeUser(80, "Guerrero", 5); await MakeUser(81, "Ninja", 5);
var hpDay = new DateOnly(2020, 6, 1);
await arenaRepo.JoinAsync(hpDay, (ulong)(BaseId + 80), "Ocho", 1, 32); await arenaRepo.JoinAsync(hpDay, (ulong)(BaseId + 81), "Nueve", 1, 32);
var hpRes = await arena.ResolveDayAsync(hpDay);
var hpStored = await arenaRepo.GetMatchesAsync(hpDay);
Check(hpRes!.Matches.Single().WinnerHpPercent is >= 1 and <= 100 && hpStored.Single().WinnerHpPercent == hpRes.Matches.Single().WinnerHpPercent, $"el % de vida del ganador se calcula, se guarda y se lee igual ({hpStored.Single().WinnerHpPercent}%)");

Console.WriteLine("\n== 14. Sin rareza en drops y equipo ==");
var tmpl = new MonsterTemplate("Puma de las Cenizas", "🐆", 10, 20, 2, 4, ["Garra de Puma Cenizo"]);
var dropBlocks = DropsCatalog.BuildZoneBlocks([new ZoneMonster(2, MonsterKind.Hunt, tmpl)], new Dictionary<string, DropItemInfo> { ["Garra de Puma Cenizo"] = new("Garra de Puma Cenizo", "Raro", "<:garra:123>") });
Check(dropBlocks.Count == 1 && dropBlocks[0].Text.Contains("Garra de Puma Cenizo") && !dropBlocks[0].Text.Contains("Raro"), "/drops muestra el drop con su emoji y sin la rareza (la zona ya lo dice)");
var mini = new CombatState("hunt", "Jabalí", "🐗", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
var turnW = CombatTurnResolver.ResolveTurn(new CombatantProfile(100, 0, "Guerrero", ClassPassives.For("Guerrero")), default, PlayerAction.Attack, 100, 100, 1, 5);
var victoryMethod = typeof(AdventureModule).GetMethod("BuildVictoryEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
var withDrop = (Embed)victoryMethod.Invoke(null, [mini, turnW, new CombatReward(10, 10, true), new Item { Name = "Garra de Puma Cenizo", Rarity = "Raro", Emoji = "<:garra:123>" }, new LevelUpOutcome(new User { DiscordId = 1, Level = 1, MaxHp = 100, CurrentHp = 100, Class = "Guerrero" }, 0), true])!;
var dropField = withDrop.Fields.Single(f => f.Name.Contains("Material")).Value;
Check(dropField.Contains("Garra de Puma Cenizo") && !dropField.Contains("Raro") && !dropField.Contains("("), "la victoria muestra el material sin '(Raro)'");

Console.WriteLine("\n== 15. Límites de Discord para registrar los comandos ==");
var tooLong = interactions.SlashCommands.Where(c => c.Description.Length > 100 || c.Name.Length > 32 || c.Parameters.Any(p => p.Description.Length > 100 || p.Name.Length > 32)).Select(c => c.Name).ToList();
var groupsTooLong = interactions.Modules.Where(m => m.IsSlashGroup && (m.Description?.Length ?? 0) > 100).Select(m => m.Name).ToList();
Check(tooLong.Count == 0 && groupsTooLong.Count == 0 && interactions.SlashCommands.Count < 100, $"{interactions.SlashCommands.Count} comandos: ninguno pasa 100 caracteres de descripción ni 32 de nombre ({string.Join(", ", tooLong.Concat(groupsTooLong))})");
var dup = interactions.SlashCommands.GroupBy(c => (c.Module.SlashGroupName ?? "") + "/" + c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
Check(dup.Count == 0, $"sin comandos de barra duplicados ({string.Join(", ", dup)})");
var customIds = interactions.ComponentCommands.Select(c => c.Name).ToList();
Check(customIds.Distinct().Count() == customIds.Count, "sin handlers de componentes duplicados");

await Cleanup();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

IUser FakeUser(long id, string name, bool bot) => (IUser)FakeProxy.Create(typeof(IUser), (n, a) => n switch
{
    "get_Id" => (ulong)id,
    "get_IsBot" => bot,
    "get_Username" => name,
    _ => null,
});

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

sealed class FakeTarget : ICombatReplyTarget
{
    public int Updates;
    public Embed? LastEmbed;
    public MessageComponent? LastComponents;

    public Task UpdateAsync(Embed embed, MessageComponent components)
    {
        Interlocked.Increment(ref Updates);
        LastEmbed = embed;
        LastComponents = components;
        return Task.CompletedTask;
    }
}

sealed class FakeEvents : IGameEvents
{
    private readonly ConcurrentBag<(ulong Id, string Kind)> _recorded = [];
    public int Kinds(ulong id, string kind) => _recorded.Count(r => r.Id == id && r.Kind == kind);

    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null)
    {
        _recorded.Add((discordId, kind));
        return Task.CompletedTask;
    }

    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}

// El servicio real de la Arena con un repositorio que solo ve ciertos días abiertos (para no tocar torneos reales al probar ResolveDue).
sealed class FilteredArenaRepo(IArenaRepository inner, DateOnly[] visible) : IArenaRepository
{
    public Task<ArenaJoinStatus> JoinAsync(DateOnly day, ulong discordId, string displayName, ulong channelId, int maxPlayers, CancellationToken cancellationToken = default) => inner.JoinAsync(day, discordId, displayName, channelId, maxPlayers, cancellationToken);
    public Task<IReadOnlyList<ArenaEntry>> GetEntriesAsync(DateOnly day, CancellationToken cancellationToken = default) => inner.GetEntriesAsync(day, cancellationToken);
    public Task<ArenaDayInfo?> GetDayAsync(DateOnly day, CancellationToken cancellationToken = default) => inner.GetDayAsync(day, cancellationToken);
    public Task<ArenaDayInfo?> GetLatestResolvedAsync(CancellationToken cancellationToken = default) => inner.GetLatestResolvedAsync(cancellationToken);
    public Task<IReadOnlyList<ArenaMatchRow>> GetMatchesAsync(DateOnly day, CancellationToken cancellationToken = default) => inner.GetMatchesAsync(day, cancellationToken);
    public async Task<IReadOnlyList<DateOnly>> GetOpenDaysBeforeAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        (await inner.GetOpenDaysBeforeAsync(today, cancellationToken)).Where(visible.Contains).ToList();
    public Task<bool> CancelDayAsync(DateOnly day, int participants, CancellationToken cancellationToken = default) => inner.CancelDayAsync(day, participants, cancellationToken);
    public Task<ArenaResolveOutcome> ResolveDayAsync(DateOnly day, IReadOnlyList<ArenaMatchRow> matches, int participants, int rounds, ulong winnerId, string winnerName, RewardSpec reward, int zoneRank, CancellationToken cancellationToken = default) =>
        inner.ResolveDayAsync(day, matches, participants, rounds, winnerId, winnerName, reward, zoneRank, cancellationToken);
}

// Un IArenaService a medida para probar los mensajes y el reloj sin tocar la base.
sealed class FakeArena : IArenaService
{
    private ArenaJoinResult? _join;
    private ArenaListing? _listing;
    private ArenaResults? _results;
    private IReadOnlyList<ArenaResolution> _due = [];
    public bool Throw;

    public FakeArena With(ArenaJoinResult join) { _join = join; return this; }
    public FakeArena WithListing(ArenaListing listing) { _listing = listing; return this; }
    public FakeArena WithResults(ArenaResults? results) { _results = results; return this; }
    public FakeArena WithDue(IReadOnlyList<ArenaResolution> due) { _due = due; return this; }

    public Task<ArenaJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default) => Task.FromResult(_join!);
    public Task<ArenaListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default) => Task.FromResult(_listing!);
    public Task<ArenaResults?> GetLatestResultsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_results);
    public Task<IReadOnlyList<ArenaResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        Throw ? throw new InvalidOperationException("falla simulada") : Task.FromResult(_due);
    public Task<ArenaResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default) => Task.FromResult<ArenaResolution?>(null);
}
