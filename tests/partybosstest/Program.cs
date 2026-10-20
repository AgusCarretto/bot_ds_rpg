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

// v0.19.0: el jefe EN EQUIPO ("/boss @amigo", "aa boss @amigo"; reemplaza al viejo /raid). Contra la base real con usuarios descartables, el RaidSessionService y el AdventureRepository de verdad y un
// destino de mensajes falso:
//   1) armar el grupo: las invitaciones se rechazan (con el motivo y el nombre) si alguien no tiene cuenta, tiene otra zona máxima, el cooldown ocupado, está en otro combate, sin vida, sin nivel...
//   2) los botones: aceptar (solo los nombrados, y se vuelve a validar al aceptar), rechazar, cancelar, empezar ya y el vencimiento de las invitaciones
//   3) los TURNOS: pega uno, el jefe le responde a ese, y le toca al siguiente; fuera de turno no se toca nada; los caídos y los que se fueron se saltean
//   4) el reloj de turnos: perder el turno, el segundo seguido retira, y jugar lo perdona
//   5) victoria / abandono (el caído paga la penalidad) / registro de los comandos con el InteractionService y el CommandService reales
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var buffs = sp.GetRequiredService<IBuffRepository>();
var combat = sp.GetRequiredService<ICombatSessionService>();
var cooldowns = sp.GetRequiredService<ICooldownRepository>();
var adventure = sp.GetRequiredService<IAdventureRepository>();
var monsters = sp.GetRequiredService<IMonsterRepository>();
var zones = sp.GetRequiredService<IZoneRepository>();
var events = sp.GetRequiredService<IGameEvents>();
var inventory = sp.GetRequiredService<IInventoryRepository>();
var zoneBoxes = sp.GetRequiredService<IZoneBoxService>();
var bonus = sp.GetRequiredService<IPlayerBonusService>();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

const long BaseId = 999000000000080000L;
ulong A = (ulong)BaseId + 1, B = (ulong)BaseId + 2, C = (ulong)BaseId + 3, D = (ulong)BaseId + 4, Ghost = (ulong)BaseId + 99;
ulong[] all = [A, B, C, D];
var boss = CooldownCatalog.Boss;
int originalMin = RaidSettings.MinParticipants;
var originalWindow = RaidSettings.InviteWindow;
var originalTurn = RaidSettings.TurnTimeout;

async Task Cleanup()
{
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
    await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id BETWEEN @a AND @b", new { a = BaseId, b = BaseId + 999 });
}

// Un jugador listo: nivel 10, zona 1, sin haber vencido a ningún jefe (zona máxima 1), con la vida llena.
async Task Make(ulong id, int level = 10, int cleared = 0, int hp = 200, int gold = 1000, int xp = 0, string cls = "Guerrero")
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync(
        "UPDATE users SET class = @c, level = @l, gold = @g, xp = @x, max_hp = 200, current_hp = @h, current_zone_id = 1, highest_zone_cleared = @z, in_gate = false, weapon_id = NULL, amulet_id = NULL WHERE discord_id = @id",
        new { id = (long)id, c = cls, l = level, g = gold, x = xp, h = hp, z = cleared });
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
}
async Task ResetAll()
{
    await Cleanup();
    foreach (var id in all) await Make(id);
}
async Task BusyBoss(ulong id) => await db.ExecuteAsync(
    "INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'boss', now()) ON CONFLICT (discord_id, command_name) DO UPDATE SET last_executed_at = now()", new { id = (long)id });
async Task<TimeSpan?> Left(ulong id) => await cooldowns.GetRemainingAsync(id, boss.CommandName, boss.Duration);
async Task<long> Gold(ulong id) => await db.ExecuteScalarAsync<long>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
async Task<long> Xp(ulong id) => await db.ExecuteScalarAsync<long>("SELECT xp FROM users WHERE discord_id = @id", new { id = (long)id });

RaidModule.PartyMember M(ulong id, string? name = null) => new(id, name ?? $"J{id % 100}");
async Task<RaidModule.PartyPreparation> Prepare(IRaidSessionService svc, ulong starter, params RaidModule.PartyMember[] companions) =>
    await RaidModule.PreparePartyAsync(users, items, monsters, zones, combat, svc, cooldowns, buffs, bonus, starter, $"J{starter % 100}", companions);
string Reject(RaidModule.PartyPreparation p) => p.Rejection is { } r ? (r.PlainMessage ?? "") + (r.Embed is { } e ? AllText(e) : "") : "";

// Publica el grupo como lo hacen /boss y "aa boss" (con el contexto del reloj de turnos y un destino de mensajes falso).
FakeTarget Open(RaidSession session, IRaidSessionService svc)
{
    var target = new FakeTarget();
    session.ReplyTarget = target;
    Check(RaidModule.TryOpenParty(session, svc, adventure, new RaidTurnContext(users, events, svc), 123), $"se publica el grupo de {session.Invited.Count} invitado(s)");
    return target;
}
async Task<string> Accept(RaidSession s, IRaidSessionService svc, ulong id) =>
    await RaidModule.AcceptInviteCoreAsync(s, id, $"J{id % 100}", users, items, zones, combat, svc, cooldowns, buffs, adventure, bonus);

