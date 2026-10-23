using System.Globalization;
using System.Text.Json;

namespace BotDsRpg.GameData;

// El idioma del bot (español / inglés), v0.26.0. Diseño: docs/superpowers/specs/2026-10-23-idioma-design.md.
//
// El texto en ESPAÑOL es la clave (estilo gettext): el código sigue legible y no hay claves que inventar. Un archivo (Locales/en.json, incrustado en el ensamblado) dice «texto en español» → «English text».
// Si no hay traducción sale el español: un texto sin traducir nunca rompe nada.
//   Loc.T("Estás en plena pelea")      texto simple
//   Loc.F($"Tenés {hp} HP")            texto con datos: la plantilla es «Tenés {0} HP» y la traducción puede reordenar los {0}, {1}
// Loc.Mark / Loc.Tr son para el texto que vive en tablas fijas (diálogos, descripciones): Mark marca el literal donde se declara y Tr lo traduce donde se muestra.
//
// El idioma actual es un AsyncLocal que se fija UNA vez por comando (Loc.Use): así las decenas de métodos estáticos que arman pantallas no cambian de firma y dos jugadores a la vez, cada uno en
// su idioma, no se pisan. OJO: un AsyncLocal que se fija adentro de un método async NO vuelve al que lo llamó, así que Loc.Use se llama SIEMPRE en el método que va a seguir usando el idioma
// (después de resolverlo con await), nunca adentro de un helper async que devuelva el alcance.
public static class Loc
{
    public const string Spanish = "es";
    public const string English = "en";

    private static readonly AsyncLocal<string?> CurrentLanguage = new();
    private static volatile IReadOnlyDictionary<string, string> _english = new Dictionary<string, string>();

    public static string Current => CurrentLanguage.Value ?? Spanish;

    // "en", "EN", "english" y los de región ("en-US") son inglés; cualquier otra cosa (o nada) es español.
    public static string Normalize(string? language)
    {
        string text = (language ?? string.Empty).Trim().ToLowerInvariant();
        return text is "en" or "english" || text.StartsWith("en-", StringComparison.Ordinal) ? English : Spanish;
    }

    // Fija el idioma hasta que se cierre el alcance, y entonces restaura el anterior (se pueden anidar).
    public static IDisposable Use(string language)
    {
        string? previous = CurrentLanguage.Value;
        CurrentLanguage.Value = Normalize(language);
        return new Scope(previous);
    }

    public static string T(string es) => Current == English ? Lookup(es) : es;

    // En español devuelve EXACTAMENTE lo que daba el $"…" de antes (es.ToString(), misma cultura); en inglés busca la plantilla (es.Format) y le pone los datos. Una traducción mal armada
    // (un {1} donde hay un solo dato) no tira: cae al español.
    public static string F(FormattableString es)
    {
        if (Current == English && _english.TryGetValue(es.Format, out string? template) && !string.IsNullOrEmpty(template))
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, es.GetArguments());
            }
            catch (FormatException)
            {
                // cae al español, abajo
            }
        }

        return es.ToString();
    }

    // Marca un literal como texto traducible de una tabla (el escáner de tools/loc lo toma como clave del catálogo). No cambia nada: devuelve el mismo texto.
    public static string Mark(string es) => es;

    // Traduce un texto que ya viene de una tabla (declarado con Mark): lo busca en el catálogo igual que T.
    public static string Tr(string esRuntime) => T(esRuntime);

    // Reemplaza todo el catálogo inglés (pruebas y arranque).
    public static void SetCatalog(IReadOnlyDictionary<string, string> english) =>
        _english = new Dictionary<string, string>(english, StringComparer.Ordinal);

    // Lee Locales/en.json (incrustado). NUNCA tira: sin catálogo todo sale en español.
    public static void LoadEmbedded()
    {
        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream("Locales.en.json");
            if (stream is null)
            {
                Console.WriteLine("[IDIOMA] el catálogo inglés no está incrustado: todo sale en español.");
                return;
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (map is not null)
            {
                SetCatalog(map);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IDIOMA] no pude leer el catálogo inglés ({ex.Message}): todo sale en español.");
        }
    }

    private static string Lookup(string es) =>
        _english.TryGetValue(es, out string? en) && !string.IsNullOrEmpty(en) ? en : es;

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                CurrentLanguage.Value = previous;
            }
        }
    }
}
