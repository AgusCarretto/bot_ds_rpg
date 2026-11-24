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
// Cada tipo de pelea es su propio bloque y cada monstruo UN renglón, «**Monstruo** → emoji Ítem» (v0.53.2; antes eran dos
// renglones con el emoji del monstruo y un └, y el dueño lo vio con «mucho emoji junto»).
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

        AddBlock(blocks, Loc.T("🏹 Cazar"), Loc.F($"{CombatRewardCalculator.HuntDropChancePercent}% al ganar"), monsters, MonsterKind.Hunt, items);
        // El viaje no suelta material: paga oro y EXP (x30 una cacería) y puede dar la caja de la zona.
        AddBlock(blocks, Loc.T("🗺️ Viajar (élite)"), travelBox is null ? Loc.T("sin material") : Loc.F($"{travelBox.ChancePercent}% de caja"), monsters, MonsterKind.Travel, items,
            showDrops: false, travelBox: travelBox is null ? null : DescribeItem(travelBox.BoxName, items));
        string bossRepeatLine = bossRepeat is null
            ? string.Empty
            : Loc.F($"\n🔁 Después: {bossRepeat.ChancePercent}% de {DescribeItem(bossRepeat.BoxName, items)}");
        AddBlock(blocks, Loc.T("👑 Jefe"), Loc.F($"cofre: {CombatRewardCalculator.BossChestFirstClearPercent}% la 1.ª vez"), monsters, MonsterKind.Boss, items, bossRepeatLine);

        return blocks;
    }

    // La vista COMPACTA (v0.65.0, pedido de los jugadores: «la versión vieja, que muestra todas las zonas de una»): el texto de UNA zona en tres renglones: el título, la cacería (cada monstruo con lo que suelta) y el viaje (el élite y
    // la caja que puede dar). Sin el jefe (el dueño: «zona 1: fila 1 hunt y qué da, fila 2 travel y qué da»): es la versión corta, el detalle de cada zona sigue en la vista de siempre. Las zonas se separan con una línea en blanco.
    public static string BuildCompactZone(
        string title, IEnumerable<ZoneMonster> zoneMonsters, IReadOnlyDictionary<string, DropItemInfo> items, ZoneBoxRow? travelBox = null)
    {
        var monsters = zoneMonsters.ToList();
        var lines = new List<string> { title };

        var hunt = monsters.Where(m => m.Kind == MonsterKind.Hunt).Select(m => $"**{m.Monster.Name}** → {DescribeDrops(m.Monster, items)}").ToList();
        if (hunt.Count > 0)
        {
            lines.Add("🏹 " + string.Join(" · ", hunt));
        }

        var travel = monsters.Where(m => m.Kind == MonsterKind.Travel).Select(m =>
            travelBox is null ? $"**{m.Monster.Name}**" : $"**{m.Monster.Name}** → {DescribeItem(travelBox.BoxName, items)} ({travelBox.ChancePercent}%)").ToList();
        if (travel.Count > 0)
        {
            lines.Add("🗺️ " + string.Join(" · ", travel));
        }

        return string.Join('\n', lines);
    }

    // Lo que suelta un monstruo, ya formateado («_nada_» si no suelta nada).
    private static string DescribeDrops(MonsterTemplate monster, IReadOnlyDictionary<string, DropItemInfo> items) =>
        monster.DropItemNames.Count == 0
            ? Loc.T("_nada_")
            : string.Join(" / ", monster.DropItemNames.Select(name => DescribeItem(name, items)));

    private static void AddBlock(
        List<DropsBlock> blocks, string title, string chanceText, List<ZoneMonster> monsters, MonsterKind kind,
        IReadOnlyDictionary<string, DropItemInfo> items, string extraLine = "", bool showDrops = true, string? travelBox = null)
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
                // El viaje no suelta material: si se conoce la caja de la zona, va en el renglón del monstruo.
                return travelBox is null ? $"**{monster.Name}**" : $"**{monster.Name}** → {travelBox}";
            }

            return $"**{monster.Name}** → {DescribeDrops(monster, items)}";
        });

        // v0.53.2: un renglón por monstruo y sin el emoji del monstruo (el dueño: «es mucho emoji junto»); solo el ítem lleva el suyo.
        string text = string.Join("\n", entries) + extraLine;
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