try
{
    RaidSettings.Configure(2);
    var passives = ClassPassives.For("Guerrero");

    // Un participante armado a mano (no pasa por la base: solo lo que necesita el combate).
    RaidParticipant Member(ulong id, int hp = 100000, int damage = 60, int defense = 0, string cls = "Guerrero") => new()
    {
        DiscordId = id, DisplayName = $"J{id % 100}", Damage = damage, Defense = defense, Level = 10, PlayerClass = cls, Passives = ClassPassives.For(cls), MaxHp = hp, StartingHp = hp, CurrentHp = hp,
    };

    // Una pelea ACTIVA armada a mano con esos jugadores (en ese orden), registrada en un servicio propio. Sin Turns: no hay reloj salvo que la prueba lo pida.
    (RaidSession Session, FakeTarget Target, RaidSessionService Service) Active(int bossHp, int bossDamage, params RaidParticipant[] members)
    {
        var service = new RaidSessionService();
        var target = new FakeTarget();
        var session = new RaidSession
        {
            RaidId = Guid.NewGuid(), BossName = "Rey Jabalí", BossEmoji = "🐗", BossBaseHp = bossHp, ZoneRank = 1, BossMaxHp = bossHp, BossCurrentHp = bossHp, BossDamage = bossDamage, BossDropItemNames = [],
            BossGoldBonus = 0, BossXpBonus = 0, ZoneId = 1, ZoneName = "Praderas del Mate", StarterId = members[0].DiscordId, ReplyTarget = target, Phase = RaidPhase.Active, TurnIndex = 0, TurnNumber = 1,
        };
        foreach (var m in members)
        {
            session.Participants.Add(m);
            service.TryRegisterParticipant(m.DiscordId, session.RaidId);
        }

        service.TryAdd(session);
        return (session, target, service);
    }

    async Task Attack(RaidSession s, IRaidSessionService svc, ulong id, FakeOutput output, bool ability = false) =>
        await RaidModule.ResolveAttackCoreAsync(svc, users, items, adventure, events, zoneBoxes, s.RaidId, id, ability, output);
    async Task Flee(RaidSession s, IRaidSessionService svc, ulong id, FakeOutput output) =>
        await RaidModule.ResolveFleeCoreAsync(svc, users, s.RaidId, id, output, events);

    // ===========================================================================================================================================================================
    Console.WriteLine("== 1. Armar el grupo: las invitaciones se rechazan con el motivo ==");
    await ResetAll();
    var svc1 = new RaidSessionService();
    var okPrep = await Prepare(svc1, A, M(B), M(C));
    Check(okPrep.Session is not null && okPrep.Rejection is null, "A (zona máxima 1) invita a B y C (zona máxima 1): el grupo se arma");
    var s1 = okPrep.Session!;
    Check(s1.Invited.Select(i => i.DiscordId).SequenceEqual([B, C]) && s1.MaxZoneRank == 1 && s1.StarterId == A, "los invitados quedan en el orden en que se los nombró y el grupo recuerda la zona máxima");
    Check(s1.Participants.Select(p => p.DiscordId).SequenceEqual([A]) && s1.Phase == RaidPhase.Lobby, "al principio solo está quien lo armó (los demás tienen que aceptar)");
    Check(!svc1.IsInAnyRaid(A) && !svc1.IsInAnyRaid(B), "armarlo todavía no registra a nadie en el servicio (eso pasa al publicar)");
    var embed1 = RaidModule.BuildLobbyEmbed(s1);
    Check(AllText(embed1).Contains($"<@{B}>") && AllText(embed1).Contains("⏳") && AllText(embed1).Contains("armó el grupo"), "la invitación muestra quién armó el grupo y a quién le falta aceptar");
    Check(RaidModule.BuildInviteMentions(s1).Contains($"<@{B}>") && RaidModule.BuildInviteMentions(s1).Contains($"<@{C}>"), "el mensaje que avisa lleva la mención de los invitados (el embed no avisa a nadie)");

    var r = await Prepare(svc1, A, M(A, "yo"));
    Check(r.Session is null && Reject(r).Contains("invitarte a vos mismo"), "invitarse a uno mismo se rechaza");
    r = await Prepare(svc1, A);
    Check(r.Session is null && Reject(r).Contains("al menos a uno"), "sin nombrar a nadie se rechaza (solo se pelea con /boss)");
    r = await Prepare(svc1, A, M(B), M(C), M(D), M(Ghost), M((ulong)BaseId + 91), M((ulong)BaseId + 92));
    Check(r.Session is null && Reject(r).Contains("hasta 6"), "más de 5 compañeros se rechaza (el grupo es de hasta 6)");

    r = await Prepare(svc1, A, M(B), M(B));
    Check(r.Session is { } dup && dup.Invited.Count == 1, "nombrar dos veces al mismo cuenta una sola vez");

    r = await Prepare(svc1, A, M(Ghost, "Fantasma"));
    Check(r.Session is null && Reject(r).Contains("Fantasma") && Reject(r).Contains("/start"), "alguien sin cuenta: se dice quién y que use /start");
    Check(await users.GetByDiscordIdAsync(Ghost) is null, "y nunca se le crea una cuenta a un invitado");

    // LA REGLA DEL DUEÑO: la zona máxima distinta se rechaza.
    await Make(D, level: 10, cleared: 1); // venció al jefe de la zona 1: su zona máxima es la 2
    r = await Prepare(svc1, A, M(B), M(D, "Dora"));
    Check(r.Session is null && Reject(r).Contains("Dora") && Reject(r).Contains("zona máxima"), "UNA ZONA MÁXIMA DISTINTA se rechaza, diciendo quién");
    Check(Reject(r).Contains("Zona 2") && Reject(r).Contains("Zona 1"), "y dice las dos zonas (la de Dora y la de quien armó el grupo)");
    Check(!svc1.IsInAnyRaid(A) && svc1.RaidIdOf(A) is null, "el rechazo no deja nada colgado");
    // al revés: quien arma tiene la 2 y el invitado la 1
    r = await Prepare(svc1, D, M(A, "Ana"));
    Check(r.Session is null && Reject(r).Contains("Ana") && Reject(r).Contains("Zona 1") && Reject(r).Contains("Zona 2"), "también rechaza al revés (quien arma tiene la zona 2 y el invitado la 1)");
    // el nivel solo no cambia la zona máxima si falta vencer al jefe anterior
    await Make(D, level: 30, cleared: 0);
    r = await Prepare(svc1, A, M(D));
    Check(r.Session is { } sameRank && sameRank.MaxZoneRank == 1, "mismo rango aunque uno tenga más nivel: la zona máxima depende del jefe vencido, no solo del nivel (misma zona máxima = se puede)");
    await Make(D, level: 10, cleared: 1);

    await BusyBoss(B);
    r = await Prepare(svc1, A, M(B, "Beto"));
    Check(r.Session is null && Reject(r).Contains("Beto") && Reject(r).Contains("cooldown"), "con el cooldown del jefe ocupado se rechaza, diciendo cuánto le falta");
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)B });

    await db.ExecuteAsync("UPDATE users SET current_hp = 0 WHERE discord_id = @id", new { id = (long)B });
    r = await Prepare(svc1, A, M(B, "Beto"));
    Check(r.Session is null && Reject(r).Contains("Beto") && Reject(r).Contains("vida"), "sin vida se rechaza");
    await Make(B);

    await db.ExecuteAsync("UPDATE users SET level = 1 WHERE discord_id = @id", new { id = (long)B });
    r = await Prepare(svc1, A, M(B, "Beto"));
    Check(r.Session is null && Reject(r).Contains("Beto") && Reject(r).Contains("nivel"), "sin el nivel que pide el jefe se rechaza");
    await Make(B);

    svc1.TryRegisterParticipant(C, Guid.NewGuid());
    r = await Prepare(svc1, A, M(C, "Cami"));
    Check(r.Session is null && Reject(r).Contains("Cami") && Reject(r).Contains("otro combate"), "alguien que ya está en otro combate se rechaza");
    svc1.UnregisterParticipant(C);

    await BusyBoss(B);
    r = await Prepare(svc1, A, M(B, "Beto"), M(D, "Dora"), M(C));
    Check(r.Session is null && Reject(r).Contains("Beto") && Reject(r).Contains("Dora") && !Reject(r).Contains($"J{C % 100}"), "con varios problemas los lista a todos juntos (y no menciona a quien sí puede)");
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)B });

    await BusyBoss(A);
    r = await Prepare(svc1, A, M(B));
    Check(r.Session is null && Reject(r).Contains("todavía no podés"), "si quien arma el grupo tiene el cooldown ocupado, también se rechaza");
    await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)A });

    await db.ExecuteAsync("UPDATE users SET in_gate = true WHERE discord_id = @id", new { id = (long)A });
    r = await Prepare(svc1, A, M(B));
    Check(r.Session is null && Reject(r).Contains("Fogón") && Reject(r).Contains("solo"), "en El Fogón Eterno el jefe se pelea solo");
    await Make(A);
    svc1.TryRegisterParticipant(A, Guid.NewGuid());
    r = await Prepare(svc1, A, M(B));
    Check(r.Session is null, "quien ya está en otro combate no puede armar un grupo");
    svc1.UnregisterParticipant(A);

    // ===========================================================================================================================================================================
    Console.WriteLine("\n== 2. Los botones: aceptar, rechazar, cancelar, empezar ya ==");
    await ResetAll();
    await Make(D, level: 10, cleared: 1);

    // 2a. Todos aceptan: el combate arranca solo, con el orden en que se nombró.
    var svcA = new RaidSessionService();
    var sA = (await Prepare(svcA, A, M(B), M(C))).Session!;
    var tA = Open(sA, svcA);
    Check(svcA.IsInAnyRaid(A) && svcA.Peek(sA.RaidId) is not null && !svcA.IsInAnyRaid(B), "publicado: quien lo armó queda anotado (no puede cazar) y los invitados siguen libres");
    string? msg = await Accept(sA, svcA, D);
    Check((msg ?? "").Contains("no es para vos") && sA.Participants.Count == 1, "aceptar sin haber sido invitado se rechaza");
    msg = await Accept(sA, svcA, C); // acepta primero C, aunque se nombró primero a B
    Check(sA.Participants.Select(p => p.DiscordId).SequenceEqual([A, C]) && sA.Phase == RaidPhase.Lobby && svcA.IsInAnyRaid(C), "C acepta primero: queda adentro y registrada, y el grupo sigue esperando a B");
    Check(AllText(tA.Updates.Last()).Contains("✅") && AllText(tA.Updates.Last()).Contains($"<@{B}>"), "la invitación se actualiza: C figura aceptada y B pendiente");
    msg = await Accept(sA, svcA, C);
    Check((msg ?? "").Contains("Ya estás adentro"), "aceptar dos veces avisa que ya está adentro");
    msg = await Accept(sA, svcA, B);
    Check(sA.Phase == RaidPhase.Active, "cuando contesta el último invitado, el combate arranca solo (sin esperar el tiempo)");
    Check(sA.Participants.Select(p => p.DiscordId).SequenceEqual([A, B, C]), "el orden de los turnos es el de las menciones (A, B, C), no el de aceptación");
    Check(sA.TurnIndex == 0 && sA.TurnNumber >= 1 && sA.BossMaxHp == RaidDifficulty.BossHp(sA.BossBaseHp, 3, sA.ZoneRank), "empieza A y el jefe se escala para 3 jugadores");
    Check(await Left(A) is { } lA && lA > TimeSpan.FromMinutes(29) && await Left(B) is not null && await Left(C) is not null, "los tres tienen corriendo el cooldown del jefe");
    Check(AllText(tA.Updates.Last()).Contains("jefe en equipo") && AllText(tA.Updates.Last()).Contains($"Le toca a <@{A}>"), "la pantalla de combate dice a quién le toca");
    svcA.Remove(sA.RaidId);

    // 2b. El único invitado rechaza: se cancela y nadie queda con el cooldown gastado.
    await ResetAll();
    var svcB = new RaidSessionService();
    var sB = (await Prepare(svcB, A, M(B))).Session!;
    var tB = Open(sB, svcB);
    msg = await RaidModule.DeclineInviteCoreAsync(sB, D, svcB, adventure);
    Check((msg ?? "").Contains("no es para vos") && sB.Phase == RaidPhase.Lobby, "rechazar sin haber sido invitado no hace nada");
    msg = await RaidModule.DeclineInviteCoreAsync(sB, B, svcB, adventure);
    Check(sB.Phase == RaidPhase.Resolved && svcB.Peek(sB.RaidId) is null && !svcB.IsInAnyRaid(A), "si el único invitado rechaza, el grupo se cancela y quien lo armó queda libre");
    Check(await Left(A) is null && await Left(B) is null, "y no se gastó el cooldown de nadie");
    Check(AllText(tB.Updates.Last()).Contains("grupo cancelado"), "la invitación lo dice");

    // 2c. Cancelar es solo de quien armó el grupo.
    await ResetAll();
    var svcC = new RaidSessionService();
    var sC = (await Prepare(svcC, A, M(B))).Session!;
    var tC = Open(sC, svcC);
    msg = await RaidModule.CancelPartyCoreAsync(sC, B, svcC);
    Check((msg ?? "").Contains("Solo quien armó") && sC.Phase == RaidPhase.Lobby && svcC.IsInAnyRaid(A), "un invitado no puede cancelar el grupo");
    msg = await RaidModule.CancelPartyCoreAsync(sC, A, svcC);
    Check(sC.Phase == RaidPhase.Resolved && !svcC.IsInAnyRaid(A) && svcC.Peek(sC.RaidId) is null && AllText(tC.Updates.Last()).Contains("canceló"), "quien lo armó lo cancela: queda libre y la invitación lo dice");
    msg = await Accept(sC, svcC, B);
    Check(!(msg ?? "").Contains("Aceptaste") && sC.Participants.Count == 1, "y ya no se puede aceptar");

    // 2d. Empezar ya: arranca con quienes aceptaron.
    await ResetAll();
    var svcD = new RaidSessionService();
    var sD = (await Prepare(svcD, A, M(B), M(C))).Session!;
    var tD = Open(sD, svcD);
    msg = await RaidModule.StartNowCoreAsync(sD, A, svcD, adventure);
    Check((msg ?? "").Contains("mínimo") && sD.Phase == RaidPhase.Lobby, "empezar ya sin que nadie haya aceptado dice que hace falta el mínimo y no arranca");
    await Accept(sD, svcD, B);
    msg = await RaidModule.StartNowCoreAsync(sD, B, svcD, adventure);
    Check((msg ?? "").Contains("Solo quien armó") && sD.Phase == RaidPhase.Lobby, "empezar ya es solo de quien armó el grupo");
    msg = await RaidModule.StartNowCoreAsync(sD, A, svcD, adventure);
    Check(sD.Phase == RaidPhase.Active && sD.Participants.Select(p => p.DiscordId).SequenceEqual([A, B]), "con uno aceptado, empezar ya arranca con los dos (el que no contestó queda afuera)");
    Check(!svcD.IsInAnyRaid(C) && await Left(C) is null && await Left(B) is not null, "C no queda registrada ni con el cooldown gastado");
    svcD.Remove(sD.RaidId);

    // 2e. Al aceptar se vuelve a validar (pasó tiempo desde la invitación).
    await ResetAll();
    var svcE = new RaidSessionService();
    var sE = (await Prepare(svcE, A, M(B), M(C))).Session!;
    var tE = Open(sE, svcE);
    await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = 1 WHERE discord_id = @id", new { id = (long)B }); // B venció al jefe de la zona 1 mientras tanto: su zona máxima ahora es la 2
    msg = await Accept(sE, svcE, B);
    Check((msg ?? "").Contains("No podés sumarte") && (msg ?? "").Contains("zona máxima") && sE.Participants.All(p => p.DiscordId != B) && sE.Declined.Contains(B), "si al aceptar su zona máxima ya cambió, no entra y queda marcado como que no se suma");
    Check(sE.Phase == RaidPhase.Lobby && AllText(tE.Updates.Last()).Contains("no se suma"), "el grupo sigue esperando a los demás");
    msg = await Accept(sE, svcE, C);
    Check(sE.Phase == RaidPhase.Active && sE.Participants.Select(p => p.DiscordId).SequenceEqual([A, C]), "al contestar C (el último), arranca con A y C");
    svcE.Remove(sE.RaidId);

    // 2f. El vencimiento de las invitaciones: arranca con quienes aceptaron.
    await ResetAll();
    RaidSettings.InviteWindow = TimeSpan.FromMilliseconds(500);
    var svcF = new RaidSessionService();
    var sF = (await Prepare(svcF, A, M(B), M(C))).Session!;
    var tF = Open(sF, svcF);
    await Accept(sF, svcF, B);
    Check(await WaitFor(() => sF.Phase == RaidPhase.Active, 5000) && sF.Participants.Select(p => p.DiscordId).SequenceEqual([A, B]), "al vencer el tiempo, arranca con quienes aceptaron (C no contestó)");
    svcF.Remove(sF.RaidId);

    await ResetAll();
    var svcG = new RaidSessionService();
    var sG = (await Prepare(svcG, A, M(B))).Session!;
    var tG = Open(sG, svcG);
    Check(await WaitFor(() => sG.Phase == RaidPhase.Resolved && !svcG.IsInAnyRaid(A) && tG.Updates.Any(e => AllText(e).Contains("grupo cancelado")), 5000) && await Left(A) is null,
        "si al vencer no contestó nadie, se cancela sin gastar el cooldown y quien armó el grupo queda libre");
    RaidSettings.InviteWindow = originalWindow;

    // ===========================================================================================================================================================================
    Console.WriteLine("\n== 3. Los turnos ==");
    await ResetAll();
    var (s3, t3, svc3) = Active(100000, 1, Member(A), Member(B), Member(C));
    var o3 = new FakeOutput();
    await Attack(s3, svc3, B, o3);
    Check(o3.Notes.Count == 1 && o3.Notes[0].Contains("Todavía no es tu turno") && o3.Notes[0].Contains($"J{A % 100}"), "B pega fuera de turno: se rechaza diciendo a quién le toca");
    Check(s3.BossCurrentHp == 100000 && s3.TurnIndex == 0 && s3.Participants[1].TurnsTaken == 0 && o3.Shown.Count == 0, "y no se tocó nada (ni el jefe, ni el turno, ni los turnos jugados de B)");
    await Attack(s3, svc3, A, o3);
    Check(o3.Shown.Count == 1 && s3.BossCurrentHp < 100000 && s3.TurnIndex == 1 && s3.Participants[0].TurnsTaken == 1, "A pega: el jefe pierde vida y el turno pasa a B");
    Check(AllText(o3.Shown[0]).Contains($"Le toca a <@{B}>") && AllText(o3.Shown[0]).Contains("▶️"), "y la pantalla dice que le toca a B");
    int hpAfterA = s3.BossCurrentHp;
    await Attack(s3, svc3, A, o3);
    Check(o3.Notes.Count == 2 && s3.BossCurrentHp == hpAfterA && s3.TurnIndex == 1, "A no puede pegar dos veces seguidas");
    await Attack(s3, svc3, B, o3);
    Check(s3.TurnIndex == 2, "B pega: le toca a C");
    await Attack(s3, svc3, C, o3);
    Check(s3.TurnIndex == 0, "C pega: vuelve a A");
    Check(s3.Participants.All(p => p.CurrentHp <= p.MaxHp) && s3.Participants.All(p => p.TotalDamageTaken >= 0), "el jefe le respondió a cada uno en su turno");
    await Attack(s3, svc3, Ghost, o3);
    Check(o3.Notes.Last().Contains("No sos parte"), "alguien que no es parte del combate recibe el aviso");
    Check(t3.Pings.Count == 0, "una pelea armada a mano (sin el contexto del reloj) no manda avisos de turno");

    // La habilidad: si no está lista, no gasta el turno.
    var (s3b, _, svc3b) = Active(100000, 1, Member(A), Member(B));
    var o3b = new FakeOutput();
    await Attack(s3b, svc3b, A, o3b, ability: true);
    Check(s3b.TurnIndex == 1 && s3b.Participants[0].Ability.CooldownRemaining > 0, "usar la habilidad es jugar el turno: pasa al siguiente y queda en enfriamiento");
    await Attack(s3b, svc3b, B, o3b);
    await Attack(s3b, svc3b, A, o3b, ability: true);
    Check(s3b.TurnIndex == 0 && o3b.Notes.Count == 1 && o3b.Notes[0].Contains("enfriamiento"), "pedirla en enfriamiento se rechaza SIN gastar el turno (sigue siendo de A)");
    await Attack(s3b, svc3b, A, o3b);
    Check(s3b.TurnIndex == 1, "y después juega normal");

    // Un caído se saltea.
    var (s3c, t3c, svc3c) = Active(100000, 500, Member(A), Member(B, hp: 1), Member(C));
    var o3c = new FakeOutput();
    // El jefe falla un 5 % de sus golpes (esquive base), así que se repite la ronda hasta que B cae: el resultado es determinista aunque la cantidad de rondas no.
    for (int round = 0; round < 80 && !s3c.Participants[1].IsKnockedOut; round++)
    {
        await Attack(s3c, svc3c, A, o3c);
        await Attack(s3c, svc3c, B, o3c);
        if (!s3c.Participants[1].IsKnockedOut) await Attack(s3c, svc3c, C, o3c);
    }
    Check(s3c.Participants[1].IsKnockedOut && s3c.TurnIndex == 2, "B pega y el jefe lo derriba: el turno pasa a C");
    await Attack(s3c, svc3c, C, o3c);
    Check(s3c.TurnIndex == 0, "después de C vuelve a A: B (caído) se saltea");
    await Attack(s3c, svc3c, B, o3c);
    Check(o3c.Notes.Last().Contains("Ya no podés seguir"), "el caído no puede jugar");
    Check(AllText(o3c.Shown.Last()).Contains("💀"), "y la pantalla lo marca");

    // Huir: se puede en cualquier momento, y el turno solo pasa si era el de quien huye.
    await ResetAll();
    var (s3d, t3d, svc3d) = Active(100000, 1, Member(A), Member(B), Member(C));
    var o3d = new FakeOutput();
    await Flee(s3d, svc3d, C, o3d);
    Check(s3d.Participants[2].HasFled && s3d.TurnIndex == 0 && !svc3d.IsInAnyRaid(C), "C huye fuera de turno: se va, el turno sigue siendo de A, y queda libre");
    await Attack(s3d, svc3d, A, o3d);
    await Flee(s3d, svc3d, B, o3d); // le toca a B y huye
    Check(s3d.Participants[1].HasFled && s3d.TurnIndex == 0 && s3d.Phase == RaidPhase.Active, "B huye en su turno: el turno pasa al siguiente en pie (A; C ya se fue)");
    await Attack(s3d, svc3d, C, o3d);
    Check(o3d.Notes.Last().Contains("Ya no podés seguir"), "quien huyó no puede volver a jugar");
    await Flee(s3d, svc3d, A, o3d);
    Check(s3d.Phase == RaidPhase.Resolved && svc3d.Peek(s3d.RaidId) is null && AllText(t3d.Updates.Last()).Contains("abandonado"), "cuando huye el último, el combate termina como abandonado");

    // Un caído + el último en pie que se va: el caído paga la penalidad (antes no pagaba nada).
    await ResetAll();
    await db.ExecuteAsync("UPDATE users SET gold = 1000, xp = 50 WHERE discord_id = @id", new { id = (long)A });
    var (s3e, t3e, svc3e) = Active(100000, 500, Member(A, hp: 1), Member(B));
    var o3e = new FakeOutput();
    for (int round = 0; round < 80 && !s3e.Participants[0].IsKnockedOut; round++)
    {
        await Attack(s3e, svc3e, A, o3e);
        if (!s3e.Participants[0].IsKnockedOut) await Attack(s3e, svc3e, B, o3e);
    }
    Check(s3e.Participants[0].IsKnockedOut && s3e.TurnIndex == 1, "A pega y cae");
    await Flee(s3e, svc3e, B, o3e);
    Check(s3e.Phase == RaidPhase.Resolved && await Gold(A) == 950 && await Xp(A) == 0, "B se va y el combate termina: A (el caído) pagó la penalidad de la derrota (5 % del oro y su EXP)");
    Check(!svc3e.IsInAnyRaid(A) && !svc3e.IsInAnyRaid(B), "y los dos quedan libres");

    // ===========================================================================================================================================================================
    Console.WriteLine("\n== 4. El reloj de turnos ==");
    await ResetAll();
    RaidSettings.TurnTimeout = TimeSpan.FromMilliseconds(600);

    (RaidSession Session, FakeTarget Target, RaidSessionService Service) Lobby(params ulong[] ids)
    {
        var service = new RaidSessionService();
        var target = new FakeTarget();
        var session = new RaidSession
        {
            RaidId = Guid.NewGuid(), BossName = "Rey Jabalí", BossEmoji = "🐗", BossBaseHp = 100000, ZoneRank = 1, BossMaxHp = 100000, BossCurrentHp = 100000, BossDamage = 1, BossDropItemNames = [],
            BossGoldBonus = 0, BossXpBonus = 0, ZoneId = 1, ZoneName = "Praderas del Mate", StarterId = ids[0], ReplyTarget = target, Turns = new RaidTurnContext(users, events, service),
        };
        foreach (var id in ids)
        {
            session.Participants.Add(Member(id));
            service.TryRegisterParticipant(id, session.RaidId);
        }

        service.TryAdd(session);
        return (session, target, service);
    }

    var (s4, t4, svc4) = Lobby(A, B);
    await RaidModule.TryActivateAsync(s4, svc4, adventure);
    Check(s4.Phase == RaidPhase.Active && s4.TurnIndex == 0, "el combate arranca con A y se arma el reloj");
    int bossHp4 = s4.BossCurrentHp; // la activación escala la vida del jefe para 2 jugadores
    Check(await WaitFor(() => s4.Participants[0].MissedTurns == 1 && s4.TurnIndex == 1, 4000), "A no juega: pierde el turno y pasa a B");
    Check(await WaitFor(() => t4.Updates.Any(e => AllText(e).Contains("tardó demasiado") && AllText(e).Contains($"J{A % 100}")), 3000) && s4.BossCurrentHp == bossHp4, "se avisa, y el jefe NO le pegó (perder el turno no cuesta vida)");
    var o4 = new FakeOutput();
    await Attack(s4, svc4, B, o4);
    Check(s4.TurnIndex == 0 && s4.Participants[1].MissedTurns == 0, "B juega a tiempo: le toca a A y el reloj de B no dispara después");
    Check(await WaitFor(() => s4.Participants[0].MissedTurns == 2 || s4.Participants[0].HasFled, 4000), "A vuelve a no jugar: es el segundo vencimiento seguido");
    Check(await WaitFor(() => t4.Updates.Any(e => AllText(e).Contains("se retiró del combate")), 3000) && s4.Participants[0].HasFled && !svc4.IsInAnyRaid(A), "A se retira del combate (como si huyera) y queda libre");
    Check(s4.Phase == RaidPhase.Active && s4.TurnIndex == 1, "el combate sigue con B");
    Check(await WaitFor(() => s4.Phase == RaidPhase.Resolved && !svc4.IsInAnyRaid(B) && svc4.Peek(s4.RaidId) is null && t4.Updates.Any(e => AllText(e).Contains("abandonado")), 6000),
        "B tampoco juega dos turnos seguidos: se retira, no queda nadie y el combate termina como abandonado");

    // Jugar perdona el vencimiento anterior.
    await ResetAll();
    var (s4b, t4b, svc4b) = Lobby(A, B);
    await RaidModule.TryActivateAsync(s4b, svc4b, adventure);
    Check(await WaitFor(() => s4b.Participants[0].MissedTurns == 1 && s4b.TurnIndex == 1, 4000), "A pierde el primer turno");
    var o4b = new FakeOutput();
    await Attack(s4b, svc4b, B, o4b);
    await Attack(s4b, svc4b, A, o4b);
    Check(s4b.Participants[0].MissedTurns == 0 && s4b.TurnIndex == 1, "A juega su siguiente turno y el aviso se le perdona (vuelve a 0)");
    await Flee(s4b, svc4b, A, o4b);
    await Flee(s4b, svc4b, B, o4b);
    Check(s4b.Phase == RaidPhase.Resolved && svc4b.Peek(s4b.RaidId) is null, "al irse los dos, el combate se cierra");
    int updatesBefore = t4b.Updates.Count;
    await Task.Delay(1500); // un reloj suelto no tiene que revivir ni tocar una sesión cerrada
    Check(s4b.Phase == RaidPhase.Resolved && t4b.Updates.Count == updatesBefore, "y el reloj de turnos ya no dispara nada sobre un combate terminado");
    RaidSettings.TurnTimeout = originalTurn;

    // ===========================================================================================================================================================================
    Console.WriteLine("\n== 4b. El aviso de turno (una mención por turno; cada uno borra el anterior) ==");
    await ResetAll();
    var (s8, t8, svc8) = Lobby(A, B);
    await RaidModule.TryActivateAsync(s8, svc8, adventure);
    Check(t8.Pings.Count == 1 && t8.Pings[0].Contains($"<@{A}>") && t8.Pings[0].Contains("turno"), "al arrancar se avisa, con mención, a quien le toca primero");
    var o8 = new FakeOutput();
    await Attack(s8, svc8, B, o8);
    Check(t8.Pings.Count == 1 && t8.Deleted == 0, "un clic fuera de turno no manda avisos ni borra el que está");
    await Attack(s8, svc8, A, o8);
    Check(t8.Pings.Count == 2 && t8.Pings[1].Contains($"<@{B}>") && t8.Deleted == 1, "A juega: se avisa a B y se borra el aviso anterior (en el canal queda uno solo)");
    await Attack(s8, svc8, B, o8);
    Check(t8.Pings.Count == 3 && t8.Pings[2].Contains($"<@{A}>") && t8.Deleted == 2, "B juega: se avisa a A");
    await Flee(s8, svc8, A, o8); // le tocaba a A: se va y el turno pasa a B
    Check(t8.Pings.Count == 4 && t8.Pings[3].Contains($"<@{B}>") && t8.Deleted == 3, "si se va quien tenía el turno, se avisa al que sigue");
    await Attack(s8, svc8, B, o8); // B es el único que queda: el turno vuelve a B
    Check(t8.Pings.Count == 4 && t8.Deleted == 4 && s8.TurnIndex == 1, "el único que queda en pie no se avisa a sí mismo (y el aviso viejo se borra igual)");
    s8.BossCurrentHp = 1;
    await Attack(s8, svc8, B, o8); // lo remata
    Check(s8.Phase == RaidPhase.Resolved && t8.Pings.Count == 4, "al terminar el combate no se manda ningún aviso nuevo");

    await ResetAll();
    var (s8b, t8b, svc8b) = Lobby(A, B);
    await RaidModule.TryActivateAsync(s8b, svc8b, adventure);
    s8b.BossCurrentHp = 1;
    await Attack(s8b, svc8b, A, new FakeOutput()); // A remata al jefe en su turno
    Check(s8b.Phase == RaidPhase.Resolved && t8b.Pings.Count == 1 && t8b.Deleted == 1, "si el jefe cae, el último aviso de turno se borra (no queda colgado)");

    await ResetAll();
    RaidSettings.TurnTimeout = TimeSpan.FromMilliseconds(600);
    var (s8c, t8c, svc8c) = Lobby(A, B);
    await RaidModule.TryActivateAsync(s8c, svc8c, adventure);
    Check(await WaitFor(() => t8c.Pings.Count >= 2, 4000) && t8c.Pings[1].Contains($"<@{B}>"), "si A pierde el turno por el reloj, también se avisa a B");
    var o8c = new FakeOutput();
    await Flee(s8c, svc8c, A, o8c);
    await Flee(s8c, svc8c, B, o8c);
    RaidSettings.TurnTimeout = originalTurn;

    Console.WriteLine("\n== 4c. Comer en el jefe en equipo (una vez por jugador, gasta el turno; /use y la posada no sirven ahí) ==");
    await ResetAll();
    var mate = (await items.GetByNameAsync("Mate Amargo"))!;
    var empanada = (await items.GetByNameAsync("Empanada de Carne"))!;
    var banquete = (await items.GetByNameAsync("Asado Completo del Domingo en Familia"))!;
    async Task Give(ulong id, Item it, int q) => await db.ExecuteAsync(
        "INSERT INTO inventory (discord_id, item_id, quantity) VALUES (@id, @i, @q) ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)id, i = it.ItemId, q });
    async Task<int> Qty(ulong id, Item it) => await inventory.GetQuantityAsync(id, it.ItemId);
    async Task<string> Eat(IRaidSessionService svc, ulong id, string food) => await RaidModule.ExecuteEatAsync(svc, users, items, inventory, buffs, events, id, food);

    await Give(A, mate, 3); await Give(B, mate, 2);
    var (s9, t9, svc9) = Active(100000, 3, Member(A, hp: 100), Member(B, hp: 100));
    s9.Participants[0].CurrentHp = 20; s9.Participants[1].CurrentHp = 20;
    var a9 = s9.Participants[0];
    Check((await Eat(svc9, B, "Mate Amargo")).Contains("Todavía no es tu turno") && await Qty(B, mate) == 2 && s9.Participants[1].MealsEaten == 0, "comer fuera de turno se rechaza y no gasta la comida");
    Check((await Eat(svc9, D, "Mate Amargo")).Contains("No estás en ningún combate"), "quien no está en un combate en equipo recibe el aviso");
    string ate = await Eat(svc9, A, "Mate Amargo");
    Check(ate.Contains("Comiste") && a9.MealsEaten == 1 && await Qty(A, mate) == 2, "A come en su turno: se gasta UNA comida y queda anotado");
    Check(a9.CurrentHp > 20 && a9.CurrentHp <= 20 + mate.StatValue, $"cura de verdad la vida de la pelea (20 → {a9.CurrentHp}, con el golpe del jefe ya descontado)");
    Check(s9.TurnIndex == 1 && a9.TurnsTaken == 1 && !a9.Contributed, "comer gasta el turno (pasa a B) y no cuenta como pegarle al jefe");
    Check(AllText(t9.Updates.Last()).Contains("comió") && AllText(t9.Updates.Last()).Contains("comida usada") && AllText(t9.Updates.Last()).Contains("comida lista"),
        "el mensaje del combate lo cuenta y el roster marca quién ya comió y quién no");
    await Attack(s9, svc9, B, new FakeOutput()); // pasa B, vuelve a A
    Check((await Eat(svc9, A, "Mate Amargo")).Contains("Ya comiste") && await Qty(A, mate) == 2, "una sola vez por pelea (la segunda no se gasta)");

    await Attack(s9, svc9, A, new FakeOutput()); // le toca a B
    Check((await Eat(svc9, B, "Empanada de Carne")).Contains("No tenés") && s9.EatingTurnNumber == 0, "sin la comida no pasa nada y el turno queda libre");
    Check((await Eat(svc9, B, "Cosa Inexistente")).Contains("No encontré"), "una comida que no existe se avisa");
    s9.EatingTurnNumber = s9.TurnNumber; // simula «está comiendo»
    var o9 = new FakeOutput();
    int turnsBefore9 = s9.Participants[1].TurnsTaken;
    await Attack(s9, svc9, B, o9);
    Check(o9.Notes.Last().Contains("comiendo") && s9.Participants[1].TurnsTaken == turnsBefore9, "mientras está comiendo no puede atacar en el mismo turno");
    Check((await Eat(svc9, B, "Mate Amargo")).Contains("Ya estás comiendo"), "ni comer dos veces a la vez");
    s9.EatingTurnNumber = 0;
    Check((await Eat(svc9, B, "Mate Amargo")).Contains("Comiste") && await Qty(B, mate) == 1 && s9.TurnIndex == 0, "B come en su turno");

    // Banquete (Mítico) en el combate en equipo: cura Y da su +% de ataque en el acto, vale con la vida llena, ocupa la comida de la pelea y el buff queda guardado.
    await Give(A, banquete, 2);
    var buffInfo = (await buffs.GetItemBuffAsync(banquete.ItemId))!;
    var (sb, tb, svcb) = Active(100000, 3, Member(A, hp: 100, damage: 100), Member(B, hp: 100));
    var ab = sb.Participants[0]; // vida LLENA
    string ateB = await Eat(svcb, A, "Asado Completo del Domingo en Familia");
    Check(ateB.Contains("Comiste") && ateB.Contains("ataque") && ab.MealsEaten == 1 && await Qty(A, banquete) == 1, "un banquete se come en la pelea aunque estés al máximo de HP (lo que vale es el buff) y gasta la comida");
    Check(ab.Damage == AttackBuff.Apply(100, buffInfo.AttackPercent) && ab.AttackBuffPercent == buffInfo.AttackPercent, $"el ataque sube en el acto (100 → {ab.Damage}, +{buffInfo.AttackPercent} %)");
    Check((await buffs.GetActiveAttackAsync(A)) is { } stored && stored.AttackPercent == buffInfo.AttackPercent, "y el buff queda guardado como siempre (dura minutos)");
    Check(AllText(tb.Updates.Last()).Contains("de ataque"), "el mensaje del combate lo cuenta");
    ab.MealsEaten = 0; // otro banquete: no apila
    await Attack(sb, svcb, B, new FakeOutput());
    await Eat(svcb, A, "Asado Completo del Domingo en Familia");
    Check(ab.Damage == AttackBuff.Apply(100, buffInfo.AttackPercent), "comer un segundo banquete NO apila +% sobre +%");
    var prepared = await Prepare(new RaidSessionService(), A, M(B));
    Check(prepared.Session is { } ps && ps.Participants[0].AttackBuffPercent == buffInfo.AttackPercent, "quien entra al grupo con un banquete activo lo lleva anotado (y tampoco se apila si come otro)");

    var (s9b, _, svc9b) = Active(1000, 1, Member(A), Member(B));
    int mateBefore = await Qty(A, mate);
    Check((await Eat(svc9b, A, "Mate Amargo")).Contains("al máximo") && await Qty(A, mate) == mateBefore && s9b.Participants[0].MealsEaten == 0, "con la vida llena no se come (igual que en el jefe solo)");

    // El jefe lo derriba mientras come (el 5 % de esquive base obliga a repetir hasta que pase).
    bool koSeen = false;
    for (int attempt = 0; attempt < 60 && !koSeen; attempt++)
    {
        await Give(A, mate, 1);
        var (sk, _, svck) = Active(100000, 500, Member(A, hp: 5), Member(B));
        sk.Participants[0].CurrentHp = 1;
        await Eat(svck, A, "Mate Amargo");
        koSeen = sk.Participants[0].IsKnockedOut;
        if (koSeen)
        {
            Check(sk.Phase == RaidPhase.Active && sk.TurnIndex == 1, "si el jefe derriba a quien come, el combate sigue con el otro");
        }
    }

    Check(koSeen, "comer no es invulnerable: el jefe puede derribar a quien come");

    // Y si era el último en pie: la caída de todo el grupo, con la penalidad.
    bool wipeSeen = false;
    for (int attempt = 0; attempt < 60 && !wipeSeen; attempt++)
    {
        await db.ExecuteAsync("UPDATE users SET gold = 1000, xp = 50 WHERE discord_id = @id", new { id = (long)A });
        await Give(A, mate, 1);
        var (sw, tw, svcw) = Active(100000, 500, Member(A, hp: 5), Member(B));
        sw.Participants[0].CurrentHp = 1;
        sw.Participants[1].HasFled = true;
        await Eat(svcw, A, "Mate Amargo");
        wipeSeen = sw.Phase == RaidPhase.Resolved;
        if (wipeSeen)
        {
            Check(AllText(tw.Updates.Last()).Contains("cayó") && await Gold(A) == 950 && await Xp(A) == 0 && !svcw.IsInAnyRaid(A), "si el último en pie cae comiendo, es la caída del grupo: paga la penalidad y queda libre");
        }
    }

    Check(wipeSeen, "(se llegó a ver la caída del grupo comiendo)");

    // /use, /heal y la posada dentro del combate en equipo.
    await ResetAll();
    await Give(A, mate, 2);
    var (s10, _, svc10) = Active(100000, 3, Member(A, hp: 100), Member(B, hp: 100));
    s10.Participants[0].CurrentHp = 30;
    var viaUse = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, A, "Mate Amargo", svc10, events);
    Check(viaUse.PlainMessage is { } usedText && usedText.Contains("Comiste") && s10.Participants[0].MealsEaten == 1 && await Qty(A, mate) == 1, "/use en un jefe en equipo que está peleando hace comer EN el combate (ya no cura la base por las dudas)");
    var heal10 = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, A, null, svc10);
    Check(heal10.Title.Contains("No podés comer tranquilo"), "/heal queda bloqueado en el combate en equipo");
    var shop = sp.GetRequiredService<IShopRepository>();
    string talk = await TabernaModule.ExecuteActionAsync("taberna_eat", users, items, inventory, shop, buffs, combat, events, A, "Mate Amargo", null, svc10);
    Check(!talk.Contains("Comiste") && await Qty(A, mate) == 1 && s10.Participants[0].MealsEaten == 1, "y también comer desde la posada");
    var (sl, _, svcl) = Lobby(A, B);
    var lobbyUse = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, A, "Mate Amargo", svcl, events);
    Check(lobbyUse.Embed?.Title.Contains("al máximo") == true && await Qty(A, mate) == 1, "mientras el grupo solo está en la sala de espera, /use sigue siendo el de siempre");

    // El reloj de turnos y el aviso con la comida.
    await ResetAll();
    RaidSettings.TurnTimeout = TimeSpan.FromMilliseconds(600);
    var (s11, t11, svc11) = Lobby(A, B);
    s11.Participants[0].CurrentHp = 99000;
    await RaidModule.TryActivateAsync(s11, svc11, adventure);
    s11.EatingTurnNumber = s11.TurnNumber; // «está comiendo»
    await Task.Delay(1500);
    Check(s11.Participants[0].MissedTurns == 0 && s11.TurnIndex == 0, "el reloj de turnos no le saca el turno a quien está comiendo");
    s11.EatingTurnNumber = 0;
    Check((await Eat(svc11, A, "Empanada de Carne")).Contains("No tenés") && await WaitFor(() => s11.Participants[0].MissedTurns == 1, 4000), "si la comida no se gasta, el reloj se vuelve a armar y vence normal");
    await Flee(s11, svc11, A, new FakeOutput());
    await Flee(s11, svc11, B, new FakeOutput());
    RaidSettings.TurnTimeout = originalTurn;

    await ResetAll();
    await Give(A, mate, 1);
    var (s12, t12, svc12) = Lobby(A, B);
    s12.Participants[0].CurrentHp = 99000;
    await RaidModule.TryActivateAsync(s12, svc12, adventure);
    Check(t12.Pings.Count == 1 && t12.Pings[0].Contains($"<@{A}>"), "(arranca avisando a A)");
    await Eat(svc12, A, "Mate Amargo");
    Check(t12.Pings.Count == 2 && t12.Pings[1].Contains($"<@{B}>") && t12.Deleted == 1, "comer cambia el turno: se avisa a B y se borra el aviso anterior");
    await Flee(s12, svc12, A, new FakeOutput());
    await Flee(s12, svc12, B, new FakeOutput());

    Console.WriteLine("\n== 5. Victoria ==");
    await ResetAll();
    var (s5, t5, svc5) = Active(300, 1, Member(A, damage: 100), Member(B, damage: 100));
    await adventure.TryClaimCooldownAsync(A, boss.CommandName, boss.Duration, boss.RetryAfterFailure);
    await adventure.TryClaimCooldownAsync(B, boss.CommandName, boss.Duration, boss.RetryAfterFailure);
    long goldA = await Gold(A), goldB = await Gold(B);
    var o5 = new FakeOutput();
    for (int i = 0; i < 40 && s5.Phase == RaidPhase.Active; i++)
    {
        ulong turnOf = s5.Participants[s5.TurnIndex].DiscordId;
        await Attack(s5, svc5, turnOf, o5);
    }

    Check(s5.Phase == RaidPhase.Resolved && s5.BossCurrentHp <= 0 && svc5.Peek(s5.RaidId) is null, "pegando por turnos, el jefe cae y el combate termina");
    Check(s5.Participants.All(p => p.Contributed) && AllText(t5.Updates.Last()).Contains("derrotado"), "los dos le pegaron y la pantalla final lo anuncia");
    Check(await Gold(A) > goldA && await Gold(B) > goldB, "los dos cobran la recompensa entera");
    Check(await Left(A) is { } l5a && l5a > TimeSpan.FromHours(4.9) && await Left(B) is { } l5b && l5b > TimeSpan.FromHours(4.9), "y el cooldown del jefe de los dos queda en las 5 horas de una victoria");
    Check(!svc5.IsInAnyRaid(A) && !svc5.IsInAnyRaid(B), "y quedan libres");

    // ===========================================================================================================================================================================
    // La pantalla de un clic usa la interacción de ESE clic (el token de la primera dura 15 minutos y un combate por turnos puede pasarse): el final re-apunta la sesión a ella.
    var (s5b, _, _) = Active(1000, 1, Member(A), Member(B));
    var before5b = s5b.ReplyTarget;
    var click = FakeProxy.Make<Discord.IDiscordInteraction>(new());
    await new InteractionRaidTurnOutput(click).PrepareFinalAsync(s5b);
    Check(s5b.ReplyTarget is InteractionCombatReplyTarget && !ReferenceEquals(s5b.ReplyTarget, before5b), "el final de un combate en equipo se edita con la interacción del último clic (token fresco)");

    Console.WriteLine("\n== 6. Los comandos con el InteractionService y el CommandService reales ==");
    await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
    var bossCmd = interactions.SlashCommands.Single(c => c.Name == "boss");
    var companionNames = bossCmd.Parameters.Select(p => p.Name).ToList();
    Check(companionNames.SequenceEqual(["companero", "companero2", "companero3", "companero4", "companero5"]), "/boss tiene hasta 5 compañeros opcionales: " + string.Join(", ", companionNames));
    Check(bossCmd.Parameters.All(p => !p.IsRequired && p.Description.Length is >= 1 and <= 100 && p.Name.Length <= 32), "todos opcionales, con descripción de hasta 100 caracteres");
    Check(bossCmd.Description.Length is >= 1 and <= 100, $"la descripción del comando entra en 100 caracteres ({bossCmd.Description.Length})");
    Check(interactions.SlashCommands.All(c => c.Name != "raid"), "ya no existe el slash /raid");
    Check(interactions.SlashCommands.All(c => c.Description.Length is >= 1 and <= 100), "ninguna descripción de slash se pasa de 100 (si no, falla el registro de TODOS los comandos)");
    Discord.IComponentInteraction Click(string id) => FakeProxy.Make<Discord.IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<Discord.IComponentInteractionData>(new() { ["CustomId"] = id }) });
    foreach (var (prefix, method) in new[] { ("raid_join", "HandleJoinAsync"), ("raid_decline", "HandleDeclineAsync"), ("raid_start", "HandleStartNowAsync"), ("raid_cancel", "HandleCancelAsync"),
                 ("raid_eat", "HandleEatAsync"), ("raid_eatpick", "HandleEatPickAsync"), ("raid_attack", "HandleAttackAsync"), ("raid_ability", "HandleAbilityAsync"), ("raid_flee", "HandleFleeAsync") })
    {
        var routed = interactions.SearchComponentCommand(Click($"{prefix}:{Guid.NewGuid()}"));
        Check(routed.IsSuccess && routed.Command.Module.Name == "RaidModule" && routed.Command.MethodName == method, $"el botón «{prefix}:…» llega a RaidModule.{method}");
    }

    var texts = new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false });
    texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
    var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
    var textBoss = textMods.SelectMany(x => x.Commands).Single(c => c.Name == "boss");
    Check(textBoss.Parameters.Count == 1 && textBoss.Parameters[0].IsMultiple && textBoss.Parameters[0].Type == typeof(IUser), "aa boss acepta varias menciones (params IUser[])");
    Check(textMods.SelectMany(x => x.Commands).Count(c => c.Name == "raid") == 1 && textMods.SelectMany(x => x.Commands).Single(c => c.Name == "raid").Parameters.Count == 0, "aa raid sigue existiendo (solo para decir que ahora es aa boss @amigo)");
    foreach (var type in new[] { typeof(RaidModule), typeof(AdventureModule), typeof(TextCommandModule) })
    {
        bool built = true;
        try { ActivatorUtilities.CreateInstance(sp, type); } catch { built = false; }
        Check(built, $"el contenedor de DI arma {type.Name}");
    }

    // ===========================================================================================================================================================================
    Console.WriteLine("\n== 7. Textos ==");
    var help = HelpTopics.Resolve("boss");
    string helpText = AllText(help);
    Check(helpText.Contains("misma zona máxima") && helpText.Contains("por turnos") && !helpText.Contains("/raid"), "/info tema:boss explica el grupo (turnos, misma zona máxima) y ya no habla de /raid");
    Check(help.Fields.All(f => f.Value.Length <= 1024) && AllText(help).Length < 6000, "y cabe en los límites de un embed");
    Check(HelpTopics.Find("raid")?.Key == "boss" && HelpTopics.Find("grupo")?.Key == "boss", "«raid» y «grupo» llevan al tema del jefe");
    Check(CooldownCatalog.Boss.DisplayName == "Jefe" && ReminderCatalog.All.Single(k => k.Key == "boss").ReadyText.Contains("amigos") && !ReminderCatalog.All.Single(k => k.Key == "boss").ReadyText.Contains("/raid"), "/cd y el recordatorio hablan del jefe (solo o con amigos), no del raid");
    Check(AllText(RaidModule.BuildRaidCooldownEmbed(TimeSpan.FromMinutes(5))).Contains("solo o en equipo"), "el rechazo por cooldown dice que es el mismo del jefe solo");
}
finally
{
    RaidSettings.Configure(originalMin);
    RaidSettings.InviteWindow = originalWindow;
    RaidSettings.TurnTimeout = originalTurn;
    await Cleanup();
}

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs)
{
    var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < until)
    {
        if (condition()) return true;
        await Task.Delay(25);
    }

    return condition();
}

