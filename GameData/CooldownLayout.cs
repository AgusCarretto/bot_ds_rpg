namespace BotDsRpg.GameData;

// Una línea que /cd puede mostrar: su clave (lo que se guarda), el ícono y el nombre (en español; se traduce al mostrarlo con Loc.Tr).
public sealed record CooldownLine(string Key, string Emoji, string Name);

// Qué líneas de /cd ve cada jugador y en qué orden (v0.51.0, el dueño: «un select con todo activado y si alguien quiere no ver algo que lo pueda sacar… y el orden: en el config poner un número, tipo 1, y que quede primero»).
// Todo es PURO (sin Discord ni base). Por defecto se ven TODAS en el orden de siempre; lo que el jugador cambia se guarda en player_settings.cd_layout como un texto: las claves en SU orden separadas por comas y con un
// «-» delante las que ocultó («hunt,travel,-chop,mine,...»). Una línea que no aparece en el texto (una nueva en una versión futura) se ve al final, así que agregar líneas nunca rompe lo guardado. El resultado de
// no tocar nada es null (columna vacía), no un texto con todo.
public static class CooldownLayout
{
    public const string PetsKey = "pets";
    public const string DragonKey = "dragon";
    public const string ExpeditionKey = "expedition";
    public const string DailyKey = "daily";
    public const string WorldBossKey = "worldboss";
    public const string ArenaKey = "arena";

    // El valor del menú «Poner primera» que vuelve todo al orden de siempre.
    public const string ResetChoice = "reset";

    // Todas las líneas que /cd puede mostrar, en el orden de siempre (el mismo en que CooldownModule las arma: las de la tabla cooldowns, las avanzadas y el voto, las que viven en otras tablas, el diario y,
    // abajo de todo, el jefe, el jefe mundial y la Arena). Algunas solo salen según el jugador (las avanzadas con el oficio al 100, el voto si está habilitado, el dragón, la expedición...): acá están todas.
    public static IReadOnlyList<CooldownLine> Known { get; } = BuildKnown();

    private static IReadOnlyList<CooldownLine> BuildKnown()
    {
        var list = new List<CooldownLine>();
        foreach (var definition in CooldownCatalog.All.Where(d => d != CooldownCatalog.Boss))
        {
            list.Add(new CooldownLine(definition.CommandName, definition.Emoji, definition.DisplayName));
        }

        foreach (var definition in new[] { CooldownCatalog.ChopAdvanced, CooldownCatalog.MineAdvanced, CooldownCatalog.FishAdvanced, CooldownCatalog.Vote })
        {
            list.Add(new CooldownLine(definition.CommandName, definition.Emoji, definition.DisplayName));
        }

        list.Add(new CooldownLine(PetsKey, "🐾", Loc.Mark("Mascotas")));
        list.Add(new CooldownLine(DragonKey, "🐲", Loc.Mark("Dragón (criar)")));
        list.Add(new CooldownLine(ExpeditionKey, "🧭", Loc.Mark("Expedición")));
        list.Add(new CooldownLine(DailyKey, "🎁", Loc.Mark("Diario")));
        list.Add(new CooldownLine(CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Emoji, CooldownCatalog.Boss.DisplayName));
        list.Add(new CooldownLine(WorldBossKey, "🌍", Loc.Mark("Jefe mundial")));
        list.Add(new CooldownLine(ArenaKey, "🏟️", Loc.Mark("Arena")));
        return list;
    }

    public static CooldownLine? LineOf(string key) => Known.FirstOrDefault(k => k.Key == key);

