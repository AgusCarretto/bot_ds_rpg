using System.Text.RegularExpressions;
using Discord;

namespace BotDsRpg.GameData;

// El banner del perfil (v0.50.0, el dueño tomando la idea de los «backgrounds» de EPIC RPG): UNA imagen ancha al pie del /profile, solo cosmética. Hay UN SOLO banner en todo el juego, el de la bendición Brasa de
// Color, y MEJORA con cada nivel (tier I a V) de esa bendición: son 5 archivos, uno por tier. Solo lo puede activar quien tiene la bendición; se activa y se saca en /config (o con «aa banner on» / «aa banner off»)
// y sin activar no hay banner. El perfil lo adjunta como archivo (attachment://), igual que las fotos de los personajes (NpcImages): nada que alojar.
//
// ARCHIVOS: Assets/banners/banner_brasa_de_color_{tier 1..5}.png (ver docs/arte-banners.md). Se usa el archivo del tier más alto que EXISTA y no pase del nivel que tiene el jugador (un nivel III sin arte de tier
// III muestra el del tier II o el I). Si no existe ninguno, no hay banner y el perfil sale como siempre. La clave que se guarda en player_settings.profile_banner es la de la bendición; si algún día hubiera más
// banners, esta clase es donde se suman.
public static class ProfileBanners
{
    public const string Folder = "banners";

    // La clave del banner que hay (la de la bendición Brasa de Color): lo que se guarda cuando el jugador lo activa.
    public const string BannerKey = BlessingCatalog.ColorKey;

    private const string AttachmentPrefix = "attachment://";

    // Los nombres que generamos nosotros: banner_<clave>_<tier>.png. Se valida antes de convertir el nombre de un adjunto en una ruta del disco.
    private static readonly Regex FileNamePattern = new(@"^banner_[a-z_]+_[1-5]\.png$", RegexOptions.Compiled);

    public static string FileName(string key, int tier) => $"banner_{key}_{tier}.png";

    public static string PathOf(string fileName) => Path.Combine(AppContext.BaseDirectory, "Assets", Folder, fileName);

    // La bendición dueña del banner.
    public static BlessingDefinition Blessing => BlessingCatalog.Find(BannerKey)!;

    // Lo que el jugador tiene desbloqueado: el banner de la Brasa de Color con el nivel de su bendición (1 a 5); vacío si no la tiene.
    public static IReadOnlyList<(BlessingDefinition Blessing, int Level)> Unlocked(IReadOnlyDictionary<string, int> levels) =>
        levels.TryGetValue(BannerKey, out int level) && level > 0 ? [(Blessing, Math.Clamp(level, 1, BlessingCatalog.MaxLevel))] : [];

    // Lo que se guarda en player_settings.profile_banner: null = sin banner; «brasa_de_color» = el mejor (el de tu nivel, mejora solo al subir la bendición); «brasa_de_color:3» = ese tier fijo.
    public const string NoneChoice = "none";

    public const string AutoChoice = "auto";

    public static string StoredFor(int tier) => $"{BannerKey}:{tier}";

    // La opción del menú de /config («none», «auto» o «1» a «5») → lo que se guarda. null para «none» o cualquier cosa rara.
    public static string? StoredFor(string? choice) =>
        choice == AutoChoice ? BannerKey : int.TryParse(choice, out int tier) && tier is >= 1 and <= BlessingCatalog.MaxLevel ? StoredFor(tier) : null;

    // Lo guardado → (¿hay banner?, el tier fijo que eligió; null = el mejor). Un valor viejo, raro o de otra bendición cuenta como sin banner.
    public static (bool On, int? Tier) Parse(string? stored)
    {
        if (string.Equals(stored, BannerKey, StringComparison.Ordinal))
        {
            return (true, null);
        }

        string prefix = BannerKey + ":";
        if (stored is not null && stored.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(stored[prefix.Length..], out int tier) && tier is >= 1 and <= BlessingCatalog.MaxLevel)
        {
            return (true, tier);
        }

        return (false, null);
    }

    // El tier que se muestra: el fijo que eligió (sin pasar del nivel que tiene la bendición) o, sin elegir, el de su nivel. null si no hay banner o la bendición está en 0.
    public static int? TierToShow(string? stored, int level)
    {
        var (on, tier) = Parse(stored);
        if (!on || level <= 0)
        {
            return null;
        }

        int cap = Math.Min(level, BlessingCatalog.MaxLevel);
        return tier is int fixedTier ? Math.Min(fixedTier, cap) : cap;
    }

    // El archivo que se ve: el del tier que le toca y, si ese archivo no existe (el arte llega de a tandas), el del tier más alto que exista por debajo. null si no hay banner o no hay ningún archivo.
    public static string? FileFor(string? stored, int level, Func<string, bool> exists)
    {
        if (TierToShow(stored, level) is not int shown)
        {
            return null;
        }

        for (int tier = shown; tier >= 1; tier--)
        {
            string name = FileName(BannerKey, tier);
            if (exists(name))
            {
                return name;
            }
        }

        return null;
    }

    // El mismo cálculo contra el disco: lo que usa el perfil.
    public static string? FileFor(string? stored, int level) => FileFor(stored, level, name => File.Exists(PathOf(name)));

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

    // «sacar el banner»: las palabras con que se pide.
    public static bool IsNoneWord(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() is "none" or "ninguno" or "ninguna" or "nada" or "off" or "sacar" or "quitar" or "no" or "sin banner" or "sin";

    // «poner el banner» (el mejor, el de tu nivel): las palabras con que se pide, incluido el nombre de la bendición.
    public static bool IsOnWord(string? text)
    {
        string word = (text ?? string.Empty).Trim();
        return word.ToLowerInvariant() is "on" or "si" or "sí" or "yes" or "activar" or "poner" or "activado" or "banner" or "auto" or "automatico" or "automático" or "mejor" or "best"
            || AutocompleteText.SameName(word, Blessing.Name) || AutocompleteText.SameName(word, BannerKey) || AutocompleteText.SameName(word, BannerKey.Replace('_', ' '));
    }

    // El tier que pidió escribiendo: «3», «tier 3», «t3», «iii», «tier iii»... (1 a 5); null si no es un tier.
    public static int? TierFrom(string? text)
    {
        string word = (text ?? string.Empty).Trim().ToLowerInvariant();
        foreach (string prefix in new[] { "tier", "t" })
        {
            if (word.StartsWith(prefix, StringComparison.Ordinal) && word.Length > prefix.Length)
            {
                word = word[prefix.Length..].Trim();
                break;
            }
        }

        if (int.TryParse(word, out int number))
        {
            return number is >= 1 and <= BlessingCatalog.MaxLevel ? number : null;
        }

        for (int tier = 1; tier <= BlessingCatalog.MaxLevel; tier++)
        {
            if (word == BlessingCatalog.RomanLevel(tier).ToLowerInvariant())
            {
                return tier;
            }
        }

        return null;
    }
}
