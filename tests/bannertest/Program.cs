using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// v0.50.0: los BANNERS del perfil (el dueño, tomando los «backgrounds» de EPIC RPG): cada bendición que tenés desbloquea UN banner y, a mejor tier (nivel de la bendición), mejor banner; se elige en /config o con
// «aa banner»; el /profile lo muestra al pie, adjunto como archivo. Lo que se prueba: las reglas puras (el archivo del tier más alto que exista, qué desbloquea cada nivel, cómo se entiende lo que se escribe, que un
// nombre de adjunto raro no abre rutas del disco), que los 65 banners provisorios de Assets/banners (los 5 tiers de cada una de las 13 bendiciones) existen, son PNG de 300x100 y cada tier se ve distinto, la configuración contra la base LOCAL (la columna nueva, que elegir un banner no pisa las
// otras opciones y que la lectura sigue andando si la columna todavía no existe), el perfil (solo con una bendición suya, el de QUIEN se mira, sin banner si no hay servicio) y /config (el campo, el menú y las palabras
// escritas), los dos idiomas y el registro con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U1 = 999000000000098301UL, U2 = 999000000000098302UL, U3 = 999000000000098303UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [U1, U2, U3];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(ConfigModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var itemsRepo = sp.GetRequiredService<IItemRepository>();
var zonesRepo = sp.GetRequiredService<IZoneRepository>();
var buffRepo = sp.GetRequiredService<IBuffRepository>();
var petRepo = sp.GetRequiredService<IPetRepository>();
var dragonRepo = sp.GetRequiredService<IDragonRepository>();
var bonusService = sp.GetRequiredService<IPlayerBonusService>();
var settingsService = sp.GetRequiredService<IPlayerSettingsService>();
var settingsRepo = sp.GetRequiredService<ISettingsRepository>();
var blessings = sp.GetRequiredService<IBlessingRepository>();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

