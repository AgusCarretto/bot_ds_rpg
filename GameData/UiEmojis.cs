using System.Text.RegularExpressions;

namespace BotDsRpg.GameData;

// Las piezas de una barra de progreso hechas con emojis: un extremo izquierdo, un medio y un extremo derecho, cada uno lleno y vacío.
public sealed record BarPieces(string LeftFull, string LeftEmpty, string MidFull, string MidEmpty, string RightFull, string RightEmpty);

// Los emojis PROPIOS de la interfaz (v0.19.4): el ícono de Fuego Nuevo, el «LV» azul, el de XP, los del oro, el banco, el polvo y la racha (v0.19.5) y las piezas de las barras de vida, XP y pesar.
// El dueño los sube como emojis de la aplicación en el Developer Portal con los nombres EXACTOS de abajo (no hay que copiar ningún id: el bot lee los emojis de su aplicación por nombre,
// Services/UiEmojiRefresher.cs, al arrancar y cada 10 minutos) y desde ese momento las pantallas los usan. Si uno no está subido todavía, cada pantalla cae a lo de siempre (🔥 FN, «Nivel»,
// barra de texto): subir el arte de a tandas no rompe nada. Pueden ser animados (GIF): el código se guarda tal cual lo devuelve Discord, «<a:nombre:id>».
// Estado estático a propósito, como GameData/ItemEmojiRegistry.cs y NpcImages: los armadores de pantallas son funciones puras que no reciben repositorios ni el cliente de Discord.
public static class UiEmojis
{
    // Íconos sueltos.
    public const string FuegoNuevoName = "ui_fn";
    public const string LevelName = "ui_lv";
    public const string XpName = "ui_xp";
    public const string GoldName = "ui_oro";
    public const string BankName = "ui_banco";
    public const string DustName = "ui_polvo";
    public const string StreakName = "ui_racha";

    // Tipos de barra: vida (rojo/verde), XP (azul/dorado) y el pesar del Asador (la historia).
    public const string BarHp = "hp";
    public const string BarXp = "xp";
    public const string BarPesar = "pesar";
    public static readonly IReadOnlyList<string> BarKinds = [BarHp, BarXp, BarPesar];

    // Partes de una barra: l = extremo izquierdo, m = medio, r = extremo derecho; estado f = lleno, e = vacío.
    private static readonly string[] Parts = ["l", "m", "r"];
    private static readonly string[] States = ["f", "e"];

    private static readonly Regex EmojiCode = new(@"^<a?:[A-Za-z0-9_]{2,32}:(\d{17,20})>$", RegexOptions.Compiled);

    private static volatile Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);

    // Los ids de TODOS los emojis que la aplicación tiene hoy (v0.22.2). Un desplegable solo lleva un emoji propio si su id está acá: con uno que Discord no conoce (borrado del portal, de otra aplicación)
    // rechaza el mensaje ENTERO, así que sin esta lista cargada (recién arrancado, o Discord no respondió) las opciones salen sin emoji, como siempre.
    private static volatile HashSet<string> _knownIds = [];

    // «bar_hp_l_f»: el nombre con el que se sube cada pieza.
    public static string BarName(string kind, string part, string state) => $"bar_{kind}_{part}_{state}";

    // Todos los nombres que el juego sabe usar, en el orden en que conviene subirlos (3 íconos + 6 piezas por tipo de barra).
    public static IReadOnlyList<string> AllNames { get; } =
        [FuegoNuevoName, LevelName, XpName, GoldName, BankName, DustName, StreakName, .. BarKinds.SelectMany(k => Parts.SelectMany(p => States.Select(s => BarName(k, p, s))))];

    // Reemplaza TODA la tabla. Un código que no tiene la forma «<:nombre:id>» o «<a:nombre:id>» no entra (así un dato raro nunca rompe un mensaje entero).
    public static void Set(IEnumerable<(string Name, string Code)> emojis)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, code) in emojis)
        {
            var match = string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code) ? null : EmojiCode.Match(code.Trim());
            if (match is { Success: true })
            {
                map[name.Trim()] = code.Trim();
                ids.Add(match.Groups[1].Value);
            }
        }

        _byName = map;
        _knownIds = ids;
    }

    // ¿Es este código «<:nombre:id>» / «<a:nombre:id>» de un emoji que la aplicación tiene ahora mismo? Es lo que decide si un desplegable puede llevarlo (ver ItemEmojiRegistry.MenuCodeOf).
    public static bool IsKnownEmoji(string? code)
    {
        var match = string.IsNullOrWhiteSpace(code) ? null : EmojiCode.Match(code.Trim());
        return match is { Success: true } && _knownIds.Contains(match.Groups[1].Value);
    }

    // El código del emoji subido con ese nombre, o fallback.
    public static string Icon(string name, string fallback) =>
        _byName.TryGetValue(name, out var code) ? code : fallback;

    // Fuego Nuevo: su ícono propio, o la llama de siempre.
    public static string FuegoNuevo => Icon(FuegoNuevoName, "🔥");

    // XP: su ícono propio, o el 📊 de siempre.
    public static string Xp => Icon(XpName, "📊");

    // Las monedas y demás del perfil: su ícono propio, o el de siempre.
    public static string Gold => Icon(GoldName, "💰");
    public static string Bank => Icon(BankName, "🏦");
    public static string Dust => Icon(DustName, "✨");
    public static string Streak => Icon(StreakName, "🎁");

    // El dragón (v0.30.0): un ícono general («ui_dragon») y uno por tipo («dragon_fuerza», «dragon_defensa», «dragon_oro», «dragon_especial», ver docs/dragon-emojis/README.md). Se suben al portal con ese nombre y las pantallas
    // los usan solas; mientras no estén, valen los emojis de siempre. No están en AllNames a propósito (esa lista son los 25 de la interfaz que cubre v0194test); Icon() busca cualquier nombre que la aplicación tenga.
    public const string DragonName = "ui_dragon";
    public static string Dragon => Icon(DragonName, "🐲");
    public static string DragonKind(string kind, string fallback) => Icon($"dragon_{kind}", fallback);

    // El «LV» azul, o null si todavía no está subido (los textos de siempre dicen «Nivel»).
    public static string? Lv => _byName.TryGetValue(LevelName, out var code) ? code : null;

    // «<LV> **41**», o «Nivel **41**» sin el emoji.
    public static string LevelTag(int level) =>
        Lv is { } lv ? $"{lv} **{level}**" : Loc.F($"Nivel **{level}**");

    // «<FN> **3**», o «🔥 FN **3**» sin el emoji (el ícono propio ya dice «FN» en su dibujo).
    public static string FuegoNuevoTag(int fuegoNuevo) =>
        _byName.TryGetValue(FuegoNuevoName, out var code) ? $"{code} **{fuegoNuevo}**" : $"🔥 FN **{fuegoNuevo}**";

    // Las seis piezas de un tipo de barra, o null si falta alguna (una barra a medias se ve peor que la de texto).
    public static BarPieces? Bar(string kind)
    {
        var map = _byName;
        string? Get(string part, string state) => map.GetValueOrDefault(BarName(kind, part, state));

        string? lf = Get("l", "f"), le = Get("l", "e"), mf = Get("m", "f"), me = Get("m", "e"), rf = Get("r", "f"), re = Get("r", "e");
        return lf is null || le is null || mf is null || me is null || rf is null || re is null
            ? null
            : new BarPieces(lf, le, mf, me, rf, re);
    }
}
