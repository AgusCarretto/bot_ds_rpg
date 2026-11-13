using System.Collections.Concurrent;
using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// El paño de la ruleta de un jugador (v0.55.1): las fichas que puso, el tamaño de ficha elegido y lo que hizo (para «Deshacer»).
// Se muta SOLO bajo Lock y sin await adentro. En memoria, como el blackjack: si el bot se reinicia, el paño se pierde, pero NO se pierde
// oro, porque las fichas no cobran nada hasta que se gira (el cobro es una sola llamada guardada, RouletteModule.ExecuteSpinAsync).
public sealed class RouletteBoard
{
    public static readonly int[] ChipMultipliers = [1, 5, 10];

    public required ulong OwnerId { get; init; }
    public required int BaseChip { get; init; }

    public object Lock { get; } = new();
    public int Multiplier { get; set; } = 1;
    public long Chip => (long)BaseChip * Multiplier;

    // código de la apuesta → cuánto tiene encima.
    public Dictionary<string, int> Bets { get; } = new();

    // Cada toque (un botón, o varios números de un menú de una) es una acción que «Deshacer» saca entera.
    public Stack<List<(string Code, int Amount)>> Actions { get; } = new();

    public long Total => Bets.Values.Sum(v => (long)v);

    // Las apuestas en el orden en que se muestran (rojo, negro, par, impar, números).
    public List<(RouletteBet Bet, int Amount)> Ordered() =>
        Bets.Select(kv => (Bet: RouletteRules.Parse(kv.Key)!, Amount: kv.Value)).OrderBy(b => RouletteRules.SortKey(b.Bet)).ToList();
}

public enum RoulettePlaceStatus { Ok, NotEnoughGold, TooMuch }

public interface IRouletteBoardService
{
    // Abre un paño nuevo (reemplaza el que tuviera el jugador).
    RouletteBoard Open(ulong ownerId, int baseChip);

    RouletteBoard? Get(ulong ownerId);

    // Pone una ficha del tamaño elegido en cada apuesta. gold = el oro que tiene AHORA: el total del paño nunca pasa de eso ni de
    // RouletteRules.MaxTotalStake. Todo o nada.
    RoulettePlaceStatus Place(RouletteBoard board, IReadOnlyList<RouletteBet> bets, int gold);

    bool Undo(RouletteBoard board);

    void Clear(RouletteBoard board);

    void SetMultiplier(RouletteBoard board, int multiplier);
}

public sealed class RouletteBoardService : IRouletteBoardService
{
    private readonly ConcurrentDictionary<ulong, RouletteBoard> _boards = new();

    public RouletteBoard Open(ulong ownerId, int baseChip)
    {
        var board = new RouletteBoard { OwnerId = ownerId, BaseChip = baseChip };
        _boards[ownerId] = board;
        return board;
    }

    public RouletteBoard? Get(ulong ownerId) => _boards.TryGetValue(ownerId, out var board) ? board : null;

    public RoulettePlaceStatus Place(RouletteBoard board, IReadOnlyList<RouletteBet> bets, int gold)
    {
        lock (board.Lock)
        {
            long add = board.Chip * bets.Count;
            long total = board.Total + add;
            if (total > RouletteRules.MaxTotalStake)
            {
                return RoulettePlaceStatus.TooMuch;
            }

            if (total > gold)
            {
                return RoulettePlaceStatus.NotEnoughGold;
            }

            var action = new List<(string, int)>();
            foreach (var bet in bets)
            {
                int chip = (int)board.Chip; // entra: el total ya quedó por debajo de MaxTotalStake
                board.Bets[bet.Code] = board.Bets.GetValueOrDefault(bet.Code) + chip;
                action.Add((bet.Code, chip));
            }

            board.Actions.Push(action);
            return RoulettePlaceStatus.Ok;
        }
    }

    public bool Undo(RouletteBoard board)
    {
        lock (board.Lock)
        {
            if (!board.Actions.TryPop(out var action))
            {
                return false;
            }

            foreach (var (code, amount) in action)
            {
                int left = board.Bets.GetValueOrDefault(code) - amount;
                if (left > 0) board.Bets[code] = left;
                else board.Bets.Remove(code);
            }

            return true;
        }
    }

    public void Clear(RouletteBoard board)
    {
        lock (board.Lock)
        {
            board.Bets.Clear();
            board.Actions.Clear();
        }
    }

    public void SetMultiplier(RouletteBoard board, int multiplier)
    {
        if (!RouletteBoard.ChipMultipliers.Contains(multiplier)) return;
        lock (board.Lock)
        {
            board.Multiplier = multiplier;
        }
    }
}
