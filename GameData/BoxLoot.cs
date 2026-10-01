namespace BotDsRpg.GameData;

public enum LootKind { Gold, Item }

// Una entrada del botín de una caja (una fila de box_loot, con el ítem ya resuelto). Weight: la chance de la entrada es su peso
// sobre la suma de pesos de la caja. MinQty/MaxQty: el oro, o las unidades del ítem, que da cuando sale.
public sealed record BoxLootEntry(
    LootKind Kind, int? ItemId, string? ItemName, string? ItemRarity, string? ItemEmoji, int Weight, int MinQty, int MaxQty);

// Una caja con todo su botín (boxes + box_loot). Rolls: cuántas tiradas hace al abrirla.
public sealed record BoxDefinition(int BoxItemId, string BoxName, int Rolls, IReadOnlyList<BoxLootEntry> Entries);

public sealed record LootedItem(int ItemId, string Name, string Rarity, string? Emoji, int Quantity);

// Lo que salió de UNA apertura: el oro total y los ítems (los repetidos ya sumados).
public sealed record BoxLootResult(int Gold, IReadOnlyList<LootedItem> Items);

// El sorteo del botín de una caja: PURO (sin base ni Discord) para poder probar la distribución con millones de tiradas.
// Cada tirada elige UNA entrada con probabilidad proporcional a su peso y una cantidad uniforme entre su mínimo y su máximo.
public static class BoxLootRoller
{
    public static BoxLootResult Roll(BoxDefinition box, Random rng)
    {
        if (box.Entries.Count == 0)
        {
            throw new InvalidOperationException($"La caja \"{box.BoxName}\" no tiene botín cargado (box_loot).");
        }

        int totalWeight = box.Entries.Sum(e => e.Weight);
        int gold = 0;
        var items = new Dictionary<int, LootedItem>();

        for (int roll = 0; roll < box.Rolls; roll++)
        {
            var entry = Pick(box.Entries, totalWeight, rng);
            int quantity = rng.Next(entry.MinQty, entry.MaxQty + 1);

            if (entry.Kind == LootKind.Gold)
            {
                gold += quantity;
                continue;
            }

            int itemId = entry.ItemId!.Value;
            items[itemId] = items.TryGetValue(itemId, out var existing)
                ? existing with { Quantity = existing.Quantity + quantity }
                : new LootedItem(itemId, entry.ItemName!, entry.ItemRarity ?? "Común", entry.ItemEmoji, quantity);
        }

        return new BoxLootResult(gold, items.Values.OrderByDescending(i => RarityCatalog.RankOf(i.Rarity)).ThenBy(i => i.Name).ToList());
    }

    private static BoxLootEntry Pick(IReadOnlyList<BoxLootEntry> entries, int totalWeight, Random rng)
    {
        int point = rng.Next(totalWeight);
        int cumulative = 0;

        foreach (var entry in entries)
        {
            cumulative += entry.Weight;
            if (point < cumulative)
            {
                return entry;
            }
        }

        return entries[^1]; // defensivo: no debería alcanzarse si los pesos están bien sumados
    }
}
