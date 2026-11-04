using System.Globalization;

namespace BotDsRpg.GameData;

public enum AdminSetKind { DragonTier, DragonLevel }

// Lo que pidió el administrador: qué fijar, a qué valor y a quién (null = a sí mismo).
public sealed record AdminSetRequest(AdminSetKind Kind, int Value, ulong? TargetId);

public sealed record AdminSetParse(AdminSetRequest? Request, string? Error);

// «aa adminset» (v0.48.1): lee lo que escribe el administrador para FIJAR un valor de un jugador (hoy, el dragón). Pura (sin Discord ni base), como AdminGive.
//   aa adminset dragon tier 5 [@alguien]    el TIER del dragón (1 a 10; el dueño pidió poder ponerle un 4 o un 5 a un amigo)
//   aa adminset dragon level 40 [@alguien]  el NIVEL de entrenamiento (0 a 100, y no más que el tope de su tier: tier × 10)
// Sin mención se lo fija a sí mismo. Sinónimos: «dragón», «settier», «setlevel», «nivel». El valor es un número entero sin más (nada de 5k ni 1,5).
public static class AdminSet
{
    public const string Usage =
        "Uso: `aa adminset dragon tier 5 [@alguien]` (tier de 1 a 10) · `aa adminset dragon level 40 [@alguien]` (nivel de entrenamiento de 0 a 100, hasta el tope de su tier). " +
        "Sin mención te lo fijás a vos. Cambiar el tier recorta el nivel al tope del tier nuevo y pone en cero la racha de fallos de la cría.";

    public static AdminSetParse Parse(string? args)
    {
        var (target, cleaned, targetError) = AdminGive.SplitTarget(args ?? string.Empty);
        if (targetError is not null)
        {
            return Fail(targetError);
        }

        var tokens = cleaned.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Fail(Usage);
        }

        string what = Normalize(tokens[0]);
        if (what is not ("dragon" or "dragones"))
        {
            return Fail($"No sé qué es «{tokens[0]}». {Usage}");
        }

        if (tokens.Length < 2)
        {
            return Fail($"Falta qué fijar (tier o level). {Usage}");
        }

        AdminSetKind? kind = Normalize(tokens[1]) switch
        {
            "tier" or "settier" => AdminSetKind.DragonTier,
            "level" or "setlevel" or "nivel" or "lvl" => AdminSetKind.DragonLevel,
            _ => null,
        };
        if (kind is null)
        {
            return Fail($"No sé qué es «{tokens[1]}»: se puede fijar el tier o el level. {Usage}");
        }

        if (tokens.Length != 3)
        {
            return Fail($"Falta el número (o sobra algo). {Usage}");
        }

        // Solo dígitos: «5k», «5.5», «-1» o «x» no son un tier ni un nivel y se rechazan en vez de adivinar.
        if (!tokens[2].All(char.IsAsciiDigit) || !int.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return Fail($"«{tokens[2]}» no es un número entero. {Usage}");
        }

        if (kind == AdminSetKind.DragonTier && value is < 1 or > DragonRules.MaxTier)
        {
            return Fail($"El tier va de **1 a {DragonRules.MaxTier}**.");
        }

        if (kind == AdminSetKind.DragonLevel && value > DragonRules.MaxLevel)
        {
            return Fail($"El nivel de entrenamiento va de **0 a {DragonRules.MaxLevel}** (y no más que el tope de su tier: tier × 10).");
        }

        return new AdminSetParse(new AdminSetRequest(kind.Value, value, target), null);
    }

    // Minúsculas y sin tilde, para que «Dragón» y «DRAGON» valgan igual.
    private static string Normalize(string word) => word.ToLowerInvariant().Replace('ó', 'o');

    private static AdminSetParse Fail(string error) => new(null, error);
}
