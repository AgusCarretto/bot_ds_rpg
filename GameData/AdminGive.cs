using System.Text.RegularExpressions;

namespace BotDsRpg.GameData;

public enum AdminGiveKind { Gold, Dust, Xp, Item }

// Lo que pidió el administrador: qué dar, cuánto (para un ítem, la cantidad), cuál ítem y a quién (null = a sí mismo).
public sealed record AdminGiveRequest(AdminGiveKind Kind, int Amount, string? ItemName, ulong? TargetId);

public sealed record AdminGiveParse(AdminGiveRequest? Request, string? Error);

// «aa admingive» (v0.34.0): lee lo que escribe el administrador. Pura (sin Discord ni base): el comando le pasa el texto después de «admingive» y recibe un pedido o el motivo por el que no se entendió.
//   aa admingive gold 1m [@alguien]            oro (acepta 1.500, 10k, 1m, 1,5m)
//   aa admingive dust 500 [@alguien]           Polvo
//   aa admingive xp 5000 [@alguien]            EXP (puede subir de nivel)
//   aa admingive item Cofre de Oro 3 [@alguien]  un ítem del catálogo (caja, material, comida, huevo, arma, amuleto, herramienta...); la cantidad va al final y es opcional
// Sin mención se lo da a sí mismo. Las palabras tienen sinónimos en español (oro, polvo, exp, objeto).
public static class AdminGive
{
    // Los topes por comando. Las columnas son INTEGER, así que el oro y el Polvo llegan hasta int.MaxValue (el repositorio además NO deja pasar el total de ese límite); la EXP y los ítems tienen un tope más bajo para que un dedazo no deje la cuenta rota.
    public const int MaxXp = 100_000_000;
    public const int MaxItemQuantity = 100_000;

    public const string Usage =
        "Uso: `aa admingive gold 1m [@alguien]` · `aa admingive dust 500 [@alguien]` · `aa admingive xp 5000 [@alguien]` · `aa admingive item Cofre de Oro 3 [@alguien]`. " +
        "Sin mención te lo das a vos. Las cantidades aceptan 1.500, 10k y 1m.";

    private static readonly Regex Mention = new(@"<@!?(\d+)>", RegexOptions.Compiled);

    public static AdminGiveParse Parse(string? args)
    {
        string text = args ?? string.Empty;

        var mentioned = Mention.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (mentioned.Count > 1)
        {
            return Fail("Mencioná a una sola persona por comando.");
        }

        ulong? target = null;
        if (mentioned.Count == 1)
        {
            if (!ulong.TryParse(mentioned[0], out ulong id) || id == 0)
            {
                return Fail("No entendí a quién mencionaste.");
            }

            target = id;
        }

        // Sin la mención y sin comillas (una caja con nombre de varias palabras se puede escribir entre comillas o suelta).
        string cleaned = Mention.Replace(text, " ").Replace('"', ' ').Replace('“', ' ').Replace('”', ' ');
        var tokens = cleaned.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Fail(Usage);
        }

        AdminGiveKind? kind = tokens[0].ToLowerInvariant().Replace('í', 'i') switch
        {
            "gold" or "oro" or "plata" => AdminGiveKind.Gold,
            "dust" or "polvo" => AdminGiveKind.Dust,
            "xp" or "exp" => AdminGiveKind.Xp,
            "item" or "items" or "objeto" or "obj" or "caja" => AdminGiveKind.Item,
            _ => null,
        };
        if (kind is null)
        {
            return Fail($"No sé qué es «{tokens[0]}». {Usage}");
        }

        if (kind != AdminGiveKind.Item)
        {
            if (tokens.Length != 2)
            {
                return Fail($"Falta la cantidad (o sobra algo). {Usage}");
            }

            int max = kind == AdminGiveKind.Xp ? MaxXp : int.MaxValue;
            var (amount, error) = ReadAmount(tokens[1], max);
            return error is not null ? Fail(error) : new AdminGiveParse(new AdminGiveRequest(kind.Value, amount, null, target), null);
        }

        if (tokens.Length < 2)
        {
            return Fail($"Falta el nombre del ítem. {Usage}");
        }

        // «Cofre de Oro 3»: si la última palabra es una cantidad (y queda al menos una para el nombre), es la cantidad; si no, es una sola unidad.
        int quantity = 1;
        string[] nameTokens = tokens[1..];
        if (nameTokens.Length >= 2 && !AmountParser.IsAllWord(nameTokens[^1]) && AmountParser.Parse(nameTokens[^1], 0).Ok)
        {
            var (parsedQuantity, error) = ReadAmount(nameTokens[^1], MaxItemQuantity);
            if (error is not null)
            {
                return Fail(error);
            }

            quantity = parsedQuantity;
            nameTokens = nameTokens[..^1];
        }

        return new AdminGiveParse(new AdminGiveRequest(AdminGiveKind.Item, quantity, string.Join(' ', nameTokens), target), null);
    }

    private static (int Amount, string? Error) ReadAmount(string token, int max)
    {
        if (AmountParser.IsAllWord(token))
        {
            return (0, "«todo» no vale acá: poné un número.");
        }

        var parsed = AmountParser.Parse(token, 0);
        if (!parsed.Ok)
        {
            return (0, parsed.Error ?? "No entendí la cantidad.");
        }

        return parsed.Amount > max
            ? (0, $"Es demasiado: el máximo por comando es **{GameHistory.Number(max)}**.")
            : (parsed.Amount, null);
    }

    private static AdminGiveParse Fail(string error) => new(null, error);
}
