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

// v0.18.4 (dos sugerencias de los jugadores del foro #sugerencias):
//   1) un raid que se cancela (o falla al arrancar) NO puede dejar el cooldown del jefe corriendo: TryActivateAsync reclamaba el cooldown de cada participante antes de saber si el raid arrancaba y no lo soltaba.
//   2) /cd muestra cuándo se puede alimentar a las mascotas.
// Contra la base real con usuarios descartables, el RaidSessionService y el AdventureRepository de verdad y un destino de mensajes falso.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var cooldowns = new CooldownRepository(factory);
var adventure = new AdventureRepository(factory);
var pets = new PetRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong A = 999000000000070001UL, B = 999000000000070002UL, C = 999000000000070003UL;
ulong[] all = [A, B, C];
var boss = CooldownCatalog.Boss;

async Task Reset()
{
    foreach (var id in all)
    {
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
        await users.GetOrCreateUserAsync(id);
    }
}
async Task BusyBoss(ulong id) => await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'boss', now()) ON CONFLICT (discord_id, command_name) DO UPDATE SET last_executed_at = now()", new { id = (long)id });
async Task<TimeSpan?> Left(ulong id) => await cooldowns.GetRemainingAsync(id, boss.CommandName, boss.Duration);
async Task<bool> HasRow(ulong id) => await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM cooldowns WHERE discord_id = @id AND command_name = 'boss')", new { id = (long)id });

Console.WriteLine("== 1. ReleaseCooldownAsync (el repositorio) ==");
await Reset();
Check(await adventure.TryClaimCooldownAsync(A, boss.CommandName, boss.Duration, boss.RetryAfterFailure) && await Left(A) is { } l1 && l1 > TimeSpan.FromMinutes(29) && l1 <= TimeSpan.FromMinutes(30), "reclamar el cooldown del jefe deja la espera corta de 30 minutos");
await adventure.ReleaseCooldownAsync(A, boss.CommandName, boss.Duration);
Check(await Left(A) is null && !await HasRow(A), "soltarlo: el cooldown desaparece");
Check(await adventure.TryClaimCooldownAsync(A, boss.CommandName, boss.Duration, boss.RetryAfterFailure), "y se puede volver a reclamar enseguida (el jefe queda libre)");
await adventure.ReleaseCooldownAsync(A, boss.CommandName, boss.Duration);
await adventure.ReleaseCooldownAsync(A, boss.CommandName, boss.Duration);
Check(!await HasRow(A), "soltar dos veces (o sin que haya nada) no falla ni cambia nada");
await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'boss', now() - interval '6 hours')", new { id = (long)A });
await adventure.ReleaseCooldownAsync(A, boss.CommandName, boss.Duration);
Check(await HasRow(A), "un cooldown que ya había vencido (hace 6 h) no se toca: solo se suelta el que sigue corriendo");
await db.ExecuteAsync("INSERT INTO cooldowns (discord_id, command_name, last_executed_at) VALUES (@id, 'hunt', now()) ON CONFLICT (discord_id, command_name) DO UPDATE SET last_executed_at = now()", new { id = (long)A });
await BusyBoss(A);
await adventure.ReleaseCooldownAsync(A, boss.CommandName, boss.Duration);
Check(!await HasRow(A) && await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM cooldowns WHERE discord_id = @id AND command_name = 'hunt')", new { id = (long)A }), "solo suelta el comando pedido: el cooldown de /hunt sigue ahí");

Console.WriteLine("\n== 2. El raid, con el servicio de sesiones real ==");
RaidSettings.Configure(2);
var passives = ClassPassives.For("Guerrero");
RaidParticipant Member(ulong id) => new() { DiscordId = id, DisplayName = $"U{id % 10}", Damage = 20, Defense = 5, Level = 10, PlayerClass = "Guerrero", Passives = passives, MaxHp = 200, StartingHp = 200, CurrentHp = 200 };
(RaidSession Session, FakeTarget Target, RaidSessionService Service) NewRaid(params ulong[] members)
{
    var service = new RaidSessionService();
    var target = new FakeTarget();
    var session = new RaidSession
    {
        RaidId = Guid.NewGuid(), BossName = "Rey Jabalí", BossEmoji = "🐗", BossBaseHp = 200, ZoneRank = 1, BossMaxHp = 200, BossDamage = 20, BossDropItemNames = [], BossGoldBonus = 0, BossXpBonus = 0,
        ZoneId = 1, ZoneName = "Praderas del Mate", StarterId = members[0], ReplyTarget = target,
    };
    foreach (var id in members)
    {
        session.Participants.Add(Member(id));
        service.TryRegisterParticipant(id, session.RaidId);
    }

    service.TryAdd(session);
    return (session, target, service);
}

