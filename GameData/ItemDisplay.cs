using System.Text.RegularExpressions;

namespace BotDsRpg.GameData;

// Formato compartido "{Emoji} {Nombre}" para mostrar un ítem en cualquier Embed o mensaje.
// Sin emoji cargado todavía (items.emoji nulo) cae a mostrar solo el nombre, sin ícono de
// relleno — evita que medio catálogo muestre un emoji genérico mientras se van subiendo
// los pixel art de a tandas.
public static class ItemDisplay
{
    public static string Format(string? emoji, string name) =>
        string.IsNullOrWhiteSpace(emoji) ? name : $"{emoji} {name}";

    private static readonly Regex EmojiCode = new(@"^<(?<animated>a?):[A-Za-z0-9_]{2,32}:(?<id>\d{17,20})>$", RegexOptions.Compiled);

    // La imagen del emoji en el CDN de Discord ("<:espada_madera:1555633462432636958>" -> https://cdn.discordapp.com/emojis/1555633462432636958.png?size=128).
    // Dentro de un texto un emoji siempre mide 22 px y Discord no deja agrandarlo (solo un mensaje hecho NADA MÁS que de emojis sale grande), así que
    // donde un ítem es el protagonista del mensaje (lo que forjaste, lo que juntaste, lo que dropeó...) se pone esta imagen de miniatura del embed (~80 px).
    // Sirve igual para los emojis de la aplicación y los del servidor. Null si el ítem no tiene emoji cargado o el código no tiene el formato esperado.
    public static string? ImageUrl(string? emoji, int size = 128)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return null;
        }

        var match = EmojiCode.Match(emoji.Trim());
        return match.Success
            ? $"https://cdn.discordapp.com/emojis/{match.Groups["id"].Value}.{(match.Groups["animated"].Length > 0 ? "gif" : "png")}?size={size}"
            : null;
    }
}
