namespace BotDsRpg.GameData;

// Lo que /drops necesita saber de un material para mostrarlo: rareza y emoji (nulo = todavía sin pixel art).
public sealed record DropItemInfo(string Name, string Rarity, string? Emoji);

// Un bloque de /drops (una zona tiene hasta tres: Cazar, Viajar y Jefe): el título con la chance y una entrada por
// monstruo. Title entra como nombre de field de Embed (límite 256) y Text como valor (límite 1024).
public sealed record DropsBlock(string Title, string Text);

// Armado PURO (sin Discord ni base) de /drops: qué suelta cada monstruo de una zona y con qué chance. Las chances
// salen de CombatRewardCalculator (las mismas constantes que usa el combate), así que lo que se muestra no se puede
// desincronizar de lo que pasa de verdad.
//
// Cada monstruo ocupa DOS líneas cortas (el nombre y, abajo, lo que suelta) en vez de una larga, y cada tipo de pelea
// es su propio bloque: es lo que lo hace leíble de un vistazo.
public static class DropsCatalog
{
    // Límite de Discord para el valor de un field de un Embed.
    public const int FieldLimit = 1024;

    // Los bloques de UNA zona, en orden Cazar / Viajar / Jefe; los tipos sin monstruos cargados no aparecen.
    // bossRepeat: la fila "repeat" de zone_boxes de la zona (la caja que cae al volver a vencer al jefe); null si no se conoce (el bloque del jefe solo dice qué pasa la primera vez).
    // travelBox (v0.36.0): la fila "travel" (la caja que PUEDE caer al ganar un viaje: el viaje ya no suelta material); null si no se conoce.
    public static IReadOnlyList<DropsBlock> BuildZoneBlocks(
        IEnumerable<ZoneMonster> zoneMonsters, IReadOnlyDictionary<string, DropItemInfo> items, ZoneBoxRow? bossRepeat = null, ZoneBoxRow? travelBox = null)
    {
        var monsters = zoneMonsters.ToList();
        var blocks = new List<DropsBlock>();

        AddBlock(blocks, "🏹 Cazar", $"{CombatRewardCalculator.HuntDropChancePercent}% al ganar", monsters, MonsterKind.Hunt, items);
        // El viaje no suelta material: paga oro y EXP (x30 una cacería) y puede dar la caja de la zona.
        AddBlock(blocks, "🗺️ Viajar (élite)", travelBox is null ? "sin material" : $"{travelBox.ChancePercent}% de caja", monsters, MonsterKind.Travel, items,
            travelBox is null ? string.Empty : $"\n\n📦 {DescribeItem(travelBox.BoxName, items)}", showDrops: false);
        string bossRepeatLine = bossRepeat is null
            ? string.Empty
            : $"\n\n🔁 Si ya lo vencías: {bossRepeat.ChancePercent}% de {DescribeItem(bossRepeat.BoxName, items)}";
        AddBlock(blocks, "👑 Jefe", $"cofre: {CombatRewardCalculator.BossChestFirstClearPercent}% la 1.ª vez", monsters, MonsterKind.Boss, items, bossRepeatLine);

        return blocks;
    }

    private static void AddBlock(
        List<DropsBlock> blocks, string title, string chanceText, List<ZoneMonster> monsters, MonsterKind kind,
        IReadOnlyDictionary<string, DropItemInfo> items, string extraLine = "", bool showDrops = true)
    {
        var ofKind = monsters.Where(m => m.Kind == kind).ToList();
        if (ofKind.Count == 0)
        {
            return;
        }

        var entries = ofKind.Select(zoneMonster =>
        {
            var monster = zoneMonster.Monster;
            if (!showDrops)
            {
                return $"{monster.Emoji} **{monster.Name}**";
            }

            string drops = monster.DropItemNames.Count == 0
                ? "_nada_"
                : string.Join(" / ", monster.DropItemNames.Select(name => DescribeItem(name, items)));
            return $"{monster.Emoji} **{monster.Name}**\n└ {drops}";
        });

        string text = string.Join("\n\n", entries) + extraLine;
        blocks.Add(new DropsBlock(
            $"{title} · {chanceText}",
            text.Length <= FieldLimit ? text : text[..(FieldLimit - 1)] + "…"));
    }

    // "{emoji} Nombre". Sin la rareza: en un drop de monstruo es lo mismo que la zona en la que se lo encuentra (el listado ya va por zona),
    // así que solo ocupaba lugar. Un material que no está en el catálogo cargado se muestra solo por nombre.
    private static string DescribeItem(string name, IReadOnlyDictionary<string, DropItemInfo> items) =>
        items.TryGetValue(name, out var info)
            ? ItemDisplay.Format(info.Emoji, info.Name)
            : name;
}