// Caso del reporte de Adiofh: arma el raid, el otro no puede entrar (tiene el cooldown del jefe ocupado) y el raid se cancela por falta de gente.
await Reset();
await BusyBoss(B);
var (s1, t1, svc1) = NewRaid(A, B);
await RaidModule.TryActivateAsync(s1, svc1, adventure);
Check(s1.Phase == RaidPhase.Resolved && t1.Updates.Count == 1 && t1.Updates[0].Title.Contains("cancelado"), "2 anotados, uno con el cooldown ocupado: el raid se cancela por falta de gente");
Check(await Left(A) is null && !await HasRow(A), "EL ARREGLO: quien armó el raid queda con el /boss LIBRE (antes le quedaban 30 minutos de cooldown por un raid que nunca peleó)");
Check(await Left(B) is { } lb && lb > TimeSpan.FromMinutes(299), "el cooldown ajeno (el de B, que ya estaba corriendo) no se toca");
Check(svc1.RaidIdOf(A) is null && svc1.RaidIdOf(B) is null, "los dos jugadores quedan libres del raid (pueden cazar, viajar o armar otro)");
Check(t1.Updates[0].Description.Contains("No se gastó el cooldown de Jefe de nadie") && t1.Updates[0].Description.Contains("1 confirmado"), "el mensaje de cancelación lo dice");
Check(await adventure.TryClaimCooldownAsync(A, boss.CommandName, boss.Duration, boss.RetryAfterFailure), "y A puede pelear el jefe solo ahora mismo");

// Un raid que sí arranca no cambia: los dos pelean y los dos tienen la espera corta corriendo.
await Reset();
var (s2, t2, svc2) = NewRaid(A, B);
await RaidModule.TryActivateAsync(s2, svc2, adventure);
Check(s2.Phase == RaidPhase.Active && t2.Updates.Count == 1 && t2.Updates[0].Title.Contains("Rey Jabalí") && !t2.Updates[0].Title.Contains("cancelado"), "con dos jugadores libres el raid arranca como siempre");
Check(await Left(A) is { } la && la > TimeSpan.FromMinutes(29) && await Left(B) is { } lb2 && lb2 > TimeSpan.FromMinutes(29), "y los dos tienen el cooldown del jefe corriendo (la espera corta de una derrota: la victoria la alarga)");

// Tres anotados, uno ocupado: arranca con los dos libres (el ocupado sale), sin soltar nada.
await Reset();
await BusyBoss(B);
var (s3, t3, svc3) = NewRaid(A, B, C);
await RaidModule.TryActivateAsync(s3, svc3, adventure);
Check(s3.Phase == RaidPhase.Active && s3.Participants.Select(p => p.DiscordId).OrderBy(x => x).SequenceEqual([A, C]) && t3.Updates[0].Description!.Contains("no pudo"), "con tres y uno ocupado, arranca con los dos libres y avisa del que no pudo sumarse");
Check(await Left(A) is { } la3 && la3 < TimeSpan.FromMinutes(31) && await Left(C) is { } lc3 && lc3 < TimeSpan.FromMinutes(31) && await Left(B) is { } lb3 && lb3 > TimeSpan.FromMinutes(299), "A y C tienen su espera corta y B conserva la suya");

// La activación explota a mitad de camino (no se puede editar el mensaje): se cierra el raid y se sueltan los cooldowns reclamados.
await Reset();
var (s4, t4, svc4) = NewRaid(A, B);
t4.ThrowAlways = true;
Exception? boom = null;
try { await RaidModule.TryActivateAsync(s4, svc4, adventure); } catch (Exception ex) { boom = ex; }
Check(boom is not null && s4.Phase == RaidPhase.Resolved && svc4.RaidIdOf(A) is null && svc4.RaidIdOf(B) is null, "si la activación explota, se vuelve a lanzar la excepción, el raid queda cerrado y nadie queda registrado en él");
Check(await Left(A) is null && await Left(B) is null && !await HasRow(A) && !await HasRow(B), "y NADIE queda con el cooldown del jefe gastado");

