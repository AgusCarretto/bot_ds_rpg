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

// v0.51.0: qué líneas de /cd ve cada jugador y en qué orden (el dueño: «un select con todo activado y si alguien quiere no ver algo que lo pueda sacar… y el orden: en el config poner un número, tipo 1, y que quede primero»).
// Lo que se prueba: las reglas puras (las 17 líneas conocidas, lo guardado y cómo se lee, ocultar y mostrar, poner una línea en un puesto contando solo las visibles, aplicar todo a las líneas de /cd, entender cómo se
// nombra cada línea), la pantalla de /cd (por defecto idéntica a la de siempre; con lo guardado, sin las ocultas y en el orden pedido; todo oculto lo dice), lo que se escribe con «aa cd» (orden, ocultar, mostrar, reset y todos
// los errores), la configuración contra la base LOCAL (la columna nueva, que no pisa las otras opciones y que la lectura sigue andando si todavía no existe), /config (el campo y los dos menús, con filas contiguas con o sin
// el banner), los dos idiomas y el registro con Discord.Net de verdad.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U1 = 999000000000098401UL, U2 = 999000000000098402UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [U1, U2];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(CooldownModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var cooldowns = sp.GetRequiredService<ICooldownRepository>();
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
        await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

await Clean();
foreach (var id in all) await users.GetOrCreateUserAsync(id);

string[] defaultKeys = ["hunt", "travel", "chop", "mine", "fish", "buybox", "chop_adv", "mine_adv", "fish_adv", "vote", "pets", "dragon", "expedition", "daily", "boss", "worldboss", "arena"];

Console.WriteLine("== 1. Las reglas puras ==");
Check(CooldownLayout.Known.Select(k => k.Key).SequenceEqual(defaultKeys) && CooldownLayout.Known.Select(k => k.Key).Distinct().Count() == 17 && CooldownLayout.Known.All(k => k.Emoji.Length > 0 && k.Name.Length > 0),
    "hay 17 líneas conocidas, en el orden de siempre (el jefe, el jefe mundial y la Arena abajo de todo), con ícono y nombre");
Check(CooldownLayout.Materialize(null).Select(e => e.Key).SequenceEqual(defaultKeys) && CooldownLayout.Materialize(null).All(e => !e.Hidden) && CooldownLayout.IsDefault(null) && CooldownLayout.IsDefault("") && CooldownLayout.Encode(CooldownLayout.Materialize(null)) is null,
    "sin nada guardado se ven todas y el resultado de no tocar nada es null (la columna vacía)");
string? hiddenChop = CooldownLayout.SetVisible(null, defaultKeys.Where(k => k != "chop"));
Check(hiddenChop!.StartsWith("hunt,travel,-chop,mine,") && CooldownLayout.Materialize(hiddenChop).Single(e => e.Hidden).Key == "chop" && !CooldownLayout.VisibleKeys(hiddenChop).Contains("chop") && CooldownLayout.VisibleKeys(hiddenChop).Count == 16
    && CooldownLayout.SetVisible(hiddenChop, defaultKeys) is null,
    "ocultar una línea la marca con «-» sin mover las demás, y volver a tildarla deja todo como siempre (null)");
string? travelFirst = CooldownLayout.MoveTo(null, "travel", 1);
Check(travelFirst!.StartsWith("travel,hunt,chop,") && CooldownLayout.VisibleKeys(travelFirst)[0] == "travel" && CooldownLayout.MoveTo(null, "hunt", 1) is null && CooldownLayout.MoveTo(null, "arena", 1)!.StartsWith("arena,hunt,")
    && CooldownLayout.VisibleKeys(CooldownLayout.MoveTo(null, "hunt", 3)).Take(3).SequenceEqual(["travel", "chop", "hunt"]) && CooldownLayout.VisibleKeys(CooldownLayout.MoveTo(null, "hunt", 99)).Last() == "hunt",
    "poner un número lo deja en ese puesto (1 = primera; «hunt» al 3 queda tercera; un puesto enorme la deja última) y volver a poner la primera donde ya estaba es no cambiar nada");
string? hideTwo = CooldownLayout.SetVisible(null, defaultKeys.Where(k => k is not ("hunt" or "travel")));
var movedAmongVisible = CooldownLayout.VisibleKeys(CooldownLayout.MoveTo(hideTwo, "arena", 2));
Check(movedAmongVisible[0] == "chop" && movedAmongVisible[1] == "arena" && CooldownLayout.Materialize(CooldownLayout.MoveTo(hideTwo, "hunt", 1)).Single(e => e.Key == "hunt").Hidden,
    "el puesto cuenta SOLO las que ves (con «hunt» y «travel» ocultas, la Arena al 2 queda segunda detrás de «chop»), y una línea oculta que se mueve sigue oculta");
