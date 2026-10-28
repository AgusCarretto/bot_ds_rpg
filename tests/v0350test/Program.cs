using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Dapper;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.35.0: el inventario se abre por PESTAÑAS (Recolección · Drops de monstruo · Consumibles · Cajas y huevos · Todo). El dueño (2026-10-28): «son demasiados ítems para un inventario».
// Lo que se prueba: qué trae cada pestaña, la pestaña por defecto, el resumen de unidades de la descripción, el desplegable (ids, opciones, la abierta marcada), los casos sin nada / con una sola categoría,
// una pestaña inexistente, que «Todo» es la pantalla completa de siempre, los límites de Discord con un inventario enorme, los dos idiomas sin fugas de español, el ruteo de `inv_tab:*:*` con el
// InteractionService de verdad y la lectura contra la base LOCAL (usuario descartable).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong VIEWER = 999000000000098101UL, TARGET = 999000000000098102UL, U1 = 999000000000098103UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Loc.LoadEmbedded();

InventoryEntry E(string name, string type, int qty, string rarity = "Común", string? emoji = null) =>
    new() { ItemName = name, Type = type, Quantity = qty, Rarity = rarity, Emoji = emoji };

var sample = new List<InventoryEntry>
{
    E("Madera de Pino", "Madera", 5), E("Madera de Roble", "Madera", 3, "Raro"), E("Hierro", "Mineral", 2, "Raro"),
    E("Cuero Curtido de Pradera", "Material", 7), E("Colmillo de Lobisón", "Material", 4),
    E("Mate Amargo", "Consumable", 4), E("Comida para Mascotas", "PetFood", 2), E("Comida de Dragón", "DragonFood", 1),
    E("Cajón de Pino", "Caja", 1), E("Huevo de Pradera", "Huevo", 1),
};

string[] Names(Embed e) => e.Fields.Select(f => f.Name).ToArray();
string Joined(Embed e) => string.Join("\n", e.Fields.Select(f => f.Name + "\n" + f.Value));
SelectMenuOption Opt(SelectMenuComponent m, int i) => m.Options.ElementAt(i);
SelectMenuComponent? Menu(MessageComponent? c) => c?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().SingleOrDefault();

Console.WriteLine("== 1. La pestaña por defecto: Recolección ==");
var (d0, c0) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, null);
Check(d0.Title == "🎒 Inventario de Agus", "el título es el de siempre");
Check(Names(d0).Any(n => n.Contains("Madera")) && Names(d0).Any(n => n.Contains("Mineral")) && !Names(d0).Any(n => n.Contains("Drops") || n.Contains("Consumibles") || n.Contains("Cajas")), "se abre con la madera y los minerales, y nada más");
Check(Joined(d0).Contains("Madera de Pino") && Joined(d0).Contains("Madera de Roble") && Joined(d0).Contains("Hierro") && !Joined(d0).Contains("Cuero Curtido") && !Joined(d0).Contains("Mate Amargo"), "los renglones son los de la recolección");
Check(d0.Fields.Length == 3 && d0.Fields[0].Inline && d0.Fields[1].Inline && d0.Fields[2].Inline && d0.Fields[2].Name == ((char)0x200B).ToString(), "madera y mineral van en dos columnas con el tercer campo vacío que cierra la fila");

Console.WriteLine("\n== 2. El resumen de la descripción ==");
// recolección 5+3+2 = 10, drops 7+4 = 11, comida 4+2+1 = 7, cajas y huevos 1+1 = 2
Check(d0.Description == "**🪵⛏️ 10** · 🩸 11 · 🍖 7 · 📦 2", $"cuenta las unidades de cada categoría y marca la abierta en negrita ({d0.Description})");
var (dDrops, _) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, GameModule.InvDrops);
Check(dDrops.Description == "🪵⛏️ 10 · **🩸 11** · 🍖 7 · 📦 2", $"al cambiar de pestaña la negrita se mueve ({dDrops.Description})");

