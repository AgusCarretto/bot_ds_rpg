using System.Reflection;

// Reproduce el bug "me quedé trabado: no puedo unirme ni cazar después de /raid" contra el .dll del
// bot (reflection, sin referenciar tipos). Corre igual contra el build viejo (tiene que FALLAR) y
// contra el arreglado (tiene que PASAR).
string dll = args.Length > 0 && File.Exists(args[0]) ? args[0] : Path.Combine(AppContext.BaseDirectory, "bot_ds_rpg.dll");
var asm = Assembly.LoadFrom(dll);
Type T(string n) => asm.GetType(n) ?? throw new Exception("no existe " + n);

var sessionType = T("BotDsRpg.Services.RaidSession");
var svcType = T("BotDsRpg.Services.RaidSessionService");
var phaseType = T("BotDsRpg.Services.RaidPhase");
var raidModule = T("RaidModule");
var settings = T("BotDsRpg.Services.RaidSettings");

object Phase(string n) => Enum.Parse(phaseType, n);
void Set(object o, string p, object? v) => o.GetType().GetProperty(p)!.SetValue(o, v);
V Get<V>(object o, string p) => (V)o.GetType().GetProperty(p)!.GetValue(o)!;
void Configure(int min) => settings.GetMethod("Configure")!.Invoke(null, [min]);
bool InRaid(object svc, ulong id) => (bool)svcType.GetMethod("IsInAnyRaid")!.Invoke(svc, [id])!;
object NewSvc() => Activator.CreateInstance(svcType)!;
async Task<object?> Await(object task)
{
    await (Task)task;
    return task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null;
}

const ulong Starter = 628696431355625472UL;
int fakeZone = 1; // zona actual del jugador de mentira (1 = la primera por dificultad)

// ---- fakes ----
object User()
{
    var u = Activator.CreateInstance(T("BotDsRpg.Models.User"))!;
    Set(u, "DiscordId", (long)Starter); Set(u, "Class", "Hechicero"); Set(u, "Level", 6);
    Set(u, "MaxHp", 145); Set(u, "CurrentHp", 132); Set(u, "CurrentZoneId", fakeZone);
    return u;
}

object Zone()
{
    var z = Activator.CreateInstance(T("BotDsRpg.Models.Zone"))!;
    Set(z, "ZoneId", 1); Set(z, "Name", "Praderas del Mate");
    return z;
}

object Boss() => Activator.CreateInstance(T("BotDsRpg.GameData.MonsterTemplate"), "Rey Jabalí", "👑🐗", 100, 140, 20, 32, new List<string>(), 50, 45, null)!;

object Repo(string iface, Func<string, object?[], object?> h) => FakeProxy.Create(T(iface), h);
object UserRepo() => Repo("BotDsRpg.Repositories.IUserRepository", (n, a) => n == "GetOrCreateUserAsync" ? User() : throw new NotSupportedException(n));
object ItemRepo() => Repo("BotDsRpg.Repositories.IItemRepository", (n, a) => n == "GetByIdAsync" ? null : throw new NotSupportedException(n));
object MonsterRepo() => Repo("BotDsRpg.Repositories.IMonsterRepository", (n, a) => n == "GetBossByZoneAsync" ? Boss() : throw new NotSupportedException(n));
object ListOf(Type t, params object[] items)
{
    var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t))!;
    foreach (var i in items) list.Add(i);
    return list;
}

object ZoneAt(int id, int minLevel) { var z = Activator.CreateInstance(T("BotDsRpg.Models.Zone"))!; Set(z, "ZoneId", id); Set(z, "Name", $"Zona {id}"); Set(z, "MinLevel", minLevel); return z; }
object ZoneRepo() => Repo("BotDsRpg.Repositories.IZoneRepository", (n, a) => n switch
{
    "GetByIdAsync" => ZoneAt(fakeZone, fakeZone == 1 ? 1 : 5),
    "GetAllAsync" => ListOf(T("BotDsRpg.Models.Zone"), ZoneAt(1, 1), ZoneAt(2, 5)),
    _ => throw new NotSupportedException(n),
});
object AdvRepo(bool throwOnClaim = false) => Repo("BotDsRpg.Repositories.IAdventureRepository", (n, a) =>
    n == "TryClaimCooldownAsync" ? (throwOnClaim ? throw new InvalidOperationException("DB caída (simulada)") : true) : throw new NotSupportedException(n));
object Reply() => Repo("BotDsRpg.Services.ICombatReplyTarget", (n, a) => n == "UpdateAsync" ? null : throw new NotSupportedException(n));

