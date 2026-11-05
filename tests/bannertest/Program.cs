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

// v0.50.0: el BANNER del perfil (el dueño, tomando los «backgrounds» de EPIC RPG): hay UN SOLO banner en todo el juego, el de la bendición Brasa de Color, y mejora con cada nivel (tier I a V) de esa bendición; solo lo
// activa quien la tiene y cada uno elige qué tier mostrar (uno que ya desbloqueó, o «el mejor» automático). Se elige en /config o con «aa banner»; el /profile lo muestra al pie, adjunto como archivo. Lo que se prueba:
// las reglas puras (lo que se guarda, qué tier se muestra, qué archivo, cómo se entiende lo que se escribe, que un nombre de adjunto raro no abre rutas del disco), que existen los 5 archivos de Assets/banners (PNG de
// 300x100, distintos entre sí) y ninguno más, la configuración contra la base LOCAL (la columna nueva, que elegir no pisa las otras opciones y que la lectura sigue andando si la columna todavía no existe), el perfil
// (solo con la bendición, el tier elegido sin pasar del nivel, el de QUIEN se mira) y /config (el campo, el menú con un tier por cada nivel desbloqueado y las palabras escritas), los dos idiomas y el registro con Discord.Net.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U1 = 999000000000098301UL, U2 = 999000000000098302UL, U3 = 999000000000098303UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [U1, U2, U3];
const string Key = "brasa_de_color";

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
Check(ProfileBanners.BannerKey == Key && BlessingCatalog.ColorKey == Key && ProfileBanners.Blessing.Name == "Brasa de Color", "el único banner del juego es el de la bendición Brasa de Color");
Check(ProfileBanners.FileName(Key, 3) == "banner_brasa_de_color_3.png", "los archivos se llaman banner_brasa_de_color_<tier>.png");
Check(ProfileBanners.Parse(null) == (false, null) && ProfileBanners.Parse(Key) == (true, null) && ProfileBanners.Parse($"{Key}:3") == (true, 3) && ProfileBanners.Parse($"{Key}:9") == (false, null) && ProfileBanners.Parse($"{Key}:0") == (false, null)
    && ProfileBanners.Parse("manada") == (false, null) && ProfileBanners.Parse("manada:2") == (false, null) && ProfileBanners.Parse("") == (false, null),
    "lo guardado se lee como sin banner / el mejor / un tier fijo (1 a 5); un valor viejo, raro o de otra bendición cuenta como sin banner");
Check(ProfileBanners.StoredFor(ProfileBanners.AutoChoice) == Key && ProfileBanners.StoredFor("3") == $"{Key}:3" && ProfileBanners.StoredFor(ProfileBanners.NoneChoice) is null && ProfileBanners.StoredFor("9") is null && ProfileBanners.StoredFor(null) is null && ProfileBanners.StoredFor(2) == $"{Key}:2",
    "las opciones del menú («auto», «1» a «5», «none») se convierten en lo que se guarda");
Check(ProfileBanners.TierToShow(Key, 4) == 4 && ProfileBanners.TierToShow($"{Key}:2", 4) == 2 && ProfileBanners.TierToShow($"{Key}:5", 3) == 3 && ProfileBanners.TierToShow(null, 4) is null && ProfileBanners.TierToShow(Key, 0) is null && ProfileBanners.TierToShow(Key, 99) == 5,
    "el tier que se ve: el mejor es el del nivel (mejora solo), uno fijo no pasa del nivel que tenés, sin banner o sin bendición no se ve nada, y el tope es V");
var present = new HashSet<string> { "banner_brasa_de_color_1.png", "banner_brasa_de_color_3.png" };
Check(ProfileBanners.FileFor(Key, 5, present.Contains) == "banner_brasa_de_color_3.png" && ProfileBanners.FileFor($"{Key}:2", 5, present.Contains) == "banner_brasa_de_color_1.png" && ProfileBanners.FileFor($"{Key}:3", 5, present.Contains) == "banner_brasa_de_color_3.png"
    && ProfileBanners.FileFor(null, 5, present.Contains) is null && ProfileBanners.FileFor("manada", 3, _ => true) is null && ProfileBanners.FileFor(Key, 0, present.Contains) is null && ProfileBanners.FileFor(Key, 3, _ => false) is null,
    "el archivo es el del tier elegido y, si falta (el arte llega de a tandas), el más alto que exista por debajo; otra bendición o sin ningún archivo → nada");
