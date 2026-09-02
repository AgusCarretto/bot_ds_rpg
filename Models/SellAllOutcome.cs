namespace BotDsRpg.Models;

// Resultado de IShopRepository.SellAllAsync.
public sealed record SellAllOutcome(User Player, int ItemsSoldCount, int GoldEarned);
