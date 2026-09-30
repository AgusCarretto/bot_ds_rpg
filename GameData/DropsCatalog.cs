namespace BotDsRpg.GameData;

// Lo que /drops necesita saber de un material para mostrarlo: rareza y emoji (nulo = todavía sin pixel art).
public sealed record DropItemInfo(string Name, string Rarity, string? Emoji);

// Armado PURO (sin Discord ni base) del texto de /drops: qué suelta cada monstruo de una zona y con qué chance. Las
// chances salen de CombatRewardCalculator (las mismas constantes que usa el combate), así que lo que se muestra no
// se puede desincronizar de lo que pasa de verdad.
public static class DropsCatalog
{
    // Límite de Discord para el valor de un field de un Embed.
    public const int FieldLimit = 1024;

    // Texto de UNA zona (va en un field): el pool de /hunt, el monstruo de /travel y el jefe, en ese orden, cada
    // bloque con su chance y una línea por monstruo: "🐗 Jabalí Rabioso → Colmillo de Jabalí (Raro)". Los bloques
    // sin monstruos cargados no aparecen.
    public static string BuildZoneText(IEnumerable<ZoneMonster> zoneMonsters, IReadOnlyDictionary<string, DropItemInfo> items)
    {
        var monsters = zoneMonsters.ToList();
        var blocks = new List<string>();

        AddBlock(blocks, "🏹 **Cazar**", CombatRewardCalculator.HuntDropChancePercent, monsters, MonsterKind.Hunt, items);
        AddBlock(blocks, "🗺️ **Viajar** (élite)", CombatRewardCalculator.TravelDropChancePercent, monsters, MonsterKind.Travel, items);
        AddBlock(blocks, "👑 **Jefe**", CombatRewardCalculator.BossDropChancePercent, monsters, MonsterKind.Boss, items);

        string text = blocks.Count == 0 ? "_Todavía no hay monstruos cargados._" : string.Join("\n", blocks);
        return text.Length <= FieldLimit ? text : text[..(FieldLimit - 1)] + "…";
    }

    private static void AddBlock(
        List<string> blocks, string header, int chancePercent, List<ZoneMonster> monsters, MonsterKind kind,
        IReadOnlyDictionary<string, DropItemInfo> items)
    {
        var ofKind = monsters.Where(m => m.Kind == kind).ToList();
        if (ofKind.Count == 0)
        {
            return;
        }

        blocks.Add($"{header} · {chancePercent}% al ganar");
        foreach (var zoneMonster in ofKind)
        {
            var monster = zoneMonster.Monster;
            string drops = monster.DropItemNames.Count == 0
                ? "_nada_"
                : string.Join(" / ", monster.DropItemNames.Select(name => DescribeItem(name, items)));
            blocks.Add($"{monster.Emoji} {monster.Name} → {drops}");
        }
    }

    // "{emoji} Nombre (Rareza)"; un material que no está en el catálogo cargado se muestra solo por nombre.
    private static string DescribeItem(string name, IReadOnlyDictionary<string, DropItemInfo> items) =>
        items.TryGetValue(name, out var info)
            ? $"{ItemDisplay.Format(info.Emoji, info.Name)} _({info.Rarity})_"
            : name;
}
