namespace BotDsRpg.GameData;

// Lo que el jugador escribe en "aa desmantelar" (v0.16.2): "<ítem>", "<cantidad> <ítem>" o "todo <ítem>". Pura, para que la lectura se pruebe sin Discord.
// La cantidad es el PRIMER pedazo solo si parece una cantidad (un número, con o sin puntos de miles, o all/todo/max): ningún ítem del catálogo empieza así,
// y así "aa desmantelar Madera de Pino" sigue siendo UNA sola Madera de Pino.
public static class DismantleRequest
{
    // QuantityText: "1" si no escribió cantidad. ItemName: vacío si faltó el ítem ("aa desmantelar todo").
    public static (string QuantityText, string ItemName) Parse(string? args)
    {
        string text = (args ?? string.Empty).Trim();
        int space = text.IndexOf(' ');
        if (space > 0)
        {
            string first = text[..space];
            if (LooksLikeQuantity(first))
            {
                return (first, text[(space + 1)..].Trim());
            }
        }
        else if (LooksLikeQuantity(text))
        {
            return (text, string.Empty);
        }

        return ("1", text);
    }

    public static bool LooksLikeQuantity(string token) =>
        AmountParser.IsAllWord(token) || (token.Any(char.IsDigit) && token.All(c => char.IsDigit(c) || c is '.' or ',' or '_'));
}