var unlocked = ProfileBanners.Unlocked(new Dictionary<string, int> { [Key] = 9, ["manada"] = 3 });
Check(unlocked.Count == 1 && unlocked[0].Level == 5 && ProfileBanners.Unlocked(new Dictionary<string, int> { ["manada"] = 3 }).Count == 0 && ProfileBanners.Unlocked(new Dictionary<string, int> { [Key] = 0 }).Count == 0,
    "solo desbloquea el banner quien tiene la Brasa de Color (nivel 1 a 5); las demás bendiciones no dan ninguno");
Check(new[] { "3", "tier 3", "t3", "iii", "tier iii", "III", "5", "v" }.Select(ProfileBanners.TierFrom).SequenceEqual([3, 3, 3, 3, 3, 3, 5, 5]) && new[] { "6", "0", "x", "t", "tier", "" }.All(w => ProfileBanners.TierFrom(w) is null),
    "se entiende «3», «tier 3», «t3», «iii»... (1 a 5) y lo que no es un tier no se confunde");
Check(new[] { "on", "auto", "mejor", "Brasa de Color", "brasa_de_color" }.All(ProfileBanners.IsOnWord) && !ProfileBanners.IsOnWord("manada") && new[] { "off", "ninguno", "none", "sacar" }.All(ProfileBanners.IsNoneWord) && !ProfileBanners.IsNoneWord("3"),
    "se entienden las palabras de poner («on», «auto», el nombre de la bendición) y de sacar («off», «ninguno»)");
Embed WithImage(string? url) => url is null ? new EmbedBuilder().WithDescription("x").Build() : new EmbedBuilder().WithDescription("x").WithImageUrl(url).Build();
Check(ProfileBanners.AttachmentPathFor(WithImage("attachment://banner_brasa_de_color_1.png")) == ProfileBanners.PathOf("banner_brasa_de_color_1.png")
    && ProfileBanners.AttachmentPathFor(WithImage("attachment://../../appsettings.json")) is null && ProfileBanners.AttachmentPathFor(WithImage("attachment://banner_x_9.png")) is null
    && ProfileBanners.AttachmentPathFor(WithImage("https://example.com/banner_brasa_de_color_1.png")) is null && ProfileBanners.AttachmentPathFor(WithImage(null)) is null,
    "el archivo a adjuntar sale solo de un nombre banner_<clave>_<1-5>.png: una ruta rara o una URL no abren nada del disco");

Console.WriteLine("\n== 2. Los 5 archivos del banner ==");
string BannerFile(int tier) => Path.Combine(TestEnv.RepoRoot, "Assets", "banners", ProfileBanners.FileName(Key, tier));
var missing = Enumerable.Range(1, 5).Where(t => !File.Exists(BannerFile(t))).ToList();
var extra = Directory.Exists(Path.Combine(TestEnv.RepoRoot, "Assets", "banners")) ? Directory.GetFiles(Path.Combine(TestEnv.RepoRoot, "Assets", "banners"), "banner_*.png").Select(Path.GetFileName).Where(n => !Enumerable.Range(1, 5).Any(t => n == ProfileBanners.FileName(Key, t))).ToList() : [];
Check(missing.Count == 0 && extra.Count == 0, $"están los 5 tiers del banner en Assets/banners y ningún otro ({(missing.Count == 0 ? "los 5" : "faltan: " + string.Join(", ", missing))}{(extra.Count == 0 ? string.Empty : "; sobran: " + string.Join(", ", extra))})");
bool allPng = true; string pngWhy = string.Empty; var hashes = new HashSet<string>();
for (int tier = 1; tier <= 5; tier++)
{
    if (!File.Exists(BannerFile(tier))) continue;
    var bytes = await File.ReadAllBytesAsync(BannerFile(tier));
    hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
    bool sig = bytes.Length > 33 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
    int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19], h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
    if (!sig || w != 300 || h != 100 || bytes.Length > 60_000) { allPng = false; pngWhy = $"tier {tier}: firma {sig}, {w}x{h}, {bytes.Length} bytes"; }
}
Check(allPng, $"son PNG de 300x100 (el tamaño decide cuánto ocupan en Discord) y livianos (cada perfil los sube) {pngWhy}");
Check(hashes.Count == 5, "los 5 tiers son imágenes DISTINTAS: el banner evoluciona con el nivel");
Check(File.Exists(ProfileBanners.PathOf(ProfileBanners.FileName(Key, 1))), "y el bot los encuentra donde los busca en ejecución (Assets/banners junto al programa)");

