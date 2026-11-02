namespace BotDsRpg.GameData.Dungeons;

// Zona 7 — El laberinto de humo. Un laberinto de 5×5 tallado al azar en cada corrida (siempre tiene camino: es un laberinto «perfecto», un árbol), con el humo tapando todo lo que no
// pisaste ni tenés al lado. Se camina con las flechas; las que dan contra una pared salen apagadas, así que el mapa se va descubriendo. Hay brasas escondidas FUERA del camino corto a la
// salida (pisar una quema, una vez; después queda apagada) y los pasos son contados: el camino más corto más un margen; pasado eso, cada paso de más ahoga (saca vida).
public sealed class SmokeMaze : IDungeonPuzzle
{
    public const int Size = 5;
    public const int Traps = 3;
    public const int StepSlack = 6;

    public const string Up = "u", Down = "d", Left = "l", Right = "r";

    // Las paredes: open[cell, dir] = se puede pasar de la celda hacia esa dirección (0 arriba, 1 abajo, 2 izquierda, 3 derecha).
    private readonly bool[,] _open = new bool[Size * Size, 4];
    private readonly HashSet<int> _traps = [];
    private readonly HashSet<int> _visited = [];
    private readonly HashSet<int> _burned = [];
    private int _position;

    public SmokeMaze(Random rng)
    {
        // Laberinto perfecto por búsqueda en profundidad desde la entrada (arriba a la izquierda).
        var stack = new Stack<int>();
        var carved = new HashSet<int> { 0 };
        stack.Push(0);
        while (stack.Count > 0)
        {
            int cell = stack.Peek();
            var options = Enumerable.Range(0, 4).Where(d => Neighbor(cell, d) is int n && !carved.Contains(n)).ToList();
            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            int dir = options[rng.Next(options.Count)];
            int next = Neighbor(cell, dir)!.Value;
            _open[cell, dir] = true;
            _open[next, Opposite(dir)] = true;
            carved.Add(next);
            stack.Push(next);
        }

        // La salida: la celda más lejana a la entrada (así nunca queda al lado), y el camino corto a ella.
        var distance = Distances(0, out var parent);
        Exit = distance.Select((d, i) => (d, i)).Where(x => x.i != 0).OrderByDescending(x => x.d).ThenBy(_ => rng.Next()).First().i;
        var path = new HashSet<int>();
        for (int c = Exit; c != -1; c = parent[c])
        {
            path.Add(c);
        }

        ShortestPath = distance[Exit];
        StepLimit = ShortestPath + StepSlack;

        var off = Enumerable.Range(0, Size * Size).Where(c => !path.Contains(c)).OrderBy(_ => rng.Next()).Take(Traps);
        foreach (int t in off)
        {
            _traps.Add(t);
        }

        _position = 0;
        _visited.Add(0);
    }

    public DungeonKind Kind => DungeonKind.SmokeMaze;

    public bool Solved { get; private set; }

    public bool Failed => false;

    public string? Note { get; private set; }

    public int Exit { get; }

    public int ShortestPath { get; }

    public int StepLimit { get; }

    public int Steps { get; private set; }

    public int Position => _position;

    public bool CanMove(int dir) => _open[_position, dir];

    // Para las pruebas (y para resolverlo de punta a punta): si desde esa celda se puede pasar en esa dirección (0 arriba, 1 abajo, 2 izquierda, 3 derecha).
    public bool IsOpen(int cell, int dir) => _open[cell, dir];

    public bool IsTrap(int cell) => _traps.Contains(cell);

    public DungeonStep Apply(string action)
    {
        if (Solved)
        {
            return DungeonStep.Ignored;
        }

        int dir = action switch { Up => 0, Down => 1, Left => 2, Right => 3, _ => -1 };
        if (dir < 0 || !_open[_position, dir])
        {
            return DungeonStep.Ignored;
        }

        _position = Neighbor(_position, dir)!.Value;
        _visited.Add(_position);
        Steps++;

        if (_position == Exit)
        {
            Solved = true;
            Note = Loc.T("🚪 ¡Encontraste la salida! El humo se queda atrás.");
            return DungeonStep.Solved;
        }

        if (_traps.Contains(_position) && _burned.Add(_position))
        {
            Note = Loc.T("🔥 ¡Una brasa escondida bajo el humo! Te quemaste.");
            return DungeonStep.Mistake;
        }

        if (Steps > StepLimit)
        {
            Note = Loc.T("😮‍💨 El humo te ahoga: te pasaste de los pasos que aguantabas, cada paso de más duele.");
            return DungeonStep.Mistake;
        }

        Note = null;
        return DungeonStep.Ok;
    }

    public string Render()
    {
        var seen = new HashSet<int>(_visited);
        foreach (int v in _visited)
        {
            for (int d = 0; d < 4; d++)
            {
                if (_open[v, d])
                {
                    seen.Add(Neighbor(v, d)!.Value);
                }
            }
        }

        var lines = new List<string>();
        for (int row = 0; row < Size; row++)
        {
            var cells = new List<string>();
            for (int col = 0; col < Size; col++)
            {
                int c = (row * Size) + col;
                cells.Add(c == _position ? "🧍"
                    : c == Exit && seen.Contains(c) ? "🚪"
                    : _burned.Contains(c) ? "🔥"
                    : _visited.Contains(c) ? "🟫"
                    : seen.Contains(c) ? "⬜"
                    : "🌫️");
            }

            lines.Add(string.Concat(cells));
        }

        string steps = Steps > StepLimit
            ? Loc.F($"Pasos: **{Steps}** (aguantabas {StepLimit}: cada paso de más quema)")
            : Loc.F($"Pasos: **{Steps}** de {StepLimit}");
        return Loc.T("Buscá la salida 🚪 entre el humo. 🧍 sos vos · 🟫 donde pisaste · ⬜ lo que ves desde ahí · 🌫️ humo. Las flechas apagadas dan contra una pared.") +
            "\n\n" + string.Join("\n", lines) + "\n\n" + steps;
    }

    public IReadOnlyList<DungeonButton> Buttons() =>
        Solved ? []
        :
        [
            new DungeonButton(Up, Loc.T("Arriba"), "⬆️", 0, Disabled: !_open[_position, 0]),
            new DungeonButton(Down, Loc.T("Abajo"), "⬇️", 0, Disabled: !_open[_position, 1]),
            new DungeonButton(Left, Loc.T("Izquierda"), "⬅️", 0, Disabled: !_open[_position, 2]),
            new DungeonButton(Right, Loc.T("Derecha"), "➡️", 0, Disabled: !_open[_position, 3]),
        ];

    private static int? Neighbor(int cell, int dir)
    {
        int row = cell / Size, col = cell % Size;
        return dir switch
        {
            0 when row > 0 => cell - Size,
            1 when row < Size - 1 => cell + Size,
            2 when col > 0 => cell - 1,
            3 when col < Size - 1 => cell + 1,
            _ => null,
        };
    }

    private static int Opposite(int dir) => dir switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };

    private int[] Distances(int from, out int[] parent)
    {
        var dist = Enumerable.Repeat(int.MaxValue, Size * Size).ToArray();
        parent = Enumerable.Repeat(-1, Size * Size).ToArray();
        var queue = new Queue<int>();
        dist[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                if (_open[c, d] && Neighbor(c, d) is int n && dist[n] == int.MaxValue)
                {
                    dist[n] = dist[c] + 1;
                    parent[n] = c;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }
}
