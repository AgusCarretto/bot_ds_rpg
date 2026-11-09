using System.Text;

namespace BotDsRpg.GameData.Dungeons;

// Zona 7 — El laberinto de humo. Un laberinto de 5 de ancho por 7 de alto tallado al azar en cada corrida (siempre tiene camino: es un laberinto «perfecto», un árbol), con el humo tapando todo lo que
// no viste.
// v0.52.0 (el dueño: «que sea libre, que se vean las paredes y los cuadrados que vienen, y que las trampas se las coman: si no se comen las trampas no tiene sentido»):
//   • Las 4 flechas SIEMPRE andan (antes las que daban contra una pared salían apagadas y los botones te guiaban). Apretar una contra una pared no mueve, no gasta un paso ni quema: solo avisa.
//   • El mapa dibuja las PAREDES entre las celdas (una grilla de 11 de ancho por 15 de alto: celdas en los lugares impares, paredes y pasos en los pares). Desde donde estás ves los 4 lados: camino
//     abierto ⬜ (y la casilla de atrás) o pared ⬛; lo que ya viste queda dibujado, lo demás sigue en humo 🌫️. El recorrido (🟩) se ve entero.
//   • Las BRASAS están en la BOCA de las ramas que se desvían del camino corto a la salida (la primera celda de cada desvío; hasta MaxTraps) y se ven ⬜ como cualquier camino hasta que las pisás:
//     cada giro equivocado quema. Pisar una quema (una vez) y, como ninguna está en el camino a la salida, esa rama no sale: hay que volver. Antes eran 3 brasas sueltas fuera del camino y casi
//     nadie las pisaba (quien elegía al azar se comía ~1 por corrida; ahora ~2, medido en el arnés).
//   • SIN límite de pasos y SIN aviso de calor: lo único que cuesta vida son las brasas.
// El ancho es lo que importa para el celu (11 emojis por fila caben; 13 puede partirse), así que el laberinto crece hacia abajo. Para que haya bifurcaciones el laberinto se vuelve a sortear hasta que
// tenga al menos MinBranches desvíos del camino corto (≈ 1 de cada 4 sorteos).
public sealed class SmokeMaze : IDungeonPuzzle
{
    public const int Cols = 5;
    public const int Rows = 7;
    public const int Cells = Cols * Rows;
    public const int MaxTraps = 6;
    public const int MinBranches = 4;
    private const int MaxAttempts = 500;

    public const string Up = "u", Down = "d", Left = "l", Right = "r";

    // Las paredes: open[cell, dir] = se puede pasar de la celda hacia esa dirección (0 arriba, 1 abajo, 2 izquierda, 3 derecha).
    private readonly bool[,] _open = new bool[Cells, 4];
    // Los lados que ya viste (abiertos o con pared): known[cell, dir]. Se anotan de los dos lados.
    private readonly bool[,] _known = new bool[Cells, 4];
    private readonly HashSet<int> _traps = [];
    private readonly HashSet<int> _visited = [];
    private readonly HashSet<int> _burned = [];
    private readonly HashSet<int> _seen = [];
    private int _position;

    public SmokeMaze(Random rng)
    {
        // Laberinto perfecto por búsqueda en profundidad desde la entrada (arriba a la izquierda); se vuelve a sortear si no tiene desvíos suficientes.
        int[] distance;
        int[] parent;
        int exit;
        List<int> mouths;
        int attempts = 0;
        do
        {
            Array.Clear(_open);
            Carve(rng);
            distance = Distances(0, out parent);

            // La salida: la celda más lejana a la entrada (así nunca queda al lado), y el camino corto a ella.
            exit = distance.Select((d, i) => (d, i)).Where(x => x.i != 0).OrderByDescending(x => x.d).ThenBy(_ => rng.Next()).First().i;
            var path = new HashSet<int>();
            for (int c = exit; c != -1; c = parent[c])
            {
                path.Add(c);
            }

            // Las bocas: celdas fuera del camino corto pegadas a él por un paso abierto = la primera celda de cada rama que se desvía.
            mouths = [];
            foreach (int pc in path)
            {
                for (int d = 0; d < 4; d++)
                {
                    if (_open[pc, d] && Neighbor(pc, d) is int n && !path.Contains(n))
                    {
                        mouths.Add(n);
                    }
                }
            }

            attempts++;
        }
        while (mouths.Count < MinBranches && attempts < MaxAttempts);

        Exit = exit;
        ShortestPath = distance[exit];

        // Una brasa en la boca de cada desvío (si hay más de MaxTraps, en MaxTraps al azar): ninguna está en el camino corto, así que siempre hay una ruta limpia, y cada giro equivocado quema.
        foreach (int mouth in mouths.OrderBy(_ => rng.Next()).Take(MaxTraps))
        {
            _traps.Add(mouth);
        }

        _position = 0;
        _visited.Add(0);
        See(0);
    }

    public DungeonKind Kind => DungeonKind.SmokeMaze;

    public bool Solved { get; private set; }

    public bool Failed => false;

    public string? Note { get; private set; }

    public int Exit { get; }

    public int ShortestPath { get; }

    // Los pasos que diste (solo para mirar: no hay tope).
    public int Steps { get; private set; }

    public int Position => _position;

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
        if (dir < 0)
        {
            return DungeonStep.Ignored;
        }

