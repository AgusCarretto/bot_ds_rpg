using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.25.4: EN PLENA PELEA SOLO VALEN LOS COMANDOS DE LA PELEA (el dueño, 2026-10-23: «bloqueá todos los comandos si estás en medio de pelea, exceptuando los de la pelea como el use; no quiero que pueda
// recolectar madera en plena pelea»), y el aviso «no tenés combate» dice por qué. La lista de lo permitido, que de verdad cubra comandos que existen, quién cuenta como «en pelea» y el mensaje.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== 1. Qué vale en pelea ==");
string[] allowed = ["use", "attack", "ability", "flee", "profile", "inventory", "cd", "info", "tutorial"];
string[] blocked = ["chop", "mine", "forge", "taberna", "daily", "play", "give", "zona", "zonas", "hunt", "travel", "boss", "autohunt", "bank", "shop", "heal", "sell", "buy", "open", "enchant", "dismantle", "trade", "exchange",
    "pet", "miniboss", "bj", "cf", "slots", "deposit", "withdraw", "fight", "arena", "missions", "achievements", "titles", "title", "class", "start", "vote", "fuegonuevo", "deepshop", "reminders", "professions", "story", "drops", "tips"];
Check(allowed.All(FightLock.IsAllowed) && allowed.All(a => FightLock.IsAllowed(a.ToUpperInvariant())), "valen comer, atacar, la habilidad, huir y lo que solo mira (perfil, mochila, cooldowns, ayuda y tutorial), sin importar mayúsculas");
Check(blocked.All(b => !FightLock.IsAllowed(b)), $"todo lo demás queda bloqueado: recolectar, forjar, la taberna, el casino, el banco, las zonas, empezar otra pelea... ({blocked.Length} comandos probados)");
Check(!FightLock.IsAllowed(null) && !FightLock.IsAllowed("") && !FightLock.IsAllowed("  ") && !FightLock.IsAllowed("comando-que-no-existe"), "un nombre vacío o inventado no se permite");
Check(FightLock.TextKey("bank deposit") == "bank" && FightLock.TextKey("USE") == "use" && FightLock.TextKey("  atk ") == "atk" && FightLock.TextKey("") == string.Empty, "un comando de texto con grupo («bank deposit») cuenta por su primera palabra");
string msg = FightLock.Message("/chop");
Check(msg.Contains("/chop") && msg.Contains("/use") && msg.Contains("pelea") && msg.Length < 400, $"el aviso dice qué comando no se puede, que es por la pelea y qué sí se puede hacer ({msg})");

Console.WriteLine("\n== 2. Lo permitido existe de verdad ==");
// Si alguien renombra un comando y la lista queda con el nombre viejo, el comando nuevo quedaría bloqueado en pelea sin que nadie lo note: se compara contra los comandos reales.
var asm = typeof(FightLock).Assembly;
var slashNames = asm.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    .Select(m => m.GetCustomAttribute<SlashCommandAttribute>()?.Name).Where(n => n is not null).Select(n => n!).ToHashSet(StringComparer.OrdinalIgnoreCase);
var textNames = asm.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    .Select(m => m.GetCustomAttribute<CommandAttribute>()?.Text).Where(n => n is not null).Select(n => n!).ToHashSet(StringComparer.OrdinalIgnoreCase);
string[] slashAllowed = ["use", "profile", "inventory", "cd", "info", "tutorial"];
string[] textAllowed = ["use", "attack", "ability", "flee", "profile", "inventory", "cd", "info", "tutorial"];
Check(slashAllowed.All(slashNames.Contains), $"los comandos de barra permitidos existen ({string.Join(", ", slashAllowed.Where(n => !slashNames.Contains(n)))} faltan)");
Check(textAllowed.All(textNames.Contains), $"los comandos de texto permitidos existen ({string.Join(", ", textAllowed.Where(n => !textNames.Contains(n)))} faltan)");
Check(allowed.All(a => slashNames.Contains(a) || textNames.Contains(a)), "y no queda en la lista un nombre que no sea ni de barra ni de texto");
var attackMethod = asm.GetTypes().SelectMany(t => t.GetMethods()).First(m => m.GetCustomAttribute<CommandAttribute>()?.Text == "attack");
Check(attackMethod.GetCustomAttribute<AliasAttribute>()?.Aliases.Contains("atk") == true, "«aa atk» es un alias de «attack»: bloquear o permitir uno es lo mismo que el otro (el nombre sale del comando que se encontró)");

Console.WriteLine("\n== 3. Quién está en pelea ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var users = new UserRepository(new NpgsqlConnectionFactory(cfg));
var combat = new CombatSessionService(users);
const ulong A = 999000000000097001UL, B = 999000000000097002UL;
var state = new CombatState("hunt", "Toro Bravo", "🐂", 100, 100, 10, [], 0, 0, null, 100, 100, 100, 10, 0, 1, "Guerrero", ClassPassives.For("Guerrero"));
Check(!FightGuard.IsInFight(combat, null, null, A), "sin combate no está en pelea");
Check(combat.TryStart(A, state, new FakeTarget()) && FightGuard.IsInFight(combat, null, null, A) && !FightGuard.IsInFight(combat, null, null, B), "con un combate solitario sí, y solo él (otro jugador no)");
Check(combat.RecentEnd(A) is null, "mientras pelea no hay «cómo terminó»");
var session = combat.Peek(A)!;
Check(combat.TryAdvance(A, session, null, null) && !FightGuard.IsInFight(combat, null, null, A), "al terminar (ganar, perder o huir) deja de estar en pelea y se desbloquea todo");
Check(combat.RecentEnd(A) == "resolved" && combat.RecentEnd(B) is null, "el servicio recuerda que ese combate se resolvió (y no inventa nada para quien no peleó)");
var raids = new RaidSessionService();
Check(!FightGuard.IsInFight(combat, raids, null, A), "un servicio de raids sin nadie no cuenta");

Console.WriteLine("\n== 4. El aviso de «no tenés combate» ==");
string none = AdventureModule.NoActiveFightMessage(combat, B);
Check(none.Contains("No tenés ningún combate activo") && none.Contains("reinició"), "sin registro explica que se corta con un reinicio del bot");
string resolved = AdventureModule.NoActiveFightMessage(combat, A);
Check(resolved.Contains("No tenés ningún combate activo") && resolved.Contains("ya terminó") && resolved.Contains("viejo"), "con un combate recién resuelto dice que el mensaje es viejo");
Check(none != resolved && !resolved.Contains("reinició"), "y los dos textos son distintos");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
    public Task ClearComponentsAsync() => Task.CompletedTask;
}