Console.WriteLine("\n== 3. Cada pestaña trae lo suyo ==");
Check(dDrops.Fields.Length == 1 && !dDrops.Fields[0].Inline && dDrops.Fields[0].Name.Contains("Drops de monstruo") && dDrops.Fields[0].Value.Contains("Cuero Curtido de Pradera") && dDrops.Fields[0].Value.Contains("Colmillo de Lobisón") && !Joined(dDrops).Contains("Madera"),
    "Drops: un campo a ancho completo con los dos drops y nada de madera");
var (dFood, _) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, GameModule.InvFood);
Check(Joined(dFood).Contains("Mate Amargo") && Joined(dFood).Contains("Comida para Mascotas") && Joined(dFood).Contains("Comida de Dragón") && !Joined(dFood).Contains("Cajón") && dFood.Fields[0].Name.Contains("Consumibles"),
    "Consumibles: la comida común, la de mascotas y la de dragón juntas (y las cajas no)");
var (dBox, _) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, GameModule.InvBoxes);
Check(Joined(dBox).Contains("Cajón de Pino") && Joined(dBox).Contains("Huevo de Pradera") && dBox.Fields[0].Name.Contains("Cajas y huevos") && dBox.Footer?.Text == "📦 Abrí tus cajas y huevos con /open", "Cajas y huevos: los dos, con el título y el pie de /open");
var (dBoxOnly, _) = GameModule.BuildInventoryView(sample.Where(e => e.Type != "Huevo").ToList(), "Agus", VIEWER, TARGET, GameModule.InvBoxes);
Check(dBoxOnly.Fields[0].Name.EndsWith("Cajas") && dBoxOnly.Footer?.Text == "📦 Abrí tus cajas con /open", "sin huevos el título y el pie dicen solo «Cajas»");
Check(!(dFood.Footer?.Text ?? "").Contains("/open") && string.IsNullOrEmpty(d0.Footer?.Text), "las otras pestañas no llevan el pie de /open");

Console.WriteLine("\n== 4. «Todo» es la pantalla completa de siempre ==");
var (dAll, _) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, GameModule.InvAll);
var legacy = (Embed)typeof(GameModule).GetMethod("BuildFullInventoryEmbed", AnyS)!.Invoke(null, [sample, "Agus"])!;
Check(dAll.Fields.Select(f => (f.Name, f.Value, f.Inline)).SequenceEqual(legacy.Fields.Select(f => (f.Name, f.Value, f.Inline))) && dAll.Description == "🪵⛏️ 10 · 🩸 11 · 🍖 7 · 📦 2",
    "«Todo» tiene exactamente los campos de la pantalla completa (más la línea del resumen, sin nada en negrita)");

Console.WriteLine("\n== 5. El desplegable ==");
var menu = Menu(c0);
Check(menu is not null && menu.CustomId == $"inv_tab:{VIEWER}:{TARGET}", "el id lleva a quien lo abrió y de quién es el inventario");
Check(menu!.Options.Select(o => o.Value).SequenceEqual(["gather", "drops", "food", "boxes", "all"]), "ofrece las cuatro categorías con algo y «Todo»");
Check(menu.Options.Single(o => o.IsDefault == true).Value == "gather" && Menu(GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, "food").Components)!.Options.Single(o => o.IsDefault == true).Value == "food", "la opción marcada es la pestaña abierta");
Check(menu.Options.All(o => o.Label.Length is > 0 and <= 100 && (o.Description ?? "").Length is > 0 and <= 100) && Opt(menu, 0).Description == "3 ítems · 10 en total" && Opt(menu, 2).Description == "3 ítems · 7 en total", $"etiquetas y descripciones entran en 100 caracteres ({Opt(menu, 0).Description})");
Check(menu.Options.Select(o => o.Label).SequenceEqual(["Recolección", "Drops de monstruo", "Consumibles", "Cajas y huevos", "Todo"]), "los nombres de las pestañas");
Check(menu.Options.All(o => o.Emote is Emoji), "los emojis de las opciones son unicode (un emoji propio rompería el mensaje si no existe)");

