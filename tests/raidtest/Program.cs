using System.Reflection;

// Arnés de concurrencia para RaidModule.ResolveParticipantTurn (private static): lo invoca por
// reflection contra el bot_ds_rpg.dll REAL, replicando el wrapper de lock del handler de "Atacar".
// Invariante a verificar: por raid, exactamente UN resultado terminal (Victory/Wipe), sin importar
// cuántos hilos peguen a la vez.
string dllPath = args.Length > 0 && File.Exists(args[0]) ? args[0] : Path.Combine(AppContext.BaseDirectory, "bot_ds_rpg.dll");
var asm = Assembly.LoadFrom(dllPath);

var sessionType = asm.GetType("BotDsRpg.Services.RaidSession")!;
var participantType = asm.GetType("BotDsRpg.Services.RaidParticipant")!;
var phaseType = asm.GetType("BotDsRpg.Services.RaidPhase")!;
var raidModule = asm.GetType("RaidModule")!;
var resolveTurn = raidModule.GetMethod("ResolveParticipantTurn", BindingFlags.NonPublic | BindingFlags.Static)!;
var passivesFor = asm.GetType("BotDsRpg.GameData.ClassPassives")!.GetMethod("For")!;

object PhaseValue(string name) => Enum.Parse(phaseType, name);

void Set(object target, string prop, object? value) => target.GetType().GetProperty(prop)!.SetValue(target, value);
T Get<T>(object target, string prop) => (T)target.GetType().GetProperty(prop)!.GetValue(target)!;

object NewSession(int bossHp, int bossDamage)
{
    var s = Activator.CreateInstance(sessionType)!;
    Set(s, "RaidId", Guid.NewGuid());
    Set(s, "BossName", "Jefe de prueba");
    Set(s, "BossEmoji", "👑");
    Set(s, "BossBaseHp", bossHp);
    Set(s, "ZoneRank", 1);
    Set(s, "BossMaxHp", bossHp);
    Set(s, "BossCurrentHp", bossHp);
    Set(s, "BossDamage", bossDamage);
    Set(s, "BossDropItemNames", new List<string>());
    Set(s, "BossGoldBonus", 0);
    Set(s, "BossXpBonus", 0);
    Set(s, "ZoneId", 1);
    Set(s, "ZoneName", "Zona de prueba");
    Set(s, "StarterId", 1UL);
    Set(s, "Phase", PhaseValue("Active"));
    return s;
}

object NewParticipant(ulong id, int hp, int damage, int defense)
{
    var p = Activator.CreateInstance(participantType)!;
    Set(p, "DiscordId", id);
    Set(p, "DisplayName", $"J{id}");
    Set(p, "Damage", damage);
    Set(p, "Defense", defense);
    Set(p, "Level", 5);
    // Las 4 clases rotan entre los participantes: cubre las cuatro habilidades bajo concurrencia.
    string cls = new[] { "Guerrero", "Hechicero", "Ninja", "Arquero" }[(int)((id - 1) % 4)];
    Set(p, "PlayerClass", cls);
    Set(p, "Passives", passivesFor.Invoke(null, [cls])!);
    Set(p, "MaxHp", hp);
    Set(p, "StartingHp", hp);
    Set(p, "CurrentHp", hp);
    return p;
}

// Replica del cuerpo del handler de "Atacar" bajo lock; devuelve el nombre del Kind del resultado.
string Attack(object session, object participant, bool useAbility)
{
    object lockObj = Get<object>(session, "Lock");
    object outcome;
    lock (lockObj)
    {
        if (!Get<object>(session, "Phase").Equals(PhaseValue("Active")))
        {
            return "NotActive";
        }

        if (!Get<bool>(participant, "IsActive"))
        {
            return "ParticipantInactive";
        }

        outcome = resolveTurn.Invoke(null, [session, participant, useAbility])!;
    }

    string kind = outcome.GetType().GetProperty("Kind")!.GetValue(outcome)!.ToString()!;

    // Emula el viejo ResolveVictoryAsync/ResolveWipeAsync, que marcaba Resolved DESPUÉS de soltar el
    // lock. Con el fix esto es un no-op (ya quedó Resolved adentro); sin el fix es la ventana del bug.
    if (kind is "Victory" or "Wipe")
    {
        Thread.Yield();
        lock (lockObj)
        {
            Set(session, "Phase", PhaseValue("Resolved"));
        }
    }

    return kind;
}

int trials = 3000;
int badTrials = 0;
int doubleVictory = 0;
int bossHpNegative = 0;
var hist = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

