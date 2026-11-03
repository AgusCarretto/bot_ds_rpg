namespace BotDsRpg.GameData.Dungeons;

// Zona 9 — El candado del Fogón. Una clave de 4 símbolos distintos de 5 (🔥 💧 🪨 🌿 ⚡), sorteada en cada corrida. Se arma un intento tocando los símbolos en orden (⌫ borra el
// último) y se prueba: el candado dice cuántos están EN SU LUGAR (✅) y cuántos están en la clave pero en otro lugar (🔸). Cada intento fallido quema; con 8 intentos fallidos el candado
// se traba y la mazmorra se pierde. Es de pura lógica: con buenos intentos se abre en 4 o 5.
public sealed class FireLock : IDungeonPuzzle
{
    public const int Length = 4;
    public const int MaxAttempts = 8;
    public const string Delete = "del", Try = "try";

    public static readonly string[] Symbols = ["🔥", "💧", "🪨", "🌿", "⚡"];

    private readonly int[] _code;
    private readonly List<int> _current = [];
    private readonly List<(int[] Guess, int Exact, int Near)> _history = [];

    public FireLock(Random rng)
    {
        _code = Enumerable.Range(0, Symbols.Length).OrderBy(_ => rng.Next()).Take(Length).ToArray();
    }

    public DungeonKind Kind => DungeonKind.Lock;

    public bool Solved { get; private set; }

    public bool Failed => !Solved && _history.Count >= MaxAttempts;

    public string? Note { get; private set; }

    public IReadOnlyList<int> Code => _code;

    public int Attempts => _history.Count;

    // (en su lugar, en otro lugar) de un intento contra la clave.
    public static (int Exact, int Near) Score(IReadOnlyList<int> code, IReadOnlyList<int> guess)
    {
        int exact = Enumerable.Range(0, Length).Count(i => code[i] == guess[i]);
        int common = guess.Distinct().Count(code.Contains);
        return (exact, common - exact);
    }

    public DungeonStep Apply(string action)
    {
        if (Solved || Failed)
        {
            return DungeonStep.Ignored;
        }

        if (action == Delete)
        {
            if (_current.Count == 0)
            {
                return DungeonStep.Ignored;
            }

            _current.RemoveAt(_current.Count - 1);
            Note = null;
            return DungeonStep.Ok;
        }

        if (action == Try)
        {
            if (_current.Count < Length)
            {
                return DungeonStep.Ignored;
            }

            var guess = _current.ToArray();
            _current.Clear();
            var (exact, near) = Score(_code, guess);
            _history.Add((guess, exact, near));
            if (exact == Length)
            {
                Solved = true;
                Note = Loc.T("🔓 ¡Clic! El candado cede y la puerta se abre.");
                return DungeonStep.Solved;
            }

            Note = Failed
                ? Loc.F($"🔒 El candado se trabó para siempre. La clave era {string.Join("", _code.Select(i => Symbols[i]))}.")
                : Loc.F($"🔥 No abre: **{exact}** en su lugar y **{near}** en otro lugar. El candado quema.");
            return DungeonStep.Mistake;
        }

        if (action.Length == 2 && action[0] == 's' && int.TryParse(action[1..], out int symbol) && symbol is >= 0 and < 5 && _current.Count < Length && !_current.Contains(symbol))
        {
            _current.Add(symbol);
            Note = null;
            return DungeonStep.Ok;
        }

        return DungeonStep.Ignored;
    }

    public string Render()
    {
        var lines = new List<string>
        {
            Loc.F($"Adiviná la clave de **{Length}** símbolos distintos. Te quedan **{MaxAttempts - _history.Count}** intentos."),
            Loc.T("✅ = en su lugar · 🔸 = está, pero en otro lugar"),
            string.Empty,
        };

        foreach (var (guess, exact, near) in _history)
        {
            lines.Add($"{string.Join("", guess.Select(i => Symbols[i]))}   {string.Concat(Enumerable.Repeat("✅", exact))}{string.Concat(Enumerable.Repeat("🔸", near))}{(exact + near == 0 ? "—" : string.Empty)}");
        }

        if (!Solved && !Failed)
        {
            string slots = string.Concat(Enumerable.Range(0, Length).Select(i => i < _current.Count ? Symbols[_current[i]] : "▫️"));
            lines.Add(string.Empty);
            lines.Add(Loc.F($"Tu intento: {slots}"));
        }

        return string.Join("\n", lines);
    }

    public IReadOnlyList<DungeonButton> Buttons()
    {
        if (Solved || Failed)
        {
            return [];
        }

        var buttons = Symbols.Select((s, i) => new DungeonButton($"s{i}", string.Empty, s, 0, Disabled: _current.Contains(i) || _current.Count >= Length)).ToList();
        buttons.Add(new DungeonButton(Delete, Loc.T("Borrar"), "⌫", 1, Disabled: _current.Count == 0));
        buttons.Add(new DungeonButton(Try, Loc.T("Probar"), "🔑", 1, Primary: true, Disabled: _current.Count < Length));
        return buttons;
    }
}
