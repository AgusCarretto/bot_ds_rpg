using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Idioma (español / inglés): el motor Loc, el formato regional, el idioma de cada jugador, /language, cómo se fija al despachar, el escáner de textos y, pantalla por pantalla, que el inglés
// no deje caer español. Diseño: docs/superpowers/specs/2026-10-23-idioma-design.md. Plan: docs/superpowers/plans/2026-10-23-idioma.md.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== 1. El motor Loc ==");
Loc.SetCatalog(new Dictionary<string, string>
{
    ["Estás en plena pelea"] = "You are in the middle of a fight",
    ["Tenés {0:N0} HP"] = "You have {0:N0} HP",
    ["{0} de {1}"] = "{1} of {0}",
    ["Tenés {0} HP"] = "You have {0} and {1} HP",   // rota a propósito: una plantilla con UN dato no puede traer {1}
    ["Texto de una tabla"] = "Table text",
});

Check(Loc.Current == "es", "por defecto el idioma es español");
Check(Loc.T("Estás en plena pelea") == "Estás en plena pelea" && Loc.T("Nada que ver") == "Nada que ver", "en español T devuelve el mismo texto");
using (Loc.Use("en"))
{
    Check(Loc.Current == "en" && Loc.T("Estás en plena pelea") == "You are in the middle of a fight", "en inglés T devuelve la traducción del catálogo");
    Check(Loc.T("Un texto sin traducir") == "Un texto sin traducir", "sin entrada en el catálogo cae al español");
    Check(Loc.F($"Tenés {5:N0} HP") == "You have 5 HP", "F en inglés toma la plantilla del catálogo y le pone los datos");
    int a = 3, b = 10;
    Check(Loc.F($"{a} de {b}") == "10 of 3", "la traducción puede reordenar los datos ({1} of {0})");
    Check(Loc.F($"Tenés {a} HP") == "Tenés 3 HP", "una traducción con un {1} de más NO tira: cae al español");
    Check(Loc.Tr("Texto de una tabla") == "Table text" && Loc.Tr("Otro") == "Otro", "Tr traduce un texto que viene de una tabla (y deja el que no tiene entrada)");
}

Check(Loc.F($"Tenés {5:N0} HP") == $"Tenés {5:N0} HP", "en español F da EXACTAMENTE lo que daba el $\"…\" de antes (mismo formato de número)");
Check(Loc.Mark("Texto de una tabla") == "Texto de una tabla" && Loc.Tr("Texto de una tabla") == "Texto de una tabla", "Mark es la identidad y en español Tr no cambia nada");
Check(Loc.Normalize("EN") == "en" && Loc.Normalize("english") == "en" && Loc.Normalize("es") == "es" && Loc.Normalize("pt-BR") == "es" && Loc.Normalize(null) == "es" && Loc.Normalize("") == "es", "Normalize: en/EN/english → en; todo lo demás → es");

using (Loc.Use("en"))
{
    using (Loc.Use("es")) { Check(Loc.Current == "es", "Use anidado: adentro vale el de adentro"); }
    Check(Loc.Current == "en", "y al cerrarse restaura el anterior");
}

Check(Loc.Current == "es", "fuera de todo Use vuelve a español");

var concurrent = await Task.WhenAll(Enumerable.Range(0, 60).Select(i => Task.Run(async () =>
{
    string lang = i % 2 == 0 ? "en" : "es";
    using var scope = Loc.Use(lang);
    bool ok = true;
    for (int k = 0; k < 25; k++)
    {
        await Task.Yield();
        ok &= Loc.Current == lang && (Loc.T("Estás en plena pelea") == (lang == "en" ? "You are in the middle of a fight" : "Estás en plena pelea"));
    }

    return ok;
})));
Check(concurrent.All(x => x), "60 comandos a la vez, la mitad en inglés y la mitad en español, cada uno ve SIEMPRE el suyo");