for (int t = 0; t < trials; t++)
{
    var session = NewSession(bossHp: 400, bossDamage: 20);
    var participants = Enumerable.Range(1, 6).Select(i => NewParticipant((ulong)i, hp: 300, damage: 35, defense: 5)).ToList();
    var list = Get<System.Collections.IList>(session, "Participants");
    foreach (var p in participants) list.Add(p);

    int terminal = 0;
    var tasks = new List<Task>();

    // 12 hilos: 2 por participante (un jugador que clickea dos veces seguidas es realista).
    foreach (var p in participants.Concat(participants))
    {
        var participant = p;
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                // Mitad de los clicks piden la habilidad (si está en cooldown vuelve AbilityUnavailable, no terminal).
                string kind = Attack(session, participant, Random.Shared.Next(2) == 0);
                if (kind is "Victory" or "Wipe") Interlocked.Increment(ref terminal);
                if (kind is "NotActive" or "ParticipantInactive" or "Victory" or "Wipe") break;
            }
        }));
    }

    Task.WaitAll(tasks.ToArray());

    hist.AddOrUpdate(terminal, 1, (_, v) => v + 1);
    if (terminal != 1) badTrials++;
    if (terminal > 1) doubleVictory++;
    if (Get<int>(session, "BossCurrentHp") < 0) bossHpNegative++;
    foreach (var p in participants)
    {
        if (Get<int>(p, "CurrentHp") < 0) bossHpNegative++;

        // Estado de habilidad siempre dentro de rango (struct AbilityState: cooldown 0..3, efecto 0..2).
        object ab = Get<object>(p, "Ability");
        int cd = (int)ab.GetType().GetProperty("CooldownRemaining")!.GetValue(ab)!;
        int fx = (int)ab.GetType().GetProperty("EffectTurnsRemaining")!.GetValue(ab)!;
        if (cd is < 0 or > 3 || fx is < 0 or > 2) bossHpNegative++;
    }
}

Console.WriteLine($"DLL: {dllPath}");
Console.WriteLine($"Raids simulados: {trials} (6 participantes, 12 hilos concurrentes cada uno)");
Console.WriteLine($"Resultados terminales por raid -> {string.Join(", ", hist.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value} raids"))}");
Console.WriteLine($"Raids con != 1 resultado terminal : {badTrials}");
Console.WriteLine($"Raids con victoria/derrota DOBLE  : {doubleVictory}");
Console.WriteLine($"HP negativos detectados           : {bossHpNegative}");
Console.WriteLine(badTrials == 0 && bossHpNegative == 0 ? "RESULTADO: OK" : "RESULTADO: FALLA");

// ================= Prueba 2: activación concurrente del lobby =================
// "Empezar ya" y el timeout del lobby pueden coincidir: TryActivateAsync se ejecuta dos veces en
// paralelo. Invariante: el raid termina Active (no cancelado), con todos los participantes, y el
// mensaje del combate se actualiza UNA sola vez.
var activate = raidModule.GetMethod("TryActivateAsync", BindingFlags.Public | BindingFlags.Static)!;
var advRepoType = asm.GetType("BotDsRpg.Repositories.IAdventureRepository")!;
var replyType = asm.GetType("BotDsRpg.Services.ICombatReplyTarget")!;
var raidSvcType = asm.GetType("BotDsRpg.Services.RaidSessionService")!;

int activationFailures = 0;
int activationTrials = 2000;

for (int t = 0; t < activationTrials; t++)
{
    var session = NewSession(bossHp: 400, bossDamage: 20);
    Set(session, "Phase", PhaseValue("Lobby"));
    var list = Get<System.Collections.IList>(session, "Participants");
    foreach (var id in new ulong[] { 1, 2, 3 }) list.Add(NewParticipant(id, 300, 35, 5));

    var claimed = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();
    int updates = 0;

    var advRepo = FakeProxy.Create(advRepoType, (name, a) =>
    {
        if (name == "TryClaimCooldownAsync")
        {
            // Como el upsert guardado real: el primer reclamo de un jugador gana, los siguientes pierden.
            return Task.Run(() => claimed.TryAdd((ulong)a[0]!, true));
        }
        throw new NotSupportedException(name);
    });
    var reply = FakeProxy.Create(replyType, (name, a) =>
    {
        if (name == "UpdateAsync") { Interlocked.Increment(ref updates); return Task.CompletedTask; }
        throw new NotSupportedException(name);
    });
    Set(session, "ReplyTarget", reply);
    var svc = Activator.CreateInstance(raidSvcType)!;

    var t1 = (Task)activate.Invoke(null, [session, svc, advRepo])!;
    var t2 = (Task)activate.Invoke(null, [session, svc, advRepo])!;
    await Task.WhenAll(t1, t2);

    bool ok = Get<object>(session, "Phase").Equals(PhaseValue("Active"))
        && Get<System.Collections.IList>(session, "Participants").Count == 3
        && updates == 1;
    if (!ok) activationFailures++;
}

Console.WriteLine();
Console.WriteLine($"Prueba 2 — activaciones concurrentes: {activationTrials}, fallos: {activationFailures}");
Console.WriteLine(activationFailures == 0 ? "RESULTADO 2: OK" : "RESULTADO 2: FALLA");

// DispatchProxy mínimo para simular interfaces del bot sin referenciarlas en compilación.
public class FakeProxy : DispatchProxy
{
    private Func<string, object?[], object?> _handler = null!;

    public static object Create(Type iface, Func<string, object?[], object?> handler)
    {
        var create = typeof(DispatchProxy).GetMethod("Create", 2, [])!.MakeGenericMethod(iface, typeof(FakeProxy));
        var proxy = create.Invoke(null, null)!;
        ((FakeProxy)proxy)._handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!.Name, args ?? []);
}