Console.WriteLine("\n== 3. La configuración (base LOCAL) ==");
Check((await settingsService.GetAsync(U1)).ProfileBanner is null && PlayerSettings.Default.ProfileBanner is null, "sin tocar nada no hay banner (lo de siempre)");
await settingsService.SetShowFightSummaryAsync(U1, false);
await settingsService.SetFullRecipeBookAsync(U1, true);
await settingsService.SetProfileBannerAsync(U1, $"{Key}:3");
var cfgNow = await settingsService.GetAsync(U1);
Check(cfgNow.ProfileBanner == $"{Key}:3" && !cfgNow.ShowFightSummary && cfgNow.FullRecipeBook, "elegir el banner no pisa las otras opciones (resumen apagado y recetario completo siguen)");
await settingsService.SetShowFightSummaryAsync(U1, true);
Check((await settingsService.GetAsync(U1)).ProfileBanner == $"{Key}:3", "y cambiar otra opción después no borra el banner");
await settingsService.SetProfileBannerAsync(U1, null);
Check((await settingsService.GetAsync(U1)).ProfileBanner is null, "null saca el banner");
// La lectura tiene que seguir andando si la columna todavía no existe (el binario se publicó antes del script): se lee como antes, sin banner.
await settingsRepo.SetProfileBannerAsync(U1, Key);
await settingsService.SetShowFightSummaryAsync(U1, false);
await db.ExecuteAsync("ALTER TABLE player_settings DROP COLUMN profile_banner");
PlayerSettings? legacy = null; string legacyError = string.Empty;
try { legacy = await settingsRepo.GetAsync(U1); }
catch (Exception ex) { legacyError = ex.Message; }
finally { await db.ExecuteAsync("ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS profile_banner TEXT NULL"); }
Check(legacy is { ShowFightSummary: false, ProfileBanner: null }, $"sin la columna del banner la lectura cae a la consulta de antes y no deja a nadie sin su configuración {legacyError}");
await settingsService.SetProfileBannerAsync(U1, null); // por el servicio: así su caché queda al día

Console.WriteLine("\n== 4. El perfil ==");
await Bless(U1, Key, 3); await Bless(U1, "manada", 2);
await Bless(U2, Key, 1);
async Task<Embed> Profile(ulong id, bool withSettings = true) => await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, id, "T", "https://example.com/a.png", petRepo, bonusService, dragonRepo, withSettings ? settingsService : null);
Check((await Profile(U1)).Image is null, "sin banner elegido el perfil sale como siempre (sin imagen)");
await settingsService.SetProfileBannerAsync(U1, Key);
var best = await Profile(U1);
Check(best.Image?.Url == "attachment://banner_brasa_de_color_3.png" && ProfileBanners.AttachmentPathFor(best) is { } bannerPath && File.Exists(bannerPath), "con «el mejor» el perfil lleva el banner del tier del nivel de la bendición (III), adjunto al pie");
await settingsService.SetProfileBannerAsync(U1, $"{Key}:2");
Check((await Profile(U1)).Image?.Url == "attachment://banner_brasa_de_color_2.png", "con un tier elegido (II) se ve ese, aunque la bendición esté en III");
await settingsService.SetProfileBannerAsync(U1, $"{Key}:5");
Check((await Profile(U1)).Image?.Url == "attachment://banner_brasa_de_color_3.png", "un tier fijo que está por encima del nivel (V con la bendición en III) no pasa de lo desbloqueado");
await settingsService.SetProfileBannerAsync(U1, "manada");
Check((await Profile(U1)).Image is null, "un valor de otra bendición (de una versión anterior) no muestra nada");
await settingsService.SetProfileBannerAsync(U3, Key);
Check((await Profile(U3)).Image is null, "quien NO tiene la Brasa de Color no ve banner aunque su configuración lo diga");
await settingsService.SetProfileBannerAsync(U1, Key);
await settingsService.SetProfileBannerAsync(U2, Key);
Check((await Profile(U1, withSettings: false)).Image is null, "sin el servicio de configuración (los arneses viejos) no hay banner y nada se rompe");
await settingsService.SetProfileBannerAsync(U1, null);
Check((await Profile(U2)).Image?.Url == "attachment://banner_brasa_de_color_1.png" && (await Profile(U1)).Image is null, "se ve el banner de QUIEN se mira (el perfil de otro lleva el suyo, con SU nivel de bendición)");
var plain = await Profile(U3);
Check(plain.Image is null && plain.Fields.Length == best.Fields.Length, "el banner no agrega ni saca campos del perfil: es solo la imagen del pie");

