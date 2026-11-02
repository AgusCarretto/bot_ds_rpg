namespace BotDsRpg.GameData.Dungeons;

// Zona 8 — Las brasas. Un tablero de 4×4: tocar una brasa la prende o la apaga, a ella y a sus cuatro vecinas (arriba, abajo, izquierda, derecha). Hay que dejarlas TODAS prendidas.
// Se arma desde «todo prendido» tocando unas brasas al azar, así que siempre tiene solución (tocar es su propio deshacer). Cada 3 toques sopla el viento sobre una brasa al azar
// (como si la hubiera tocado) y te quema: el viento también es un «toque», así que el tablero sigue teniendo solución, y el mensaje dice dónde sopló para poder deshacerlo.
public sealed class Embers : IDungeonPuzzle
{
    public const int Size = 4;
    public const int ScramblePresses = 5;
    public const int WindEvery = 3;

    private readonly bool[] _lit = Enumerable.Repeat(true, Size * Size).ToArray();
    private readonly Random _rng;

    public Embers(Random rng)
    {
        _rng = rng;
        // Tocar la misma brasa dos veces se anula: se eligen brasas distintas para que el desorden sea real.
        foreach (int cell in Enumerable.Range(0, Size * Size).OrderBy(_ => rng.Next()).Take(ScramblePresses))
        {
            Press(cell);
        }

        // En 4×4 algunas combinaciones de toques no cambian nada (se anulan entre sí): si quedó todo prendido, se sigue tocando al azar hasta que haya algo que resolver.
        while (_lit.All(l => l))
        {
            Press(rng.Next(Size * Size));
        }
    }

    public DungeonKind Kind => DungeonKind.Embers;

    public bool Solved { get; private set; }

    public bool Failed => false;

    public string? Note { get; private set; }

    public int Presses { get; private set; }

    public bool IsLit(int cell) => _lit[cell];

    public static string CellName(int cell) => $"{(char)('A' + (cell % Size))}{(cell / Size) + 1}";

    public DungeonStep Apply(string action)
    {
        if (Solved || action.Length < 2 || action[0] != 'e' || !int.TryParse(action[1..], out int cell) || cell is < 0 or >= Size * Size)
        {
            return DungeonStep.Ignored;
        }

        Press(cell);
        Presses++;

        if (_lit.All(l => l))
        {
            Solved = true;
            Note = Loc.T("🔥 ¡Todas las brasas prendidas! El calor abre la puerta.");
            return DungeonStep.Solved;
        }

        if (Presses % WindEvery == 0)
        {
            int gust = _rng.Next(Size * Size);
            Press(gust);
            if (_lit.All(l => l))
            {
                Solved = true;
                Note = Loc.F($"🌬️ El viento sopló sobre {CellName(gust)}… ¡y las dejó todas prendidas! La puerta se abre.");
                return DungeonStep.Solved;
            }

            Note = Loc.F($"🌬️ El viento sopló sobre la brasa **{CellName(gust)}** (como si la tocaras) y te quemó.");
            return DungeonStep.Mistake;
        }

        Note = null;
        return DungeonStep.Ok;
    }

    public string Render()
    {
        int lit = _lit.Count(l => l);
        int untilWind = WindEvery - (Presses % WindEvery);
        return Loc.T("Prendé las 16 brasas. Tocar una prende o apaga esa y sus vecinas (arriba, abajo, a los costados).") + "\n\n" +
            Loc.F($"🔥 Prendidas: **{lit}** de {Size * Size} · 🌬️ El viento sopla en **{untilWind}** toque(s)");
    }

    public IReadOnlyList<DungeonButton> Buttons() =>
        Solved ? [] : Enumerable.Range(0, Size * Size).Select(c => new DungeonButton($"e{c}", CellName(c), _lit[c] ? "🔥" : "⚫", c / Size, Primary: _lit[c])).ToList();

    private void Press(int cell)
    {
        int row = cell / Size, col = cell % Size;
        Toggle(row, col);
        Toggle(row - 1, col);
        Toggle(row + 1, col);
        Toggle(row, col - 1);
        Toggle(row, col + 1);
    }

    private void Toggle(int row, int col)
    {
        if (row is >= 0 and < Size && col is >= 0 and < Size)
        {
            _lit[(row * Size) + col] = !_lit[(row * Size) + col];
        }
    }
}