Loc.LoadEmbedded();
Check(Loc.Current == "es", "LoadEmbedded no tira y no cambia el idioma");
Check(typeof(Loc).Assembly.GetManifestResourceNames().Contains("Locales.en.json"), "el catálogo Locales/en.json viaja incrustado en el ensamblado con el nombre que lee LoadEmbedded");

Console.WriteLine("\n== 2. Formato regional ==");
Check(GameHistory.Number(1234567) == "1.234.567" && GameHistory.Number(-1500) == "-1.500" && GameHistory.Number(0) == "0" && Loc.Decimal(4.9, 1) == "4,9" && Loc.Decimal(5, 1) == "5,0" && Loc.Decimal(12, 0) == "12" && Loc.Percent(4.9) == "4,9 %" && Loc.Percent(5, 0) == "5 %",
    "en español: miles con punto, decimales con coma y «4,9 %» con espacio (como siempre)");
using (Loc.Use("en"))
{
    Check(GameHistory.Number(1234567) == "1,234,567" && GameHistory.Number(-1500) == "-1,500" && GameHistory.Number(0) == "0" && Loc.Decimal(4.9, 1) == "4.9" && Loc.Decimal(5, 1) == "5.0" && Loc.Decimal(12, 0) == "12" && Loc.Percent(4.9) == "4.9%" && Loc.Percent(5, 0) == "5%",
        "en inglés: miles con coma, decimales con punto y «4.9%» sin espacio");
}

Check(AmountParser.Parse("1,000", 5000).Amount == 1000 && AmountParser.Parse("1.000", 5000).Amount == 1000 && AmountParser.Parse("10k", 5000).Amount == 10_000 && AmountParser.Parse("1.5m", 5000).Amount == 1_500_000 && AmountParser.Parse("1,5m", 5000).Amount == 1_500_000,
    "los montos tecleados con separadores ingleses ya se entienden («1,000», «1.000», «10k», «1.5m»): un jugador puede escribir lo que lee en su idioma");

