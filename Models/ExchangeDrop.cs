namespace BotDsRpg.Models;

// Un drop de monstruo que el tabernero acepta cambiar (GameData/DropExchange.cs) y la zona de la que sale. La zona es la del monstruo que lo suelta (un drop sale de un solo
// monstruo, y los trofeos de las cajas, que no salen de ninguno, no están en esta lista).
public sealed record ExchangeDrop(int ItemId, string Name, string? Emoji, string Rarity, int ZoneId, string ZoneName, int MinLevel);
