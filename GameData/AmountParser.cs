namespace BotDsRpg.GameData;

// El resultado de leer una cantidad de oro que escribe el jugador: o un monto concreto (IsAll = lo pidió con "all"), o el motivo por el que no se entendió.
public sealed record AmountParse(bool Ok, int Amount, bool IsAll, string? Error);

// Lee la cantidad de oro de /play y /give: un número ("500", "1.000", "1,000") o "all" (también "todo", "toda", "max") para TODO el oro que tiene.
// Pura. "available" es el oro del jugador al momento de pedirlo; lo que se resuelve acá es solo el texto (que alcance o no lo valida cada comando,
// con la guarda atómica de la base).
public static class AmountParser
{
    private static readonly string[] AllWords = ["all", "todo", "toda", "todos", "todas", "max", "maximo", "máximo"];

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

    private static AmountParse Fail(string raw) =>
        new(false, 0, IsAll: false, $"No entendí la cantidad{(raw.Length > 0 ? $" **{raw}**" : string.Empty)}: escribí un número (por ejemplo 500) o **all** para todo tu oro.");
}
