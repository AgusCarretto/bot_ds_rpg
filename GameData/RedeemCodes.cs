using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BotDsRpg.GameData;

public enum RedeemRewardKind { Gold, Dust, Xp, Item }

// Un premio de un código: oro, Polvo o EXP (Amount) o un ítem de la mochila (ItemName + Amount unidades).
public sealed record RedeemReward(RedeemRewardKind Kind, int Amount, string? ItemName = null);

// Lo que el administrador pide crear: el código ya normalizado, cuántas personas lo pueden canjear (null = sin tope), cuánto dura desde que se crea (null = no vence) y sus premios.
public sealed record RedeemCodeSpec(string Code, int? MaxUses, TimeSpan? Duration, IReadOnlyList<RedeemReward> Rewards);

public sealed record RedeemCodeParse(RedeemCodeSpec? Spec, string? Error);

// Las reglas PURAS de los códigos canjeables (v0.37.0, `aa code` / `/code`, Database/add_redeem_codes.sql): cómo se escribe un código, cuántos intentos fallidos se aguantan y cómo se leen los
// argumentos de «aa admincode create». Sin Discord ni base.
public static class RedeemCodeRules
{
    public const int MinLength = 3;
    public const int MaxLength = 32;
    public const int MaxRewards = 6;
    public const int MaxUsesCap = 1_000_000;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(365);

    // Quien prueba códigos que NO existen se frena solo (para que nadie adivine códigos a fuerza bruta): 5 fallos en 10 minutos y queda 10 minutos sin poder probar.
    public const int FailedAttemptsLimit = 5;
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    public const string CreateUsage =
        "Uso: `aa admincode create CODIGO [100] [7d] | gold 5k | item Cofre de Oro 1 | dust 200 | xp 500` — después del código van, en cualquier orden y opcionales, **cuántas personas** lo pueden canjear (un número, ej. `100`) " +
        "y **cuánto dura** (`12h`, `7d`...); cada `|` agrega un premio con la misma sintaxis que `aa admingive` (oro, Polvo, EXP o un ítem de la mochila: cajas, comida, materiales, huevos...). " +
        "Hasta 6 premios. Cada persona lo canjea una sola vez. Otros: `aa admincode list` · `info CODIGO` · `off CODIGO` · `on CODIGO`.";

    // Mayúsculas, sin acentos y sin espacios alrededor; solo A-Z, 0-9, «_» y «-», entre 3 y 32 caracteres. Null si lo escrito no puede ser un código.
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (char c in typed.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        string code = builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        return code.Length is >= MinLength and <= MaxLength && Regex.IsMatch(code, "^[A-Z0-9_-]+$") ? code : null;
    }

    private static readonly Regex DurationToken = new(@"^(\d+)([mhd])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // «CODIGO [100] [7d] | gold 5k | item Cofre de Oro 1» (lo que va después de «create»). Los premios se leen con AdminGive.Parse, así que valen las mismas cantidades (1.500, 10k, 1m) y límites.
    public static RedeemCodeParse ParseCreate(string? args)
    {
        string text = args ?? string.Empty;
        var segments = text.Split('|', StringSplitOptions.TrimEntries);
        var head = segments[0].Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (head.Length == 0)
        {
            return Fail(CreateUsage);
        }

        string? code = Normalize(head[0]);
        if (code is null)
        {
            return Fail($"El código tiene que tener entre {MinLength} y {MaxLength} caracteres, solo letras, números, «_» o «-» (sin espacios). {CreateUsage}");
        }

        int? maxUses = null;
        TimeSpan? duration = null;
        foreach (string token in head.Skip(1))
        {
            var match = DurationToken.Match(token);
            if (match.Success)
            {
                long n = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var unit = match.Groups[2].Value.ToLowerInvariant() switch { "m" => TimeSpan.FromMinutes(1), "h" => TimeSpan.FromHours(1), _ => TimeSpan.FromDays(1) };
                if (n <= 0 || n > MaxDuration.TotalMinutes / unit.TotalMinutes)
                {
                    return Fail($"La duración tiene que ser mayor que cero y de un año como mucho ({token}).");
                }

                duration = unit * n;
            }
            else if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int uses))
            {
                if (uses < 1 || uses > MaxUsesCap)
                {
                    return Fail($"La cantidad de personas tiene que estar entre 1 y {GameHistory.Number(MaxUsesCap)} ({token}).");
                }

                maxUses = uses;
            }
            else
            {
                return Fail($"No entendí «{token}»: después del código van la cantidad de personas (un número) y/o la duración (`12h`, `7d`). {CreateUsage}");
            }
        }

        if (segments.Length < 2 || segments.Skip(1).Any(string.IsNullOrWhiteSpace))
        {
            return Fail($"Faltan los premios (cada uno después de un `|`). {CreateUsage}");
        }

        if (segments.Length - 1 > MaxRewards)
        {
            return Fail($"Son demasiados premios: el máximo es {MaxRewards}.");
        }

        var rewards = new List<RedeemReward>();
        foreach (string segment in segments.Skip(1))
        {
            var parsed = AdminGive.Parse(segment);
            if (parsed.Request is not { } request)
            {
                return Fail($"El premio «{segment}» no se entiende: {parsed.Error}");
            }

            if (request.TargetId is not null)
            {
                return Fail($"El premio «{segment}» no lleva mención: el código lo canjea quien lo escriba.");
            }

            rewards.Add(new RedeemReward(
                request.Kind switch { AdminGiveKind.Gold => RedeemRewardKind.Gold, AdminGiveKind.Dust => RedeemRewardKind.Dust, AdminGiveKind.Xp => RedeemRewardKind.Xp, _ => RedeemRewardKind.Item },
                request.Amount, request.ItemName));
        }

        return new RedeemCodeParse(new RedeemCodeSpec(code, maxUses, duration, rewards), null);
    }

    private static RedeemCodeParse Fail(string error) => new(null, error);
}