Console.WriteLine("\n== 5. /config y «aa banner» ==");
SelectMenuComponent? MenuOf(ConfigScreen screen) => screen.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().SingleOrDefault();
var screen0 = await ConfigModule.BuildScreenAsync(settingsService, U3, blessings);
Check(screen0.Embed.Fields.Any(f => f.Name == "🖼️ Banner del perfil" && f.Value.Contains("Se desbloquea con la bendición **Brasa de Color**")) && MenuOf(screen0) is null, "sin la bendición el campo explica cómo se desbloquea y no hay menú");
var screenOld = await ConfigModule.BuildScreenAsync(settingsService, U1);
Check(!screenOld.Embed.Fields.Any(f => f.Name.Contains("Banner")), "sin el repositorio de bendiciones la pantalla sale como antes (dos opciones)");

await settingsService.SetProfileBannerAsync(U1, null);
var sOff = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
var menuOff = MenuOf(sOff)!;
Check(menuOff.CustomId == $"config_banner:{U1}" && menuOff.Options.Select(o => o.Value).SequenceEqual(["none", "auto", "1", "2", "3"]) && menuOff.Options.Single(o => o.IsDefault == true).Value == "none"
    && sOff.Embed.Fields.Any(f => f.Name == "🖼️ Banner del perfil" && f.Value.Contains("Sin banner") && f.Value.Contains("tier III")),
    "con la bendición en III el menú lleva «Sin banner», «El mejor» y los tiers I, II y III (no el IV ni el V) y el campo dice en qué tier está");
await settingsService.SetProfileBannerAsync(U1, Key);
var sAuto = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
Check(MenuOf(sAuto)!.Options.Single(o => o.IsDefault == true).Value == "auto" && sAuto.Embed.Fields.Any(f => f.Value.Contains("El mejor (automático)") && f.Value.Contains("tier III")), "con «el mejor» elegido el menú lo marca y el campo dice que mejora solo");
await settingsService.SetProfileBannerAsync(U1, $"{Key}:2");
var sTier = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
Check(MenuOf(sTier)!.Options.Single(o => o.IsDefault == true).Value == "2" && sTier.Embed.Fields.Any(f => f.Value.Contains("**Tier II**") && f.Value.Contains("hasta el III")), "con el tier II elegido el menú lo marca y el campo lo dice");
Check(MenuOf(sTier)!.Options.All(o => o.Label.Length <= 100 && (o.Description?.Length ?? 0) <= 100) && MenuOf(sTier)!.Options.Skip(1).All(o => o.Emote is Emoji)
    && sTier.Components.Components.OfType<ActionRowComponent>().ToList() is { Count: 2 } rows && rows[0].Components.Count == 2, "las opciones entran en los límites de Discord y llevan emoji (de texto); los dos botones de antes siguen en su fila");

