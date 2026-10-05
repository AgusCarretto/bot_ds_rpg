namespace BotDsRpg.GameData;

// Qué puede salir en cada tirada de una caja:
//   Item     un ítem fijo (comida, un trofeo, una caja de un escalón menos), con su cantidad.
//   Gather   un material de RECOLECCIÓN sorteado como en /chop y /mine (la misma tabla de rarezas), de a una unidad.
//   ZoneDrop un drop de monstruo (de /hunt o /travel) de una zona permitida: ver BoxLootRoller.Roll.
//   Gold     NO es una tirada: es un bono raro que se suma a la apertura; Weight es la chance en milésimos (30 = 3%).
public enum LootKind { Gold, Item, Gather, ZoneDrop }

// Una entrada del botín de una caja (una fila de box_loot, con el ítem ya resuelto). Weight: la chance de la entrada es su peso
// sobre la suma de pesos de las tiradas de la caja (el oro es aparte). MinQty/MaxQty: el oro, o las unidades del ítem, que da cuando sale.
public sealed record BoxLootEntry(
    LootKind Kind, int? ItemId, string? ItemName, string? ItemRarity, string? ItemEmoji, int Weight, int MinQty, int MaxQty);

// Una caja con todo su botín (boxes + box_loot). MinItems/MaxItems: cuántas tiradas (= ítems) trae, y es lo que se le muestra al jugador
// ("entre 1 y 10 ítems"). TierRank: de qué zona es la caja (1 = Común ... 5 = Mítica), el tope de las zonas de las que pueden salir drops.
public sealed record BoxDefinition(int BoxItemId, string BoxName, int MinItems, int MaxItems, int TierRank, IReadOnlyList<BoxLootEntry> Entries);

// Un material de recolección que puede salir (Madera o Mineral) y un drop de monstruo con la zona de la que sale: el sorteo los recibe ya
// cargados para seguir siendo puro (sin base ni Discord).
public sealed record GatherCandidate(int ItemId, string Name, string Rarity, string? Emoji, string Type);
public sealed record ZoneDropCandidate(int ItemId, string Name, string Rarity, string? Emoji, int ZoneRank, bool IsTravel);

// El contexto de UNA apertura: hasta qué zona llegó el jugador (la más alta que tiene DESBLOQUEADA) y lo que se puede sortear.
public sealed record BoxRollContext(int MaxZoneRank, IReadOnlyList<GatherCandidate> GatherPool, IReadOnlyList<ZoneDropCandidate> ZoneDropPool);

public sealed record LootedItem(int ItemId, string Name, string Rarity, string? Emoji, int Quantity);

// Lo que salió de UNA apertura: el oro total y los ítems (los repetidos ya sumados).
public sealed record BoxLootResult(int Gold, IReadOnlyList<LootedItem> Items);

public static class BoxCatalog
{
    // "entre 1 y 10 ítems"; vacío si la caja no trae el rango cargado.
    public static string RangeText(int? min, int? max) =>
        min is int lo && max is int hi ? (lo == hi ? $"{lo} ítems" : $"entre {lo} y {hi} ítems") : string.Empty;

    // De qué zona es una caja: su rareza (Común = Zona 1 ... Legendario = Zona 4, Mítico = 5). Solo se COMPRA si ya desbloqueaste esa zona.
    public static int RequiredZoneRank(string rarity) => Math.Max(1, RarityCatalog.RankOf(rarity) + 1);
}

// El sorteo del botín de una caja: PURO (sin base ni Discord) para poder probar la distribución con millones de tiradas.
// Una apertura hace N tiradas (N uniforme entre MinItems y MaxItems) y cada una elige UNA entrada con probabilidad proporcional a su peso.
public static class BoxLootRoller
{
    // Las chances de rareza de la recolección dentro de una caja: LAS MISMAS que /chop y /mine (RarityCatalog.GatheringChances, en milésimos), con
    // UNA excepción pedida por el dueño: el Mítico baja de 0,5% a 0,2% en total, o sea 0,1% por ítem (Corteza y Meteorito): en una caja es mucho más
    // raro que farmeándolo. Un test compara las otras cuatro contra RarityCatalog para que no se desincronicen.
    public static readonly (string Rarity, int Weight)[] GatherWeights =
    [
        ("Común", 680),
        ("Raro", 210),
        ("Épico", 70),
        ("Legendario", 35),
        ("Mítico", 2),
    ];

    // La proporción real de los drops de una zona: dos monstruos de /hunt y uno de /travel (3 : 3 : 2 por minuto de farmeo).
    public const int HuntDropWeight = 3;
    public const int TravelDropWeight = 2;

