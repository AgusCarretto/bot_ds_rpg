namespace BotDsRpg.GameData;

// El arte propio de la Navidad (docs/arte-navidad.md; el dueño los subió al portal el 2026-11-17). Son emojis de la APLICACIÓN: se dibujan en la descripción y en el valor de
// los campos de un embed, NO en títulos, nombres de campo, pies, botones ni opciones de menú (ahí sigue el unicode de siempre).
//
// Cómo se usan: los textos siguen escritos con el emoji unicode (🎁, 🔔…) y así están traducidos en Locales/en.json; Paint los cambia por el propio al armar la pantalla,
// solo en las partes donde se dibuja (ChristmasEmbedExtensions.BuildChristmas). Así no se tocan las claves de traducción. Paint solo cambia los emojis del evento.
public static class ChristmasEmojis
{
    public const string Gift = "<:regalo:1572268392424153138>";
    public const string Bell = "<:campana_navidad:1572268390691643562>";
    public const string Candle = "<:vela_navidad:1572268388972105909>";
    public const string Sock = "<:calcetin:1572268387357163621>";
    public const string CandyCane = "<:baston_caramelo:1572274254941266065>";
    public const string Snowball = "<:bola_nieve:1572274253213073577>";
    public const string Firecracker = "<:petardo:1572274251648471174>";
    public const string Sled = "<:trineo:1572274250012823676>";
    public const string Krampus = "<:krampus:1572275877201575987>";
    public const string Snowman = "<:muneco_gigante:1572275875565535232>";
    public const string Event = "<:navidad_fogon:1572275873791344790>";

    // El unicode de cada uno. Los que llevan el selector de variante (U+FE0F) van primero con él y después sin él, para que no quede un FE0F suelto.
    private static readonly (string Plain, string Custom)[] Swaps =
    [
        ("🎁", Gift),
        ("🔔", Bell),
        ("🕯️", Candle), ("🕯", Candle),
        ("🧦", Sock),
        ("🍬", CandyCane),
        ("❄️", Snowball), ("❄", Snowball),
        ("🧨", Firecracker),
        ("🛷", Sled),
        ("👹", Krampus),
        ("☃️", Snowman), ("☃", Snowman),
    ];

    // El texto con los emojis del evento cambiados por los propios. Null o vacío vuelve igual.
    public static string? Paint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (plain, custom) in Swaps)
        {
            text = text.Replace(plain, custom, StringComparison.Ordinal);
        }

        return text;
    }
}
