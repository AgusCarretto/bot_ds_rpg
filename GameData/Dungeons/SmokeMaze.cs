using System.Text;

namespace BotDsRpg.GameData.Dungeons;

// Zona 7 — El laberinto de humo. Un laberinto de 5 de ancho por 8 de alto, tallado al azar en cada corrida, con el humo tapando todo lo que no viste.
// v0.52.0 (el dueño: «que sea libre, que se vean las paredes y los cuadrados que vienen, y que las trampas se las coman: si no se comen las trampas no tiene sentido»):
//   • Las 4 flechas SIEMPRE andan (antes las que daban contra una pared salían apagadas y los botones te guiaban). Apretar una contra una pared no mueve, no gasta un paso ni quema: solo avisa.
//   • El mapa dibuja las PAREDES entre las celdas (una grilla de 11 de ancho: celdas en los lugares impares, paredes y pasos en los pares). Desde donde estás ves los 4 lados: camino abierto ⬜ (y la
//     casilla de atrás) o pared ⬛; lo que ya viste queda dibujado, lo demás sigue en humo 🌫️. Los pasos que diste se ven 🟩.
//   • SIN límite de pasos y SIN aviso de calor: lo único que cuesta vida son las brasas.
// v0.52.2 (el dueño, tras jugarlo: «es muy obvio los callejones sin salida y el camino a tomar, no hay duda de cuál elegir… estaría bueno tener más bifurcaciones, dos caminos que lleguen a la salida
// igual pero por uno tenés una llama… ayer no me comí ninguna», y después «dejá 6 brasas»):
//   • El laberinto es TRENZADO: se talla el árbol de siempre y después se abren `Loops` paredes al azar. Cada una cierra un lazo, así que hay más de un camino a la salida y quedan menos
//     callejones sin salida (~1,8 contra ~3,3 del árbol); ya no se adivina el camino mirando cuáles terminan en pared.
//   • Las `Traps` BRASAS caen en celdas de esos caminos (a lo sumo `Slack` pasos de desvío sobre el camino corto, nunca en la entrada ni en la salida) y se ven ⬜ como cualquier camino hasta que las
//     pisás. Se acepta el sorteo solo si, con todas las brasas puestas, todavía se llega a la salida sin pisar ninguna: SIEMPRE queda un camino limpio, pero no se sabe cuál. Antes estaban en la boca de
//     cada rama que se desviaba y bastaba con no entrar a las ramas que se ven sin salida.
//   • Pisar una quema UNA vez y la casilla queda marcada 🔥: ya no quema y se puede cruzar. Como con lazos una brasa no tapa un callejón, ya no se dice «esa rama no sale».
// El ancho es lo que importa para el celu (11 emojis por fila caben; 13 puede partirse), así que el laberinto crece hacia abajo. El sorteo se repite hasta que el camino corto mide al menos
// `MinShortest` pasos y entran las 6 brasas con camino limpio (≈ 8 sorteos de media, medido).
public sealed class SmokeMaze : IDungeonPuzzle
{
    public const int Cols = 5;
    public const int Rows = 8;
    public const int Cells = Cols * Rows;
    // Las paredes que se abren después de tallar el árbol: cada una cierra un lazo (otro camino).
    public const int Loops = 6;
    // Las brasas por laberinto.
    public const int Traps = 6;
    // Una brasa solo cae en una celda cuyo camino entrada→celda→salida mide a lo sumo esto de más que el camino corto: está «en los caminos», no escondida lejos.
    public const int Slack = 6;
    // El camino corto de la entrada a la salida mide al menos esto (si no, se vuelve a sortear).
    public const int MinShortest = 20;
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
    // Los pasos que diste de verdad (con lazos, dos celdas pisadas pueden estar unidas por un paso que NO recorriste).
    private readonly HashSet<(int, int)> _walked = [];
    private int _position;

