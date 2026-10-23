using BotDsRpg.GameData;

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

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
