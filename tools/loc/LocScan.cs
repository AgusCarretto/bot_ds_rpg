using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// El escáner de textos del idioma (GameData/Loc.cs): recorre el código, saca la plantilla de cada Loc.T("…"), Loc.F($"…") y Loc.Mark("…") y la compara con Locales/en.json. Lo usan la consola
// (tools/loc) y el arnés loctest (que compila este mismo archivo): un solo código para las dos cosas.
//
// Qué mira: Modules, GameData, Services y Program.cs. Lo que NO es código no cuenta: comentarios, textos y cadenas crudas (si no, una línea de documentación que nombra Loc.T("…") se tomaría por un uso).
// Qué acepta: un solo literal, o literales simples unidos con +; $"…" con huecos solo en Loc.F. La plantilla de un $"…" se numera como la numera el compilador: cada hueco, en orden, es {0}, {1}…, y las
// llaves escapadas («{{», «}}») se conservan: es exactamente lo que ve FormattableString.Format, que es la clave del catálogo. Cualquier otra cosa (una variable, una cadena textual @"…") se informa como NonLiteral.
public sealed record LocUse(string Template, string File, int Line, string Kind);

public sealed record LocReport(IReadOnlyList<string> Missing, IReadOnlyList<string> Stale, IReadOnlyList<string> PlaceholderMismatch, IReadOnlyList<string> NonLiteral)
{
    public bool Ok => Missing.Count == 0 && Stale.Count == 0 && PlaceholderMismatch.Count == 0 && NonLiteral.Count == 0;
}

