using BotDsRpg.GameData;
using Discord;

// El emoji propio de un ítem en las opciones de un desplegable (v0.22.2, el dueño: «agregar los emojis a los combos desplegables, el forje y todo»). Discord no dibuja un emoji personalizado dentro del
// TEXTO de una opción, pero cada opción tiene un campo de emoji aparte y ahí sí lo muestra; en las listas de autocompletado de los comandos (/trade, /exchange, /forge make...) no hay dónde ponerlo.
public static class ItemMenuExtensions
{
    // El emoji de la opción para este ítem, o null. Solo si la aplicación lo tiene de verdad (ver ItemEmojiRegistry.MenuCodeOf): uno desconocido haría que Discord rechace el mensaje ENTERO.
    public static IEmote? ItemEmote(string itemName) =>
        ItemEmojiRegistry.MenuCodeOf(itemName) is { } code && Emote.TryParse(code, out var emote) ? emote : null;

    // Agrega una opción cuyo emoji es el del ítem itemName (el valor de la opción casi siempre es ese mismo nombre, pero no siempre: el Inframundo usa «corteza» / «meteorito»).
    public static SelectMenuBuilder AddItemOption(this SelectMenuBuilder menu, string itemName, string label, string value, string? description = null) =>
        menu.AddOption(label, value, description, ItemEmote(itemName));
}
