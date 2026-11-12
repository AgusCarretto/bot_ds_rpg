namespace BotDsRpg.GameData;

// Piedra, papel o tijera apostando oro entre dos jugadores (v0.54.0). El dueño (2026-11-12): una sola mano, sin tope de
// apuesta, y un logro por partidas ganadas (sin títulos). Lo pidió un jugador: «que pongan la misma cantidad de oro y el que
// gana se lleva todo». Puro: sin Discord ni base.
public enum RpsChoice { Rock, Paper, Scissors }

public static class RpsRules
{
    // La apuesta mínima, como la del casino: menos que eso no vale la pena moverlo.
    public const int MinBet = 10;

    // Cuánto dura la propuesta sin que la acepten, y cuánto tienen para elegir una vez aceptada (el que no elige, pierde).
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PickTimeout = TimeSpan.FromSeconds(60);

    // Si empatan se vuelve a jugar; después de tantos empates seguidos la partida se cancela sin mover oro.
    public const int MaxTies = 3;

    // 1 si gana a, -1 si gana b, 0 si empatan. Piedra rompe tijera, tijera corta papel, papel envuelve piedra.
    public static int Compare(RpsChoice a, RpsChoice b) =>
        a == b ? 0 : (a, b) is (RpsChoice.Rock, RpsChoice.Scissors) or (RpsChoice.Scissors, RpsChoice.Paper) or (RpsChoice.Paper, RpsChoice.Rock) ? 1 : -1;

    // La letra que va en el id del botón: nunca se traduce.
    public static string Code(RpsChoice choice) => choice switch
    {
        RpsChoice.Rock => "r",
        RpsChoice.Paper => "p",
        _ => "s",
    };

    public static RpsChoice? FromCode(string? code) => code switch
    {
        "r" => RpsChoice.Rock,
        "p" => RpsChoice.Paper,
        "s" => RpsChoice.Scissors,
        _ => null,
    };

    public static string Emoji(RpsChoice choice) => choice switch
    {
        RpsChoice.Rock => "🪨",
        RpsChoice.Paper => "📄",
        _ => "✂️",
    };

    public static string Name(RpsChoice choice) => choice switch
    {
        // «Piedra» ya está en el catálogo como el mineral («Stone»); acá es la del juego («Rock»), así que se elige por idioma.
        RpsChoice.Rock => Loc.Current == Loc.English ? "Rock" : "Piedra",
        RpsChoice.Paper => Loc.T("Papel"),
        _ => Loc.T("Tijera"),
    };

    public static string Label(RpsChoice choice) => $"{Emoji(choice)} {Name(choice)}";
}
