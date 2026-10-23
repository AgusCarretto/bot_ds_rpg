using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
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
