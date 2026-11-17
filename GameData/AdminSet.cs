using System.Globalization;

namespace BotDsRpg.GameData;

public enum AdminSetKind { DragonTier, DragonLevel, Cooldown }

// Lo que pidió el administrador: qué fijar, a qué valor y a quién (null = a sí mismo). Para Cooldown: los command_name a resetear y cómo se nombran en la respuesta («Jefe», «todos»).
public sealed record AdminSetRequest(AdminSetKind Kind, int Value, ulong? TargetId, IReadOnlyList<string>? Cooldowns = null, string? CooldownLabel = null);

public sealed record AdminSetParse(AdminSetRequest? Request, string? Error);

// «aa adminset» (v0.48.1): lee lo que escribe el administrador para FIJAR un valor de un jugador (hoy, el dragón). Pura (sin Discord ni base), como AdminGive.
//   aa adminset dragon tier 5 [@alguien]    el TIER del dragón (1 a 10; el dueño pidió poder ponerle un 4 o un 5 a un amigo)
//   aa adminset dragon level 40 [@alguien]  el NIVEL de entrenamiento (0 a 100, y no más que el tope de su tier: tier × 10)
//   aa adminset cd jefe [@alguien]          (v0.58.1, también «aa adminsetcd») resetea ESE cooldown; «todo» resetea todos los de ResettableCooldowns
// Sin mención se lo fija a sí mismo. Sinónimos: «dragón», «settier», «setlevel», «nivel». El valor es un número entero sin más (nada de 5k ni 1,5).
public static class AdminSet
{
    public const string Usage =
        "Uso: `aa adminset dragon tier 5 [@alguien]` (tier de 1 a 10) · `aa adminset dragon level 40 [@alguien]` (nivel de entrenamiento de 0 a 100, hasta el tope de su tier). " +
        "`aa adminset cd <comando|todo> [@alguien]` (o `aa adminsetcd`) resetea un cooldown: " + CooldownWords + ". " +
        "Sin mención te lo fijás a vos. Cambiar el tier recorta el nivel al tope del tier nuevo y pone en cero la racha de fallos de la cría.";

    private const string CooldownWords = "cazar, viajar, aventura, talar, minar, pescar, jefe, caja, tala/minería/pesca avanzada, cría del dragón (dragon) o todo";

    // v0.62.1 (el dueño, probando el tablón: «¿cómo reseteaba el cd de dragon breed?»): el descanso de 12 h de la cría NO está en la tabla cooldowns (es player_dragons.last_bred_at), así que va con
    // una clave propia que el repositorio entiende aparte. También entra en «todo».
    public const string DragonBreedKey = "dragon";
    public const string DragonBreedLabel = "Cría del dragón";
    private static readonly string[] DragonBreedWords = ["dragon", "cria", "criar", "breed", "dragon breed", "cria del dragon", "cria dragon"];

    // Los cooldowns que se pueden resetear: los de la tabla cooldowns que se juegan. El voto NO: su fila guarda qué voto de top.gg ya se cobró, y borrarla dejaría
    // cobrar el mismo voto otra vez. El diario, la cría del dragón y las mascotas no viven en esa tabla.
    public static IReadOnlyList<CooldownDefinition> ResettableCooldowns { get; } =
        [.. CooldownCatalog.All, CooldownCatalog.ChopAdvanced, CooldownCatalog.MineAdvanced, CooldownCatalog.FishAdvanced];

    // Palabras que acepta cada uno, además de su nombre interno (hunt, travel…) y el de pantalla (Cazar, Jefe…). Sin tildes y en minúsculas.
    private static readonly Dictionary<string, string[]> CooldownAliases = new()
    {
        ["hunt"] = ["caza", "cazar", "caceria"],
        ["travel"] = ["viaje", "viajar"],
        ["aventura"] = ["adventure", "av"],
        ["chop"] = ["tala", "talar", "ch"],
        ["mine"] = ["mina", "minar", "m"],
        ["fish"] = ["pesca", "pescar", "f"],
        ["boss"] = ["jefe", "raid"],
        ["buybox"] = ["caja", "box", "comprar caja"],
        ["chop_adv"] = ["tala avanzada", "chop adv", "chop a"],
        ["mine_adv"] = ["mineria avanzada", "mine adv", "mine a"],
        ["fish_adv"] = ["pesca avanzada", "fish adv", "fish a"],
    };

    private static readonly string[] AllWords = ["todo", "todos", "all"];

    // El cooldown que nombra lo escrito (null si ninguno).
    public static CooldownDefinition? FindCooldown(string text)
    {
        string wanted = Normalize(text.Trim());
        return ResettableCooldowns.FirstOrDefault(c => Normalize(c.CommandName) == wanted || Normalize(c.DisplayName) == wanted
            || (CooldownAliases.TryGetValue(c.CommandName, out var aliases) && aliases.Contains(wanted)));
    }

    private static AdminSetParse ParseCooldown(string[] tokens, ulong? target)
    {
        if (tokens.Length < 2)
        {
            return Fail($"Falta qué cooldown resetear: {CooldownWords}.");
        }

        string text = string.Join(" ", tokens.Skip(1));
        if (AllWords.Contains(Normalize(text)))
        {
            return new AdminSetParse(new AdminSetRequest(AdminSetKind.Cooldown, 0, target, [.. ResettableCooldowns.Select(c => c.CommandName), DragonBreedKey], "todos"), null);
        }

        if (DragonBreedWords.Contains(Normalize(text)))
        {
            return new AdminSetParse(new AdminSetRequest(AdminSetKind.Cooldown, 0, target, [DragonBreedKey], DragonBreedLabel), null);
        }

        return FindCooldown(text) is { } cooldown
            ? new AdminSetParse(new AdminSetRequest(AdminSetKind.Cooldown, 0, target, [cooldown.CommandName], cooldown.DisplayName), null)
            : Fail($"No conozco el cooldown «{text}». Se puede: {CooldownWords}.");
    }

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
        if (what is "cd" or "cds" or "cooldown" or "cooldowns")
        {
            return ParseCooldown(tokens, target);
        }

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
    private static string Normalize(string word) =>
        new(word.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    private static AdminSetParse Fail(string error) => new(null, error);
}