    public SmokeMaze(Random rng)
    {
        int exit = 0, shortest = 0;
        for (int attempt = 1; ; attempt++)
        {
            Array.Clear(_open);
            _traps.Clear();
            Carve(rng);
            Braid(rng);

            // La salida: la celda más lejana a la entrada (así nunca queda al lado).
            int[] fromStart = Distances(0);
            exit = fromStart.Select((d, i) => (d, i)).Where(x => x.i != 0).OrderByDescending(x => x.d).ThenBy(_ => rng.Next()).First().i;
            shortest = fromStart[exit];
            if (shortest >= MinShortest)
            {
                PlaceTraps(rng, exit, fromStart, shortest);
                if (_traps.Count == Traps)
                {
                    break;
                }
            }

            if (attempt >= MaxAttempts)
            {
                break;   // no pasa en la práctica; el laberinto igual es válido (con las brasas que hayan entrado)
            }
        }

        Exit = exit;
        ShortestPath = shortest;
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

        int from = _position;
        _position = Neighbor(_position, dir)!.Value;
        _visited.Add(_position);
        _walked.Add(Edge(from, _position));
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
            Note = Loc.T("🔥 ¡Una brasa! Te quemaste: esa casilla queda marcada y ya no quema.");
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
            // Solo el emoji (auditoría de UI, M12): con texto, «Izquierda» y «Derecha» se cortaban en el celular. Izquierda y derecha en las puntas.
            new DungeonButton(Left, string.Empty, "⬅️", 0),
            new DungeonButton(Up, string.Empty, "⬆️", 0),
            new DungeonButton(Down, string.Empty, "⬇️", 0),
            new DungeonButton(Right, string.Empty, "➡️", 0),
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

        // Con lazos, dos celdas pisadas pueden estar unidas por un paso que no diste: solo los pasos que recorriste son 🟩.
        return _walked.Contains(Edge(cell, other)) ? "🟩" : "⬜";
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

    // El árbol: laberinto perfecto por búsqueda en profundidad desde la entrada (arriba a la izquierda).
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

    // Trenzar: abrir `Loops` paredes interiores al azar. En un árbol toda pared entre dos celdas vecinas cierra un lazo, así que cada una da otro camino.
    private void Braid(Random rng)
    {
        var walls = new List<(int Cell, int Dir)>();
        for (int c = 0; c < Cells; c++)
        {
            foreach (int d in new[] { 1, 3 })
            {
                if (Neighbor(c, d) is not null && !_open[c, d])
                {
                    walls.Add((c, d));
                }
            }
        }

        foreach (var (cell, dir) in walls.OrderBy(_ => rng.Next()).Take(Loops))
        {
            _open[cell, dir] = true;
            _open[Neighbor(cell, dir)!.Value, Opposite(dir)] = true;
        }
    }

    // Las brasas: celdas de los caminos a la salida (entrada→celda→salida a lo sumo `Slack` más largo que el camino corto), una por una, y solo se queda la que deja un camino sin brasas.
    private void PlaceTraps(Random rng, int exit, int[] fromStart, int shortest)
    {
        int[] fromExit = Distances(exit);
        var candidates = Enumerable.Range(0, Cells)
            .Where(c => c != 0 && c != exit && fromStart[c] + fromExit[c] <= shortest + Slack)
            .OrderBy(_ => rng.Next())
            .ToList();
        foreach (int c in candidates)
        {
            if (_traps.Count >= Traps)
            {
                break;
            }

            _traps.Add(c);
            if (Distances(0, _traps)[exit] == int.MaxValue)
            {
                _traps.Remove(c);   // taparía el último camino limpio
            }
        }
    }

    private static (int, int) Edge(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

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

    // Distancia en pasos desde `from` por los pasos abiertos, sin pisar las celdas `blocked` (int.MaxValue = no se llega).
    private int[] Distances(int from, HashSet<int>? blocked = null)
    {
        var dist = Enumerable.Repeat(int.MaxValue, Cells).ToArray();
        if (blocked?.Contains(from) == true)
        {
            return dist;
        }

        var queue = new Queue<int>();
        dist[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                if (_open[c, d] && Neighbor(c, d) is int n && dist[n] == int.MaxValue && blocked?.Contains(n) != true)
                {
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }
}