async Task Clean()
{
    foreach (var id in all)
    {
        await db.ExecuteAsync("DELETE FROM player_settings WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM player_blessings WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

async Task Bless(ulong id, string key, int level) =>
    await db.ExecuteAsync("INSERT INTO player_blessings (discord_id, blessing_key, level) VALUES (@id, @k, @l) ON CONFLICT (discord_id, blessing_key) DO UPDATE SET level = EXCLUDED.level", new { id = (long)id, k = key, l = level });

await Clean();
foreach (var id in all) await users.GetOrCreateUserAsync(id);

Console.WriteLine("== 1. Las reglas puras ==");
Check(ProfileBanners.All.Count == BlessingCatalog.All.Count && ProfileBanners.All.Count == 13 && ProfileBanners.All.All(b => System.Text.RegularExpressions.Regex.IsMatch(b.Key, "^[a-z_]+$")),
    "hay un banner por cada una de las 13 bendiciones y sus claves sirven para nombres de archivo");
Check(ProfileBanners.FileName("manada", 3) == "banner_manada_3.png", "los archivos se llaman banner_<clave>_<tier>.png");
var present = new HashSet<string> { "banner_manada_1.png", "banner_manada_3.png" };
Check(ProfileBanners.FileFor("manada", 5, present.Contains) == "banner_manada_3.png" && ProfileBanners.FileFor("manada", 3, present.Contains) == "banner_manada_3.png"
    && ProfileBanners.FileFor("manada", 2, present.Contains) == "banner_manada_1.png" && ProfileBanners.FileFor("manada", 1, present.Contains) == "banner_manada_1.png",
    "se usa el archivo del tier más alto que EXISTA y no pase del nivel (nivel V con arte de I y III → III; nivel II → I)");
Check(ProfileBanners.FileFor("manada", 0, present.Contains) is null && ProfileBanners.FileFor("pico_fino", 4, present.Contains) is null && ProfileBanners.FileFor(null, 3, present.Contains) is null
    && ProfileBanners.FileFor("no_existe", 3, _ => true) is null && ProfileBanners.FileFor("manada", 99, present.Contains) == "banner_manada_3.png",
    "nivel 0, sin ningún archivo, sin clave o una clave que no es de una bendición → sin banner; un nivel absurdo se topa en V");
var unlockedNow = ProfileBanners.Unlocked(new Dictionary<string, int> { ["brasa_de_color"] = 9, ["mano_lenador"] = 3, ["pico_fino"] = 0, ["desconocida"] = 2 });
Check(unlockedNow.Count == 2 && unlockedNow[0].Blessing.Key == "mano_lenador" && unlockedNow[0].Level == 3 && unlockedNow[1].Blessing.Key == "brasa_de_color" && unlockedNow[1].Level == 5,
    "desbloquea una por cada bendición que tenés (nivel 1 a 5, en el orden del catálogo; lo desconocido y el nivel 0 no cuentan)");
Check(ProfileBanners.Find("Mano de Leñador")?.Key == "mano_lenador" && ProfileBanners.Find("mano lenador")?.Key == "mano_lenador" && ProfileBanners.Find("MANO_LENADOR")?.Key == "mano_lenador" && ProfileBanners.Find("pico fino")?.Key == "pico_fino"
    && ProfileBanners.Find("xyz") is null && ProfileBanners.Find("  ") is null && ProfileBanners.IsNoneWord("ninguno") && ProfileBanners.IsNoneWord("none") && !ProfileBanners.IsNoneWord("manada"),
    "se entiende «mano de leñador» con o sin tildes y mayúsculas, la clave, y «ninguno»/«none» para sacarlo");
Embed WithImage(string? url) => url is null ? new EmbedBuilder().WithDescription("x").Build() : new EmbedBuilder().WithDescription("x").WithImageUrl(url).Build();
Check(ProfileBanners.AttachmentPathFor(WithImage("attachment://banner_manada_1.png")) == ProfileBanners.PathOf("banner_manada_1.png")
    && ProfileBanners.AttachmentPathFor(WithImage("attachment://../../appsettings.json")) is null && ProfileBanners.AttachmentPathFor(WithImage("attachment://banner_x_9.png")) is null
    && ProfileBanners.AttachmentPathFor(WithImage("https://example.com/banner_manada_1.png")) is null && ProfileBanners.AttachmentPathFor(WithImage(null)) is null,
    "el archivo a adjuntar sale solo de un nombre banner_<clave>_<1-5>.png: una ruta rara o una URL no abren nada del disco");

Console.WriteLine("\n== 2. Los 65 banners provisorios (5 tiers por bendición) ==");
string BannerFile(string key, int tier) => Path.Combine(TestEnv.RepoRoot, "Assets", "banners", ProfileBanners.FileName(key, tier));
var missingFiles = ProfileBanners.All.SelectMany(b => Enumerable.Range(1, BlessingCatalog.MaxLevel).Select(t => (b.Key, t))).Where(x => !File.Exists(BannerFile(x.Key, x.t))).Select(x => $"{x.Key}_{x.t}").ToList();
Check(missingFiles.Count == 0, $"existe el banner de CADA tier (I a V) de las 13 bendiciones en Assets/banners ({(missingFiles.Count == 0 ? "los 65" : "faltan: " + string.Join(", ", missingFiles))})");
bool allPng = true; string pngWhy = string.Empty;
var distinctPerBlessing = true; string distinctWhy = string.Empty;
foreach (var b in ProfileBanners.All)
{
    var hashes = new HashSet<string>();
    for (int tier = 1; tier <= BlessingCatalog.MaxLevel; tier++)
    {
        string file = BannerFile(b.Key, tier);
        if (!File.Exists(file)) continue;
        var bytes = await File.ReadAllBytesAsync(file);
        hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        bool sig = bytes.Length > 33 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
        int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19], h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        if (!sig || w != 300 || h != 100 || bytes.Length > 60_000) { allPng = false; pngWhy = $"{b.Key}_{tier}: firma {sig}, {w}x{h}, {bytes.Length} bytes"; }
    }

    if (hashes.Count != BlessingCatalog.MaxLevel) { distinctPerBlessing = false; distinctWhy = $"{b.Key}: {hashes.Count} imágenes distintas"; }
}
Check(allPng, $"son PNG de 300x100 (el tamaño decide cuánto ocupan en Discord) y livianos (cada perfil los sube) {pngWhy}");
Check(distinctPerBlessing, $"los 5 tiers de cada bendición son imágenes DISTINTAS: el banner evoluciona con el nivel {distinctWhy}");
Check(File.Exists(ProfileBanners.PathOf(ProfileBanners.FileName("manada", 1))), "y el bot los encuentra donde los busca en ejecución (Assets/banners junto al programa)");

Console.WriteLine("\n== 3. La configuración (base LOCAL) ==");
Check((await settingsService.GetAsync(U1)).ProfileBanner is null && PlayerSettings.Default.ProfileBanner is null, "sin tocar nada no hay banner (lo de siempre)");
await settingsService.SetShowFightSummaryAsync(U1, false);
await settingsService.SetFullRecipeBookAsync(U1, true);
await settingsService.SetProfileBannerAsync(U1, "manada");
var cfgNow = await settingsService.GetAsync(U1);
Check(cfgNow.ProfileBanner == "manada" && !cfgNow.ShowFightSummary && cfgNow.FullRecipeBook, "elegir el banner no pisa las otras opciones (resumen apagado y recetario completo siguen)");
await settingsService.SetShowFightSummaryAsync(U1, true);
Check((await settingsService.GetAsync(U1)).ProfileBanner == "manada", "y cambiar otra opción después no borra el banner");
await settingsService.SetProfileBannerAsync(U1, null);
Check((await settingsService.GetAsync(U1)).ProfileBanner is null, "null saca el banner");
// La lectura tiene que seguir andando si la columna todavía no existe (el binario se publicó antes del script): se lee como antes, sin banner.
await settingsRepo.SetProfileBannerAsync(U1, "manada");
await settingsService.SetShowFightSummaryAsync(U1, false);
await db.ExecuteAsync("ALTER TABLE player_settings DROP COLUMN profile_banner");
PlayerSettings? legacy = null; string legacyError = string.Empty;
try { legacy = await settingsRepo.GetAsync(U1); }
catch (Exception ex) { legacyError = ex.Message; }
finally { await db.ExecuteAsync("ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS profile_banner TEXT NULL"); }
Check(legacy is { ShowFightSummary: false, ProfileBanner: null }, $"sin la columna del banner la lectura cae a la consulta de antes y no deja a nadie sin su configuración {legacyError}");
await settingsService.SetProfileBannerAsync(U1, null); // por el servicio: así su caché queda al día

Console.WriteLine("\n== 4. El perfil ==");
await Bless(U1, "manada", 3); await Bless(U1, "brasa_de_color", 2);
await Bless(U2, "pico_fino", 1);
async Task<Embed> Profile(ulong id, bool withSettings = true) => await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, id, "T", "https://example.com/a.png", petRepo, bonusService, dragonRepo, withSettings ? settingsService : null);
Check((await Profile(U1)).Image is null, "sin banner elegido el perfil sale como siempre (sin imagen)");
await settingsService.SetProfileBannerAsync(U1, "manada");
var withBanner = await Profile(U1);
Check(withBanner.Image?.Url == "attachment://banner_manada_3.png" && ProfileBanners.AttachmentPathFor(withBanner) is { } bannerPath && File.Exists(bannerPath),
    "con una bendición suya elegida el perfil lleva el banner al pie como adjunto (la manada está en nivel III → el banner de tier III)");
Check((await Profile(U1, withSettings: false)).Image is null, "sin el servicio de configuración (los arneses viejos) no hay banner y nada se rompe");
await settingsService.SetProfileBannerAsync(U1, "filo_antiguo");
Check((await Profile(U1)).Image is null, "un banner elegido de una bendición que NO tiene no se ve (solo vale el desbloqueado)");
await settingsService.SetProfileBannerAsync(U2, "pico_fino");
await settingsService.SetProfileBannerAsync(U1, null);
Check((await Profile(U2)).Image?.Url == "attachment://banner_pico_fino_1.png" && (await Profile(U1)).Image is null, "se ve el banner de QUIEN se mira (el perfil de otro jugador lleva el suyo, no el tuyo)");
var plain = await Profile(U3);
Check(plain.Image is null && plain.Fields.Length == withBanner.Fields.Length, "el banner no agrega ni saca campos del perfil: es solo la imagen del pie");

Console.WriteLine("\n== 5. /config y «aa banner» ==");
var screen0 = await ConfigModule.BuildScreenAsync(settingsService, U3, blessings);
Check(screen0.Embed.Fields.Any(f => f.Name == "🖼️ Banner del perfil" && f.Value.Contains("Todavía no tenés ninguna bendición")) && !screen0.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Any(),
    "sin bendiciones el campo explica cómo se desbloquean y no hay menú");
var screenOld = await ConfigModule.BuildScreenAsync(settingsService, U1);
Check(!screenOld.Embed.Fields.Any(f => f.Name.Contains("Banner")), "sin el repositorio de bendiciones la pantalla sale como antes (dos opciones)");
await settingsService.SetProfileBannerAsync(U1, "manada");
var screen1 = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
var menu = screen1.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single();
Check(menu.CustomId == $"config_banner:{U1}" && menu.Options.Select(o => o.Value).SequenceEqual(["none", "manada", "brasa_de_color"]) && menu.Options.Single(o => o.IsDefault == true).Value == "manada"
    && screen1.Embed.Fields.Any(f => f.Name == "🖼️ Banner del perfil" && f.Value.Contains("Manada") && f.Value.Contains("tier III")),
    "el menú lleva «Sin banner» y un banner por bendición suya (en orden de catálogo), marca el elegido y el campo dice cuál es y de qué tier");
Check(menu.Options.All(o => o.Label.Length <= 100 && (o.Description?.Length ?? 0) <= 100) && menu.Options.Skip(1).All(o => o.Emote is Emoji), "las opciones entran en los límites de Discord y llevan el emoji (de texto) de la bendición");
Check(screen1.Components.Components.OfType<ActionRowComponent>().ToList() is { Count: 2 } rows && rows[0].Components.Count == 2, "los dos botones de antes siguen en la primera fila y el menú va en la segunda");

var (nOk, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "brasa de color", blessings);
Check(nOk!.Contains("✅") && nOk.Contains("Brasa de Color") && (await settingsService.GetAsync(U1)).ProfileBanner == "brasa_de_color", "«aa config banner brasa de color» lo pone y lo avisa");
var (nNone, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "bg", "ninguno", blessings);
Check(nNone!.Contains("sin banner") && (await settingsService.GetAsync(U1)).ProfileBanner is null, "«ninguno» lo saca (la opción también se llama bg)");
var (nBad, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "pico fino", blessings);
Check(nBad!.Contains("Todavía no tenés la bendición") && (await settingsService.GetAsync(U1)).ProfileBanner is null, "uno que no tenés lo rechaza y NO guarda nada");
var (nUnknown, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "xyz", blessings);
var (nOpen, openScreen) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "", blessings);
Check(nUnknown!.Contains("No conozco ese banner") && nOpen is null && openScreen.Embed.Fields.Any(f => f.Name.Contains("Banner")), "un nombre que no existe se dice, y «aa banner» solo abre la pantalla");
Check(await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, "filo_antiguo") == BannerSetStatus.NotOwned && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, "manada") == BannerSetStatus.Saved
    && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, null) == BannerSetStatus.Saved, "guardar valida que la bendición sea tuya (NotOwned no escribe) y null siempre se puede");