// Soltar no puede romper la activación si la base falla: un repositorio que tira al soltar igual cancela bien.
await Reset();
await BusyBoss(B);
var (s5, t5, svc5) = NewRaid(A, B);
var flakyRepo = DispatchProxy.Create<IAdventureRepository, FlakyRelease>();
var flaky = (FlakyRelease)(object)flakyRepo;
flaky.Inner = adventure;
var oldErr = Console.Out; var sw = new StringWriter(); Console.SetOut(sw);
try { await RaidModule.TryActivateAsync(s5, svc5, flakyRepo); } finally { Console.SetOut(oldErr); }
Check(s5.Phase == RaidPhase.Resolved && t5.Updates.Count == 1 && t5.Updates[0].Title.Contains("cancelado") && flaky.ReleaseCalls == 1, "si soltar el cooldown falla, el raid se cancela igual y el mensaje sale (el fallo queda en el log)");

Console.WriteLine("\n== 3. /cd con las mascotas ==");
var t0 = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
PetSpecies Species(int n) => new(n, n, $"Zona {n}", $"Mascota{n}", null, "gold", 5, 100 + n, $"Huevo{n}");
OwnedPet Pet(int n, int points, double? minutesAgo) => new(Species(n), points, minutesAgo is { } m ? t0.AddMinutes(-m) : null);
int max = PetRules.PointsToReach(PetRules.MaxLevel);
Check(CooldownModule.PetLine([], null, t0) is null, "sin mascotas la línea no sale");
Check(CooldownModule.PetLine([Pet(1, max, null), Pet(2, max, 5)], null, t0) is null, "con todas al nivel máximo tampoco (no hay nada que alimentar)");
string rNever = CooldownModule.PetLine([Pet(1, 0, null)], null, t0)!;
Check(rNever.Contains("¡Listo!") && rNever.Contains("1 puede comer") && rNever.Contains("/pet feed") && rNever.StartsWith("🐾 **Mascotas**:"), $"una que nunca comió: «¡Listo!», 1 puede comer y el comando ({rNever})");
Check(CooldownModule.PetLine([Pet(1, 0, null), Pet(2, 3, 90), Pet(3, 2, 10)], null, t0)!.Contains("2 pueden comer"), "tres mascotas, dos listas y una que comió hace 10 minutos: «2 pueden comer»");
string rWait = CooldownModule.PetLine([Pet(1, 0, 20)], null, t0)!;
Check(rWait.Contains("40m") && rWait.Contains("restantes") && !rWait.Contains("la primera") && !rWait.Contains("¡Listo!"), $"una sola que comió hace 20 minutos: faltan ~40m, sin «la primera» ({rWait})");
string rFirst = CooldownModule.PetLine([Pet(1, 0, 10), Pet(2, 0, 50)], null, t0)!;
Check(rFirst.Contains("10m") && rFirst.Contains("(la primera)"), $"dos que esperan (comieron hace 10 y 50 minutos): faltan 10m para la PRIMERA ({rFirst})");
Check(CooldownModule.PetLine([Pet(1, 0, 40)], TimeSpan.FromMinutes(35), t0)!.Contains("¡Listo!") && CooldownModule.PetLine([Pet(1, 0, 40)], null, t0)!.Contains("restantes"), "con el Buen Pienso (35 min de espera) la misma mascota ya puede comer; con la hora de siempre todavía no");
Check(CooldownModule.PetLine([Pet(1, max, 5), Pet(2, 0, 20)], null, t0)!.Contains("40m") && !CooldownModule.PetLine([Pet(1, max, 5), Pet(2, 0, 20)], null, t0)!.Contains("la primera"), "una al máximo no cuenta: solo se espera a la que todavía puede subir");