    public static BoxLootResult Roll(BoxDefinition box, BoxRollContext context, Random rng)
    {
        var draws = box.Entries.Where(e => e.Kind != LootKind.Gold).ToList();
        if (draws.Count == 0)
        {
            throw new InvalidOperationException($"La caja \"{box.BoxName}\" no tiene botín cargado (box_loot).");
        }

        if (box.MinItems < 1 || box.MaxItems < box.MinItems)
        {
            throw new InvalidOperationException($"La caja \"{box.BoxName}\" tiene un rango de ítems inválido ({box.MinItems}-{box.MaxItems}).");
        }

        int totalWeight = draws.Sum(e => e.Weight);
        int zoneCap = Math.Min(box.TierRank, context.MaxZoneRank);
        int count = rng.Next(box.MinItems, box.MaxItems + 1);
        var items = new Dictionary<int, LootedItem>();

        for (int roll = 0; roll < count; roll++)
        {
            var entry = Pick(draws, totalWeight, rng);
            int quantity = rng.Next(entry.MinQty, entry.MaxQty + 1);

            switch (entry.Kind)
            {
                case LootKind.Item:
                    Add(items, entry.ItemId!.Value, entry.ItemName!, entry.ItemRarity ?? "Común", entry.ItemEmoji, quantity);
                    break;

                case LootKind.ZoneDrop:
                    var drop = PickZoneDrop(context.ZoneDropPool, zoneCap, rng);
                    if (drop is not null)
                    {
                        Add(items, drop.ItemId, drop.Name, drop.Rarity, drop.Emoji, quantity);
                        break;
                    }

                    // Sin drops cargados para las zonas permitidas: la tirada no se pierde, cae en recolección (el jugador tiene que recibir
                    // los ítems que la caja promete).
                    var fallback = PickGather(context.GatherPool, rng);
                    Add(items, fallback.ItemId, fallback.Name, fallback.Rarity, fallback.Emoji, quantity);
                    break;

                default: // Gather
                    var picked = PickGather(context.GatherPool, rng);
                    Add(items, picked.ItemId, picked.Name, picked.Rarity, picked.Emoji, quantity);
                    break;
            }
        }

        // El oro es un bono: cada entrada de oro se juega aparte, con su chance en milésimos, y no cuenta como ítem.
        int gold = 0;
        foreach (var entry in box.Entries.Where(e => e.Kind == LootKind.Gold))
        {
            if (rng.Next(1000) < entry.Weight)
            {
                gold += rng.Next(entry.MinQty, entry.MaxQty + 1);
            }
        }

        return new BoxLootResult(gold, items.Values.OrderByDescending(i => RarityCatalog.RankOf(i.Rarity)).ThenBy(i => i.Name).ToList());
    }

    private static void Add(Dictionary<int, LootedItem> items, int itemId, string name, string rarity, string? emoji, int quantity) =>
        items[itemId] = items.TryGetValue(itemId, out var existing)
            ? existing with { Quantity = existing.Quantity + quantity }
            : new LootedItem(itemId, name, rarity, emoji, quantity);

    // Como /chop y /mine: primero la rareza (con GatherWeights, solo entre las que tienen algo cargado), después Madera o Mineral parejo entre los
    // que tienen algo de esa rareza, y al final un ítem parejo de ese tipo y esa rareza (por eso el Raro de mineral se reparte entre Carbón e Hierro).
    private static GatherCandidate PickGather(IReadOnlyList<GatherCandidate> pool, Random rng)
    {
        if (pool.Count == 0)
        {
            throw new InvalidOperationException("No hay materiales de recolección cargados para sortear en las cajas.");
        }

        var available = GatherWeights.Where(w => pool.Any(c => c.Rarity == w.Rarity)).ToList();
        int total = available.Sum(w => w.Weight);
        int point = rng.Next(total);
        string rarity = available[^1].Rarity;
        int cumulative = 0;
        foreach (var (candidate, weight) in available)
        {
            cumulative += weight;
            if (point < cumulative)
            {
                rarity = candidate;
                break;
            }
        }

        var ofRarity = pool.Where(c => c.Rarity == rarity).ToList();
        var types = ofRarity.Select(c => c.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        string type = types[rng.Next(types.Count)];
        var finalists = ofRarity.Where(c => c.Type == type).ToList();
        return finalists[rng.Next(finalists.Count)];
    }

    // Un drop de monstruo de una zona <= zoneCap, con el peso real de su tipo (hunt 3, travel 2). Null si no hay ninguno permitido.
    private static ZoneDropCandidate? PickZoneDrop(IReadOnlyList<ZoneDropCandidate> pool, int zoneCap, Random rng)
    {
        var eligible = pool.Where(z => z.ZoneRank <= zoneCap).ToList();
        if (eligible.Count == 0)
        {
            return null;
        }

        int total = eligible.Sum(z => z.IsTravel ? TravelDropWeight : HuntDropWeight);
        int point = rng.Next(total);
        int cumulative = 0;
        foreach (var candidate in eligible)
        {
            cumulative += candidate.IsTravel ? TravelDropWeight : HuntDropWeight;
            if (point < cumulative)
            {
                return candidate;
            }
        }

        return eligible[^1];
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