Console.WriteLine("\n== 3. El idioma de cada jugador ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong L1 = 999000000000098001UL, L2 = 999000000000098002UL, L3 = 999000000000098003UL, L4 = 999000000000098004UL, L5 = 999000000000098005UL;
async Task CleanLanguages() => await db.ExecuteAsync("DELETE FROM player_language WHERE discord_id BETWEEN 999000000000098001 AND 999000000000098009");
await CleanLanguages();
var languageRepo = new LanguageRepository(factory);
var service = new PlayerLanguageService(languageRepo);

Check(PlayerLanguageService.LanguageForLocale("es-ES") == "es" && PlayerLanguageService.LanguageForLocale("es-419") == "es" && PlayerLanguageService.LanguageForLocale("ES") == "es"
    && PlayerLanguageService.LanguageForLocale("en-US") == "en" && PlayerLanguageService.LanguageForLocale("pt-BR") == "en" && PlayerLanguageService.LanguageForLocale("fr") == "en"
    && PlayerLanguageService.LanguageForLocale(null) is null && PlayerLanguageService.LanguageForLocale("") is null && PlayerLanguageService.LanguageForLocale("  ") is null,
    "el idioma de Discord: es* → español, cualquier otro → inglés, y sin dato → ninguno");
Check(await service.ResolveAsync(L1, "es-ES") == "es" && await languageRepo.GetAsync(L1) == "es", "con locale español devuelve es y lo deja guardado");
Check(await service.ResolveAsync(L2, "en-US") == "en" && await languageRepo.GetAsync(L2) == "en", "con locale inglés devuelve en y lo deja guardado");
Check(await new PlayerLanguageService(languageRepo).ResolveAsync(L2, "es-ES") == "en", "lo guardado gana al locale (un servicio nuevo, sin caché)");
Check(await service.ResolveAsync(L3, null) == "es" && await languageRepo.GetAsync(L3) is null, "sin locale (un comando de texto) y sin nada guardado: es, y NO guarda");
Check(await service.ResolveAsync(L4, "pt-BR") == "en", "un locale raro (pt-BR) cae a inglés");
await service.SetAsync(L1, "en");
Check(await service.ResolveAsync(L1, "es-ES") == "en" && await languageRepo.GetAsync(L1) == "en", "SetAsync guarda y el caché se actualiza al instante");
await service.SetAsync(L1, "klingon");
Check(await languageRepo.GetAsync(L1) == "es", "un idioma desconocido se guarda como español (Normalize)");

var counting = new CountingRepo(languageRepo);
var cached = new PlayerLanguageService(counting);
await cached.ResolveAsync(L2, null);
await cached.ResolveAsync(L2, "en-US");
await cached.ResolveAsync(L2, "es-ES");
Check(counting.Gets == 1, $"la 2.ª y la 3.ª resolución salen del caché (lecturas a la base: {counting.Gets})");

var broken = new PlayerLanguageService(new ThrowingRepo());
Check(await broken.ResolveAsync(L5, "en-US") == "es", "si la base falla (o la tabla no existe) devuelve español y NO tira");
await CleanLanguages();

Console.WriteLine("\n== 4. /language y aa language ==");
var langService = new PlayerLanguageService(languageRepo);
await langService.SetAsync(L1, "en");
string shownEn = await LanguageModule.ExecuteLanguageAsync(langService, L1, null, null);
Check(shownEn.Contains("Your language is **English**") && shownEn.Contains("/language"), $"sin argumento y con inglés guardado dice cuál tenés, en inglés ({shownEn})");
await langService.SetAsync(L2, "es");
string shownEs = await LanguageModule.ExecuteLanguageAsync(langService, L2, "", null);
Check(shownEs.Contains("Tu idioma es **Español**") && shownEs.Contains("/language"), $"sin argumento y con español guardado, en español ({shownEs})");
string setEn = await LanguageModule.ExecuteLanguageAsync(langService, L2, "english", null);
Check(await languageRepo.GetAsync(L2) == "en" && setEn.Contains("Done") && setEn.Contains("**English**"), $"«english» guarda en y contesta YA en inglés ({setEn})");
string setEs = await LanguageModule.ExecuteLanguageAsync(langService, L2, "ESPAÑOL", null);
Check(await languageRepo.GetAsync(L2) == "es" && setEs.Contains("Listo") && setEs.Contains("**Español**"), $"«ESPAÑOL» vuelve a es y contesta en español ({setEs})");
await LanguageModule.ExecuteLanguageAsync(langService, L2, "Inglés", null);
Check(await languageRepo.GetAsync(L2) == "en", "«Inglés» con tilde también vale");
await LanguageModule.ExecuteLanguageAsync(langService, L2, "es", null);
string unknown = await LanguageModule.ExecuteLanguageAsync(langService, L2, "klingon", null);
Check(await languageRepo.GetAsync(L2) == "es" && unknown.Contains("**es**") && unknown.Contains("**en**") && unknown.Contains("No conozco"), $"un idioma desconocido no guarda nada y lista las opciones ({unknown})");
string insideEn;
using (Loc.Use("en")) { insideEn = await LanguageModule.ExecuteLanguageAsync(langService, L2, null, null); }
Check(insideEn.Contains("Tu idioma es **Español**"), "el idioma que se muestra es el GUARDADO del jugador, no el que haya en el momento");
string failed = await LanguageModule.ExecuteLanguageAsync(new PlayerLanguageService(new ThrowingRepo()), L5, "en", null);
Check(failed.Contains("No pude guardar"), $"si no se puede guardar lo dice (no tira) ({failed})");

var slash = typeof(LanguageModule).GetMethod("HandleLanguageAsync")!;
var slashAttr = slash.GetCustomAttribute<SlashCommandAttribute>()!;
Check(slashAttr.Name == "language" && slashAttr.Description.Length <= 100 && slash.GetParameters()[0].GetCustomAttributes<ChoiceAttribute>().Count() == 2, $"el slash se llama language, su descripción mide {slashAttr.Description.Length} (≤ 100) y tiene dos opciones");
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(LanguageModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModuleAsync(typeof(LanguageModule), sp);
Check(interactions.SlashCommands.Count(c => c.Name == "language") == 1 && interactions.SlashCommands.Single(c => c.Name == "language").Parameters.Single().Choices.Count == 2, "con el InteractionService real /language queda registrado con sus dos opciones");
Check(ActivatorUtilities.CreateInstance(sp, typeof(LanguageModule)) is LanguageModule && sp.GetService<IPlayerLanguageService>() is not null, "la inyección de dependencias arma LanguageModule y conoce el servicio de idioma");
var textCommands = new CommandService();
await textCommands.AddModuleAsync<TextCommandModule>(sp);
Check(textCommands.Search("idioma").Commands.FirstOrDefault().Command?.Name == "language" && textCommands.Search("language en").IsSuccess, "con el CommandService real «aa idioma» y «aa language» llegan al mismo comando");
await CleanLanguages();

Console.WriteLine("\n== 5. El idioma al despachar y en los recordatorios ==");
// La trampa del AsyncLocal (ver el comentario de GameData/Loc.cs): fijado ADENTRO de un helper async no vuelve al que lo llamó; fijado en el propio método, sí vale para todo lo que sigue.
static async Task<IDisposable> UseInsideHelperAsync(string language) { await Task.Yield(); return Loc.Use(language); }
async Task<string> InCallerFrame(IPlayerLanguageService svc, ulong id, string? locale)
{
    using var scope = Loc.Use(await svc.ResolveAsync(id, locale));
    await Task.Yield();
    return Loc.Current;
}

using (await UseInsideHelperAsync("en")) { Check(Loc.Current == "es", "un Loc.Use hecho adentro de un helper async NO le llega a quien lo llamó (por eso el idioma se fija en el propio método despachador)"); }
await langService.SetAsync(L3, "en");
Check(await InCallerFrame(langService, L3, null) == "en" && Loc.Current == "es", "el patrón de Program.cs: Loc.Use(await ResolveAsync(…)) en el propio método vale para todo lo que sigue y se suelta al terminar");
Check(await InCallerFrame(new PlayerLanguageService(new ThrowingRepo()), L5, "en-US") == "es", "si el servicio de idioma falla, el comando sigue en español");

Check(ReminderCatalog.Compose(L1, ["hunt"]) == $"<@{L1}> 🏹 ¡Cacería lista! Ya podés volver a cazar con `/hunt`.", "el recordatorio en español es el de siempre");
using (Loc.Use("en"))
{
    string one = ReminderCatalog.Compose(L1, ["hunt"]);
    string two = ReminderCatalog.Compose(L1, ["travel", "daily"]);
    Check(one.StartsWith($"<@{L1}> 🏹 ") && one.Contains("Hunt ready") && one.Contains("`/hunt`") && !one.Contains("Cacería"), $"un recordatorio en inglés ({one})");
    Check(two.Contains("You can go again") && two.Contains("**Travel**") && two.Contains("**Daily**") && !two.Contains("Viaje") && !two.Contains("Diario"), $"varios juntos, en inglés ({two.Replace("\n", " | ")})");
}

// El reloj arma el aviso de cada jugador en SU idioma (mismo ciclo, dos jugadores).
const ulong R1 = 999000000000098006UL, R2 = 999000000000098007UL;
const ulong CHAN = 777000000000000011UL;
var reminders = new ReminderRepository(factory);
var cooldowns = new CooldownRepository(factory);
var userRepo = new UserRepository(factory);
var bonusService = new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), new GameEventRepository(factory));
foreach (var id in new[] { R1, R2 }) { await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id }); await userRepo.GetOrCreateUserAsync(id); }
await langService.SetAsync(R1, "en");
await langService.SetAsync(R2, "es");
var sentMessages = new System.Collections.Concurrent.ConcurrentQueue<(ulong User, string Text)>();
var scheduler = new ReminderScheduler(reminders, new ReminderService(reminders, cooldowns, userRepo, new PetRepository(factory), bonusService), new CombatSessionService(userRepo), new RaidSessionService(), new DiscordSocketClient(), langService)
{
    Sender = (user, channel, text, del) => { sentMessages.Enqueue((user, text)); return Task.CompletedTask; },
};
foreach (var id in new[] { R1, R2 })
{
    await db.ExecuteAsync("INSERT INTO reminders (discord_id, kind, channel_id, due_at) VALUES (@id, 'boss', @c, now() - interval '1 second') ON CONFLICT (discord_id, kind) DO UPDATE SET due_at = EXCLUDED.due_at", new { id = (long)id, c = (long)CHAN });
}