        // Contra una pared (o el borde) no te movés: no gasta un paso ni quema, solo te avisa. Cuenta como una jugada (re-dibuja y reinicia el reloj), por eso es Ok y no Ignored.
        if (!_open[_position, dir])
        {
            Note = Loc.T("🧱 Ahí hay una pared.");
            return DungeonStep.Ok;
        }

        _position = Neighbor(_position, dir)!.Value;
        _visited.Add(_position);
        Steps++;
        See(_position);

        if (_position == Exit)
        {
            Solved = true;
            Note = Loc.T("🚪 ¡Encontraste la salida! El humo se queda atrás.");
            return DungeonStep.Solved;
        }

        if (_traps.Contains(_position) && _burned.Add(_position))
        {
            Note = Loc.T("🔥 ¡Una brasa! Ese camino no lleva a la salida: volvé.");
            return DungeonStep.Mistake;
        }

        Note = null;
        return DungeonStep.Ok;
    }

    public string Render()
    {
        var lines = new List<string>();
        for (int gr = 0; gr <= 2 * Rows; gr++)
        {
            var row = new StringBuilder();
            for (int gc = 0; gc <= 2 * Cols; gc++)
            {
                row.Append(Tile(gr, gc));
            }

            lines.Add(row.ToString());
        }

        return Loc.T("Buscá la salida 🚪 entre el humo. 🧍 sos vos · 🟩 por donde pasaste · ⬜ camino · ⬛ pared · 🌫️ humo. Una brasa se ve como cualquier camino.") +
            "\n\n" + string.Join("\n", lines) + "\n\n" + Loc.F($"Pasos: **{Steps}**");
    }

    // Los botones: las 4 flechas, siempre prendidas (ver el comentario de arriba).
    public IReadOnlyList<DungeonButton> Buttons() =>
        Solved ? []
        :
        [
            new DungeonButton(Up, Loc.T("Arriba"), "⬆️", 0),
            new DungeonButton(Down, Loc.T("Abajo"), "⬇️", 0),
            new DungeonButton(Left, Loc.T("Izquierda"), "⬅️", 0),
            new DungeonButton(Right, Loc.T("Derecha"), "➡️", 0),
        ];

    // Lo que ves desde una celda: sus 4 lados (queda anotado para siempre) y, por los que están abiertos, la casilla de atrás.
    private void See(int cell)
    {
        for (int d = 0; d < 4; d++)
        {
            if (Neighbor(cell, d) is not int n)
            {
                continue;
            }

            _known[cell, d] = true;
            _known[n, Opposite(d)] = true;
            if (_open[cell, d])
            {
                _seen.Add(n);
            }
        }
    }

    // La grilla de (2·Rows+1) filas por (2·Cols+1) columnas: las celdas están en las filas y columnas impares; entre dos celdas va el paso o la pared; en las esquinas y en el borde, pared.
    private string Tile(int gr, int gc)
    {
        bool rowOdd = gr % 2 == 1, colOdd = gc % 2 == 1;
        if (rowOdd && colOdd)
        {
            return CellTile((gr / 2 * Cols) + (gc / 2));
        }

        if (!rowOdd && !colOdd)
        {
            return "⬛";   // una esquina de pared
        }

        if (gr == 0 || gc == 0 || gr == 2 * Rows || gc == 2 * Cols)
        {
            return "⬛";   // el borde de afuera
        }

        // Entre dos celdas: el paso hacia abajo de la de arriba (fila par) o hacia la derecha de la de la izquierda (columna par).
        int cell = rowOdd ? (gr / 2 * Cols) + (gc / 2) - 1 : (((gr / 2) - 1) * Cols) + (gc / 2);
        int dir = rowOdd ? 3 : 1;
        int other = Neighbor(cell, dir)!.Value;
        if (!_known[cell, dir])
        {
            return "🌫️";
        }

        if (!_open[cell, dir])
        {
            return "⬛";
        }

        // En un laberinto perfecto (un árbol) el paso entre dos celdas pisadas es siempre un paso que diste.
        return _visited.Contains(cell) && _visited.Contains(other) ? "🟩" : "⬜";
    }

    private string CellTile(int cell)
    {
        if (cell == Exit && Solved)
        {
            return "🚪";
        }

        if (cell == _position)
        {
            return "🧍";
        }

        if (_burned.Contains(cell))
        {
            return "🔥";
        }

        if (_visited.Contains(cell))
        {
            return "🟩";
        }

        if (cell == Exit && _seen.Contains(cell))
        {
            return "🚪";
        }

        // Una brasa que no pisaste se ve igual que cualquier camino.
        return _seen.Contains(cell) ? "⬜" : "🌫️";
    }

    private void Carve(Random rng)
    {
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
    }

    private static int? Neighbor(int cell, int dir)
    {
        int row = cell / Cols, col = cell % Cols;
        return dir switch
        {
            0 when row > 0 => cell - Cols,
            1 when row < Rows - 1 => cell + Cols,
            2 when col > 0 => cell - 1,
            3 when col < Cols - 1 => cell + 1,
            _ => null,
        };
    }

    private static int Opposite(int dir) => dir switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };

    private int[] Distances(int from, out int[] parent)
    {
        var dist = Enumerable.Repeat(int.MaxValue, Cells).ToArray();
        parent = Enumerable.Repeat(-1, Cells).ToArray();
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
