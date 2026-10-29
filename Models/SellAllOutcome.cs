namespace BotDsRpg.Models;

// Resultado de IShopRepository.SellAllAsync.
// KeptCount (v0.38.0): cuántas unidades vendibles NO se vendieron porque se conservan en el Fuego Nuevo (comida de mascotas y de dragón), para avisarlo en la pantalla.
public sealed record SellAllOutcome(User Player, int ItemsSoldCount, int GoldEarned, int KeptCount = 0);