await scheduler.TickAsync(DateTime.UtcNow);
var delivered = sentMessages.ToList();
Check(delivered.Count == 2 && delivered.Single(m => m.User == R1).Text.Contains("Boss ready") && delivered.Single(m => m.User == R2).Text.Contains("Jefe listo"), $"en el mismo ciclo, un jugador con inglés guardado recibe «Boss ready» y el otro «Jefe listo» ({string.Join(" || ", delivered.Select(d => d.Text))})");
Check(Loc.Current == "es", "y el reloj no deja el idioma pisado");
foreach (var id in new[] { R1, R2 }) { await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id }); }
await CleanLanguages();
await db.ExecuteAsync("DELETE FROM player_language WHERE discord_id IN (999000000000098006, 999000000000098007)");

Console.WriteLine("\n== 6. El escáner de textos (tools/loc) ==");
string fixture = """"
class X {
  void M(int hp, int a, int b, bool ok) {
    var s1 = Loc.T("Estás en plena pelea");
    var s2 = Loc.F($"Tenés {hp} HP");
    var s3 = Loc.F($"{hp:N0} de {Math.Max(a, b)}");
    var s4 = Loc.F($"Va {(ok ? "sí" : "no")} y {{llaves}} {hp,5}");
    var s5 = Loc.T("uno " + "dos");
    var s6 = Loc.T(variable);
    var s7 = Loc.Mark("Texto de tabla");
    // Loc.T("comentario") no cuenta
    var s8 = "Loc.T(\"dentro de un texto\")";
    var s9 = Loc.T("con \"comillas\" y \\ barra\ny salto");
    var s10 = Loc.F($"Plano sin datos");
    var s11 = Loc.T($"mal {hp}");
    var s12 = Loc.Tr(variable);
    var sql = """
        SELECT Loc.T("no es código")
        """;
  }
}
"""";
var scanned = LocScan.ExtractFromSource(fixture, "X.cs");
var good = scanned.Where(u => u.Kind != "NonLiteral").ToList();
string[] expectedTemplates = ["Estás en plena pelea", "Tenés {0} HP", "{0:N0} de {1}", "Va {0} y {{llaves}} {1,5}", "uno dos", "Texto de tabla", "con \"comillas\" y \\ barra\ny salto", "Plano sin datos"];
Check(good.Select(u => u.Template).SequenceEqual(expectedTemplates), $"extrae las plantillas como las numera el compilador, sin tocar comentarios, textos ni cadenas crudas ({string.Join(" | ", good.Select(u => u.Template.Replace("\n", "\\n")))})");
Check(good.Select(u => u.Kind).SequenceEqual(["T", "F", "F", "F", "T", "Mark", "T", "F"]) && good[0].Line == 3 && good[1].Line == 4 && good[0].File == "X.cs", "cada uso dice si es T, F o Mark y en qué línea está");
Check(scanned.Count(u => u.Kind == "NonLiteral") == 2, "una llamada que no es un literal (la variable, o un $\"…\" dentro de T) se marca como NonLiteral");

