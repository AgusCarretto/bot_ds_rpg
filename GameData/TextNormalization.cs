using System.Globalization;
using System.Text;

namespace BotDsRpg.GameData;

public static class TextNormalization
{
    // Saca tildes/diacríticos para poder comparar nombres sin importar si el usuario los tipeó
    // o no (ej. "jabali" debe matchear "Jabalí"). No modifica datos guardados, solo se usa al
    // comparar texto tipeado por el usuario contra nombres conocidos.
    public static string RemoveDiacritics(string text)
    {
        string normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
