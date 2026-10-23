using System.Text.RegularExpressions;
using Discord;

// El revisor de «fugas de español» de los arneses del idioma (docs/superpowers/plans/2026-10-23-idioma.md): una pantalla armada con Loc.Use("en") no puede dejar caer texto en español, salvo los
// NOMBRES que se quedan en español a propósito (ítems, monstruos, zonas, personajes, clases y títulos). Es una heurística pensada para atrapar lo que se olvida de envolver, no un traductor.
public static class LeakCheck
{
    // Letras que solo existen en el español de este juego y palabras sueltas muy comunes en sus textos.
    private static readonly Regex Accented = new("[áéíóúñ¿¡ÁÉÍÓÚÑ]", RegexOptions.Compiled);
    private static readonly Regex Words = new(@"\b(de|tu|tus|tenés|podés|para|con|que|el|la|los|las|una|un|por|ya|sos|hacés|elegí|usá|mirá|probá)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Los nombres permitidos se sacan del texto antes de mirar (se compara sin importar mayúsculas). Los comandos entre comillas invertidas tampoco cuentan: `/taberna` es un nombre.
    public static (bool Ok, string? Found) NoSpanish(string text, IEnumerable<string> allowedNames)
    {
        string cleaned = text;
        foreach (string name in allowedNames.Where(n => n.Length > 0).OrderByDescending(n => n.Length))
        {
            cleaned = cleaned.Replace(name, " ", StringComparison.OrdinalIgnoreCase);
        }

        cleaned = Regex.Replace(cleaned, "`[^`]*`", " ");     // `/taberna`, `aa language es`
        cleaned = Regex.Replace(cleaned, "<[^>]*>", " ");     // menciones y emojis propios
        var accent = Accented.Match(cleaned);
        if (accent.Success)
        {
            return (false, Around(cleaned, accent.Index));
        }

        var word = Words.Match(cleaned);
        return word.Success ? (false, Around(cleaned, word.Index)) : (true, null);
    }

    // Lo que Discord acepta: 6.000 caracteres entre todo el embed y 1.024 por campo.
    public static bool FitsDiscord(Embed embed) =>
        embed.Length <= 6000 && embed.Fields.All(f => f.Value.Length <= 1024 && f.Name.Length <= 256);

    // Todo el texto de un embed junto, para pasarlo por NoSpanish.
    public static string AllText(Embed e) =>
        e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? string.Empty);

    private static string Around(string text, int index) =>
        text.Substring(Math.Max(0, index - 15), Math.Min(text.Length - Math.Max(0, index - 15), 40)).Replace("\n", " ");
}
