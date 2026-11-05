using System.Text.RegularExpressions;
using Discord;

namespace BotDsRpg.GameData;

// Los banners del perfil (v0.50.0, el dueño tomando la idea de los «backgrounds» de EPIC RPG): una imagen ancha al pie del /profile, solo cosmética. REGLA: cada bendición que tenés desbloquea UN banner y, a mejor nivel
// (tier I a V) de la bendición, mejor banner. Se elige en /config (o «aa banner»); sin elegir no hay banner. El perfil lo adjunta como archivo (attachment://), igual que las fotos de los personajes (NpcImages): nada que alojar.
//
// ARCHIVOS: Assets/banners/banner_{clave de la bendición}_{tier 1..5}.png (ver docs/arte-banners.md). El arte llega de a tandas: de cada bendición se usa el archivo del tier más alto que EXISTA y no pase del nivel que tiene el
// jugador (un nivel III sin arte de tier II ni III muestra el del tier I). Si no existe ninguno, no hay banner y el perfil sale como siempre.
public static class ProfileBanners
{
    public const string Folder = "banners";

    // La opción «ningún banner» tal como viaja en el menú de /config.
    public const string NoneValue = "none";

    private const string AttachmentPrefix = "attachment://";

    // Los nombres que generamos nosotros: banner_<clave>_<tier>.png. Se valida antes de convertir el nombre de un adjunto en una ruta del disco.
    private static readonly Regex FileNamePattern = new(@"^banner_[a-z_]+_[1-5]\.png$", RegexOptions.Compiled);

    public static string FileName(string blessingKey, int tier) => $"banner_{blessingKey}_{tier}.png";

    public static string PathOf(string fileName) => Path.Combine(AppContext.BaseDirectory, "Assets", Folder, fileName);

    // Los banners que hay: uno por bendición (el mismo orden del catálogo).
    public static IReadOnlyList<BlessingDefinition> All => BlessingCatalog.All;

    // Lo que el jugador tiene desbloqueado: sus bendiciones (nivel 1 a 5), en el orden del catálogo.
    public static IReadOnlyList<(BlessingDefinition Blessing, int Level)> Unlocked(IReadOnlyDictionary<string, int> levels) =>
        All.Where(b => levels.TryGetValue(b.Key, out int level) && level > 0)
            .Select(b => (b, Math.Clamp(levels[b.Key], 1, BlessingCatalog.MaxLevel)))
            .ToList();

    // El archivo que se ve para esa bendición a ese nivel: el del tier más alto que exista y no pase del nivel. null si no hay ninguno (o el nivel es 0).
    public static string? FileFor(string? blessingKey, int level, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(blessingKey) || BlessingCatalog.Find(blessingKey) is null)
        {
            return null;
        }

        for (int tier = Math.Min(level, BlessingCatalog.MaxLevel); tier >= 1; tier--)
        {
            string name = FileName(blessingKey, tier);
            if (exists(name))
            {
                return name;
            }
        }

        return null;
    }

    // El mismo cálculo contra el disco: lo que usa el perfil.
    public static string? FileFor(string? blessingKey, int level) => FileFor(blessingKey, level, name => File.Exists(PathOf(name)));

    // Si el embed lleva un banner local (attachment://banner_...), la ruta del archivo que hay que mandar JUNTO con el mensaje; null si no hace falta.
    public static string? AttachmentPathFor(Embed embed)
    {
        string? url = embed.Image?.Url;
        if (url is null || !url.StartsWith(AttachmentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string name = url[AttachmentPrefix.Length..];
        return FileNamePattern.IsMatch(name) ? PathOf(name) : null;
    }

    public static string AttachmentUrl(string fileName) => AttachmentPrefix + fileName;

    // «mano de leñador», «mano_lenador», «Pico Fino»... → la bendición (sin mayúsculas ni tildes). null si no se entiende.
    public static BlessingDefinition? Find(string? text)
    {
        string wanted = (text ?? string.Empty).Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        return All.FirstOrDefault(b => AutocompleteText.SameName(b.Name, wanted) || AutocompleteText.SameName(b.Key, wanted) || AutocompleteText.SameName(b.Key.Replace('_', ' '), wanted));
    }

    // «ningún banner»: las palabras con que se pide sacarlo.
    public static bool IsNoneWord(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() is "none" or "ninguno" or "ninguna" or "nada" or "off" or "sacar" or "quitar" or "no" or "sin banner" or "sin";
}