var englishFixture = new Dictionary<string, string>
{
    ["Estás en plena pelea"] = "You are in a fight",
    ["Tenés {0} HP"] = "You have {1} HP",   // rota: pide {1} y la plantilla trae {0}
    ["uno dos"] = "one two",
    ["{0:N0} de {1}"] = "{1} of {0:N0}",    // reordenar está permitido
    ["viejo"] = "old",
};
var report = LocScan.Compare(scanned, englishFixture);
Check(report.Missing.Count == 4 && report.Missing.Any(m => m.Contains("Texto de tabla")) && report.Missing.Any(m => m.Contains("Plano sin datos")) && report.Missing.Any(m => m.Contains("llaves")), $"informa lo que FALTA en inglés ({report.Missing.Count})");
Check(report.Stale.SequenceEqual(["viejo"]), "informa lo que SOBRA en el catálogo (el texto cambió y la traducción quedó huérfana)");
Check(report.PlaceholderMismatch.Count == 1 && report.PlaceholderMismatch[0].Contains("Tenés {0} HP"), "informa un {1} que no coincide con la plantilla, pero deja pasar un reordenamiento");
Check(report.NonLiteral.Count == 2 && !report.Ok, "informa las llamadas que no son literales y el informe no está OK");

string tempDir = Path.Combine(Path.GetTempPath(), "loc-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
File.WriteAllText(Path.Combine(tempDir, "Pantalla.cs"), """
class P {
  void M() {
    var a = "Tenés poco oro";
    var b = "SELECT * FROM users WHERE x = 1";
    var c = "btn_attack";
    Console.WriteLine("Esto va a la consola");
    BotLog.Error("algo salió mal en la base");
    var d = Loc.T("Ya está traducido");
    var e = "";
    var f = "🔥";
    var g = $"Ganaste {a} de oro";
    [SlashCommand("hunt", "Cazá un monstruo de tu zona")]
  }
}
""");
var pending = LocScan.Untranslated(tempDir);
Check(pending.Count == 2 && pending[0].Contains("Tenés poco oro") && pending[1].Contains("Ganaste {a} de oro") && pending[0].Contains("Pantalla.cs:3"), $"lista los textos para el jugador que faltan envolver y deja afuera consultas, ids, consola, registro, lo ya envuelto y atributos ({string.Join(" | ", pending)})");
Directory.Delete(tempDir, true);

// El repositorio de verdad: nada falta, nada sobra, ningún {0} desparejo, ningún literal raro.
var catalogOnDisk = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestEnv.RepoRoot, "Locales", "en.json")))!;
var repoReport = LocScan.Compare(LocScan.Extract(TestEnv.RepoRoot), catalogOnDisk);
Check(repoReport.Ok, $"el código y Locales/en.json van parejos: faltan {repoReport.Missing.Count}, sobran {repoReport.Stale.Count}, {{n}} distintos {repoReport.PlaceholderMismatch.Count}, no literales {repoReport.NonLiteral.Count}");
foreach (string problem in repoReport.Missing.Concat(repoReport.Stale).Concat(repoReport.PlaceholderMismatch).Concat(repoReport.NonLiteral).Take(15)) { Console.WriteLine("     " + problem); }
var typed = System.Text.RegularExpressions.Regex.Matches(string.Join("\n", catalogOnDisk.Values), "`aa ([a-zñáéíóú]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
var unknownCommands = typed.Where(word => !textCommands.Search(word).IsSuccess).ToList();
Check(unknownCommands.Count == 0, $"todo «`aa <comando>`» que nombra un texto en inglés existe de verdad ({typed.Count} comandos; faltan: {string.Join(", ", unknownCommands)})");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un repositorio que cuenta las lecturas, para probar el caché.
sealed class CountingRepo(ILanguageRepository inner) : ILanguageRepository
{
    public int Gets;
    public Task<string?> GetAsync(ulong discordId, CancellationToken ct = default) { Gets++; return inner.GetAsync(discordId, ct); }
    public Task SetAsync(ulong discordId, string language, CancellationToken ct = default) => inner.SetAsync(discordId, language, ct);
}

// Un repositorio que falla siempre (la tabla sin crear, la base caída).
sealed class ThrowingRepo : ILanguageRepository
{
    public Task<string?> GetAsync(ulong discordId, CancellationToken ct = default) => throw new InvalidOperationException("simulado: la tabla no existe");
    public Task SetAsync(ulong discordId, string language, CancellationToken ct = default) => throw new InvalidOperationException("simulado");
}