// Contra la base real
await Reset();
var species = (await pets.GetSpeciesAsync()).OrderBy(s => s.ZoneId).ToList();
var cdNoPets = await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, A, null, null, false, pets);
Check(!AllText(cdNoPets).Contains("Mascotas"), "/cd de alguien sin mascotas no trae la línea");
await db.ExecuteAsync("INSERT INTO player_pets (discord_id, species_id, feed_points, last_fed_at) VALUES (@id, @sp, 0, NULL)", new { id = (long)A, sp = species[0].SpeciesId });
var cdReady = AllText(await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, A, null, null, false, pets));
Check(cdReady.Contains("🐾 **Mascotas**: **¡Listo!** ✅"), "con una mascota que puede comer, /cd dice «¡Listo!»");
await db.ExecuteAsync("UPDATE player_pets SET last_fed_at = now() - interval '15 minutes' WHERE discord_id = @id", new { id = (long)A });
var cdWait = AllText(await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, A, null, null, false, pets));
Check(cdWait.Contains("🐾 **Mascotas**:") && cdWait.Contains("restantes") && (cdWait.Contains("44m") || cdWait.Contains("45m")), "si comió hace 15 minutos, /cd cuenta lo que falta (≈ 45 min)");
Check(cdWait.IndexOf("🐾", StringComparison.Ordinal) < cdWait.IndexOf("🎁", StringComparison.Ordinal) && cdWait.IndexOf("⏱", StringComparison.Ordinal) >= -1, "la línea va antes de la del diario");
var cdNoRepo = AllText(await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, A, null, null, false));
Check(!cdNoRepo.Contains("Mascotas") && cdNoRepo.Contains("🎁 **Diario**"), "sin el repositorio de mascotas (otros usos de /cd) todo sigue igual");
var broken = DispatchProxy.Create<IPetRepository, ThrowingPets>();
var cdBroken = AllText(await CooldownModule.BuildStatusEmbedAsync(cooldowns, users, A, null, null, false, broken));
Check(!cdBroken.Contains("Mascotas") && cdBroken.Contains("🎁 **Diario**") && cdBroken.Contains("Cazar"), "si leer las mascotas falla, /cd sale igual, solo sin esa línea");

Console.WriteLine("\n== 4. Comandos y DI con el InteractionService real ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var cdCmd = interactions.SlashCommands.Single(c => c.Name == "cd");
Check(cdCmd.Description.Length is >= 1 and <= 100 && cdCmd.Description.Contains("mascotas") && cdCmd.Parameters.Count == 0, $"/cd sigue siendo un comando sin parámetros y su descripción (que ahora nombra las mascotas) entra en Discord ({cdCmd.Description.Length} caracteres)");
int built = 0; var buildFails = new List<string>();
foreach (var t in typeof(BoxModule).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsClass && (t.Name.EndsWith("Module") || typeof(IInteractionModuleBase).IsAssignableFrom(t) || typeof(Discord.Commands.IModuleBase).IsAssignableFrom(t))))
{
    try { Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, t); built++; } catch (Exception ex) { buildFails.Add($"{t.Name}: {ex.GetBaseException().Message}"); }
}
Check(buildFails.Count == 0 && built >= 30, $"el contenedor de DI arma TODOS los módulos ({built}{(buildFails.Count > 0 ? "; fallan: " + string.Join(" | ", buildFails) : string.Empty)})");

foreach (var id in all) await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
Check(true, "(limpieza) usuarios descartables borrados");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------------------------------
class FakeTarget : ICombatReplyTarget
{
    public List<Embed> Updates { get; } = [];
    public bool ThrowAlways { get; set; }
    public Task UpdateAsync(Embed embed, MessageComponent components)
    {
        if (ThrowAlways) throw new InvalidOperationException("no se puede editar el mensaje");
        Updates.Add(embed);
        return Task.CompletedTask;
    }
}

// Un AdventureRepository real salvo que soltar el cooldown falla (para probar que eso no rompe la cancelación).
class FlakyRelease : DispatchProxy
{
    public IAdventureRepository Inner = null!;
    public int ReleaseCalls;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "ReleaseCooldownAsync") { ReleaseCalls++; throw new InvalidOperationException("la base no responde"); }
        try { return targetMethod.Invoke(Inner, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }
}

// Un IPetRepository que tira en cualquier método (para probar que /cd no se rompe).
class ThrowingPets : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("la base no responde");
}