    // Lo guardado → TODAS las líneas conocidas, en el orden del jugador y con su marca de oculta. Lo que no está en el texto (o es una clave que ya no existe) se ignora o se agrega al final, visible.
    public static IReadOnlyList<(string Key, bool Hidden)> Materialize(string? stored)
    {
        var known = Known.Select(k => k.Key).ToList();
        var seen = new HashSet<string>();
        var result = new List<(string Key, bool Hidden)>();
        foreach (string raw in (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool hidden = raw.StartsWith('-');
            string key = hidden ? raw[1..] : raw;
            if (known.Contains(key) && seen.Add(key))
            {
                result.Add((key, hidden));
            }
        }

        foreach (string key in known)
        {
            if (seen.Add(key))
            {
                result.Add((key, false));
            }
        }

        return result;
    }

    // El texto que se guarda: null si queda todo como siempre (todas visibles y en el orden de siempre), así la columna vuelve a estar vacía.
    public static string? Encode(IEnumerable<(string Key, bool Hidden)> entries)
    {
        var list = entries.ToList();
        if (list.SequenceEqual(Known.Select(k => (k.Key, false))))
        {
            return null;
        }

        return string.Join(',', list.Select(e => (e.Hidden ? "-" : string.Empty) + e.Key));
    }

    public static bool IsDefault(string? stored) => Encode(Materialize(stored)) is null;

    // Las líneas que van a verse (las visibles) en el orden del jugador: lo que usa el menú de «poner primera» y la pantalla de /config.
    public static IReadOnlyList<string> VisibleKeys(string? stored) => Materialize(stored).Where(e => !e.Hidden).Select(e => e.Key).ToList();

    // Elige qué líneas se ven (el menú de /config: lo tildado se ve, lo demás se oculta). No cambia el orden.
    public static string? SetVisible(string? stored, IEnumerable<string> visibleKeys)
    {
        var visible = visibleKeys.ToHashSet();
        return Encode(Materialize(stored).Select(e => (e.Key, !visible.Contains(e.Key))));
    }

    // Oculta o muestra UNA línea (aa cd ocultar / mostrar).
    public static string? SetLineVisible(string? stored, string key, bool visible) =>
        Encode(Materialize(stored).Select(e => e.Key == key ? (e.Key, !visible) : e));

    public static string? ShowAll(string? stored) => Encode(Materialize(stored).Select(e => (e.Key, false)));

    // Pone la línea en el puesto `position` (1 = primera) CONTANDO SOLO LAS VISIBLES (el jugador piensa en lo que ve): un puesto mayor que la cantidad la deja última. Una línea oculta que se mueve sigue oculta.
    public static string? MoveTo(string? stored, string key, int position)
    {
        var list = Materialize(stored).ToList();
        int index = list.FindIndex(e => e.Key == key);
        if (index < 0)
        {
            return stored;
        }

        var entry = list[index];
        list.RemoveAt(index);

        int wanted = Math.Max(1, position);
        int visibleSeen = 0;
        int insertAt = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Hidden)
            {
                continue;
            }

            visibleSeen++;
            if (visibleSeen == wanted)
            {
                insertAt = i;
                break;
            }
        }

        list.Insert(insertAt, entry);
        return Encode(list);
    }

    // Aplica lo guardado a las líneas que /cd armó (clave + texto, en el orden de siempre): saca las ocultas y reordena. Sin nada guardado devuelve las mismas líneas, en el mismo orden. Una línea con una clave que
    // no está en Known (por si algún día se suma una) se ve igual, al final.
    public static IReadOnlyList<string> Apply(IReadOnlyList<(string Key, string Text)> lines, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return lines.Select(l => l.Text).ToList();
        }

        var byKey = new Dictionary<string, string>();
        foreach (var (key, text) in lines)
        {
            byKey.TryAdd(key, text);
        }

        var result = new List<string>();
        foreach (var (key, hidden) in Materialize(stored))
        {
            if (!hidden && byKey.TryGetValue(key, out string? text))
            {
                result.Add(text);
            }
        }

        var knownKeys = Known.Select(k => k.Key).ToHashSet();
        result.AddRange(lines.Where(l => !knownKeys.Contains(l.Key)).Select(l => l.Text));
        return result;
    }

    // Otras formas de llamar a cada línea cuando se escribe («aa cd orden cazar 1»), además de su clave y su nombre.
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["hunt"] = ["cacería", "caceria", "cazar", "caza", "cacear"],
        ["travel"] = ["viaje", "viajar"],
        ["chop"] = ["tala", "talar", "leñador", "lenador"],
        ["mine"] = ["mina", "minería", "mineria", "minar"],
        ["fish"] = ["pesca", "pescar", "pescador"],
        ["buybox"] = ["caja", "cajas", "comprar cajas", "box"],
        ["chop_adv"] = ["tala avanzada", "chop avanzada", "chop advanced"],
        ["mine_adv"] = ["minería avanzada", "mineria avanzada", "mine avanzada", "mine advanced"],
        ["fish_adv"] = ["pesca avanzada", "fish avanzada", "fish advanced"],
        ["vote"] = ["voto", "votar", "votos"],
        [PetsKey] = ["mascota", "pet", "pets"],
        [DragonKey] = ["dragón", "criar", "dragon breed"],
        [ExpeditionKey] = ["expedicion", "aventura", "expedition"],
        [DailyKey] = ["daily", "racha", "diaria"],
        ["boss"] = ["jefes"],
        [WorldBossKey] = ["mundial", "jefe mundial", "miniboss", "world boss", "wb"],
        [ArenaKey] = ["torneo"],
    };

    // La línea que quiso decir el jugador (clave, nombre en español o en inglés, o un alias; sin tildes ni mayúsculas). null si no se entiende.
    public static CooldownLine? Find(string? text)
    {
        string wanted = (text ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (var line in Known)
        {
            if (AutocompleteText.SameName(line.Key, wanted) || AutocompleteText.SameName(line.Name, wanted) || AutocompleteText.SameName(Loc.Tr(line.Name), wanted)
                || (Aliases.TryGetValue(line.Key, out var names) && names.Any(n => AutocompleteText.SameName(n, wanted))))
            {
                return line;
            }
        }

        return null;
    }
}
