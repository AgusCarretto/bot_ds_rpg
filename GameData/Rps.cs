namespace BotDsRpg.GameData;

// «Elementos» (v0.54.0): piedra, papel o tijera con fuego, hielo y agua, apostando oro entre dos jugadores, dentro de /play. El dueño
// (2026-11-12): una sola mano, sin tope de apuesta, y un logro por partidas ganadas (sin títulos); el 2026-11-13: que vaya en /play
// («ahí están los juegos de apuestas») y con elementos: el fuego derrite el hielo, el hielo congela el agua, el agua apaga el fuego.
// Lo pidió un jugador: «que pongan la misma cantidad de oro y el que gana se lleva todo». Puro: sin Discord ni base.
// (Los nombres internos siguen siendo Rps*: el juego nació como piedra, papel o tijera.)
public enum RpsChoice { Fire, Ice, Water }

public static class RpsRules
{
    // El valor de la opción «game» de /play y la palabra de «aa play elementos»: nunca se traduce.
    public const string GameKey = "elementos";

    // La apuesta mínima, como la del casino: menos que eso no vale la pena moverlo.
    public const int MinBet = 10;

    // Cuánto dura la propuesta sin que la acepten, y cuánto tienen para elegir una vez aceptada (el que no elige, pierde).
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PickTimeout = TimeSpan.FromSeconds(60);

    // Si empatan se vuelve a jugar; después de tantos empates seguidos la partida se cancela sin mover oro.
    public const int MaxTies = 3;

    // 1 si gana a, -1 si gana b, 0 si empatan. Fuego le gana a hielo, hielo a agua, agua a fuego.
    public static int Compare(RpsChoice a, RpsChoice b) =>
        a == b ? 0 : (a, b) is (RpsChoice.Fire, RpsChoice.Ice) or (RpsChoice.Ice, RpsChoice.Water) or (RpsChoice.Water, RpsChoice.Fire) ? 1 : -1;

    // La letra que va en el id del botón: nunca se traduce.
    public static string Code(RpsChoice choice) => choice switch
    {
        RpsChoice.Fire => "f",
        RpsChoice.Ice => "i",
        _ => "w",
    };

    public static RpsChoice? FromCode(string? code) => code switch
    {
        "f" => RpsChoice.Fire,
        "i" => RpsChoice.Ice,
        "w" => RpsChoice.Water,
        _ => null,
    };

    public static string Emoji(RpsChoice choice) => choice switch
    {
        RpsChoice.Fire => "🔥",
        RpsChoice.Ice => "❄️",
        _ => "💧",
    };

    public static string Name(RpsChoice choice) => choice switch
    {
        RpsChoice.Fire => Loc.T("Fuego"),
        RpsChoice.Ice => Loc.T("Hielo"),
        _ => Loc.T("Agua"),
    };

    public static string Label(RpsChoice choice) => $"{Emoji(choice)} {Name(choice)}";

    // Por qué ganó el que ganó, una frase: «🔥 Fuego derrite ❄️ Hielo».
    public static string WhyWins(RpsChoice winner) => winner switch
    {
        RpsChoice.Fire => Loc.T("🔥 el fuego derrite el hielo"),
        RpsChoice.Ice => Loc.T("❄️ el hielo congela el agua"),
        _ => Loc.T("💧 el agua apaga el fuego"),
    };
}
