using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
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
    var s13 = $"** {Loc.T("dentro de un hueco")} y {(ok ? Loc.T("otro hueco") : string.Empty)}";
    var sql = """
        SELECT Loc.T("no es código")
        """;
  }
}
"""";
var scanned = LocScan.ExtractFromSource(fixture, "X.cs");
var good = scanned.Where(u => u.Kind != "NonLiteral").ToList();
string[] expectedTemplates = ["Estás en plena pelea", "Tenés {0} HP", "{0:N0} de {1}", "Va {0} y {{llaves}} {1,5}", "uno dos", "Texto de tabla", "con \"comillas\" y \\ barra\ny salto", "Plano sin datos", "dentro de un hueco", "otro hueco"];
Check(good.Select(u => u.Template).SequenceEqual(expectedTemplates), $"extrae las plantillas como las numera el compilador, sin tocar comentarios, textos ni cadenas crudas ({string.Join(" | ", good.Select(u => u.Template.Replace("\n", "\\n")))})");
Check(good.Select(u => u.Kind).SequenceEqual(["T", "F", "F", "F", "T", "Mark", "T", "F", "T", "T"]) && good[0].Line == 3 && good[1].Line == 4 && good[0].File == "X.cs", "cada uso dice si es T, F o Mark y en qué línea está");
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
Check(report.Missing.Count == 6 && report.Missing.Any(m => m.Contains("Texto de tabla")) && report.Missing.Any(m => m.Contains("Plano sin datos")) && report.Missing.Any(m => m.Contains("llaves")), $"informa lo que FALTA en inglés ({report.Missing.Count})");
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

Console.WriteLine("\n== 7. El revisor de fugas y los mensajes comunes ==");
var allowedNames = new List<string> { "El Tabernero", "El Herrero", "El Tendero", "Asado y Acero RPG", "Asado y Acero" };
allowedNames.AddRange(await db.QueryAsync<string>("SELECT name FROM items"));
allowedNames.AddRange(await db.QueryAsync<string>("SELECT name FROM monsters"));
allowedNames.AddRange(await db.QueryAsync<string>("SELECT name FROM zones"));
allowedNames.AddRange(ClassCatalog.All.Select(c => c.Name));
Check(LeakCheck.NoSpanish("Mate Amargo", allowedNames).Ok && LeakCheck.NoSpanish("You bought a Baúl de Roble", allowedNames).Ok && !LeakCheck.NoSpanish("Tenés 3 HP", allowedNames).Ok && !LeakCheck.NoSpanish("Estás acá", allowedNames).Ok && LeakCheck.NoSpanish("Use `/taberna` or `aa daily`", allowedNames).Ok,
    "el revisor deja pasar los nombres permitidos y los comandos entre comillas, y atrapa «Tenés» y los acentos del español");

const string fightEs = "⚔️ **Estás en plena pelea**: no podés usar `/chop` hasta que termine. Mientras tanto atacá, comé algo con `/use` o huí.";
Check(FightLock.Message("/chop") == fightEs, "el bloqueo de pelea en español es el de siempre");
var stubNone = new EndStub(null); var stubDone = new EndStub("resolved"); var stubTime = new EndStub("timeout");
string[] noFightEs = [AdventureModule.NoActiveFightMessage(stubNone, L1), AdventureModule.NoActiveFightMessage(stubDone, L1), AdventureModule.NoActiveFightMessage(stubTime, L1)];
Check(noFightEs.All(m => m.StartsWith("No tenés ningún combate activo")), "los tres avisos de «no tenés combate» en español siguen empezando igual");

string[] englishTexts;
using (Loc.Use("en"))
{
    englishTexts =
    [
        FightLock.Message("/chop"),
        AdventureModule.NoActiveFightMessage(stubNone, L1), AdventureModule.NoActiveFightMessage(stubDone, L1), AdventureModule.NoActiveFightMessage(stubTime, L1),
        CommonMessages.NotRegistered(L1), CommonMessages.UnexpectedError(), CommonMessages.UnknownCommand(), CommonMessages.CommandFailed(),
    ];
}

for (int k = 0; k < englishTexts.Length; k++)
{
    var (ok, found) = LeakCheck.NoSpanish(englishTexts[k], allowedNames);
    Check(ok && englishTexts[k] != fightEs && englishTexts[k].Length > 10, $"en inglés, mensaje común {k + 1}: sin español colado ({found ?? englishTexts[k][..Math.Min(70, englishTexts[k].Length)]})");
}

Check(englishTexts[0].Contains("`/chop`") && englishTexts[0].Contains("/use") && englishTexts[4].Contains("/start") && englishTexts[4].Contains(L1.ToString()), "y conservan lo que no se traduce: los comandos y la mención");
Check(AdventureModule.NoActiveFightMessage(stubNone, L1) == noFightEs[0] && CommonMessages.NotRegistered(L1).StartsWith($"<@{L1}> ¡Bienvenido"), "fuera de un Loc.Use los mismos mensajes salen en español");

Embed cooldownEs = AdventureModule.BuildCooldownEmbed(CooldownCatalog.Hunt, TimeSpan.FromSeconds(90));
Check(cooldownEs.Title == "⏳ 🏹 Cazar: todavía no podés" && cooldownEs.Description == "Te falta **1m 30s** para volver a intentarlo.", "el embed de «todavía no podés» en español es el de siempre");
Embed cooldownEn;
using (Loc.Use("en")) { cooldownEn = AdventureModule.BuildCooldownEmbed(CooldownCatalog.Hunt, TimeSpan.FromSeconds(90)); }
Check(cooldownEn.Title.Contains("Hunt") && cooldownEn.Description.Contains("1m 30s") && LeakCheck.NoSpanish(LeakCheck.AllText(cooldownEn), allowedNames).Ok && LeakCheck.FitsDiscord(cooldownEn), $"el mismo embed en inglés ({cooldownEn.Title} / {cooldownEn.Description})");

const ulong CD = 999000000000098008UL;
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)CD });
await userRepo.GetOrCreateUserAsync(CD);
Embed statusEs = await CooldownModule.BuildStatusEmbedAsync(cooldowns, userRepo, CD);
Embed statusEn;
using (Loc.Use("en")) { statusEn = await CooldownModule.BuildStatusEmbedAsync(cooldowns, userRepo, CD); }
Check(statusEs.Title == "⏱️ Tus cooldowns" && statusEs.Description.Contains("**Cazar**: **¡Listo!** ✅") && statusEs.Description.Contains("🎁 **Diario**: **¡Listo!** ✅"), "/cd en español es el de siempre");
Check(statusEn.Title == "⏱️ Your cooldowns" && statusEn.Description.Contains("**Hunt**: **Ready!** ✅") && statusEn.Description.Contains("🎁 **Daily**: **Ready!** ✅") && LeakCheck.NoSpanish(LeakCheck.AllText(statusEn), allowedNames).Ok && LeakCheck.FitsDiscord(statusEn),
    $"/cd en inglés ({LeakCheck.NoSpanish(LeakCheck.AllText(statusEn), allowedNames).Found})");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)CD });

var petsList = new List<OwnedPet>();
Check(CooldownModule.ArenaLine(true, TimeSpan.FromMinutes(95)) == $"🏟️ **Arena**: anotado ✅ — se juega en {TimeFormat.Remaining(TimeSpan.FromMinutes(95))}", "la línea de la Arena en español es la de siempre");
string arenaEn, arenaNotEn;
using (Loc.Use("en")) { arenaEn = CooldownModule.ArenaLine(true, TimeSpan.FromMinutes(95)); arenaNotEn = CooldownModule.ArenaLine(false, TimeSpan.FromMinutes(95)); }
Check(arenaEn.Contains("signed up") && arenaEn.Contains("1h 35m") && arenaNotEn.Contains("/arena join") && LeakCheck.NoSpanish(arenaEn + arenaNotEn, allowedNames).Ok, $"y en inglés ({arenaEn} | {arenaNotEn})");

Console.WriteLine("\n== 8. Perfil, aviso de nivel y /start ==");
allowedNames.AddRange(TitleCatalog.All.Select(t => t.Name));
allowedNames.AddRange(ClassCatalog.All.Select(c => c.WeaponType));
const ulong P8 = 999000000000098009UL;
const string Zw = "​";
var itemRepo8 = new ItemRepository(factory); var zoneRepo8 = new ZoneRepository(factory); var buffRepo8 = new BuffRepository(factory);
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)P8 });
await userRepo.GetOrCreateUserAsync(P8);
await db.ExecuteAsync("UPDATE users SET class = 'Ninja', level = 13, xp = 1646, has_bank = true, bank_gold = 156635, dust = 5, daily_streak = 15, gold = 32013, weapon_id = NULL, amulet_id = NULL WHERE discord_id = @id", new { id = (long)P8 });
async Task<Embed> ProfileIn(string lang) { using (Loc.Use(lang)) { return await GameModule.BuildProfileEmbedAsync(userRepo, itemRepo8, zoneRepo8, buffRepo8, P8, "T", "https://example.com/a.png"); } }
var profEs = await ProfileIn("es"); var profEn = await ProfileIn("en");
Check(profEs.Fields.Select(f => f.Name).SequenceEqual(["XP", "Vida", Zw]) && profEs.Description.Contains("**Zona:**") && ProfileProbe.Line(profEs, "Oro") != string.Empty && profEs.Footer!.Value.Text == "Asado y Acero RPG • Preparando las brasas...",
    "el perfil en español es el de siempre (campos XP, Vida y el bloque, «Zona:», «Oro:», el pie)");
string profEnText = LeakCheck.AllText(profEn);
Check(profEn.Fields.Select(f => f.Name).SequenceEqual(["XP", "Health", Zw]) && profEn.Description.Contains("**Zone:**") && profEn.Description.Contains("(max. ") && profEn.Description.Contains("Level"), $"el perfil en inglés: campos XP / Health, «Zone:» y «Level» ({profEn.Description.Replace("\n", " | ")})");
string statsEn = profEn.Fields[2].Value;
Check(statsEn.Contains("**ATK:**") && statsEn.Contains("**DEF:**") && statsEn.Contains("(no weapon)") && statsEn.Contains("(no amulet)") && statsEn.Contains("**Gold:** 32,013") && statsEn.Contains("**Bank:** 156,635") && statsEn.Contains("**Dust:** 5") && statsEn.Contains("**Streak:** day 15"),
    $"el bloque de números en inglés, con la coma de miles ({statsEn.Replace("\n", " | ")})");
Check(LeakCheck.NoSpanish(profEnText, allowedNames).Ok && LeakCheck.FitsDiscord(profEn) && profEn.Footer!.Value.Text.Contains("Asado y Acero RPG") && !profEn.Footer!.Value.Text.Contains("brasas"), $"y sin español colado ({LeakCheck.NoSpanish(profEnText, allowedNames).Found})");
string notRegisteredEn; using (Loc.Use("en")) { notRegisteredEn = GameModule.BuildNotRegisteredMessage("Pepe"); }
Check(notRegisteredEn.Contains("hasn't started") && notRegisteredEn.Contains("`/start`") && LeakCheck.NoSpanish(notRegisteredEn, allowedNames).Ok && GameModule.BuildNotRegisteredMessage("Pepe").Contains("todavía no arrancó"), "«todavía no arrancó su aventura» en los dos idiomas");

var zonesForCard = (await zoneRepo8.GetAllAsync()).ToList();
var zone2 = zonesForCard.OrderBy(z => z.MinLevel).First(z => z.MinLevel > 1);
var cardEs = LevelUpCard.Compose(L1, 7, 1, zonesForCard, new Random(1));
Check(cardEs.Title == "🎉 ¡SUBISTE DE NIVEL! 🎉" && cardEs.Fields[0].Name == "❤️ Vida máxima" && cardEs.Fields[3].Value == "Curada al máximo", "la tarjeta de nivel en español es la de siempre");
foreach (var (gained, newLevel) in new[] { (1, 7), (3, 12), (1, zone2.MinLevel) })
{
    LevelUpCardContent cardEn;
    using (Loc.Use("en")) { cardEn = LevelUpCard.Compose(L1, newLevel, gained, zonesForCard, new Random(1)); }
    string cardText = cardEn.Title + "\n" + cardEn.Description + "\n" + string.Join("\n", cardEn.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + cardEn.Footer;
    Check(LeakCheck.NoSpanish(cardText, allowedNames).Ok && cardEn.Title.Contains("LEVEL") && cardEn.Fields[0].Name.Contains("Max health") && cardEn.Fields[3].Value == "Fully healed",
        $"la tarjeta de nivel en inglés (subió {gained}, nivel {newLevel}): {cardEn.Title} / {LeakCheck.NoSpanish(cardText, allowedNames).Found}");
}

for (int seed = 0; seed < 40; seed++)
{
    string cheerEn; using (Loc.Use("en")) { cheerEn = LevelUpCard.Compose(L1, 7, 1, zonesForCard, new Random(seed)).Footer; }
    if (!LeakCheck.NoSpanish(cheerEn, allowedNames).Ok) { Check(false, $"una frase de festejo en inglés quedó en español: «{cheerEn}»"); break; }
}

var allClasses = ClassCatalog.All;
Embed welcomeEs = OnboardingModule.BuildWelcomeEmbed();
Embed welcomeEn; using (Loc.Use("en")) { welcomeEn = OnboardingModule.BuildWelcomeEmbed(); }
Check(welcomeEs.Title == "🔥 ¡Bienvenido a Asado y Acero RPG! ⚔️" && welcomeEn.Title.Contains("Welcome") && LeakCheck.NoSpanish(LeakCheck.AllText(welcomeEn), allowedNames).Ok && LeakCheck.FitsDiscord(welcomeEn), $"el embed de bienvenida de /start en los dos idiomas ({LeakCheck.NoSpanish(LeakCheck.AllText(welcomeEn), allowedNames).Found})");
Check(allClasses.All(c => { using (Loc.Use("en")) { return LeakCheck.NoSpanish(Loc.Tr(c.Description), allowedNames).Ok && Loc.Tr(c.Description) != c.Description; } }), "las cuatro descripciones de clase tienen su traducción");
var confirm = typeof(OnboardingModule).GetMethod("BuildConfirmationEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
var newPlayer = new User { Class = "Ninja", Level = 1, MaxHp = 100, CurrentHp = 100, Gold = 50 };
Embed confirmEs = (Embed)confirm.Invoke(null, ["T", newPlayer, allClasses.First(c => c.Name == "Ninja"), true])!;
Embed confirmEn; using (Loc.Use("en")) { confirmEn = (Embed)confirm.Invoke(null, ["T", newPlayer, allClasses.First(c => c.Name == "Ninja"), true])!; }
Check(confirmEs.Title == "🔥 Bienvenido a las Tierras de Cenizas" && confirmEn.Title.Contains("Ashlands") && LeakCheck.NoSpanish(LeakCheck.AllText(confirmEn), allowedNames).Ok && LeakCheck.FitsDiscord(confirmEn), $"la confirmación de /start (con el kit inicial) en los dos idiomas ({LeakCheck.NoSpanish(LeakCheck.AllText(confirmEn), allowedNames).Found})");
string alreadyEn; using (Loc.Use("en")) { alreadyEn = OnboardingModule.BuildAlreadyRegisteredMessage(newPlayer); }
Check(alreadyEn.Contains("You're already registered") && LeakCheck.NoSpanish(alreadyEn, allowedNames).Ok, "«ya estás registrado» en inglés");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)P8 });

Console.WriteLine("\n== 9. Combate solitario ==");
allowedNames.AddRange(["Aguante", "Bola de Fuego", "Sombra", "Lluvia de Flechas", "Sifón de Almas", "Sangre", "Fogón"]);   // habilidades, pasivas y lugares: nombres que se quedan en español
const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
Embed CallE(Type t, string method, params object?[] args) => (Embed)t.GetMethod(method, AnyStatic)!.Invoke(null, args)!;
CombatState Fight(string cmd, string cls = "Guerrero") => new(cmd, "Jabalí Rabioso", "🐗", 100, 60, 10, [], 0, 0, null, 100, 80, 100, 10, 2, 5, cls, ClassPassives.For(cls));
var userFight = new User { MaxHp = 100, CurrentHp = 80, Level = 5, Class = "Guerrero" };
var outcomeFight = new LevelUpOutcome(userFight, 0);
var rewardFight = new CombatReward(10, 10, false);
var dropFight = new Item { ItemId = 1, Name = "Colmillo de Jabalí", Type = "Material", Rarity = "Raro", Emoji = "<:colmillo_jabali:1555578251605442581>" };
var chestFight = new Item { ItemId = 3, Name = "Cajón de Pino", Type = "Caja", Rarity = "Común" };
var foodFight = new Item { ItemId = 2, Name = "Mate Amargo", Type = "Consumable", Rarity = "Común" };
var plainTurn = new TurnResult(null, false, 12, 0, 0, 60, 100, new MonsterHitOutcome(false, 7), 93, default);
var dodgeTurn = new TurnResult(null, false, 0, 0, 0, 60, 100, new MonsterHitOutcome(true, 0), 100, default);
var critDodgeTurn = new TurnResult(ClassAbilities.For("Hechicero"), false, 30, 2, 5, 40, 100, new MonsterHitOutcome(true, 0), 100, default, 2);
var killTurn = new TurnResult(null, false, 12, 1, 4, 0, 100, null, 100, default, 1);
var lossTurn = new TurnResult(null, false, 12, 0, 0, 40, 100, new MonsterHitOutcome(false, 90), 0, default);
var penaltyFight = new DeathPenaltyOutcome(120, 300, 2280);

(string Name, Func<Embed> Make)[] fightScreens =
[
    ("encuentro de /hunt", () => AdventureModule.BuildEncounterEmbed(Fight("hunt"))),
    ("encuentro de /travel", () => AdventureModule.BuildEncounterEmbed(Fight("travel"))),
    ("encuentro del jefe", () => AdventureModule.BuildEncounterEmbed(Fight("boss", "Hechicero"))),
    ("turno con golpe y contraataque", () => CallE(typeof(AdventureModule), "BuildOngoingEmbed", Fight("hunt"), plainTurn)),
    ("turno con esquive", () => CallE(typeof(AdventureModule), "BuildOngoingEmbed", Fight("hunt"), dodgeTurn)),
    ("turno con críticos, Sifón y Sangre", () => CallE(typeof(AdventureModule), "BuildOngoingEmbed", Fight("hunt", "Hechicero"), critDodgeTurn)),
    ("victoria con drop", () => CallE(typeof(AdventureModule), "BuildVictoryEmbed", Fight("hunt"), killTurn, rewardFight, dropFight, outcomeFight, true)),
    ("victoria del jefe, primera vez, con cofre", () => CallE(typeof(AdventureModule), "BuildVictoryEmbed", Fight("boss"), killTurn, rewardFight, chestFight, outcomeFight, true)),
    ("victoria del jefe, repetida", () => CallE(typeof(AdventureModule), "BuildVictoryEmbed", Fight("boss"), killTurn, rewardFight, null, outcomeFight, false)),
    ("derrota con penalidad", () => CallE(typeof(AdventureModule), "BuildDefeatEmbed", Fight("hunt"), lossTurn, penaltyFight)),
    ("huida", () => CallE(typeof(AdventureModule), "BuildFleeEmbed", Fight("hunt"))),
    ("sin HP para pelear", AdventureModule.BuildNoHpEmbed),
    ("zona sin monstruos", AdventureModule.BuildNoMonstersInZoneEmbed),
    ("zona sin jefe", AdventureModule.BuildNoBossInZoneEmbed),
    ("estás en el Fogón", AdventureModule.BuildInGateEmbed),
    ("falta el equipo del Fogón", AdventureModule.BuildGateGearMissingEmbed),
    ("nivel para el jefe", () => AdventureModule.BuildNotLeveledForBossEmbed(10)),
    ("/autohunt: victoria", () => CallE(typeof(AutoHuntModule), "BuildVictoryEmbed", Fight("hunt"), outcomeFight, rewardFight, dropFight)),
    ("/autohunt: derrota", () => CallE(typeof(AutoHuntModule), "BuildDefeatEmbed", Fight("hunt"), penaltyFight)),
    ("/autohunt: muy herido", () => AutoHuntModule.BuildTooHurtEmbed(30, 100)),
    ("/use en pelea", () => CallE(typeof(UseModule), "BuildCombatOngoingEmbed", Fight("travel"), foodFight, 5, false)),
    ("/use en pelea, esquivando", () => CallE(typeof(UseModule), "BuildCombatOngoingEmbed", Fight("travel"), foodFight, 5, true)),
    ("/use y la derrota", () => CallE(typeof(UseModule), "BuildCombatDefeatEmbed", Fight("travel"), foodFight, 5, penaltyFight)),
    ("combate abandonado", () => CombatSessionService.BuildAbandonedEmbed(Fight("hunt"))),
];

foreach (var (screenName, make) in fightScreens)
{
    Embed screenEs = make();
    Embed screenEn; using (Loc.Use("en")) { screenEn = make(); }
    string screenEnText = LeakCheck.AllText(screenEn);
    var screenLeak = LeakCheck.NoSpanish(screenEnText, allowedNames);
    Check(screenLeak.Ok && LeakCheck.FitsDiscord(screenEn) && screenEnText != LeakCheck.AllText(screenEs), $"en inglés, {screenName}: sin español colado ({screenLeak.Found})");
}

Embed huntEs = AdventureModule.BuildEncounterEmbed(Fight("hunt"));
Embed huntEn; using (Loc.Use("en")) { huntEn = AdventureModule.BuildEncounterEmbed(Fight("hunt")); }
Check(huntEs.Title == "🏹 ¡Encuentro en la caza!" && huntEs.Fields[0].Name == "❤️ Tu HP" && huntEs.Footer!.Value.Text.Contains("UNA vez"), "el encuentro de /hunt en español es el de siempre");
Check(huntEn.Title == "🏹 Hunt encounter!" && huntEn.Fields[0].Name == "❤️ Your HP" && huntEn.Fields[1].Name == "🐗 Jabalí Rabioso's HP" && huntEn.Description!.Contains("appears"), $"y en inglés ({huntEn.Title} | {huntEn.Fields[1].Name})");
Embed defeatEs = CallE(typeof(AdventureModule), "BuildDefeatEmbed", Fight("hunt"), lossTurn, penaltyFight);
Embed defeatEn; using (Loc.Use("en")) { defeatEn = CallE(typeof(AdventureModule), "BuildDefeatEmbed", Fight("hunt"), lossTurn, penaltyFight); }
Check(defeatEs.Fields.Any(f => f.Name == "☠️ Penalidad" && f.Value.Contains("Perdiste **120** de oro")) && defeatEn.Fields.Any(f => f.Name == "☠️ Penalty" && f.Value.Contains("You lost **120** gold")),
    $"la derrota y su penalidad en los dos idiomas ({defeatEn.Fields.FirstOrDefault(f => f.Name.Contains("Penal")).Value})");
Embed critEn; using (Loc.Use("en")) { critEn = CallE(typeof(AdventureModule), "BuildOngoingEmbed", Fight("hunt", "Hechicero"), critDodgeTurn); }
Check(critEn.Description!.Contains("2 CRITICAL HITS") && critEn.Description.Contains("dodged") && critEn.Description.Contains("Sifón de Almas healed you for **3** HP") && critEn.Description.Contains("Sangre healed you for **2** HP"),
    $"los críticos, el esquive, el Sifón y la Sangre del turno ({critEn.Description!.Replace("\n", " | ")})");
Check(AdventureModule.BuildCombatSummaryLine(Fight("hunt") with { TurnsElapsed = 3, TotalDamageDealt = 40, TotalDamageTaken = 21, CritCount = 1, DodgeCount = 2, TotalHealed = 6 }) ==
    "⏱️ 3 turno(s) · 🗡️ 40 de daño hecho · 🩸 21 de daño recibido · 💥 1 crítico(s) · 💨 Esquivaste 2 golpe(s) · 🔮 6 curados", "el resumen del combate en español es el de siempre");
string summaryEn; using (Loc.Use("en")) { summaryEn = AdventureModule.BuildCombatSummaryLine(Fight("hunt") with { TurnsElapsed = 3, TotalDamageDealt = 40, TotalDamageTaken = 21, CritCount = 1, DodgeCount = 2, TotalHealed = 6 }); }
Check(summaryEn == "⏱️ 3 turn(s) · 🗡️ 40 damage dealt · 🩸 21 damage taken · 💥 1 crit(s) · 💨 You dodged 2 hit(s) · 🔮 6 healed", $"y el mismo resumen en inglés ({summaryEn})");

IEnumerable<string> ButtonLabels(MessageComponent c) => c.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => b.Label);
string? MenuPlaceholder(MessageComponent c) => c.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().FirstOrDefault()?.Placeholder;
Check(ButtonLabels(AdventureModule.BuildCombatButtons(Fight("travel", "Ninja"))).SequenceEqual(["Atacar", "Sombra", "Huir"]) && MenuPlaceholder(AdventureModule.BuildCombatButtons(Fight("travel", "Ninja"))) == "🍖 No tenés comida para curarte", "los botones y el desplegable del combate en español son los de siempre");
MessageComponent buttonsEn, buttonsUsedEn;
using (Loc.Use("en")) { buttonsEn = AdventureModule.BuildCombatButtons(Fight("travel", "Ninja")); buttonsUsedEn = AdventureModule.BuildCombatButtons(Fight("boss", "Ninja") with { HealUsed = true }); }
Check(ButtonLabels(buttonsEn).SequenceEqual(["Attack", "Sombra", "Flee"]) && MenuPlaceholder(buttonsEn) == "🍖 You have no food to heal with" && MenuPlaceholder(buttonsUsedEn) == "🍖 You already healed in this fight", $"los botones en inglés (el nombre de la habilidad se queda): {string.Join(", ", ButtonLabels(buttonsEn))} | {MenuPlaceholder(buttonsEn)}");
string abilityLineEs = AdventureModule.BuildAbilityStatusLine("Hechicero", new AbilityState(2, 0));
string abilityLineEn, abilityEffectEn; using (Loc.Use("en")) { abilityLineEn = AdventureModule.BuildAbilityStatusLine("Hechicero", new AbilityState(2, 0)); abilityEffectEn = AdventureModule.BuildAbilityStatusLine("Guerrero", new AbilityState(0, 1)); }
Check(abilityLineEs == "🔥 Bola de Fuego: ⏳ en 2 turno(s)" && abilityLineEn == "🔥 Bola de Fuego: ⏳ in 2 turn(s)" && abilityEffectEn == "🛡️ Aguante: ready · Aguante active (1 turn(s))", $"la línea de la habilidad ({abilityLineEn} | {abilityEffectEn})");

var typedTables = NpcDialogue.AllTables().Where(t => !t.Key.StartsWith("herrero") && !t.Key.StartsWith("tendero") && !t.Key.StartsWith("tabernero")).ToList();
int notTranslated = 0, leaked = 0; string? firstMissing = null, firstLeak = null;
using (Loc.Use("en"))
{
    foreach (var (tableKey, lines) in typedTables)
    {
        foreach (string line in lines)
        {
            string translated = Loc.Tr(line);
            if (translated == line) { notTranslated++; firstMissing ??= $"{tableKey}: {line}"; continue; }
            if (!LeakCheck.NoSpanish(translated, allowedNames).Ok) { leaked++; firstLeak ??= $"{tableKey}: {translated}"; }
        }
    }
}

Check(typedTables.Count >= 60 && notTranslated == 0, $"TODA frase de monstruo y de jefe (incluidos los escalones del Asador y las genéricas) tiene su traducción: {typedTables.Count} tablas, {typedTables.Sum(t => t.Lines.Count)} frases, sin traducir {notTranslated} ({firstMissing})");
Check(leaked == 0, $"y ninguna traducción deja español colado ({firstLeak})");
string introEs = NpcDialogue.Monster("Jabalí Rabioso", "🐗", NpcDialogue.BossLine.Intro, rng: new Random(1));
string introEn, bossEn, genericEn;
using (Loc.Use("en"))
{
    introEn = NpcDialogue.Monster("Jabalí Rabioso", "🐗", NpcDialogue.BossLine.Intro, rng: new Random(1));
    bossEn = NpcDialogue.Monster("Rey Jabalí", "👑", NpcDialogue.BossLine.Victory, isBoss: true, rng: new Random(2));
    genericEn = NpcDialogue.Monster("Bicho Nuevo", "👾", NpcDialogue.BossLine.Defeated, rng: new Random(3));
}

Check(introEs.StartsWith("🐗 **Jabalí Rabioso:** «") && introEn.StartsWith("🐗 **Jabalí Rabioso:** «") && introEn != introEs && bossEn.StartsWith("👑 **Rey Jabalí:** «") && LeakCheck.NoSpanish(bossEn + genericEn, allowedNames).Ok,
    $"la frase se elige igual en los dos idiomas, con el nombre del monstruo sin traducir ({introEn} | {bossEn} | {genericEn})");

Console.WriteLine("\n== 10. Recolectar e inventario ==");
const ulong G10 = 999000000000098010UL;
var gatherRepo10 = sp.GetRequiredService<IGatheringRepository>(); var itemSvc10 = sp.GetRequiredService<IItemRepository>();
var bonusSvc10 = sp.GetRequiredService<IPlayerBonusService>(); var eventsSvc10 = sp.GetRequiredService<IGameEvents>(); var invRepo10 = sp.GetRequiredService<IInventoryRepository>();
var woodcutter = ProfessionCatalog.Get(ProfessionCatalog.WoodcutterKey);
ProfessionProgress Prog(long xp) => ProfessionRules.ProgressFor(xp);
(string Name, Func<string> Make)[] footerCases =
[
    ("pie normal", () => GatheringModule.ProfessionFooter(woodcutter, Prog(100), Prog(107), 7)),
    ("pie al subir de nivel", () => GatheringModule.ProfessionFooter(woodcutter, Prog(0), Prog(30), 30)),
    ("pie al llegar al máximo", () => GatheringModule.ProfessionFooter(woodcutter, Prog(0), Prog(100_000_000), 1)),
    ("pie con el máximo ya alcanzado", () => GatheringModule.ProfessionFooter(woodcutter, Prog(100_000_000), Prog(100_000_000), 1)),
];

foreach (var (footerName, makeFooter) in footerCases)
{
    string footerEs = makeFooter(); string footerEn; using (Loc.Use("en")) { footerEn = makeFooter(); }
    var footerLeak = LeakCheck.NoSpanish(footerEn, allowedNames);
    Check(footerEs.Contains("Leñador") && footerEn.Contains("Woodcutter") && footerLeak.Ok, $"{footerName}: «{footerEs}» / «{footerEn}» ({footerLeak.Found})");
}

var woodItem = (await itemSvc10.GetRandomByTypeAndRarityAsync("Madera", "Común"))!;
var rareOre = (await itemSvc10.GetRandomByTypeAndRarityAsync("Mineral", "Raro"))!;
(string Name, Func<Embed> Make)[] gatherScreens =
[
    ("resultado de /chop", () => GatheringModule.BuildResultEmbed(CooldownCatalog.Chop, woodItem, 3, GatheringModule.ProfessionFooter(woodcutter, Prog(100), Prog(107), 7))),
    ("resultado de /mine con un material raro", () => GatheringModule.BuildResultEmbed(CooldownCatalog.Mine, rareOre, 1)),
    ("resultado de la tala avanzada", () => GatheringModule.BuildAdvancedResultEmbed(CooldownCatalog.ChopAdvanced, [(woodItem, 4), (rareOre, 2)], GatheringModule.ProfessionFooter(woodcutter, Prog(0), Prog(30), 30))),
    ("cooldown de /mine", () => GatheringModule.BuildCooldownEmbed(CooldownCatalog.Mine, TimeSpan.FromSeconds(90))),
];

foreach (var (gatherName, makeGather) in gatherScreens)
{
    Embed gatherEs = makeGather(); Embed gatherEn; using (Loc.Use("en")) { gatherEn = makeGather(); }
    string gatherEnText = LeakCheck.AllText(gatherEn); var gatherLeak = LeakCheck.NoSpanish(gatherEnText, allowedNames);
    Check(gatherLeak.Ok && LeakCheck.FitsDiscord(gatherEn) && gatherEnText != LeakCheck.AllText(gatherEs), $"en inglés, {gatherName}: sin español colado ({gatherLeak.Found})");
}

Embed chopEs = GatheringModule.BuildResultEmbed(CooldownCatalog.Chop, woodItem, 3); Embed chopEn; using (Loc.Use("en")) { chopEn = GatheringModule.BuildResultEmbed(CooldownCatalog.Chop, woodItem, 3); }
Check(chopEs.Title == "🪓 ¡Talar exitoso!" && chopEs.Fields[0].Name == "Rareza" && chopEs.Fields[0].Value == "Común" && chopEs.Description!.StartsWith("Conseguiste **3× "), "el resultado de /chop en español es el de siempre");
Check(chopEn.Title == "🪓 Chop successful!" && chopEn.Fields[0].Name == "Rarity" && chopEn.Fields[0].Value == "Common" && chopEn.Description!.StartsWith("You got **3× "), $"y en inglés ({chopEn.Title} | {chopEn.Fields[0].Name}: {chopEn.Fields[0].Value} | {chopEn.Description})");
Embed advEn; using (Loc.Use("en")) { advEn = GatheringModule.BuildAdvancedResultEmbed(CooldownCatalog.ChopAdvanced, [(woodItem, 4), (rareOre, 2)]); }
Check(advEn.Title == "🪓 Advanced chopping successful!" && advEn.Description!.Contains("Rare") && advEn.Description.Contains("Common"), $"la recolección avanzada en inglés ({advEn.Title} | {advEn.Description!.Replace("\n", " | ")})");

// El flujo de verdad contra la base: el oficio bloqueado, un /chop y el cooldown inmediato.
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)G10 });
await userRepo.GetOrCreateUserAsync(G10);
Task<GatheringModule.GatherResult> Gather(bool advanced) => GatheringModule.ExecuteGatherAsync(cooldowns, gatherRepo10, userRepo, itemSvc10, bonusSvc10, eventsSvc10, G10, CooldownCatalog.Chop, "Madera", advanced);
var lockedEs = await Gather(true); GatheringModule.GatherResult lockedEn, firstEn, secondEn;
using (Loc.Use("en")) { lockedEn = await Gather(true); firstEn = await Gather(false); secondEn = await Gather(false); }
Check(lockedEs.PlainMessage!.Contains("se desbloquea con el oficio **Leñador**") && lockedEn.PlainMessage!.Contains("unlocks with the **Woodcutter** profession") && LeakCheck.NoSpanish(lockedEn.PlainMessage, allowedNames).Ok, $"«la tala avanzada está bloqueada» en los dos idiomas ({lockedEn.PlainMessage})");
Check(firstEn.Embed!.Title == "🪓 Chop successful!" && firstEn.Embed.Footer!.Value.Text.Contains("Woodcutter") && firstEn.Embed.Footer!.Value.Text.Contains("level") &&LeakCheck.NoSpanish(LeakCheck.AllText(firstEn.Embed), allowedNames).Ok, $"un /chop de verdad sale en inglés ({firstEn.Embed.Title} | {firstEn.Embed.Footer!.Value.Text})");
Check(secondEn.Embed!.Title.Contains("Chop") && secondEn.Embed.Title.Contains("you can't yet") && secondEn.Embed.Description!.StartsWith("You need to wait **"),$"y el segundo, seguido, trae el cooldown en inglés ({secondEn.Embed.Title} | {secondEn.Embed.Description})");

// El inventario de dos columnas.
await db.ExecuteAsync("""
    INSERT INTO inventory (discord_id, item_id, quantity)
    SELECT @id, item_id, CASE WHEN name = 'Madera de Pino' THEN 1234 ELSE 3 END FROM items
    WHERE name IN ('Madera de Pino', 'Hierro', 'Colmillo de Jabalí', 'Mate Amargo', 'Cajón de Pino') OR type = 'Huevo'
    ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity
    """, new { id = (long)G10 });
Embed invEs = await GameModule.BuildInventoryEmbedAsync(invRepo10, G10, "Pepe"); Embed invEn; using (Loc.Use("en")) { invEn = await GameModule.BuildInventoryEmbedAsync(invRepo10, G10, "Pepe"); }
string invEsText = LeakCheck.AllText(invEs), invEnText = LeakCheck.AllText(invEn);
Check(invEs.Title == "🎒 Inventario de Pepe" && invEsText.Contains("🪵 Madera") && invEsText.Contains("🩸 Drops de monstruo") && invEsText.Contains("🍖 Consumibles") && invEsText.Contains("1.234") && invEs.Footer!.Value.Text == "📦 Abrí tus cajas y huevos con /open", "el inventario en español es el de siempre");
Check(invEn.Title == "🎒 Pepe's inventory" && invEnText.Contains("🪵 Wood") && invEnText.Contains("🩸 Monster drops") && invEnText.Contains("🍖 Consumables") && invEnText.Contains("1,234") && invEn.Footer!.Value.Text == "📦 Open your boxes and eggs with /open" && LeakCheck.NoSpanish(invEnText, allowedNames).Ok && LeakCheck.FitsDiscord(invEn),
    $"y en inglés, con la coma de miles ({LeakCheck.NoSpanish(invEnText, allowedNames).Found})");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id AND item_id IN (SELECT item_id FROM items WHERE type = 'Huevo')", new { id = (long)G10 });
Embed invBoxesEn; using (Loc.Use("en")) { invBoxesEn = await GameModule.BuildInventoryEmbedAsync(invRepo10, G10, "Pepe"); }
Check(invBoxesEn.Footer!.Value.Text == "📦 Open your boxes with /open" && LeakCheck.AllText(invBoxesEn).Contains("📦 **Boxes**") && !LeakCheck.AllText(invBoxesEn).Contains("eggs"),$"sin huevos el título y el pie dicen solo «cajas» ({invBoxesEn.Footer!.Value.Text})");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)G10 });
Embed invEmptyEn; using (Loc.Use("en")) { invEmptyEn = await GameModule.BuildInventoryEmbedAsync(invRepo10, G10, "Pepe"); }
Check(invEmptyEn.Description == "You don't have any materials yet. Try /hunt, /chop or /mine!", $"el inventario vacío en inglés ({invEmptyEn.Description})");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)G10 });

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

// Un servicio de combate que solo sabe «cómo terminó el último»: para probar los tres textos de «no tenés combate».
sealed class EndStub(string? reason) : ICombatSessionService
{
    public bool TryStart(ulong discordId, CombatState state, ICombatReplyTarget replyTarget) => false;
    public CombatSession? Peek(ulong discordId) => null;
    public bool TryAdvance(ulong discordId, CombatSession expectedSession, CombatState? nextState, ICombatReplyTarget? nextReplyTarget) => false;
    public string? RecentEnd(ulong discordId) => reason;
}

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
