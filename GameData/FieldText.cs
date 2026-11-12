namespace BotDsRpg.GameData;

// Recorte de defensa para el valor de un campo de embed (Discord admite 1024 caracteres y rechaza el mensaje ENTERO si se pasa).
// Corta por RENGLÓN entero y cierra con «…»: cortar por carácter podía caer en el medio de un emoji propio (`<:nombre:id>`, que
// quedaba como código roto) o de un `**` (que dejaba en negrita todo lo que seguía). Puro: se prueba sin Discord.
public static class FieldText
{
    public const int MaxFieldValue = 1024;

    public static string FitLines(string text, int max = MaxFieldValue)
    {
        if (text.Length <= max)
        {
            return text;
        }

        // Lugar para el salto y el «…» del final.
        int cut = text.LastIndexOf('\n', max - 2);
        if (cut <= 0)
        {
            // Un solo renglón larguísimo: no hay dónde cortar prolijo, queda el corte de siempre.
            return text[..(max - 1)] + "…";
        }

        return text[..cut].TrimEnd('\n') + "\n…";
    }
}
