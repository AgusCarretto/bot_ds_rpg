namespace BotDsRpg.GameData;

// Formato compartido "{Emoji} {Nombre}" para mostrar un ítem en cualquier Embed o mensaje.
// Sin emoji cargado todavía (items.emoji nulo) cae a mostrar solo el nombre, sin ícono de
// relleno — evita que medio catálogo muestre un emoji genérico mientras se van subiendo
// los pixel art de a tandas.
public static class ItemDisplay
{
    public static string Format(string? emoji, string name) =>
        string.IsNullOrWhiteSpace(emoji) ? name : $"{emoji} {name}";
}
