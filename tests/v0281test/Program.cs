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

// v0.28.1: el recetario de la herrería vuelve a ser el CLÁSICO por defecto (una zona por página, un bloque por receta con ✅/❌ de lo que tenés) y el recetario completo de la v0.27.0 pasa a ser una opción de /config.
// Lo que se prueba: las palabras de la opción, la columna nueva de player_settings (las dos opciones se guardan sin pisarse), la pantalla /config con su segundo botón en los dos idiomas, 'aa config recetas',
// la página clásica de una zona (la Hoja de Acero Puro SOLO en la Zona 1), la escena de la herrería en los dos modos (lista para forjar + selector de zonas / botón), el espacio entre zonas del recetario completo,
// 'aa forge recipes' según la opción de cada jugador y el ruteo de los botones con el InteractionService de verdad. Contra la base LOCAL (usuarios descartables).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U1 = 999000000000097101UL, U2 = 999000000000097102UL, U3 = 999000000000097103UL, U4 = 999000000000097104UL, U5 = 999000000000097105UL, UP = 999000000000097109UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
string ZW = ((char)0x200B).ToString();

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
async Task Clean()
{
    foreach (var id in new[] { U1, U2, U3, U4, U5, UP })
    {
        await db.ExecuteAsync("DELETE FROM player_settings WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

await Clean();

var userRepo = new UserRepository(factory);
var recipesRepo = new RecipeRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var inventory = new InventoryRepository(factory);
var recipes = await recipesRepo.GetAllAsync();
var zones = await zonesRepo.GetAllAsync();
var zonesByRank = ZoneRanking.OrderByDifficulty(zones);
var lastZone = zonesByRank[^1];

async Task MakePlayer(ulong id, int level, int zoneId, int highestCleared = 0, string cls = "Guerrero", int gold = 500)
{
    await userRepo.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET class = @c, level = @l, current_zone_id = @z, highest_zone_cleared = @h, gold = @g, current_hp = max_hp WHERE discord_id = @id", new { id = (long)id, c = cls, l = level, z = zoneId, h = highestCleared, g = gold });
}

Console.WriteLine("== 1. Las palabras de la opción nueva ==");
Check(new[] { "recetas", "Recetario", "recipes", "RECIPE BOOK", "recipebook", "herreria", "herrería", "forge" }.All(w => SettingWords.KeyOf(w) == SettingKey.RecipeBook) && SettingWords.KeyOf("resumen") == SettingKey.FightSummary,
    "«recetas», «recetario», «recipes», «recipe book»… nombran la opción nueva y «resumen» sigue siendo la de siempre");
Check(new[] { "completo", "Completo", "full", "todas", "all", "on", "si" }.All(w => SettingWords.ParseRecipeBook(w) == (true, false)) && new[] { "clasico", "clásico", "Classic", "simple", "zona", "off", "no" }.All(w => SettingWords.ParseRecipeBook(w) == (false, false))
    && new[] { "toggle", "cambiar" }.All(w => SettingWords.ParseRecipeBook(w) == (null, true)) && SettingWords.ParseRecipeBook("quizás") == (null, false) && SettingWords.ParseRecipeBook(null) == (null, false),
    "completo/full = el recetario nuevo, clásico/classic = el de siempre (on/off también), toggle invierte y lo demás no se entiende");
Check(!PlayerSettings.Default.FullRecipeBook && PlayerSettings.Default.ShowFightSummary, "por defecto el recetario es el CLÁSICO (y el resumen del combate sigue prendido)");

Console.WriteLine("\n== 2. La tabla player_settings con dos opciones ==");
var repo = new SettingsRepository(factory);
Check(await repo.GetAsync(U1) is null, "un jugador sin fila no tiene nada guardado");
await repo.SetFullRecipeBookAsync(U1, true);
Check(await repo.GetAsync(U1) is { FullRecipeBook: true, ShowFightSummary: true }, "guardar el recetario completo crea la fila y deja el resumen como estaba (prendido)");
await repo.SetShowFightSummaryAsync(U1, false);
Check(await repo.GetAsync(U1) is { FullRecipeBook: true, ShowFightSummary: false }, "apagar el resumen NO pisa el recetario que ya había elegido");
await repo.SetFullRecipeBookAsync(U1, false);
Check(await repo.GetAsync(U1) is { FullRecipeBook: false, ShowFightSummary: false } && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_settings WHERE discord_id = @id", new { id = (long)U1 }) == 1, "volver al clásico no pisa el resumen y sigue habiendo una sola fila por jugador");
await Task.WhenAll(Enumerable.Range(0, 20).Select(i => repo.SetFullRecipeBookAsync(U2, i % 2 == 0)));
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_settings WHERE discord_id = @id", new { id = (long)U2 }) == 1, "20 cambios a la vez dejan una sola fila y ninguno tira");

Console.WriteLine("\n== 3. El servicio: caché que no pierde la otra opción ==");
var service = new PlayerSettingsService(repo);
await service.SetFullRecipeBookAsync(U3, true);
Check((await service.GetAsync(U3)) is { FullRecipeBook: true, ShowFightSummary: true }, "elegir el completo queda en el caché con el resumen prendido");
await service.SetShowFightSummaryAsync(U3, false);
Check((await service.GetAsync(U3)) is { FullRecipeBook: true, ShowFightSummary: false } && (await new PlayerSettingsService(repo).GetAsync(U3)) is { FullRecipeBook: true, ShowFightSummary: false },
    "apagar el resumen en el caché conserva el recetario (y un servicio nuevo lee lo mismo de la base)");
var coldService = new PlayerSettingsService(repo);
await coldService.SetFullRecipeBookAsync(U1, true);
Check((await coldService.GetAsync(U1)) is { FullRecipeBook: true, ShowFightSummary: false }, "con el caché frío, guardar una opción tampoco pierde la otra que ya estaba en la base");
bool threw = false; try { await new PlayerSettingsService(new ThrowingRepo()).SetFullRecipeBookAsync(U4, true); } catch (InvalidOperationException) { threw = true; }
Check(threw && !await ConfigModule.TrySetRecipeBookAsync(new PlayerSettingsService(new ThrowingRepo()), U4, true) && !(await new PlayerSettingsService(new ThrowingRepo()).GetAsync(U4)).FullRecipeBook, "guardar tira en el servicio y TrySetRecipeBookAsync lo atrapa (false); leer con la base caída da el clásico");

Console.WriteLine("\n== 4. La pantalla /config con el segundo botón ==");
var screenClassic = await ConfigModule.BuildScreenAsync(service, U4);
await service.SetFullRecipeBookAsync(U4, true);
var screenFull = await ConfigModule.BuildScreenAsync(service, U4);
ConfigScreen screenFullEn; using (Loc.Use("en")) { screenFullEn = await ConfigModule.BuildScreenAsync(service, U4); }
await service.SetFullRecipeBookAsync(U4, false);
ConfigScreen screenClassicEn; using (Loc.Use("en")) { screenClassicEn = await ConfigModule.BuildScreenAsync(service, U4); }
List<ButtonComponent> ButtonsOf(ConfigScreen s) => s.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(screenClassic.Embed.Fields.Length == 2 && screenClassic.Embed.Fields[0].Name == "📋 Resumen del combate" && screenClassic.Embed.Fields[1].Name == "📜 Recetario de la herrería" && screenClassic.Embed.Fields[1].Value.Contains("**Clásico**") && ButtonsOf(screenClassic).Count == 2
    && ButtonsOf(screenClassic)[0].CustomId.StartsWith("config_summary:") && ButtonsOf(screenClassic)[1] is { Label: "Recetario completo" } b1 && b1.CustomId == $"config_recipes:{U4}:full",
    "dos opciones: el resumen y, abajo, el recetario («Clásico» por defecto) con un botón que pasa al completo (el id lleva al dueño y el valor al que pasa)");
Check(screenFull.Embed.Fields[1].Value.Contains("**Completo**") && ButtonsOf(screenFull)[1] is { Label: "Recetario clásico" } b2 && b2.CustomId == $"config_recipes:{U4}:classic", "con el completo elegido: dice «Completo» y el botón vuelve al clásico");
Check(screenFullEn.Embed.Fields[1].Name == "📜 Forge recipe book" && screenFullEn.Embed.Fields[1].Value.Contains("**Full**") && ButtonsOf(screenFullEn)[1].Label == "Classic recipe book" && screenClassicEn.Embed.Fields[1].Value.Contains("**Classic**") && ButtonsOf(screenClassicEn)[1].Label == "Full recipe book",
    $"y en inglés ({screenFullEn.Embed.Fields[1].Name} | {screenFullEn.Embed.Fields[1].Value})");
string screenEnText = LeakCheck.AllText(screenFullEn.Embed) + " " + string.Join(" ", ButtonsOf(screenFullEn).Select(b => b.Label)) + " " + LeakCheck.AllText(screenClassicEn.Embed) + " " + string.Join(" ", ButtonsOf(screenClassicEn).Select(b => b.Label));
Check(LeakCheck.NoSpanish(screenEnText, []).Ok && LeakCheck.FitsDiscord(screenFullEn.Embed) && LeakCheck.FitsDiscord(screenClassic.Embed), $"la pantalla en inglés no deja español colado ({LeakCheck.NoSpanish(screenEnText, []).Found})");

Console.WriteLine("\n== 5. aa config recetas ==");
var (n1, s1) = await ConfigModule.ExecuteTypedAsync(service, U5, "recetas", "completo");
Check((await service.GetAsync(U5)).FullRecipeBook && n1!.Contains("completo") && s1.Embed.Fields[1].Value.Contains("**Completo**"), $"«aa config recetas completo» lo pasa al completo y la pantalla de vuelta ya está al día ({n1})");
var (n2, _) = await ConfigModule.ExecuteTypedAsync(service, U5, "recetario", "clásico");
Check(!(await service.GetAsync(U5)).FullRecipeBook && n2!.Contains("clásico"), $"«aa config recetario clásico» vuelve al de siempre ({n2})");
var (n3, _) = await ConfigModule.ExecuteTypedAsync(service, U5, "recipes", "toggle");
Check((await service.GetAsync(U5)).FullRecipeBook, "«toggle» lo invierte");
var (n4, _) = await ConfigModule.ExecuteTypedAsync(service, U5, "recetas", "quizás");
Check((await service.GetAsync(U5)).FullRecipeBook && n4!.Contains("completo") && n4.Contains("clásico"), $"un valor que no se entiende no cambia nada y dice cuáles hay ({n4})");
await ConfigModule.ExecuteTypedAsync(service, U5, "resumen", "off");
Check(!(await service.GetAsync(U5)).ShowFightSummary && (await service.GetAsync(U5)).FullRecipeBook, "tocar el resumen por escrito no cambia el recetario");
var (n5, _) = await ConfigModule.ExecuteTypedAsync(service, U5, "color", "off");
Check(n5!.Contains("recetas") && n5.Contains("resumen"), $"una opción desconocida lista las dos que existen ({n5})");
string n6; using (Loc.Use("en")) { (n6, _) = await ConfigModule.ExecuteTypedAsync(service, U5, "recipes", "classic"); }
Check(n6.Contains("Done") && n6.Contains("**classic**") && LeakCheck.NoSpanish(n6, []).Ok, $"el aviso en inglés ({n6})");

Console.WriteLine("\n== 6. La página clásica de una zona ==");
var z1 = zonesByRank[0]; var z2 = zonesByRank[1];
var page1 = RecipeCatalog.PageFor(recipes, zones, "Guerrero", z1.ZoneId);
var page2 = RecipeCatalog.PageFor(recipes, zones, "Guerrero", z2.ZoneId);
Check(page1.Zone?.ZoneId == z1.ZoneId && page1.Recipes.Count == 3 && page1.Recipes.Count(r => r.ZoneId is null) == 1 && page2.Zone?.ZoneId == z2.ZoneId && page2.Recipes.Count == 2 && page2.Recipes.All(r => r.ZoneId == z2.ZoneId),
    $"la receta SIN zona (la Hoja de Acero Puro) va en la página de la Zona 1 (3 recetas: arma de clase, Hoja, amuleto) y NO en la de la Zona 2 (2 recetas) ({page1.Recipes.Count}/{page2.Recipes.Count})");
Check(RecipeCatalog.ViewFor(recipes, zones, "Guerrero", z2.ZoneId).Recipes.Any(r => r.ZoneId is null), "la lista para FORJAR sigue ofreciendo la Hoja desde cualquier zona (solo cambia la página de recetas)");
var owned = new Dictionary<string, int> { ["Hierro"] = 5, ["Madera de Pino"] = 40 };
var embed1 = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", z1.ZoneId, owned, 120, 30);
var hoja = embed1.Fields.Single(f => f.Name.Contains("Hoja de Acero Puro"));
Check(embed1.Title == "⚒️ Recetas del Herrero" && embed1.Description.Contains($"Zona {z1.ZoneId}: {z1.Name}") && embed1.Fields.Length == 3 && embed1.Length < 6000
    && hoja.Value.Contains("arma general") && hoja.Value.Contains("+9 ATQ") && hoja.Value.Contains("— 5/2") && hoja.Value.Contains("— 40/10") && hoja.Value.Contains("✅"),
    $"página de la Zona 1: un bloque por receta, la Hoja con «tenés X de Y» y ✅ por ingrediente ({string.Join(" | ", embed1.Fields.Select(f => f.Name))})");
Console.WriteLine("   " + hoja.Value.Replace("\n", "\n   "));
var poor = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", z1.ZoneId, new Dictionary<string, int>(), 3, 30);
Check(poor.Fields.All(f => f.Value.Contains("❌")) && poor.Fields.All(f => f.Value.Contains("(tenés 3)")), "sin materiales ni oro: todo ❌ y el oro dice cuánto tenés");
var embed2 = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", z2.ZoneId, owned, 120, 30);
Check(embed2.Fields.Length == 2 && !embed2.Fields.Any(f => f.Name.Contains("Hoja")) && embed2.Fields.Any(f => f.Value.Contains("arma de tu clase")) && embed2.Fields.Any(f => f.Value.Contains("amuleto")), "página de la Zona 2: el arma de TU clase y el amuleto, sin la Hoja");
var locked = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", lastZone.ZoneId, owned, 120, 1);
Check(locked.Description.Contains("🔒") && locked.Description.Contains($"nivel {lastZone.MinLevel}"), "una zona que todavía no alcanzás lleva el aviso 🔒 con el nivel que pide");
var mage = ForgeModule.RenderZonePageEmbed(recipes, zones, "Hechicero", z1.ZoneId, owned, 120, 30);
Check(!mage.Fields.Any(f => f.Name.Contains("Espada")) && mage.Fields.Count(f => f.Value.Contains("arma de tu clase")) == 1, "cada clase ve el arma de la suya (un Hechicero no ve la espada)");
var gateClosed = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", lastZone.ZoneId, owned, 120, 30, highestZoneCleared: zonesByRank[^2].ZoneId);
var gateOpen = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", lastZone.ZoneId, owned, 120, 30, highestZoneCleared: lastZone.ZoneId);
var gateOpenZ1 = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", z1.ZoneId, owned, 120, 30, highestZoneCleared: lastZone.ZoneId);
Check(!gateClosed.Fields.Any(f => f.Name.Contains("Fogón")) && gateOpen.Fields.Any(f => f.Name.Contains("Fogón")) && gateOpen.Fields.Any(f => f.Name.Contains("Trinche")) && gateOpen.Fields.Any(f => f.Name.Contains("Brasa")) && !gateOpenZ1.Fields.Any(f => f.Name.Contains("Fogón")) && gateOpen.Length < 6000
    && !string.Join(" ", gateOpen.Fields.Select(f => f.Value)).Contains("sinergia"),
    "el equipo del Fogón sale en la página de la ÚLTIMA zona y solo con la puerta abierta (y ya no habla de la sinergia, que no existe)");
var emptyPage = ForgeModule.RenderZonePageEmbed(recipes, zones, "Guerrero", 9999, owned, 120, 30);
Check(emptyPage.Title == "⚒️ Recetas del Herrero" && emptyPage.Length < 6000, "una zona que no existe no rompe (muestra la más cercana que tiene recetas)");

Console.WriteLine("\n== 7. La herrería en los dos modos ==");
await MakePlayer(U1, 30, z1.ZoneId, 0, "Guerrero", 500);
List<SelectMenuComponent> Menus(BlacksmithScene s) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().ToList() ?? [];
List<ButtonComponent> Buttons(BlacksmithScene s) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList() ?? [];
Task<BlacksmithScene> Forge(bool fullBook = false, bool showBook = false, int? zone = null, ulong? id = null) => BlacksmithModule.BuildSceneAsync(userRepo, recipesRepo, zonesRepo, inventory, id ?? U1, showBook, fullBook, zone);
var classic = await Forge();
var cm = Menus(classic);
Check(cm.Count == 2 && cm[0].CustomId == $"blacksmith_pick:{U1}" && cm[1].CustomId == $"blacksmith_zone:{U1}" && Buttons(classic).Count == 0 && cm[1].Options.Count == zonesByRank.Count && classic.Embed.Title == "⚒️ La Herrería",
    $"por defecto (clásico): la lista para forjar y, debajo, el selector de zonas con una opción por zona; sin botón ({string.Join(", ", cm.Select(m => m.CustomId))})");
Check(classic.Embed.Description.Contains("segunda lista") && cm.All(m => m.Options.Count is >= 1 and <= 25) && classic.Components!.Components.OfType<ActionRowComponent>().Count() == 2, "el saludo avisa que la segunda lista muestra las recetas de cada zona y entra en los límites de Discord");
var viewed = await Forge(zone: z2.ZoneId);
var vm = Menus(viewed);
Check(viewed.Embed.Title == "⚒️ Recetas del Herrero" && viewed.Embed.Description.Contains($"Zona {z2.ZoneId}") && vm.Count == 2 && vm[1].Options.Single(o => o.IsDefault == true).Value == z2.ZoneId.ToString() && vm[1].Options.Count(o => o.IsDefault == true) == 1,
    "al elegir una zona la escena muestra su página y deja marcada la que elegiste");
var viewedZ1 = await Forge(zone: z1.ZoneId);
Check(viewedZ1.Embed.Fields.Any(f => f.Name.Contains("Hoja de Acero Puro")) && !viewed.Embed.Fields.Any(f => f.Name.Contains("Hoja de Acero Puro")), "en la escena, la Hoja aparece en la página de la Zona 1 y no en la de la Zona 2");
var full = await Forge(fullBook: true);
var fb = Buttons(full);
Check(Menus(full).Count == 1 && fb.Count == 1 && fb[0].CustomId == $"blacksmith_book:{U1}" && fb[0].Label.Contains("Ver todas las recetas") && !Menus(full).Any(m => m.CustomId.StartsWith("blacksmith_zone")),
    "con el recetario completo elegido: la lista para forjar y el botón «Ver todas las recetas» (sin selector de zonas)");
var book = await Forge(fullBook: true, showBook: true);
Check(book.Embed.Fields.Any(f => f.Name.Contains(" · nivel ")) && Buttons(book).Single().CustomId == $"blacksmith_home:{U1}", "y el botón abre el recetario completo, con un botón para volver");
var ghost = await Forge(id: 999000000000097999UL);
Check(ghost.Components is null && ghost.Embed.Description.Contains("/start"), "sin cuenta: manda a /start en cualquiera de los modos");

Console.WriteLine("\n== 8. El espacio entre zonas del recetario completo ==");
var fullEmbed = ForgeModule.RenderRecipesEmbed(recipes, zones, "Guerrero", null, 30, highestZoneCleared: lastZone.ZoneId);
var fields = fullEmbed.Fields.ToList();
Check(fields.Count == zonesByRank.Count + 2 && fields.Take(fields.Count - 1).All(f => f.Value.EndsWith("\n" + ZW, StringComparison.Ordinal)) && !fields[^1].Value.EndsWith(ZW, StringComparison.Ordinal),
    $"cada parte termina con una línea en blanco (un carácter invisible) menos la última, para separar zona de zona ({fields.Count} partes)");
Check(fields.All(f => f.Value.Length <= 1024) && fullEmbed.Length < 6000 && fields.Take(zonesByRank.Count).All(f => f.Value.TrimEnd('\n', (char)0x200B).Split('\n').Length == 2), "el espacio no mete recetas de más ni se pasa de los límites de Discord");
var oneZone = ForgeModule.RenderRecipesEmbed(recipes, zones, "Guerrero", z2.ZoneId, 30);
Check(oneZone.Fields.Length == 1 && !oneZone.Fields[0].Value.EndsWith(ZW, StringComparison.Ordinal), "una sola zona no lleva espacio de más");

Console.WriteLine("\n== 9. 'aa forge recipes' según lo que eligió cada jugador ==");
await MakePlayer(U2, 30, z2.ZoneId, 0, "Guerrero", 500);
var pU2 = (await userRepo.GetByDiscordIdAsync(U2))!;
var textClassicHere = await ForgeModule.BuildRecipeBookForPlayerAsync(recipesRepo, zonesRepo, inventory, pU2, fullBook: false, zoneId: null);
var textClassicZ1 = await ForgeModule.BuildRecipeBookForPlayerAsync(recipesRepo, zonesRepo, inventory, pU2, fullBook: false, zoneId: z1.ZoneId);
var textFull = await ForgeModule.BuildRecipeBookForPlayerAsync(recipesRepo, zonesRepo, inventory, pU2, fullBook: true, zoneId: null);
var textFullZ3 = await ForgeModule.BuildRecipeBookForPlayerAsync(recipesRepo, zonesRepo, inventory, pU2, fullBook: true, zoneId: zonesByRank[2].ZoneId);
Check(textClassicHere.Description.Contains($"Zona {z2.ZoneId}") && textClassicHere.Fields.Length == 2 && textClassicZ1.Description.Contains($"Zona {z1.ZoneId}") && textClassicZ1.Fields.Any(f => f.Name.Contains("Hoja")),
    "clásico: 'aa forge recipes' sin zona muestra la página de TU zona (la Hoja solo si estás en la 1) y con una zona muestra esa");
Check(textFull.Fields.Length == zonesByRank.Count + 1 && textFullZ3.Fields.Length == 1 && textFullZ3.Fields[0].Name.Contains($"Zona {zonesByRank[2].ZoneId}"), "completo: todas las zonas juntas, o solo la que pidas");
Check(!textClassicHere.Description.Contains("/forge make") && textClassicHere.Description.Contains("aa forge make"), "la frase de cómo forjar es la del comando de texto");

Console.WriteLine("\n== 10. Botones y comandos registrados con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(ConfigModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(ConfigModule).Assembly, sp);
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var rRecipes = interactions.SearchComponentCommand(Click($"config_recipes:{U1}:full"));
var rSummary = interactions.SearchComponentCommand(Click($"config_summary:{U1}:off"));
Check(rRecipes.IsSuccess && rRecipes.Command.MethodName == "HandleRecipesButtonAsync" && rSummary.IsSuccess && rSummary.Command.MethodName == "HandleSummaryButtonAsync", "config_recipes:{dueño}:{valor} llega a su método y config_summary sigue llegando al suyo");
Check(new[] { "blacksmith_pick", "blacksmith_zone", "blacksmith_book", "blacksmith_home" }.All(p => interactions.ComponentCommands.Any(c => c.Name.StartsWith(p))), "la herrería tiene los cuatro handlers: forjar, selector de zonas (clásico), ver todo y volver (completo)");
var slashConfig = interactions.SlashCommands.Single(c => c.Name == "config");
Check(slashConfig.Description.Length is > 0 and <= 100 && slashConfig.Description.Contains("recetario"), $"la descripción de /config nombra el recetario y entra en 100 caracteres ({slashConfig.Description.Length})");
var textCommands = new CommandService();
await textCommands.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "config recetas completo", "set recipes classic", "conf recetario" }.All(f => textCommands.Search(f) is { IsSuccess: true } found && found.Commands.First().Command.Name == "config"), "aa config / set / conf aceptan la opción nueva");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un repositorio que siempre falla (la tabla o la columna no existen).
sealed class ThrowingRepo : ISettingsRepository
{
    public Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken ct = default) => throw new InvalidOperationException("simulado: la tabla no existe");
    public Task SetShowFightSummaryAsync(ulong discordId, bool value, CancellationToken ct = default) => throw new InvalidOperationException("simulado");
    public Task SetFullRecipeBookAsync(ulong discordId, bool value, CancellationToken ct = default) => throw new InvalidOperationException("simulado");
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
