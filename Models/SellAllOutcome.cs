namespace BotDsRpg.Models;

// Una pieza de equipo puesta que «vender todo» también vende (v0.53.0): el arma o el amuleto, con lo que paga la tienda.
public sealed record SellAllGear(int ItemId, string Name, string? Emoji, bool IsWeapon, int SellPrice);

// Lo que «vender todo» vendería AHORA (IShopRepository.PreviewSellAllAsync), sin tocar nada: las unidades vendibles de la mochila y su oro, cuántas unidades se quedan porque se conservan en el Fuego Nuevo, y el equipo puesto.
public sealed record SellAllPreview(int ItemsCount, int BagGold, int KeptCount, IReadOnlyList<SellAllGear> Gear)
{
    public int TotalGold => (int)Math.Min(int.MaxValue, (long)BagGold + Gear.Sum(g => (long)g.SellPrice));
    public bool IsEmpty => ItemsCount == 0 && Gear.Count == 0;
}

// Resultado de IShopRepository.SellAllAsync.
// KeptCount (v0.38.0): cuántas unidades vendibles NO se vendieron porque se conservan en el Fuego Nuevo (comida de mascotas y de dragón), para avisarlo en la pantalla.
// GearSold (v0.53.0): el arma y/o el amuleto puestos que se vendieron (su oro ya está dentro de GoldEarned; ItemsSoldCount cuenta solo las unidades de la mochila).
// Changed (v0.53.0): el total que el jugador vio al confirmar ya no era el real (cambió la mochila o el equipo): NO se vendió nada.
public sealed record SellAllOutcome(User Player, int ItemsSoldCount, int GoldEarned, int KeptCount = 0, IReadOnlyList<SellAllGear>? GearSold = null, bool Changed = false);