// Igual que RaidModule.HandleRaidAsync / TextCommandModule.RaidAsync: construye la sesión, la agrega y
// registra a quien arrancó.
async Task<object> StartRaid(object svc)
{
    var m = raidModule.GetMethod("BuildSessionAsync")!;
    var callArgs = m.GetParameters().Select(p => p.ParameterType.Name switch
    {
        "IUserRepository" => UserRepo(),
        "IItemRepository" => ItemRepo(),
        "IMonsterRepository" => MonsterRepo(),
        "IZoneRepository" => ZoneRepo(),
        "IBuffRepository" => Repo("BotDsRpg.Repositories.IBuffRepository", (n, a) => n == "GetActiveAttackAsync" ? null : throw new NotSupportedException(n)),
        "IPetRepository" or "IPlayerBonusService" => null,
        "UInt64" => (object)Starter,
        "String" => "Agus",
        _ => throw new Exception("param " + p.ParameterType.Name),
    }).ToArray();
    var session = (await Await(m.Invoke(null, callArgs)!))!;
    bool added = (bool)svcType.GetMethod("TryAdd")!.Invoke(svc, [session])!;
    bool reg = (bool)svcType.GetMethod("TryRegisterParticipant")!.Invoke(svc, [Starter, Get<Guid>(session, "RaidId")])!;
    if (!added || !reg) throw new Exception("no se pudo registrar el raid");
    Set(session, "ReplyTarget", Reply());
    return session;
}

async Task Activate(object session, object svc, object adv)
{
    try
    {
        await Await(raidModule.GetMethod("TryActivateAsync")!.Invoke(null, [session, svc, adv])!);
    }
    catch (Exception)
    {
        // La excepción simulada puede escapar; lo que importa es el estado en el que queda todo.
    }
}

int fails = 0;
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALLA")} {name} — {detail}");
    if (!ok) fails++;
}

// T1: el servicio libera al jugador aunque NO esté en session.Participants (defensa en profundidad).
{
    var svc = NewSvc();
    var s = Activator.CreateInstance(sessionType)!;
    var id = Guid.NewGuid();
    Set(s, "RaidId", id); Set(s, "StarterId", Starter);
    svcType.GetMethod("TryAdd")!.Invoke(svc, [s]);
    svcType.GetMethod("TryRegisterParticipant")!.Invoke(svc, [Starter, id]);
    svcType.GetMethod("Remove")!.Invoke(svc, [id]);
    Check("T1 Remove() libera a un jugador registrado que no está en Participants", !InRaid(svc, Starter), $"IsInAnyRaid tras Remove = {InRaid(svc, Starter)}");
}

// T2: quien arranca el raid queda ANOTADO (roster) desde el primer momento.
{
    var svc = NewSvc(); Configure(1);
    var session = await StartRaid(svc);
    var list = Get<System.Collections.IList>(session, "Participants");
    bool starterIn = list.Count == 1 && (ulong)list[0]!.GetType().GetProperty("DiscordId")!.GetValue(list[0])! == Starter;
    Check("T2 quien arranca el raid ya está anotado", starterIn, $"Participants.Count = {list.Count}");
}

// T3: con mínimo 1 (modo prueba) el raid arranca solo.
{
    var svc = NewSvc(); Configure(1);
    var session = await StartRaid(svc);
    await Activate(session, svc, AdvRepo());
    var phase = Get<object>(session, "Phase");
    Check("T3 con mínimo 1, 'Empezar ya' arranca (Active)", phase.Equals(Phase("Active")), $"Phase = {phase}");
}

// T4: con mínimo 2 y un solo jugador: se cancela Y el jugador queda LIBRE (no trabado).
{
    var svc = NewSvc(); Configure(2);
    var session = await StartRaid(svc);
    await Activate(session, svc, AdvRepo());
    var phase = Get<object>(session, "Phase");
    Check("T4a con mínimo 2 y 1 jugador el raid se cancela", phase.Equals(Phase("Resolved")), $"Phase = {phase}");
    Check("T4b ...y el jugador queda libre para /hunt (no está en ningún raid)", !InRaid(svc, Starter), $"IsInAnyRaid = {InRaid(svc, Starter)}");
    var peek = svcType.GetMethod("Peek")!.Invoke(svc, [Get<Guid>(session, "RaidId")]);
    Check("T4c ...y la sesión sale del servicio", peek is null, $"Peek = {(peek is null ? "null" : "todavía existe")}");
}

// T5 (endurecimiento): si la activación EXPLOTA a la mitad (ej. la base se cae reclamando el cooldown),
// nadie puede quedar trabado en un raid fantasma.
{
    var svc = NewSvc(); Configure(1);
    var session = await StartRaid(svc);
    await Activate(session, svc, AdvRepo(throwOnClaim: true));
    Check("T5 si la activación falla, el jugador queda libre", !InRaid(svc, Starter), $"IsInAnyRaid = {InRaid(svc, Starter)}");
}