sealed class FakeTarget : ICombatReplyTarget
{
    private readonly List<string> _pings = [];
    private int _deleted;
    // Los avisos de turno que mandó (con la mención) y cuántos se borraron después.
    public List<string> Pings { get { lock (_pings) return _pings.ToList(); } }
    public int Deleted => _deleted;
    public Task<Func<Task>?> SendPingAsync(string text)
    {
        lock (_pings) _pings.Add(text);
        Func<Task>? deleter = () => { Interlocked.Increment(ref _deleted); return Task.CompletedTask; };
        return Task.FromResult(deleter);
    }

    private readonly List<Embed> _updates = [];
    // Una copia: el reloj de turnos escribe desde otro hilo.
    public List<Embed> Updates { get { lock (_updates) return _updates.ToList(); } }
    public int Cleared { get; private set; }
    public Task UpdateAsync(Embed embed, MessageComponent components) { lock (_updates) _updates.Add(embed); return Task.CompletedTask; }
    public Task ClearComponentsAsync() { Cleared++; return Task.CompletedTask; }
}

sealed class FakeOutput : IRaidTurnOutput
{
    public List<string> Notes { get; } = [];
    public List<Embed> Shown { get; } = [];
    public Task NoteAsync(string text) { Notes.Add(text); return Task.CompletedTask; }
    public Task ShowAsync(RaidSession session, Embed embed, MessageComponent components) { Shown.Add(embed); return Task.CompletedTask; }
    public Task PrepareFinalAsync(RaidSession session) => Task.CompletedTask;
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