Check(CooldownLayout.Materialize("foo,-bar,hunt,hunt,-fish").Select(e => e.Key).Take(2).SequenceEqual(["hunt", "fish"]) && CooldownLayout.Materialize("foo,-bar,hunt,hunt,-fish").Count == 17 && CooldownLayout.Materialize("foo,-bar,hunt,hunt,-fish").Single(e => e.Key == "fish").Hidden,
    "un texto guardado con basura se lee igual: lo desconocido se ignora, lo repetido cuenta una vez y lo que no aparece se ve al final");
var sample = new List<(string Key, string Text)> { ("hunt", "A"), ("travel", "B"), ("chop", "C"), ("nueva", "U") };
Check(CooldownLayout.Apply(sample, null).SequenceEqual(["A", "B", "C", "U"]) && CooldownLayout.Apply(sample, "-travel,chop,hunt").SequenceEqual(["C", "A", "U"]) && CooldownLayout.Apply(sample, string.Join(',', defaultKeys.Select(k => "-" + k))).SequenceEqual(["U"]),
    "aplicar lo guardado a las líneas de /cd: sin nada salen igual, ocultas fuera, el orden del jugador y una línea de una versión futura siempre se ve (al final)");
Check(CooldownLayout.Find("cazar")?.Key == "hunt" && CooldownLayout.Find("Cacería")?.Key == "hunt" && CooldownLayout.Find("HUNT")?.Key == "hunt" && CooldownLayout.Find("jefe")?.Key == "boss" && CooldownLayout.Find("jefe mundial")?.Key == "worldboss"
    && CooldownLayout.Find("mundial")?.Key == "worldboss" && CooldownLayout.Find("tala avanzada")?.Key == "chop_adv" && CooldownLayout.Find("tala")?.Key == "chop" && CooldownLayout.Find("dragón")?.Key == "dragon" && CooldownLayout.Find("expedicion")?.Key == "expedition"
    && CooldownLayout.Find("Arena")?.Key == "arena" && CooldownLayout.Find("diario")?.Key == "daily" && CooldownLayout.Find("xyz") is null && CooldownLayout.Find("  ") is null && CooldownLayout.Find(null) is null,
    "se entiende cómo se nombra cada línea (clave, nombre, alias; sin tildes ni mayúsculas) y no se confunde «jefe» con «jefe mundial»");

Console.WriteLine("\n== 2. La pantalla de /cd (base LOCAL) ==");
Task<Embed> Cd(ulong id, string? layout = null) => CooldownModule.BuildStatusEmbedAsync(cooldowns, users, id, layout: layout);
var cdDefault = await Cd(U1);
var cdNullLayout = await Cd(U1, null);
string[] defaultLines = cdDefault.Description.Split('\n');
Check(cdDefault.Description == cdNullLayout.Description && defaultLines.Length >= 8 && defaultLines[0].Contains("Cazar") && defaultLines[^1].Contains("Jefe") && cdDefault.Footer?.Text.Contains("/config") == true,
    $"sin nada guardado /cd sale como siempre ({defaultLines.Length} líneas, el jefe abajo) y el pie dice dónde elegir");
var cdCustom = await Cd(U1, "travel,-chop,hunt");
string[] customLines = cdCustom.Description.Split('\n');
Check(customLines[0].Contains("Viajar") && customLines[1].Contains("Cazar") && !cdCustom.Description.Contains("Talar") && customLines.Length == defaultLines.Length - 1,
    "con lo guardado: «Viajar» primera, «Cazar» segunda y «Talar» ya no está");
var cdNone = await Cd(U1, string.Join(',', defaultKeys.Select(k => "-" + k)));
Check(cdNone.Description.Contains("todas las líneas ocultas") && cdNone.Description.Contains("aa cd mostrar todo") && cdNone.Description.Length > 0, "si ocultó todo, la pantalla lo dice (no queda vacía: Discord no acepta una descripción vacía)");

