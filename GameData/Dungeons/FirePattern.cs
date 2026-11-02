namespace BotDsRpg.GameData.Dungeons;

// Zona 6 — El patrón del fuego. Las llamas de la sala anuncian una serie de golpes (🔥 fuego, 💧 agua, 🪨 piedra); se memoriza, se toca «Listo» (la serie se tapa) y se contesta cada golpe
// con su contraataque, en orden: 💧 apaga 🔥, 🪨 frena 💧, 🔥 derrite 🪨. Cuatro pisos: la serie del piso siguiente es la misma con un golpe más (3, 4, 5 y 6), así que lo aprendido
// sirve. Un error saca vida y vuelve a mostrar la serie de ese piso desde el principio. La serie se sortea en cada corrida.
public sealed class FirePattern : IDungeonPuzzle
{
    public const int Floors = 4;
    public const int FirstLength = 3;

    // Las acciones son los tres elementos (lo que se TOCA es el contraataque).
    public const string Fire = "F", Water = "W", Stone = "S", Ready = "ready";

    private readonly string[] _sequence;
    private int _floor;
    private int _position;
    private bool _watching = true;

    public FirePattern(Random rng)
    {
        string[] all = [Fire, Water, Stone];
        _sequence = Enumerable.Range(0, FirstLength + Floors - 1).Select(_ => all[rng.Next(all.Length)]).ToArray();
    }

    public DungeonKind Kind => DungeonKind.FirePattern;

    public bool Solved { get; private set; }

    public bool Failed => false;

    public string? Note { get; private set; }

    public int Floor => _floor;

    public bool Watching => _watching;

    // La serie del piso actual (lo que se muestra al mirar).
    public IReadOnlyList<string> CurrentSeries => _sequence.Take(FirstLength + _floor).ToArray();

    // El contraataque de cada golpe.
    public static string Counter(string attack) => attack switch
    {
        Fire => Water,
        Water => Stone,
        _ => Fire,
    };

    public static string Emoji(string element) => element switch
    {
        Fire => "🔥",
        Water => "💧",
        _ => "🪨",
    };

    public DungeonStep Apply(string action)
    {
        if (Solved)
        {
            return DungeonStep.Ignored;
        }

        if (_watching)
        {
            if (action != Ready)
            {
                return DungeonStep.Ignored;
            }

            _watching = false;
            _position = 0;
            Note = null;
            return DungeonStep.Ok;
        }

        if (action is not (Fire or Water or Stone))
        {
            return DungeonStep.Ignored;
        }

        var series = CurrentSeries;
        string expected = Counter(series[_position]);
        if (action != expected)
        {
            Note = Loc.F($"❌ El golpe {_position + 1} era {Emoji(series[_position])} y se contesta con {Emoji(expected)}: el fuego te alcanzó. Mirá la serie otra vez.");
            _watching = true;
            _position = 0;
            return DungeonStep.Mistake;
        }

        _position++;
        if (_position < series.Count)
        {
            Note = Loc.F($"✅ {Emoji(expected)} ¡bien! Van {_position} de {series.Count}.");
            return DungeonStep.Ok;
        }

        _floor++;
        _position = 0;
        _watching = true;
        if (_floor >= Floors)
        {
            Solved = true;
            Note = Loc.T("🔥 Contestaste la última serie: las llamas se apagan y la puerta se abre.");
            return DungeonStep.Solved;
        }

        Note = Loc.F($"✅ ¡Piso superado! El piso {_floor + 1} repite la serie con un golpe más.");
        return DungeonStep.Ok;
    }

    public string Render()
    {
        string legend = Loc.T("💧 apaga 🔥 · 🪨 frena 💧 · 🔥 derrite 🪨");
        string header = Loc.F($"**Piso {_floor + 1} de {Floors}**");
        if (Solved)
        {
            return $"{header}\n\n{legend}";
        }

        if (_watching)
        {
            string series = string.Join("  ", CurrentSeries.Select(Emoji));
            return header + "\n\n" + Loc.T("Las llamas de la sala anuncian sus golpes. Memorizalos:") + $"\n## {series}\n\n" + legend + "\n" + Loc.T("Cuando estés, tocá **Listo**: la serie se tapa y hay que contestarla de memoria.");
        }

        string progress = string.Join(" ", Enumerable.Range(0, CurrentSeries.Count).Select(i => i < _position ? "✅" : "▫️"));
        return header + "\n\n" + Loc.T("Contestá cada golpe con su contraataque, en orden:") + $"\n## {progress}\n\n" + legend;
    }

    public IReadOnlyList<DungeonButton> Buttons() =>
        Solved ? []
        : _watching ? [new DungeonButton(Ready, Loc.T("Listo, lo memoricé"), "👁️", 0, Primary: true)]
        : [new DungeonButton(Water, Loc.T("Agua"), "💧", 0), new DungeonButton(Stone, Loc.T("Piedra"), "🪨", 0), new DungeonButton(Fire, Loc.T("Fuego"), "🔥", 0)];
}
