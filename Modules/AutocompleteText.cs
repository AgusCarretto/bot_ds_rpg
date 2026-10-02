using System.Globalization;
using System.Text;

// Piezas comunes de todas las listas desplegables de Discord (ver Modules/ItemAutocomplete.cs,
// ZoneAutocomplete.cs y ForgeAutocomplete.cs): límites de Discord y el filtrado por texto.
public static class AutocompleteText
{
    // Discord muestra como máximo 25 sugerencias, y cada nombre de opción admite hasta 100 caracteres.
    public const int MaxChoices = 25;
    public const int MaxLength = 100;

    // Discord.Net tira excepción si el VALOR de una opción pasa de 100 caracteres (lo que rompería la
    // lista entera). Ningún nombre real se acerca, pero uno así se omite en vez de romper.
    public static bool FitsAsValue(string value) => value.Length <= MaxLength;

    // Sin texto escrito entra todo; con texto, entra lo que lo contenga sin importar mayúsculas ni
    // tildes ("jabali" encuentra "Colmillo de Jabalí", "cordero" encuentra "Cordero Patagónico").
    public static bool Matches(string name, string typed) =>
        string.IsNullOrWhiteSpace(typed) || Normalize(name).Contains(Normalize(typed), StringComparison.Ordinal);

    // Mismo nombre sin importar mayúsculas, tildes ni espacios de los costados ("cordero patagonico" == "Cordero Patagónico").
    public static bool SameName(string a, string b) => Normalize(a) == Normalize(b);

    // Los que EMPIEZAN con lo escrito van antes que los que solo lo contienen.
    public static int Relevance(string name, string typed) =>
        !string.IsNullOrWhiteSpace(typed) && Normalize(name).StartsWith(Normalize(typed), StringComparison.Ordinal) ? 0 : 1;

    public static string Truncate(string label) =>
        label.Length <= MaxLength ? label : label[..(MaxLength - 1)] + "…";

    private static string Normalize(string text)
    {
        string decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