Console.WriteLine("\n== 3. Lo que se escribe con «aa cd» ==");
async Task<string?> Stored(ulong id) => (await settingsService.GetAsync(id)).CdLayout;
Check(await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "", "") is null && await CooldownModule.ExecuteLayoutAsync(settingsService, U1, null, null) is null && await Stored(U1) is null, "sin nada después de «aa cd» solo se muestra el estado (no avisa ni guarda)");
var nOrder = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "viajar 1");
Check(nOrder!.Contains("✅") && nOrder.Contains("Viajar") && nOrder.Contains("puesto **1**") && CooldownLayout.VisibleKeys(await Stored(U1))[0] == "travel", "«aa cd orden viajar 1» la pone primera y lo avisa");
var nMulti = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "jefe mundial 2");
Check(nMulti!.Contains("Jefe mundial") && nMulti.Contains("puesto **2**") && CooldownLayout.VisibleKeys(await Stored(U1)).Take(2).SequenceEqual(["travel", "worldboss"]), "el nombre puede tener espacios («aa cd orden jefe mundial 2»: la última palabra es el puesto)");
var nHide = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "ocultar", "talar");
Check(nHide!.Contains("ya no se ve") && CooldownLayout.Materialize(await Stored(U1)).Single(e => e.Key == "chop").Hidden, "«aa cd ocultar talar» la saca de la lista y lo avisa");
var nOrderHidden = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "talar 1");
Check(nOrderHidden!.Contains("oculta") && nOrderHidden.Contains("aa cd mostrar chop"), "poner un puesto a una línea oculta lo avisa y dice cómo mostrarla");
var nShow = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "mostrar", "talar");
Check(nShow!.Contains("vuelve a verse") && !CooldownLayout.Materialize(await Stored(U1)).Single(e => e.Key == "chop").Hidden, "«aa cd mostrar talar» la vuelve a mostrar");
await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "ocultar", "cazar");
var nShowAll = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "mostrar", "todo");
Check(nShowAll!.Contains("todas") && CooldownLayout.Materialize(await Stored(U1)).All(e => !e.Hidden) && CooldownLayout.VisibleKeys(await Stored(U1))[0] == "chop",
    "«aa cd mostrar todo» muestra todas sin tocar el orden que ya elegiste");
var nReset = await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "reset", "");
Check(nReset!.Contains("vuelve a mostrar todas") && await Stored(U1) is null, "«aa cd reset» deja todo como siempre (y la columna vuelve a null)");
Check((await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "cazar"))!.Contains("Decime qué línea") && (await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "cazar 0"))!.Contains("Decime qué línea")
    && (await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "xyz 1"))!.Contains("Decime qué línea") && (await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "ocultar", "xyz"))!.Contains("No conozco esa línea")
    && (await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "mostrar", ""))!.Contains("No conozco esa línea") && (await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "volar", "cazar"))!.Contains("No entendí") && await Stored(U1) is null,
    "los errores se dicen (falta el puesto, un puesto 0, una línea que no existe, una palabra que no es del comando) y NO guardan nada");
var nFail = await CooldownModule.ExecuteLayoutAsync(new FailingSetService(), U1, "ocultar", "cazar");
Check(nFail!.Contains("No pude guardar"), "si la base no deja guardar, lo dice (no tira)");

Console.WriteLine("\n== 4. La configuración guardada (base LOCAL) ==");
await settingsService.SetShowFightSummaryAsync(U2, false);
await settingsService.SetFullRecipeBookAsync(U2, true);
await settingsService.SetProfileBannerAsync(U2, "brasa_de_color:2");
await settingsService.SetCdLayoutAsync(U2, "travel,-chop");
var cfgNow = await settingsService.GetAsync(U2);
Check(cfgNow.CdLayout == "travel,-chop" && !cfgNow.ShowFightSummary && cfgNow.FullRecipeBook && cfgNow.ProfileBanner == "brasa_de_color:2" && PlayerSettings.Default.CdLayout is null, "guardar las líneas de /cd no pisa las otras opciones (resumen, recetario, banner) y por defecto no hay nada guardado");
await settingsService.SetShowFightSummaryAsync(U2, true);
Check((await settingsService.GetAsync(U2)).CdLayout == "travel,-chop", "y cambiar otra opción después no borra las líneas de /cd");
await db.ExecuteAsync("ALTER TABLE player_settings DROP COLUMN cd_layout");
PlayerSettings? legacy = null; string legacyError = string.Empty;
try { legacy = await settingsRepo.GetAsync(U2); }
catch (Exception ex) { legacyError = ex.Message; }
finally { await db.ExecuteAsync("ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS cd_layout TEXT NULL"); }
Check(legacy is { FullRecipeBook: true, CdLayout: null } && legacy.ProfileBanner == "brasa_de_color:2", $"sin la columna de /cd la lectura cae a la consulta anterior y conserva lo demás (banner incluido) {legacyError}");
await settingsService.SetCdLayoutAsync(U2, null);

