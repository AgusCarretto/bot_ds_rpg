namespace BotDsRpg.GameData;

// El emoji de cada ítem BUSCADO POR NOMBRE (v0.19.3). Hay pantallas que solo tienen el nombre de un ítem y no el ítem entero (el premio de una misión, el cofre del daily o del voto, el huevo que
// cayó, la comida de las mascotas): antes escribían un emoji común (📦, 🥚, 🦴) en lugar del emoji propio del ítem, y el dueño lo vio en pantallas como los cofres. Esta tabla la llena
// Services/ItemEmojiRefresher.cs desde la base (al arrancar y cada 10 minutos, así un apply_emojis.sql nuevo se nota sin reiniciar) y las pantallas piden el ícono con IconOf/Label.
// Es un estado estático a propósito, como NpcImages: los formateadores de premios son funciones puras que no reciben repositorios. Vacía (las pruebas, o hasta que carga) cae al emoji común de siempre.
public static class ItemEmojiRegistry
{
    private static volatile Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);

    // Reemplaza TODA la tabla (un ítem sin emoji no entra).
    public static void Set(IEnumerable<(string Name, string? Emoji)> items)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, emoji) in items)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(emoji))
            {
                map[name] = emoji;
            }
        }

        _byName = map;
    }

    // El emoji propio del ítem, o fallback si no se conoce (todavía no cargó la tabla, o el ítem no tiene arte).
    public static string IconOf(string name, string fallback) =>
        _byName.TryGetValue(name, out var emoji) ? emoji : fallback;

    // "<:emoji:id> Nombre", o "📦 Nombre" con el emoji común si no se conoce el propio. Sin emoji y sin fallback, el nombre solo.
    public static string Label(string name, string fallback)
    {
        string icon = IconOf(name, fallback);
        return icon.Length == 0 ? name : $"{icon} {name}";
    }
}
