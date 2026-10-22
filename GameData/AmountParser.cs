namespace BotDsRpg.GameData;

// El resultado de leer una cantidad de oro que escribe el jugador: o un monto concreto (IsAll = lo pidió con "all"), o el motivo por el que no se entendió.
public sealed record AmountParse(bool Ok, int Amount, bool IsAll, string? Error);

// Lee la cantidad de oro de /play, /give y /bank: un número ("500", "1.000", "1,000"), con k de mil o m de millón ("10k" = 10.000, "1m" = 1.000.000, "1,5m" = 1.500.000), o "all" (también "todo", "toda", "max")
// para TODO el oro que tiene. Con k o m el punto y la coma son DECIMALES ("1.5k" = 1.500); sin k ni m siguen siendo separadores de miles ("1.500" = 1.500).
// Pura. "available" es el oro del jugador al momento de pedirlo; lo que se resuelve acá es solo el texto (que alcance o no lo valida cada comando,
// con la guarda atómica de la base).
public static class AmountParser
{
    private static readonly string[] AllWords = ["all", "todo", "toda", "todos", "todas", "max", "maximo", "máximo"];

    // true si el texto es una de las palabras de "todo" (all, todo, toda, max...). Lo usa también /dismantle ("todo" = todas las unidades de ese ítem).
    public static bool IsAllWord(string? text) => AllWords.Contains((text ?? string.Empty).Trim().ToLowerInvariant());

    public static AmountParse Parse(string? text, int available)
    {
        string raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return Fail(raw);
        }

        if (AllWords.Contains(raw.ToLowerInvariant()))
        {
            return available > 0
                ? new AmountParse(true, available, IsAll: true, null)
                : new AmountParse(false, 0, IsAll: true, "No tenés oro para esto: tu saldo es **0**.");
        }

        // "10k" / "1m" / "1,5m": mil y millón (v0.23.1, el dueño: «que exista la forma de poner 10k haciendo referencia a 10000, lo mismo con m de millones»).
        if (TryScaled(raw, out decimal scaled))
        {
            if (scaled > int.MaxValue)
            {
                return new AmountParse(false, 0, IsAll: false, "Esa cantidad es demasiado grande.");
            }

            int whole = (int)Math.Floor(scaled);
            return whole < 1
                ? new AmountParse(false, 0, IsAll: false, "La cantidad tiene que ser al menos 1.")
                : new AmountParse(true, whole, IsAll: false, null);
        }

        // "1.000" / "1,000" / "1 000": los separadores de miles se ignoran (en español el punto separa miles; un monto de oro no lleva decimales).
        string digits = new string(raw.Where(c => c is not ('.' or ',' or ' ' or '_')).ToArray());
        if (digits.Length == 0 || !digits.All(char.IsDigit))
        {
            return Fail(raw);
        }

        if (!long.TryParse(digits, out long value) || value > int.MaxValue)
        {
            return new AmountParse(false, 0, IsAll: false, "Esa cantidad es demasiado grande.");
        }

        return value < 1
            ? new AmountParse(false, 0, IsAll: false, "La cantidad tiene que ser al menos 1.")
            : new AmountParse(true, (int)value, IsAll: false, null);
    }

    // «10k», «1m», «1,5m», «2.5K»: un número (con a lo sumo un punto o una coma decimal, sin separadores de miles) y una k o una m al final. False si no tiene esa forma.
    private static bool TryScaled(string raw, out decimal value)
    {
        value = 0;
        if (raw.Length < 2 || char.ToLowerInvariant(raw[^1]) is not ('k' or 'm'))
        {
            return false;
        }

        string number = raw[..^1].Trim().Replace(',', '.');
        if (number.Length == 0 || number.Count(c => c == '.') > 1 || number.Any(c => !char.IsAsciiDigit(c) && c != '.') || number.All(c => c == '.'))
        {
            return false;
        }

        if (!decimal.TryParse(number, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed))
        {
            return false;
        }

        value = parsed * (char.ToLowerInvariant(raw[^1]) == 'k' ? 1_000m : 1_000_000m);
        return true;
    }

    private static AmountParse Fail(string raw) =>
        new(false, 0, IsAll: false, $"No entendí la cantidad{(raw.Length > 0 ? $" **{raw}**" : string.Empty)}: escribí un número (por ejemplo 500, 10k o 1m) o **all** para todo tu oro.");
}
