namespace BotDsRpg.GameData.Dungeons;

// Zona 10 — El ritmo del Asador. El Asador pega siguiendo un CICLO de 5 golpes (🔥 llamarada, 🪓 hachazo, 💨 ráfaga) que se repite sin fin; se sortea en cada corrida. Antes de empezar
// calienta con los 3 primeros golpes del ciclo, a la vista. Después, en cada ronda, hay que elegir de qué cubrirse: si acertás el golpe que viene, sumás a la racha; si no, te pega
// (saca vida) y la racha vuelve a cero. Con 7 aciertos seguidos lo descolocás y la puerta se abre. Se ve la lista de los últimos golpes, para ir aprendiendo el ciclo. Si en 40 rondas no
// lo lograste, se cansa de jugar con vos y la mazmorra se pierde.
public sealed class Rhythm : IDungeonPuzzle
{
    public const int CycleLength = 5;
    public const int WarmUp = 3;
    public const int StreakToWin = 7;
    public const int MaxRounds = 40;

    public const string Flame = "f", Axe = "a", Gust = "g";

    private readonly string[] _cycle;
    private readonly List<string> _seen = [];
    private int _next;

    public Rhythm(Random rng)
    {
        string[] all = [Flame, Axe, Gust];
        do
        {
            _cycle = Enumerable.Range(0, CycleLength).Select(_ => all[rng.Next(all.Length)]).ToArray();
        }
        while (_cycle.Distinct().Count() < 3);

        // La entrada en calor: los primeros golpes, a la vista.
        for (int i = 0; i < WarmUp; i++)
        {
            _seen.Add(_cycle[i]);
        }

        _next = WarmUp;
    }

    public DungeonKind Kind => DungeonKind.Rhythm;

    public bool Solved { get; private set; }

    public bool Failed => !Solved && Rounds >= MaxRounds;

    public string? Note { get; private set; }

    public int Streak { get; private set; }

    public int Rounds { get; private set; }

    public IReadOnlyList<string> Cycle => _cycle;

    // El golpe que viene (para las pruebas).
    public string NextAttack => _cycle[_next % CycleLength];

    public static string Emoji(string attack) => attack switch
    {
        Flame => "🔥",
        Axe => "🪓",
        _ => "💨",
    };

    public DungeonStep Apply(string action)
    {
        if (Solved || Failed || action is not (Flame or Axe or Gust))
        {
            return DungeonStep.Ignored;
        }

        string attack = NextAttack;
        _next++;
        Rounds++;
        _seen.Add(attack);

        if (action == attack)
        {
            Streak++;
            if (Streak >= StreakToWin)
            {
                Solved = true;
                Note = Loc.T("🎵 Le leíste el ritmo entero: el Asador se descoloca y la puerta se abre.");
                return DungeonStep.Solved;
            }

            Note = Loc.F($"✅ Te cubriste de {Emoji(attack)}. Racha: **{Streak}** de {StreakToWin}.");
            return DungeonStep.Ok;
        }

        Streak = 0;
        Note = Failed
            ? Loc.T("😤 El Asador se cansó de jugar con vos: la mazmorra se cierra.")
            : Loc.F($"❌ Venía {Emoji(attack)} y te cubriste de {Emoji(action)}: te pegó. La racha vuelve a cero.");
        return DungeonStep.Mistake;
    }

    public string Render()
    {
        string history = string.Join(" ", _seen.TakeLast(15).Select(Emoji));
        return Loc.T("El Asador pega siguiendo un ciclo que se repite. Elegí de qué cubrirte: acertá el golpe que viene.") + "\n\n" +
            Loc.F($"Últimos golpes: {history}") + "\n" +
            Loc.F($"Racha: **{Streak}** de {StreakToWin} · Ronda **{Rounds}** de {MaxRounds}");
    }

    public IReadOnlyList<DungeonButton> Buttons() =>
        Solved || Failed ? []
        : [new DungeonButton(Flame, Loc.T("Llamarada"), "🔥", 0), new DungeonButton(Axe, Loc.T("Hachazo"), "🪓", 0), new DungeonButton(Gust, Loc.T("Ráfaga"), "💨", 0)];
}