Check(SettingWords.KeyOf("banner") == SettingKey.Banner && SettingWords.KeyOf("fondo") == SettingKey.Banner && SettingWords.KeyOf("bg") == SettingKey.Banner && SettingWords.KeyOf("resumen") == SettingKey.FightSummary && SettingWords.KeyOf("recetas") == SettingKey.RecipeBook,
    "las palabras de la opción no pisan las de las otras dos");

await settingsService.SetProfileBannerAsync(U1, "manada");
string leakText;
using (Loc.Use("en"))
{
    var screenEn = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
    var (noteEn, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "brasa de color", blessings);
    var (noteEn2, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "none", blessings);
    var menuEn = screenEn.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Single();
    leakText = LeakCheck.AllText(screenEn.Embed) + " " + noteEn + " " + noteEn2 + " " + string.Join(" ", menuEn.Options.Select(o => o.Label + " " + o.Description));
    var leak = LeakCheck.NoSpanish(leakText, BlessingCatalog.All.Select(b => b.Name).Concat(["Manada"]).ToArray());
    Check(leakText.Contains("Profile banner") && menuEn.Options.First().Label == "No banner" && noteEn!.Contains("Done") && noteEn2!.Contains("no banner") && leak.Ok, $"en inglés todo sale en inglés (los nombres de las bendiciones siguen en español) ({leak.Found})");
}