Console.WriteLine("\n== 5. /config ==");
(List<SelectMenuComponent> Menus, int Rows) Parts(ConfigScreen s)
{
    var rows = s.Components.Components.OfType<ActionRowComponent>().ToList();
    return (rows.SelectMany(r => r.Components).OfType<SelectMenuComponent>().ToList(), rows.Count);
}
var sc0 = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
var p0 = Parts(sc0);
var show0 = p0.Menus.Single(m => m.CustomId == $"config_cdshow:{U1}");
var first0 = p0.Menus.Single(m => m.CustomId == $"config_cdfirst:{U1}");
Check(sc0.Embed.Fields.Any(f => f.Name == "⏱️ Cooldowns (/cd)" && f.Value.Contains("**todas**") && f.Value.Contains("aa cd orden cazar 3")) && show0.Options.Count == 17 && show0.Options.All(o => o.IsDefault == true) && show0.MinValues == 0 && show0.MaxValues == 17
    && first0.Options.Count == 18 && first0.Options.Last().Value == "reset" && first0.Options.Select(o => o.Value).Take(17).SequenceEqual(defaultKeys),
    "el menú de qué ver trae las 17 líneas todas tildadas (se puede destildar todo) y el de «poner primera» las 17 en orden más «volver al orden de siempre»");
Check(p0.Rows == 3 && sc0.Embed.Length <= 6000 && show0.Options.All(o => o.Label.Length <= 100 && o.Emote is Emoji) && first0.Options.All(o => o.Label.Length <= 100 && (o.Description?.Length ?? 0) <= 100),
    "sin el banner las filas son 3 (botones y los dos menús, contiguas) y todo entra en los límites de Discord");
await Bless(U2, "brasa_de_color", 2);
await settingsService.SetCdLayoutAsync(U1, "travel,-chop,hunt");
var sc1 = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
var p1 = Parts(sc1);
var show1 = p1.Menus.Single(m => m.CustomId == $"config_cdshow:{U1}");
var first1 = p1.Menus.Single(m => m.CustomId == $"config_cdfirst:{U1}");
Check(sc1.Embed.Fields.Any(f => f.Name == "⏱️ Cooldowns (/cd)" && f.Value.Contains("**16 de 17**") && f.Value.Contains("tu orden")) && show1.Options.Single(o => o.Value == "chop").IsDefault == false && show1.Options.Count(o => o.IsDefault == true) == 16
    && show1.Options.Select(o => o.Value).Take(3).SequenceEqual(["travel", "chop", "hunt"]) && !first1.Options.Any(o => o.Value == "chop") && first1.Options.Count == 17 && first1.Options.First().Value == "travel" && first1.Options.First().Description!.Contains("puesto 1"),
    "con lo guardado el campo dice «16 de 17 en tu orden», el menú de qué ver deja «chop» destildada y el de «primera» no la ofrece y arranca por la que va primera");
await settingsService.SetProfileBannerAsync(U2, "brasa_de_color");
var sc2 = await ConfigModule.BuildScreenAsync(settingsService, U2, blessings);
Check(Parts(sc2).Rows == 4 && Parts(sc2).Menus.Count == 3, "con el banner (quien tiene la Brasa de Color) las filas son 4: botones, banner y los dos menús de /cd");
Check(await ConfigModule.TrySetCdLayoutAsync(settingsService, U2, cur => CooldownLayout.SetVisible(cur, defaultKeys.Where(k => k != "fish"))) && CooldownLayout.Materialize(await Stored(U2)).Single(e => e.Key == "fish").Hidden
    && await ConfigModule.TrySetCdLayoutAsync(settingsService, U2, cur => CooldownLayout.MoveTo(cur, "arena", 1)) && CooldownLayout.VisibleKeys(await Stored(U2))[0] == "arena"
    && await ConfigModule.TrySetCdLayoutAsync(settingsService, U2, cur => null) && await Stored(U2) is null && !await ConfigModule.TrySetCdLayoutAsync(new FailingSetService(), U2, cur => "hunt"),
    "lo que hacen los dos menús (tildar y poner primera) se guarda sobre lo que ya había; «volver al orden de siempre» lo deja en null; y si no se puede guardar devuelve false sin tirar");