Console.WriteLine("\n== 6. Casos borde ==");
var (dNone, cNone) = GameModule.BuildInventoryView([], "Agus", VIEWER, TARGET, null);
Check(cNone is null && dNone.Description == "Todavía no tenés ningún material. ¡Probá /hunt, /chop o /mine!", "sin nada: el aviso de siempre y sin desplegable");
var (dZero, cZero) = GameModule.BuildInventoryView([E("Hierro", "Mineral", 0)], "Agus", VIEWER, TARGET, null);
Check(cZero is null && dZero.Fields.Length == 0, "una fila con cantidad 0 no cuenta como que hay algo");
var onlyDrops = sample.Where(e => e.Type == "Material").ToList();
var (dOne, cOne) = GameModule.BuildInventoryView(onlyDrops, "Agus", VIEWER, TARGET, null);
Check(cOne is null && dOne.Fields[0].Name.Contains("Drops de monstruo") && dOne.Description == "**🩸 11**", "con una sola categoría se abre ESA, sin desplegable (no habría a dónde ir)");
var (dOneAll, _) = GameModule.BuildInventoryView(onlyDrops, "Agus", VIEWER, TARGET, GameModule.InvAll);
Check(dOneAll.Fields[0].Name.Contains("Drops de monstruo") && dOneAll.Description == "**🩸 11**", "pedir «Todo» con una sola categoría muestra esa categoría");
var (dBad, cBad) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, "inventada");
Check(dBad.Description == d0.Description && Menu(cBad)!.Options.Single(o => o.IsDefault == true).Value == "gather", "una pestaña inexistente cae a la primera con algo");
var (dEmptied, _) = GameModule.BuildInventoryView(sample.Where(e => e.Type != "Mineral" && e.Type != "Madera").ToList(), "Agus", VIEWER, TARGET, GameModule.InvGather);
Check(dEmptied.Fields[0].Name.Contains("Drops de monstruo"), "pedir una pestaña que quedó vacía (gastaste todo) cae a la primera con algo");
var (dOnlyMin, _) = GameModule.BuildInventoryView([E("Hierro", "Mineral", 2, "Raro")], "Agus", VIEWER, TARGET, null);
Check(dOnlyMin.Fields.Length == 1 && !dOnlyMin.Fields[0].Inline && dOnlyMin.Fields[0].Name.Contains("Mineral"), "recolección con solo minerales: un campo a ancho completo, sin columna vacía");

Console.WriteLine("\n== 7. Un inventario enorme entra en los límites de Discord ==");
var big = new List<InventoryEntry>();
string longEmoji = "<:" + new string('x', 30) + ":1234567890123456789>";
for (int i = 0; i < 40; i++) big.Add(E($"Cuero Curtido de Pradera número {i}", "Material", 99999, emoji: longEmoji));
for (int i = 0; i < 12; i++) big.Add(E($"Madera de Roble de la zona {i}", "Madera", 99999, emoji: longEmoji));
for (int i = 0; i < 12; i++) big.Add(E($"Mineral de Hierro de la zona {i}", "Mineral", 99999, emoji: longEmoji));
for (int i = 0; i < 12; i++) big.Add(E($"Comida de prueba número {i}", "Consumable", 99999, emoji: longEmoji));
for (int i = 0; i < 12; i++) big.Add(E($"Caja de prueba número {i}", "Caja", 99999, emoji: longEmoji));
bool allFit = true;
foreach (string t in new[] { "gather", "drops", "food", "boxes" })
{
    var (e, c) = GameModule.BuildInventoryView(big, "Agus", VIEWER, TARGET, t);
    allFit &= LeakCheck.FitsDiscord(e) && e.Fields.Length <= 25 && Menu(c)!.Options.Count <= 25;
}

Check(allFit, "cada pestaña con cientos de renglones respeta 1024 por campo y 6000 por embed (las pestañas son más chicas que la pantalla completa)");