// T6: el HP del jefe se escala AL ARRANCAR según cuántos jugadores terminan peleando
// (base x 1.76 x (1 + 0.5 por jugador extra); eran 2.2 hasta la v0.17.1, que subió la vida guardada de los jefes x1.25 y dividió esto por lo mismo), y el jefe arranca con la vida llena.
foreach (int n in new[] { 1, 2, 3, 4 })
{
    var svc = NewSvc(); Configure(1);
    var session = await StartRaid(svc);
    var participantType = T("BotDsRpg.Services.RaidParticipant");
    var list = Get<System.Collections.IList>(session, "Participants");
    for (int extra = 2; extra <= n; extra++)
    {
        var p = Activator.CreateInstance(participantType)!;
        Set(p, "DiscordId", (ulong)extra); Set(p, "DisplayName", $"J{extra}"); Set(p, "PlayerClass", "Guerrero");
        Set(p, "MaxHp", 100); Set(p, "CurrentHp", 100); Set(p, "StartingHp", 100);
        list.Add(p);
        svcType.GetMethod("TryRegisterParticipant")!.Invoke(svc, [(ulong)extra, Get<Guid>(session, "RaidId")]);
    }

    await Activate(session, svc, AdvRepo());
    int baseHp = Get<int>(session, "BossBaseHp");
    int expected = (int)Math.Round(baseHp * 1.76 * (1 + 0.5 * (n - 1)));
    int max = Get<int>(session, "BossMaxHp");
    int cur = Get<int>(session, "BossCurrentHp");
    Check($"T6 N={n}: HP del jefe escalado al arrancar", max == expected && cur == max && Get<object>(session, "Phase").Equals(Phase("Active")),
        $"base {baseHp} -> max {max} (esperado {expected}), actual {cur}");
}

// T7: el daño del jefe de raid es 1.35x el del /boss. El jefe de mentira de este arnés (Boss()) tiene
// daño 20-32 => tiene que quedar en 27-43 (20*1.35 = 27, 32*1.35 = 43.2).
{
    Configure(1);
    var dmgs = new HashSet<int>();
    for (int i = 0; i < 300; i++) dmgs.Add(Get<int>(await StartRaid(NewSvc()), "BossDamage"));
    Check("T7 daño del jefe de raid = 1.35x el del rango base (20-32 => 27-43)", dmgs.Min() == 27 && dmgs.Max() == 43, $"rango observado {dmgs.Min()}-{dmgs.Max()}");
}


// T8: los multiplicadores del raid dependen de la POSICIÓN de la zona: la primera conserva x2.2 de HP / x1.35 de daño
// (calibrados con el Rey Jabalí) y de la segunda en adelante usa x1.5 / x1.1 (los jefes ya están en el estándar de
// la escalera; con x2.2 el raid de Zona 2 quedaba imposible: 0-3% de victoria).
foreach (var (zone, hpMul, dmgMul) in new[] { (1, 1.76, 1.35), (2, 1.2, 1.1) })
{
    fakeZone = zone;
    var svc = NewSvc(); Configure(1);
    var session = await StartRaid(svc);
    await Activate(session, svc, AdvRepo());
    int baseHp = Get<int>(session, "BossBaseHp");
    int rank = Get<int>(session, "ZoneRank");
    int expectedHp = (int)Math.Round(baseHp * hpMul);
    int max = Get<int>(session, "BossMaxHp");
    var dmgs2 = new HashSet<int>();
    for (int i = 0; i < 300; i++) dmgs2.Add(Get<int>(await StartRaid(NewSvc()), "BossDamage"));
    int dmgLo = (int)Math.Round(20 * dmgMul), dmgHi = (int)Math.Round(32 * dmgMul);
    Check($"T8 zona {zone} (posición {rank}): HP x{hpMul} y daño x{dmgMul}", rank == zone && max == expectedHp && dmgs2.Min() == dmgLo && dmgs2.Max() == dmgHi,
        $"HP base {baseHp} -> {max} (esperado {expectedHp}); daño {dmgs2.Min()}-{dmgs2.Max()} (esperado {dmgLo}-{dmgHi})");
}
fakeZone = 1;
Console.WriteLine(fails == 0 ? "\nRESULTADO: OK" : $"\nRESULTADO: FALLA ({fails})");

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

    protected override object? Invoke(MethodInfo? m, object?[]? a)
    {
        object? r = _h(m!.Name, a ?? []);
        var rt = m.ReturnType;
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(rt.GetGenericArguments()[0]).Invoke(null, [r]);
        return r;
    }
}