using (Loc.Use("en"))
{
    var screenEn = await ConfigModule.BuildScreenAsync(settingsService, U1, blessings);
    var cdEn = await Cd(U1, "travel,-chop");
    var cdAllHiddenEn = await Cd(U1, string.Join(',', defaultKeys.Select(k => "-" + k)));
    var notes = new[] { await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "viajar 1"), await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "ocultar", "talar"), await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "mostrar", "todo"),
        await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "reset", ""), await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "orden", "cazar"), await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "ocultar", "xyz"), await CooldownModule.ExecuteLayoutAsync(settingsService, U1, "volar", "x") };
    var menusEn = Parts(screenEn).Menus;
    string leakText = LeakCheck.AllText(screenEn.Embed) + " " + LeakCheck.AllText(cdEn) + " " + cdAllHiddenEn.Description + " " + cdAllHiddenEn.Footer?.Text + " " + string.Join(" ", notes) + " " + string.Join(" ", menusEn.SelectMany(m => m.Options).Select(o => o.Label + " " + o.Description)) + " " + string.Join(" ", menusEn.Select(m => m.Placeholder));
    var leak = LeakCheck.NoSpanish(leakText, ["Brasa de Color"]);
    Check(leakText.Contains("Cooldowns (/cd)") && cdEn.Description.StartsWith("🗺️ **Travel**") && cdEn.Footer!.Value.Text.Contains("Choose which lines") && notes[0]!.Contains("Done") && leak.Ok, $"en inglés todo sale en inglés (las líneas, los menús, los avisos y el pie) ({leak.Found})");
}

Console.WriteLine("\n== 6. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(ConfigModule).Assembly, sp);
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routedShow = interactions.SearchComponentCommand(Click($"config_cdshow:{U1}"));
var routedFirst = interactions.SearchComponentCommand(Click($"config_cdfirst:{U1}"));
Check(routedShow.IsSuccess && routedShow.Command.MethodName == "HandleCdShowAsync" && routedFirst.IsSuccess && routedFirst.Command.MethodName == "HandleCdFirstAsync" && interactions.SearchComponentCommand(Click($"config_banner:{U1}")).Command.MethodName == "HandleBannerSelectAsync",
    "los menús config_cdshow:{dueño} y config_cdfirst:{dueño} llegan a su método (y el del banner sigue llegando al suyo)");
Check(interactions.SlashCommands.Single(c => c.Name == "cd").Description.Length is > 0 and <= 100, "/cd sigue registrado con su descripción dentro de los 100 caracteres");
var texts = new CommandService();
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(texts.Search("cd") is { IsSuccess: true } one && one.Commands.Count == 1 && one.Commands[0].Command.Name == "cd" && new[] { "cd orden cazar 1", "cd ocultar viajar", "cd mostrar todo", "cd reset", "cd orden jefe mundial 2" }.All(f => texts.Search(f) is { IsSuccess: true } r && r.Commands.First().Command.Name == "cd"),
    "«aa cd» sigue siendo un solo comando y entiende «orden», «ocultar», «mostrar» y «reset» con el nombre de la línea (aunque tenga espacios)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(CooldownModule)); ActivatorUtilities.CreateInstance(sp, typeof(ConfigModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built, "los módulos se construyen con el DI real");
Check(FightLock.IsAllowed("cd"), "/cd se puede mirar en plena pelea (solo mira); cambiar el orden con «aa cd orden» también (el comando es el mismo)");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

async Task Bless(ulong id, string key, int level) =>
    await db.ExecuteAsync("INSERT INTO player_blessings (discord_id, blessing_key, level) VALUES (@id, @k, @l) ON CONFLICT (discord_id, blessing_key) DO UPDATE SET level = EXCLUDED.level", new { id = (long)id, k = key, l = level });

// Un servicio que lee bien pero NO puede guardar.
sealed class FailingSetService : IPlayerSettingsService
{
    public Task<PlayerSettings> GetAsync(ulong discordId) => Task.FromResult(PlayerSettings.Default);
    public Task SetShowFightSummaryAsync(ulong discordId, bool value) => throw new InvalidOperationException("simulado");
    public Task SetFullRecipeBookAsync(ulong discordId, bool value) => throw new InvalidOperationException("simulado");
    public Task SetProfileBannerAsync(ulong discordId, string? blessingKey) => throw new InvalidOperationException("simulado");
    public Task SetCdLayoutAsync(ulong discordId, string? layout) => throw new InvalidOperationException("simulado");
}

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