Console.WriteLine("\n== 6. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(ConfigModule).Assembly, sp);
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"config_banner:{U1}"));
Check(routed.IsSuccess && routed.Command.MethodName == "HandleBannerSelectAsync" && routed.Command.Module.Name == "ConfigModule", "el menú config_banner:{dueño} llega a su método");
var routedOld = interactions.SearchComponentCommand(Click($"config_summary:{U1}:off"));
Check(routedOld.IsSuccess && routedOld.Command.MethodName == "HandleSummaryButtonAsync", "y el botón del resumen sigue llegando al suyo");
Check(interactions.SlashCommands.Single(c => c.Name == "config").Description.Length is > 0 and <= 100, "/config sigue registrado con su descripción dentro de los 100 caracteres");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "banner", "bg", "fondo", "background" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "banner") && texts.Search("banner mano de leñador") is { IsSuccess: true },
    "aa banner, bg, fondo y background son el mismo comando y ningún otro lo pisa (acepta el nombre con espacios)");
Check(new[] { "config resumen off", "config recetas completo", "config banner mano de leñador", "set summary on" }.All(f => texts.Search(f) is { IsSuccess: true } r && r.Commands.First().Command.Name == "config"),
    "aa config sigue entendiendo resumen, recetas y banner (con el nombre completo)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(ConfigModule)); ActivatorUtilities.CreateInstance(sp, typeof(GameModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built, "los módulos se construyen con el DI real");
Check(!FightLock.IsAllowed("banner") && !FightLock.IsAllowed("bg"), "en plena pelea «aa banner» queda bloqueado (como todo lo que no es de la pelea)");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

public class FakeProxy : DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var v)) return v;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public static T Make<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)proxy).Values = values;
        return proxy;
    }
}