Console.WriteLine("\n== 8. Los dos idiomas ==");
var allowed = sample.Select(e => e.ItemName).Concat(["Agus", "Pradera", "Lobisón", "Mascotas", "Dragón", "Cajón"]).ToList();
using (Loc.Use("en"))
{
    var (dEn, cEn) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, null);
    var mEn = Menu(cEn)!;
    string text = LeakCheck.AllText(dEn) + "\n" + mEn.Placeholder + "\n" + string.Join("\n", mEn.Options.Select(o => o.Label + " " + o.Description));
    Check(dEn.Title == "🎒 Agus's inventory" && mEn.Placeholder == "Switch tab" && Opt(mEn, 0).Label == "Gathering" && Opt(mEn, 2).Label == "Consumables" && Opt(mEn, 4).Label == "Everything", $"las pestañas en inglés ({Opt(mEn, 0).Label}/{Opt(mEn, 2).Label}/{Opt(mEn, 4).Label})");
    Check(Opt(mEn, 0).Description == "3 items · 10 in total" && LeakCheck.NoSpanish(text, allowed).Ok && LeakCheck.FitsDiscord(dEn), $"sin fugas de español ({LeakCheck.NoSpanish(text, allowed).Found})");
    Check(dEn.Description == "**🪵⛏️ 10** · 🩸 11 · 🍖 7 · 📦 2", "el resumen no cambia de formato");
    var (dBoxEn, _) = GameModule.BuildInventoryView(sample, "Agus", VIEWER, TARGET, "boxes");
    Check(dBoxEn.Footer?.Text == "📦 Open your boxes and eggs with /open" && dBoxEn.Fields[0].Name.Contains("Boxes and eggs"), "la pestaña de cajas en inglés");
    var (dEmptyEn, _) = GameModule.BuildInventoryView([], "Agus", VIEWER, TARGET, null);
    Check(dEmptyEn.Description!.StartsWith("You don't have any materials yet"), "sin nada, el aviso en inglés");
}

Console.WriteLine("\n== 9. Contra la base local (usuario descartable) ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
async Task Clean()
{
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U1 });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U1 });
}

await Clean();
await new UserRepository(factory).GetOrCreateUserAsync(U1);
// Un ítem de cada tipo que va a la mochila (el primero por nombre; los que el catálogo local no tenga, se saltan).
foreach (string type in new[] { "Madera", "Mineral", "Material", "Consumable", "PetFood", "DragonFood", "Caja", "Huevo" })
{
    await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 3 FROM items WHERE type = @t ORDER BY name LIMIT 1", new { id = (long)U1, t = type });
}

var inventory = new InventoryRepository(factory);
var real = await GameModule.BuildInventoryViewAsync(inventory, U1, "Descartable", U1, null);
var realAll = await GameModule.BuildInventoryViewAsync(inventory, U1, "Descartable", U1, GameModule.InvAll);
var realLegacy = await GameModule.BuildInventoryEmbedAsync(inventory, U1, "Descartable");
Check(real.Embed.Fields.Length >= 2 && Menu(real.Components)!.CustomId == $"inv_tab:{U1}:{U1}" && Menu(real.Components)!.Options.Count == 5, "con el catálogo real: se abre una pestaña y el desplegable tiene las 4 categorías y «Todo»");
Check(realAll.Embed.Fields.Select(f => (f.Name, f.Value)).SequenceEqual(realLegacy.Fields.Select(f => (f.Name, f.Value))), "«Todo» con datos reales es idéntico a BuildInventoryEmbedAsync (la pantalla que usan los demás arneses)");
int foodUnits = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(i.quantity), 0) FROM inventory i JOIN items t ON t.item_id = i.item_id WHERE i.discord_id = @id AND t.type IN ('Consumable','PetFood','DragonFood')", new { id = (long)U1 });
Check(real.Embed.Description!.Contains($"🍖 {foodUnits}") && foodUnits >= 3, $"el resumen cuenta las unidades de la base ({real.Embed.Description})");
await Clean();

Console.WriteLine("\n== 10. El ruteo con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(GameModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(GameModule).Assembly, sp);
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"inv_tab:{VIEWER}:{TARGET}"));
Check(routed.IsSuccess && routed.Command.MethodName == "HandleInventoryTabAsync" && routed.RegexCaptureGroups?.Length == 2, "inv_tab:{quien}:{dueño} llega a HandleInventoryTabAsync con sus dos datos");
var slash = interactions.SlashCommands.Single(c => c.Name == "inventory");
Check(slash.Description.Length is > 0 and <= 100, $"la descripción de /inventory entra en 100 caracteres ({slash.Description.Length})");

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