public static class LocScan
{
    private static readonly string[] Roots = ["Modules", "GameData", "Services"];
    private static readonly Regex CallPattern = new(@"\bLoc\.(T|F|Mark)\s*\(", RegexOptions.Compiled);
    private static readonly Regex SqlStart = new(@"^\s*(SELECT|INSERT|UPDATE|DELETE|WITH|CREATE|ALTER|DROP)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HoleIndex = new(@"\{(\d+)", RegexOptions.Compiled);

    // ───────────── Uso: sacar las plantillas del código ─────────────

    public static IReadOnlyList<LocUse> Extract(string repoRoot)
    {
        var uses = new List<LocUse>();
        foreach (string file in SourceFiles(repoRoot))
        {
            uses.AddRange(ExtractFromSource(File.ReadAllText(file), Path.GetRelativePath(repoRoot, file).Replace('\\', '/')));
        }

        return uses;
    }

    public static IReadOnlyList<LocUse> ExtractFromSource(string source, string file)
    {
        string masked = Mask(source);
        var uses = new List<LocUse>();
        foreach (Match call in CallPattern.Matches(masked))
        {
            string kind = call.Groups[1].Value;
            int line = LineOf(source, call.Index);
            var (template, problem) = ParseArgument(source, call.Index + call.Length, kind);
            uses.Add(template is null ? new LocUse(problem ?? "no es un literal", file, line, "NonLiteral") : new LocUse(template, file, line, kind));
        }

        return uses;
    }

    private static IEnumerable<string> SourceFiles(string repoRoot)
    {
        foreach (string root in Roots)
        {
            string dir = Path.Combine(repoRoot, root);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (!normalized.Contains("/obj/") && !normalized.Contains("/bin/"))
                {
                    yield return file;
                }
            }
        }

        string program = Path.Combine(repoRoot, "Program.cs");
        if (File.Exists(program))
        {
            yield return program;
        }
    }

    // El argumento de la llamada: un literal o varios simples unidos con +, y a continuación «)». Devuelve la plantilla, o null y el motivo.
    private static (string? Template, string? Problem) ParseArgument(string source, int pos, string kind)
    {
        var template = new StringBuilder();
        int i = pos;
        while (true)
        {
            i = SkipSpaces(source, i);
            if (i >= source.Length)
            {
                return (null, "la llamada no termina");
            }

            if (source[i] == '$' && i + 1 < source.Length && source[i + 1] == '"' && !(i + 2 < source.Length && source[i + 2] == '"'))
            {
                if (kind != "F" || template.Length > 0)
                {
                    return (null, "un $\"…\" con datos va en Loc.F, no en " + "Loc." + kind);
                }

                int end = SkipString(source, i);
                template.Append(InterpolatedTemplate(source, i + 2, end - 1));
                i = SkipSpaces(source, end);
                return source[i] is ')' or ',' ? (template.ToString(), null) : (null, "después del texto interpolado hay más código: " + Snippet(source, pos));
            }

            if (source[i] == '"' && !(i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"'))
            {
                int end = SkipString(source, i);
                template.Append(Unescape(source, i + 1, end - 1));
                i = SkipSpaces(source, end);
                if (i < source.Length && source[i] == '+')
                {
                    i++;
                    continue;
                }

                return i < source.Length && source[i] is ')' or ',' ? (template.ToString(), null) : (null, "después del texto hay más código: " + Snippet(source, pos));
            }

            return (null, "no es un literal: " + Snippet(source, pos));
        }
    }

    // ───────────── Plantillas de $"…": los huecos se numeran como el compilador ─────────────

    private static string InterpolatedTemplate(string s, int start, int end)
    {
        var result = new StringBuilder();
        int index = 0;
        int i = start;
        while (i < end)
        {
            char c = s[i];
            if (c == '\\')
            {
                i = UnescapeOne(s, i, result);
            }
            else if ((c == '{' || c == '}') && i + 1 < end && s[i + 1] == c)
            {
                result.Append(c).Append(c);
                i += 2;
            }
            else if (c == '{')
            {
                int j = i + 1;
                int depth = 0;
                int align = -1, format = -1, close = -1;
                while (j < end)
                {
                    char d = s[j];
                    if (d == '"' || ((d == '$' || d == '@') && j + 1 < s.Length && (s[j + 1] == '"' || s[j + 1] == '$' || s[j + 1] == '@')))
                    {
                        j = SkipString(s, j);
                        continue;
                    }

                    if (d == '\'')
                    {
                        j = SkipChar(s, j);
                        continue;
                    }

                    if (d is '(' or '[' or '{')
                    {
                        depth++;
                    }
                    else if (d is ')' or ']')
                    {
                        depth--;
                    }
                    else if (d == '}')
                    {
                        if (depth == 0)
                        {
                            close = j;
                            break;
                        }

                        depth--;
                    }
                    else if (depth == 0 && d == ',' && align < 0 && format < 0)
                    {
                        align = j;
                    }
                    else if (depth == 0 && d == ':' && format < 0)
                    {
                        // El formato llega hasta la próxima «}» (un formato no lleva llaves).
                        format = j;
                        int closeAt = s.IndexOf('}', j);
                        close = closeAt < 0 || closeAt >= end ? end : closeAt;
                        break;
                    }

                    j++;
                }

                if (close < 0)
                {
                    close = end;
                }

                result.Append('{').Append(index++);
                if (align >= 0)
                {
                    int alignEnd = format >= 0 ? format : close;
                    result.Append(',').Append(s, align + 1, alignEnd - align - 1);
                }

                if (format >= 0)
                {
                    result.Append(':').Append(s, format + 1, close - format - 1);
                }

                result.Append('}');
                i = close + 1;
            }
            else
            {
                result.Append(c);
                i++;
            }
        }

        return result.ToString();
    }

    // ───────────── Lectura de literales ─────────────

    // El texto de un literal común entre start y end (sin las comillas), con los escapes resueltos.
    private static string Unescape(string s, int start, int end)
    {
        var result = new StringBuilder();
        int i = start;
        while (i < end)
        {
            i = s[i] == '\\' ? UnescapeOne(s, i, result) : AppendAndAdvance(s, i, result);
        }

        return result.ToString();
    }

    private static int AppendAndAdvance(string s, int i, StringBuilder result)
    {
        result.Append(s[i]);
        return i + 1;
    }

    // Resuelve UN escape que empieza en s[i] == '\\' y devuelve el índice que sigue.
    private static int UnescapeOne(string s, int i, StringBuilder result)
    {
        if (i + 1 >= s.Length)
        {
            result.Append('\\');
            return i + 1;
        }

        char c = s[i + 1];
        switch (c)
        {
            case 'n': result.Append('\n'); return i + 2;
            case 'r': result.Append('\r'); return i + 2;
            case 't': result.Append('\t'); return i + 2;
            case '0': result.Append('\0'); return i + 2;
            case 'a': result.Append('\a'); return i + 2;
            case 'b': result.Append('\b'); return i + 2;
            case 'f': result.Append('\f'); return i + 2;
            case 'v': result.Append('\v'); return i + 2;
            case 'u' when i + 5 < s.Length + 0 && int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u):
                result.Append((char)u);
                return i + 6;
            case 'U' when i + 9 < s.Length + 0 && int.TryParse(s.AsSpan(i + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int big):
                result.Append(char.ConvertFromUtf32(big));
                return i + 10;
            default:
                result.Append(c);
                return i + 2;
        }
    }

    // Dado s[i] en «"», «$», o «@» (el inicio de un literal), devuelve el índice que sigue a su cierre. Entiende comunes, textuales (@"…"), interpolados ($"…" con huecos que pueden llevar
    // textos adentro) y crudos ("""…"""). Sin cierre devuelve el largo del texto.
    private static int SkipString(string s, int i)
    {
        int j = i;
        bool interpolated = false, verbatim = false;
        while (j < s.Length && (s[j] == '$' || s[j] == '@'))
        {
            interpolated |= s[j] == '$';
            verbatim |= s[j] == '@';
            j++;
        }

        if (j >= s.Length || s[j] != '"')
        {
            return j;
        }

        int quotes = 0;
        while (j + quotes < s.Length && s[j + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            // Cadena cruda: termina en la próxima tanda de al menos esas comillas.
            string closing = new('"', quotes);
            int at = s.IndexOf(closing, j + quotes, StringComparison.Ordinal);
            if (at < 0)
            {
                return s.Length;
            }

            int end = at + quotes;
            while (end < s.Length && s[end] == '"')
            {
                end++;
            }

            return end;
        }

        j++;
        while (j < s.Length)
        {
            char c = s[j];
            if (!verbatim && c == '\\')
            {
                j += 2;
                continue;
            }

            if (c == '"')
            {
                if (verbatim && j + 1 < s.Length && s[j + 1] == '"')
                {
                    j += 2;
                    continue;
                }

                return j + 1;
            }

            if (interpolated && c == '{')
            {
                if (j + 1 < s.Length && s[j + 1] == '{')
                {
                    j += 2;
                    continue;
                }

                j = SkipHole(s, j + 1);
                continue;
            }

            j++;
        }

        return s.Length;
    }

    // El hueco de un $"…" que empieza justo después de «{»: devuelve el índice que sigue a su «}».
    private static int SkipHole(string s, int i)
    {
        int depth = 0;
        int j = i;
        while (j < s.Length)
        {
            char c = s[j];
            if (c == '"' || ((c == '$' || c == '@') && j + 1 < s.Length && (s[j + 1] == '"' || s[j + 1] == '$' || s[j + 1] == '@')))
            {
                j = SkipString(s, j);
                continue;
            }

            if (c == '\'')
            {
                j = SkipChar(s, j);
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }
            else if (c == '}')
            {
                if (depth == 0)
                {
                    return j + 1;
                }

                depth--;
            }
            else if (c == ':' && depth == 0)
            {
                int close = s.IndexOf('}', j);
                return close < 0 ? s.Length : close + 1;
            }

            j++;
        }

        return s.Length;
    }

    private static int SkipChar(string s, int i)
    {
        int j = i + 1;
        while (j < s.Length)
        {
            if (s[j] == '\\')
            {
                j += 2;
                continue;
            }

            if (s[j] == '\'')
            {
                return j + 1;
            }

            j++;
        }

        return s.Length;
    }

    // El mismo texto con los comentarios y el interior de los textos y caracteres reemplazados por espacios (los saltos de línea se conservan: los números de línea no se corren). Sirve para
    // ubicar las llamadas Loc.T( sin confundirlas con una línea de documentación o con un texto que las nombre.
    private static string Mask(string s)
    {
        var chars = s.ToCharArray();
        MaskRange(s, 0, s.Length, chars);
        return new string(chars);
    }

    // Enmascara s[start..limit) tratándolo como CÓDIGO: los comentarios y el interior de los textos se blanquean, pero los huecos de un $"…" siguen siendo código (ahí adentro puede haber un Loc.T("…")).
    private static void MaskRange(string s, int start, int limit, char[] chars)
    {
        int i = start;
        while (i < limit)
        {
            char c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                int end = s.IndexOf('\n', i);
                end = end < 0 ? s.Length : end;
                Blank(chars, i, end);
                i = end;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int end = close < 0 ? s.Length : close + 2;
                Blank(chars, i, end);
                i = end;
            }
            else if (c == '"' || ((c == '$' || c == '@') && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '$' || s[i + 1] == '@')))
            {
                int stringEnd = SkipString(s, i);
                MaskString(s, i, stringEnd, chars);
                i = Math.Max(stringEnd, i + 1);
            }
            else if (c == '\'')
            {
                int end = SkipChar(s, i);
                Blank(chars, i, end);
                i = Math.Max(end, i + 1);
            }
            else
            {
                i++;
            }
        }
    }

    // Un texto: se blanquea entero, salvo los huecos de un $"…" (lo que va entre llaves), que se vuelven a enmascarar como código.
    private static void MaskString(string s, int start, int end, char[] chars)
    {
        Blank(chars, start, end);
        int j = start;
        bool interpolated = false, verbatim = false;
        while (j < end && (s[j] == '$' || s[j] == '@'))
        {
            interpolated |= s[j] == '$';
            verbatim |= s[j] == '@';
            j++;
        }

        if (!interpolated || j + 2 >= end || (s[j + 1] == '"' && s[j + 2] == '"'))
        {
            return; // un texto común o una cadena cruda: nada es código
        }

        int k = j + 1;
        while (k < end - 1)
        {
            char c = s[k];
            if (!verbatim && c == '\\')
            {
                k += 2;
            }
            else if (c == '{' && s[k + 1] == '{')
            {
                k += 2;
            }
            else if (c == '{')
            {
                int holeEnd = SkipHole(s, k + 1);
                for (int m = k + 1; m < holeEnd - 1 && m < s.Length; m++)
                {
                    chars[m] = s[m];
                }

                MaskRange(s, k + 1, Math.Min(holeEnd - 1, s.Length), chars);
                k = holeEnd;
            }
            else
            {
                k++;
            }
        }
    }

    private static void Blank(char[] chars, int start, int end)
    {
        for (int k = start; k < end && k < chars.Length; k++)
        {
            if (chars[k] != '\n' && chars[k] != '\r')
            {
                chars[k] = ' ';
            }
        }
    }

    private static int SkipSpaces(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    private static int LineOf(string s, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < s.Length; i++)
        {
            if (s[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string Snippet(string s, int pos) =>
        s.Substring(pos, Math.Min(40, s.Length - pos)).Replace("\r", " ").Replace("\n", " ").Trim();

    // ───────────── Comparar con el catálogo ─────────────

    public static LocReport Compare(IReadOnlyList<LocUse> uses, IReadOnlyDictionary<string, string> english)
    {
        var missing = new List<string>();
        var mismatch = new List<string>();
        var nonLiteral = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var use in uses)
        {
            if (use.Kind == "NonLiteral")
            {
                nonLiteral.Add($"{use.File}:{use.Line}: {use.Template}");
                continue;
            }

            used.Add(use.Template);
            if (!reported.Add(use.Template))
            {
                continue;
            }

            if (!english.TryGetValue(use.Template, out string? translation) || string.IsNullOrWhiteSpace(translation))
            {
                missing.Add($"{use.File}:{use.Line}: «{use.Template}»");
            }
            else if (!SamePlaceholders(use.Template, translation))
            {
                mismatch.Add($"{use.File}:{use.Line}: «{use.Template}» → «{translation}»");
            }
        }

        var stale = english.Keys.Where(key => !used.Contains(key)).ToList();
        return new LocReport(missing, stale, mismatch, nonLiteral);
    }

    // Las plantillas que todavía no tienen traducción, sin repetir y en el orden en que aparecen en el código (para armar el esqueleto de Locales/en.json con --skeleton).
    public static IReadOnlyList<string> MissingTemplates(IReadOnlyList<LocUse> uses, IReadOnlyDictionary<string, string> english) =>
        uses.Where(u => u.Kind != "NonLiteral" && (!english.TryGetValue(u.Template, out string? en) || string.IsNullOrWhiteSpace(en))).Select(u => u.Template).Distinct(StringComparer.Ordinal).ToList();

    // Los {0}, {1}… de las dos plantillas son los mismos (en otro orden, con otro formato, está bien). Las llaves escapadas no cuentan.
    private static bool SamePlaceholders(string spanish, string english) => Indexes(spanish).SetEquals(Indexes(english));

    private static HashSet<int> Indexes(string template) =>
        HoleIndex.Matches(template.Replace("{{", string.Empty).Replace("}}", string.Empty)).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToHashSet();

    // ───────────── Lo que falta envolver ─────────────

    // Los textos que parecen para el jugador y todavía NO están dentro de un Loc.*: «archivo:línea: «texto»». Es una lista de candidatos (una heurística, para no revisar a ojo), no un veredicto.
    // Se saltea: consultas, ids (sin espacios), la consola y el registro, mensajes de excepción, atributos ([SlashCommand(…)]), cadenas crudas y lo que ya está envuelto.
    public static IReadOnlyList<string> Untranslated(string path)
    {
        var found = new List<string>();
        bool isDirectory = Directory.Exists(path);
        string baseDir = isDirectory ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        IEnumerable<string> files = isDirectory
            ? Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories).Where(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/")).OrderBy(f => f, StringComparer.Ordinal)
            : new[] { path };

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            string masked = Mask(source);
            string relative = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
            int i = 0;
            while (i < source.Length)
            {
                // Un literal empieza donde el texto enmascarado lo tenía en blanco y el original no.
                char c = source[i];
                bool startsString = c == '"' || ((c == '$' || c == '@') && i + 1 < source.Length && (source[i + 1] == '"' || source[i + 1] == '$' || source[i + 1] == '@'));
                if (!startsString || masked[i] != ' ' || char.IsWhiteSpace(source[i]))
                {
                    i++;
                    continue;
                }

                int end = SkipString(source, i);
                string literal = source[i..Math.Min(end, source.Length)];
                if (IsPlayerText(source, i, literal))
                {
                    int open = literal.IndexOf('"');
                    int close = literal.LastIndexOf('"');
                    found.Add($"{relative}:{LineOf(source, i)}: «{literal.Substring(open + 1, Math.Max(0, close - open - 1)).Replace("\n", "\\n")}»");
                }

                i = Math.Max(end, i + 1);
            }
        }

        return found;
    }

    private static bool IsPlayerText(string source, int index, string literal)
    {
        if (literal.Contains("\"\"\"") || literal.StartsWith('@') || literal.StartsWith("$@", StringComparison.Ordinal))
        {
            return false;
        }

        int open = literal.IndexOf('"');
        int close = literal.LastIndexOf('"');
        if (open < 0 || close <= open)
        {
            return false;
        }

        string text = literal.Substring(open + 1, close - open - 1);
        if (!text.Contains(' ') || text.Count(char.IsLetter) < 2 || SqlStart.IsMatch(text))
        {
            return false;
        }

        int lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        string before = source[lineStart..index];
        string trimmed = before.TrimStart();
        return !(trimmed.StartsWith('[')
            || before.Contains("Loc.") || before.Contains("Console.") || before.Contains("BotLog.") || before.Contains("Debug.")
            || before.Contains("throw new") || before.Contains("nameof(") || before.Contains("Exception("));
    }
}