async Task<string?> Stored(ulong id) => (await settingsService.GetAsync(id)).ProfileBanner;
await settingsService.SetProfileBannerAsync(U1, null);
var (nOn, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "on", blessings);
Check(nOn!.Contains("✅") && nOn.Contains("mejor") && await Stored(U1) == Key, "«aa banner on» pone el mejor (el de tu nivel)");
var (nTier, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "2", blessings);
var (nRoman, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "bg", "tier iii", blessings);
Check(nTier!.Contains("tier II") && nRoman!.Contains("tier III") && await Stored(U1) == $"{Key}:3", "«aa banner 2» y «aa bg tier iii» fijan ese tier (también escrito en romano)");
var (nHigh, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "5", blessings);
Check(nHigh!.Contains("Todavía no llegaste a ese tier") && await Stored(U1) == $"{Key}:3", "un tier que todavía no desbloqueaste (V con la bendición en III) se rechaza y NO cambia nada");
var (nName, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "brasa de color", blessings);
Check(nName!.Contains("mejor") && await Stored(U1) == Key, "escribir el nombre de la bendición pone el mejor");
var (nTog, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "toggle", blessings);
var afterToggle1 = await Stored(U1);
await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "toggle", blessings);
Check(nTog!.Contains("sin banner") && afterToggle1 is null && await Stored(U1) == Key, "toggle lo invierte (puesto → sacado → puesto)");
var (nNone, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "ninguno", blessings);
Check(nNone!.Contains("sin banner") && await Stored(U1) is null, "«ninguno» (u «off») lo saca");
var (nBad, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "xyz", blessings);
var (nOpen, openScreen) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "", blessings);
Check(nBad!.Contains("Decime qué querés") && await Stored(U1) is null && nOpen is null && openScreen.Embed.Fields.Any(f => f.Name.Contains("Banner")), "lo que no se entiende se dice sin tocar nada, y «aa banner» solo abre la pantalla");
await settingsService.SetProfileBannerAsync(U3, null);
var (nNo2, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U3, "banner", "on", blessings);
Check(nNo2!.Contains("Todavía no tenés la bendición Brasa de Color") && await Stored(U3) is null, "quien no tiene la Brasa de Color no puede activarlo y no se guarda nada");
Check(await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, "manada") == BannerSetStatus.NotOwned && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, $"{Key}:4") == BannerSetStatus.TierTooHigh
    && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, $"{Key}:3") == BannerSetStatus.Saved && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U1, null) == BannerSetStatus.Saved
    && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U3, Key) == BannerSetStatus.NotOwned && await ConfigModule.TrySetBannerAsync(settingsService, blessings, U3, null) == BannerSetStatus.Saved,
    "guardar valida la bendición y el tier (NotOwned / TierTooHigh no escriben) y null (sacarlo) siempre se puede");
Check(SettingWords.KeyOf("banner") == SettingKey.Banner && SettingWords.KeyOf("fondo") == SettingKey.Banner && SettingWords.KeyOf("bg") == SettingKey.Banner && SettingWords.KeyOf("resumen") == SettingKey.FightSummary && SettingWords.KeyOf("recetas") == SettingKey.RecipeBook,
    "las palabras de la opción no pisan las de las otras dos");

await settingsService.SetProfileBannerAsync(U1, $"{Key}:2");
using (Loc.Use("en"))
{
    var screenEn = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
    var (noteEn, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "on", blessings);
    var (noteEn2, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "3", blessings);
    var (noteEn3, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "off", blessings);
    var (noteEn4, _) = await ConfigModule.ExecuteTypedAsync(settingsService, U1, "banner", "5", blessings);
    var menuEn = MenuOf(screenEn)!;
    string leakText = LeakCheck.AllText(screenEn.Embed) + " " + noteEn + " " + noteEn2 + " " + noteEn3 + " " + noteEn4 + " " + string.Join(" ", menuEn.Options.Select(o => o.Label + " " + o.Description));
    var leak = LeakCheck.NoSpanish(leakText, ["Brasa de Color"]);
    Check(leakText.Contains("Profile banner") && menuEn.Options.First().Label == "No banner" && noteEn!.Contains("Done") && noteEn3!.Contains("no banner") && noteEn4!.Contains("haven't reached") && leak.Ok, $"en inglés todo sale en inglés (el nombre de la bendición sigue en español) ({leak.Found})");
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
Check(new[] { "banner", "bg", "fondo", "background" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Count == 1 && f.Commands[0].Command.Name == "banner") && texts.Search("banner tier 3") is { IsSuccess: true },
    "aa banner, bg, fondo y background son el mismo comando y ningún otro lo pisa");
Check(new[] { "config resumen off", "config recetas completo", "config banner 3", "set summary on" }.All(f => texts.Search(f) is { IsSuccess: true } r && r.Commands.First().Command.Name == "config"),
    "aa config sigue entendiendo resumen, recetas y banner");
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
