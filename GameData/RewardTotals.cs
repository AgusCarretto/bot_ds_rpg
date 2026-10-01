using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Suma los premios que se cobraron en un mismo botón (varias misiones y el premio por completarlas, o varios tramos de logros)
// para mostrar UN recibo: "💰 +840 oro · ⭐ +320 XP · 📦 2× Cajón de Pino". Puro.
public sealed class RewardTotals
{
    private readonly Dictionary<string, int> _boxes = new();

    public int Gold { get; private set; }
    public int Xp { get; private set; }
    public int LevelsGained { get; private set; }
    public int NewLevel { get; private set; }
    public int GoldAfter { get; private set; }
    public int Count { get; private set; }

    public bool IsEmpty => Count == 0;

    public void Add(RewardReceipt receipt)
    {
        Count++;
        Gold += receipt.Reward.Gold;
        Xp += receipt.Reward.Xp;
        LevelsGained += receipt.LevelsGained;
        NewLevel = Math.Max(NewLevel, receipt.NewLevel);
        GoldAfter = receipt.GoldAfter;

        if (receipt.Reward.BoxName is not null)
        {
            _boxes[receipt.Reward.BoxName] = _boxes.GetValueOrDefault(receipt.Reward.BoxName) + receipt.Reward.BoxQuantity;
        }
    }

    public string Describe()
    {
        var parts = new List<string>();

        if (Gold > 0)
        {
            parts.Add($"💰 +{Gold} oro");
        }

        if (Xp > 0)
        {
            parts.Add($"⭐ +{Xp} XP");
        }

        foreach (var (name, quantity) in _boxes.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            parts.Add(quantity > 1 ? $"📦 {quantity}× {name}" : $"📦 {name}");
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }
}
