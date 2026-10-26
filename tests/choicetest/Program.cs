using System.Collections;
using System.Reflection;

// Verifica las listas desplegables de /shop buy y /equip (ItemChoices) contra el .dll real, sin Discord.
string dll = args.Length > 0 && File.Exists(args[0]) ? args[0] : Path.Combine(AppContext.BaseDirectory, "bot_ds_rpg.dll");
var asm = Assembly.LoadFrom(dll);
Type T(string n) => asm.GetType(n) ?? throw new Exception("no existe " + n);
var itemType = T("BotDsRpg.Models.Item");
var ownedType = T("BotDsRpg.Models.OwnedItem");
var userType = T("BotDsRpg.Models.User");
var choices = T("ItemChoices");

void Set(object o, string p, object? v) => o.GetType().GetProperty(p)!.SetValue(o, v);

object Item(int id, string name, string type, int stat, int buy = 0, string? family = null, string? classReq = null)
{
    var i = Activator.CreateInstance(itemType)!;
    Set(i, "ItemId", id); Set(i, "Name", name); Set(i, "Type", type); Set(i, "Rarity", "Común");
    Set(i, "StatValue", stat); Set(i, "BuyPrice", buy); Set(i, "WeaponFamily", family); Set(i, "ClassRequirement", classReq);
    return i;
}

object Owned(object item) => Activator.CreateInstance(ownedType, item, 1)!;

object Player(string cls, int gold = 100, int? weaponId = null, int? amuletId = null)
{
    var u = Activator.CreateInstance(userType)!;
    Set(u, "Class", cls); Set(u, "Gold", gold); Set(u, "WeaponId", weaponId); Set(u, "AmuletId", amuletId);
    return u;
}

object ListOf(Type t, params object[] items)
{
    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(t))!;
    foreach (var i in items) list.Add(i);
    return list;
}

List<(string Name, string Value)> Run(string method, params object?[] callArgs)
{
    // Los parámetros opcionales que se agregan al final (p. ej. la zona desbloqueada de ForBuy) se completan con null: por reflexión no hay valores por defecto.
    var target = choices.GetMethod(method)!;
    int missing = target.GetParameters().Length - callArgs.Length;
    if (missing > 0) callArgs = callArgs.Concat(new object?[missing]).ToArray();
    var result = (IEnumerable)target.Invoke(null, callArgs)!;
    return result.Cast<object>().Select(r => ((string)r.GetType().GetProperty("Name")!.GetValue(r)!, (string)r.GetType().GetProperty("Value")!.GetValue(r)!)).ToList();
}

int fails = 0;
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALLA")} {name} — {detail}");
    if (!ok) fails++;
}

string Show(List<(string Name, string Value)> l) => l.Count == 0 ? "(vacía)" : string.Join(" | ", l.Select(x => x.Name));

// ---------- /shop buy ----------
var shop = ListOf(itemType,
    Item(1, "Vacío al Disco", "Consumable", 80, 18),
    Item(2, "Pan Casero", "Consumable", 20, 4),
    Item(3, "Mate Amargo", "Consumable", 15, 3),
    Item(4, "Hoja de Acero Puro", "Weapon", 15, 195, "Espadas"),   // no es consumible: NO debe aparecer
    Item(5, "Asado de Tira", "Consumable", 100, 22));

var all = Run("ForBuy", shop, 100, "", null);
Check("buy: sin texto lista solo consumibles, del más barato al más caro",
    all.Select(x => x.Value).SequenceEqual(new[] { "Mate Amargo", "Pan Casero", "Asado de Tira", "Vacío al Disco" }.OrderBy(n => n switch { "Mate Amargo" => 3, "Pan Casero" => 4, "Asado de Tira" => 22, _ => 18 })), Show(all));
Check("buy: el valor de cada opción es el nombre exacto del ítem", all.All(x => new[] { "Vacío al Disco", "Pan Casero", "Mate Amargo", "Asado de Tira" }.Contains(x.Value)), string.Join(",", all.Select(x => x.Value)));
Check("buy: la etiqueta muestra cura y precio", all.Any(x => x.Name == "Pan Casero — cura 20 HP · 4 oro"), all.First(x => x.Value == "Pan Casero").Name);

var typed = Run("ForBuy", shop, 100, "pan", null);
Check("buy: filtra por texto", typed.Count == 1 && typed[0].Value == "Pan Casero", Show(typed));
var accents = Run("ForBuy", shop, 100, "VACIO", null);
Check("buy: no distingue mayúsculas ni tildes ('VACIO' encuentra 'Vacío')", accents.Count == 1 && accents[0].Value == "Vacío al Disco", Show(accents));
var none = Run("ForBuy", shop, 100, "zzz", null);
Check("buy: sin coincidencias devuelve lista vacía", none.Count == 0, Show(none));
var poor = Run("ForBuy", shop, 10, "", null);
Check("buy: marca lo que no alcanza a pagar (10 de oro)", poor.Single(x => x.Value == "Vacío al Disco").Name.Contains("te falta oro") && !poor.Single(x => x.Value == "Mate Amargo").Name.Contains("te falta oro"), Show(poor));
var unknown = Run("ForBuy", shop, null, "", null);
Check("buy: sin jugador no marca nada", unknown.Count == 4 && unknown.All(x => !x.Name.Contains("te falta oro")), Show(unknown));

var many = ListOf(itemType, Enumerable.Range(1, 40).Select(i => Item(i, $"Comida {i:00}", "Consumable", 10, i)).ToArray());
Check("buy: tope de 25 opciones (límite de Discord)", Run("ForBuy", many, 1000, "", null).Count == 25, "40 ítems -> " + Run("ForBuy", many, 1000, "", null).Count);
var longName = ListOf(itemType, Item(1, new string('X', 95), "Consumable", 10, 1));
var longLabel = Run("ForBuy", longName, 1000, "", null);
Check("buy: la etiqueta nunca pasa de 100 caracteres (nombre de 95 + precio)", longLabel.Count == 1 && longLabel[0].Name.Length <= 100 && longLabel[0].Value.Length == 95, "largo " + longLabel[0].Name.Length);
var absurdName = ListOf(itemType, Item(1, new string('X', 150), "Consumable", 10, 1), Item(2, "Pan Casero", "Consumable", 20, 4));
var absurd = Run("ForBuy", absurdName, 1000, "", null);
Check("buy: un nombre de más de 100 caracteres se omite en vez de romper la lista", absurd.Count == 1 && absurd[0].Value == "Pan Casero", Show(absurd));

// ---------- /shop sell con lo equipado ----------
var invType = T("BotDsRpg.Models.InventoryEntry");
var arma = Item(10, "Hoja de Acero Puro", "Weapon", 15, 195, "Espadas"); Set(arma, "SellPrice", 90);
var amuletoPuesto = Item(14, "Ojo de Jabalí", "Amulet", 20, 105); Set(amuletoPuesto, "SellPrice", 120);
var sinPrecio = Item(15, "Reliquia", "Weapon", 5, 0); Set(sinPrecio, "SellPrice", 0);
var sinMochila = ListOf(invType);
var equipado = Run("ForSell", sinMochila, "", ListOf(itemType, amuletoPuesto, arma, sinPrecio));
Check("sell: lo equipado se ofrece con aviso, el arma antes que el amuleto", equipado.Count == 2 && equipado[0].Value == "Hoja de Acero Puro" && equipado[1].Value == "Ojo de Jabalí" && equipado[0].Name.Contains("equipada") && equipado[1].Name.Contains("equipado") && equipado[0].Name.Contains("90 oro"), Show(equipado));
Check("sell: sin precio de venta no se ofrece", !equipado.Any(x => x.Value == "Reliquia"), Show(equipado));
Check("sell: filtra lo equipado por texto (sin tildes)", Run("ForSell", sinMochila, "ojo", ListOf(itemType, amuletoPuesto, arma)).Single().Value == "Ojo de Jabalí", "ojo");
Check("sell: sin lo equipado (llamada vieja) sigue andando", Run("ForSell", sinMochila, "", null).Count == 0, "vacío");

// ---------- /zona ----------
var zoneType = T("BotDsRpg.Models.Zone");
var zoneChoices = T("ZoneChoices");
var zoneRanking = T("BotDsRpg.GameData.ZoneRanking");

object Zone(int id, string name, int minLevel, string emoji)
{
    var z = Activator.CreateInstance(zoneType)!;
    Set(z, "ZoneId", id); Set(z, "Name", name); Set(z, "MinLevel", minLevel); Set(z, "Emoji", emoji);
    return z;
}

object PlayerZ(int level, int currentZone, int highest)
{
    var u = Activator.CreateInstance(userType)!;
    Set(u, "Class", "Guerrero"); Set(u, "Level", level); Set(u, "CurrentZoneId", currentZone); Set(u, "HighestZoneCleared", highest);
    return u;
}

// IDs no consecutivos a propósito (como la base real: 1, 4, 5, 6) y desordenados en la lista.
var zones = ListOf(zoneType, Zone(5, "Minas del Yunque", 10, "⛏️"), Zone(1, "Praderas del Mate", 1, "🌾"), Zone(6, "Cordillera del Fuego", 15, "🌋"), Zone(4, "Bosque de Cenizas", 5, "🌲"));

List<(string Name, string Value)> RunZones(object player, Dictionary<int, string> bosses, string typed)
{
    var result = (IEnumerable)zoneChoices.GetMethod("For")!.Invoke(null, [zones, player, bosses, typed, null])!;
    return result.Cast<object>().Select(r => ((string)r.GetType().GetProperty("Name")!.GetValue(r)!, r.GetType().GetProperty("Value")!.GetValue(r)!.ToString()!)).ToList();
}

var bossNames = new Dictionary<int, string> { [1] = "Rey Jabalí", [4] = "Lobisón Alfa", [5] = "Capataz de Hierro" };
var z1 = RunZones(PlayerZ(6, 1, 0), bossNames, "");
Check("zona: van en orden de dificultad (por nivel, no por ID)", z1.Select(x => x.Value).SequenceEqual(new[] { "1", "4", "5", "6" }), Show(z1));
Check("zona: la actual dice 'estás acá'", z1[0].Name.Contains("estás acá") && z1[0].Name.Contains("Praderas del Mate"), z1[0].Name);
Check("zona: nivel suficiente pero jefe sin derrotar -> 'derrotá a <jefe>'", z1[1].Name.Contains("🔒 derrotá a Rey Jabalí"), z1[1].Name);
Check("zona: sin nivel -> 'te falta nivel'", z1[2].Name.Contains("te falta nivel") && z1[3].Name.Contains("te falta nivel"), z1[2].Name);
var z2 = RunZones(PlayerZ(6, 1, 1), bossNames, "");
Check("zona: con el jefe de zona 1 derrotado, la zona 4 queda libre (sin candado)", !z2[1].Name.Contains("🔒") && !z2[1].Name.Contains("estás acá"), z2[1].Name);
var z3 = RunZones(PlayerZ(6, 1, 0), new Dictionary<int, string>(), "");
Check("zona: si la zona anterior no tiene jefe cargado no se bloquea (igual que /zona)", !z3[1].Name.Contains("🔒"), z3[1].Name);
var z4 = RunZones(PlayerZ(16, 1, 5), bossNames, "");
Check("zona: el valor de cada opción es el ID numérico de la zona", z4.Select(x => x.Value).SequenceEqual(new[] { "1", "4", "5", "6" }), string.Join(",", z4.Select(x => x.Value)));
var zb = RunZones(PlayerZ(6, 1, 0), bossNames, "bosque");
Check("zona: filtra por nombre", zb.Count == 1 && zb[0].Value == "4", Show(zb));
var zid = RunZones(PlayerZ(6, 1, 0), bossNames, "4");
Check("zona: filtra por ID", zid.Count == 1 && zid[0].Value == "4", Show(zid));
var zacc = RunZones(PlayerZ(6, 1, 0), bossNames, "CORDILLERA");
Check("zona: no distingue mayúsculas", zacc.Count == 1 && zacc[0].Value == "6", Show(zacc));

// La regla del jefe-guardián extraída (ZoneRanking.PendingGatekeeperZone) tiene que dar EXACTAMENTE lo que
// daba la lógica original inline de ZoneModule.ExecuteTravelAsync, para toda combinación.
var orderedZones = (IReadOnlyList<object>)((IEnumerable)zoneRanking.GetMethod("OrderByDifficulty")!.Invoke(null, [zones])!).Cast<object>().ToList();
var orderedTyped = ListOf(zoneType, orderedZones.ToArray());
int Rank(int zoneId) => orderedZones.ToList().FindIndex(z => (int)z.GetType().GetProperty("ZoneId")!.GetValue(z)! == zoneId) + 1;
int mismatches = 0, combos = 0;
foreach (var target in new[] { 1, 4, 5, 6 })
    foreach (var highest in new[] { 0, 1, 4, 5, 6, 99 })
    {
        combos++;
        int targetRank = Rank(target);
        int? expected = null;   // id de la zona guardiana según la lógica ORIGINAL
        if (targetRank > 1)
        {
            int clearedRank = highest == 0 ? 0 : Rank(highest);
            if (targetRank > clearedRank + 1) expected = (int)orderedZones[targetRank - 2].GetType().GetProperty("ZoneId")!.GetValue(orderedZones[targetRank - 2])!;
        }

        var actualObj = zoneRanking.GetMethod("PendingGatekeeperZone")!.Invoke(null, [orderedTyped, target, highest]);
        int? actual = actualObj is null ? null : (int)actualObj.GetType().GetProperty("ZoneId")!.GetValue(actualObj)!;
        if (expected != actual) { mismatches++; Console.WriteLine($"   diferencia: destino {target}, jefe hasta {highest}: original {expected}, nuevo {actual}"); }
    }

Check("zona: PendingGatekeeperZone equivale a la regla original inline", mismatches == 0, $"{combos} combinaciones, {mismatches} diferencias");

// ---------- forja: molde por zona (4 afinidad + 2 generales + 2 amuletos; cada jugador ve 5 de SU zona) ----------
var recipeType = T("BotDsRpg.Models.RecipeDetails");
var ingType = T("BotDsRpg.Models.RecipeIngredientDetails");
var forgeChoices = T("ForgeChoices");
var recipeCatalog = T("BotDsRpg.GameData.RecipeCatalog");
var forgeModule = T("ForgeModule");
var recipeRepoType = T("BotDsRpg.Repositories.IRecipeRepository");
var zoneRepoType = T("BotDsRpg.Repositories.IZoneRepository");

object Ing(string name, int qty) => Activator.CreateInstance(ingType, 0, name, qty, null)!;
object Rcp(int? zoneId, bool affinity, object item, int gold, params object[] ings) =>
    Activator.CreateInstance(recipeType, 0, item, gold, ListOf(ingType, ings), zoneId, affinity)!;
object PlayerAt(string cls, int gold, int zoneId)
{
    var u = Activator.CreateInstance(userType)!;
    Set(u, "Class", cls); Set(u, "Gold", gold); Set(u, "CurrentZoneId", zoneId);
    return u;
}

string RName(object recipe) { var item = recipe.GetType().GetProperty("ResultItem")!.GetValue(recipe)!; return (string)item.GetType().GetProperty("Name")!.GetValue(item)!; }

// Zona 1 (id 1) tal como la deja seed_recipes.sql.
object[] Zone1Template() =>
[
    Rcp(1, true,  Item(41, "Espada de Madera", "Weapon", 5, family: "Espadas"), 40, Ing("Madera de Pino", 3), Ing("Hierro", 1)),
    Rcp(1, true,  Item(42, "Daga Oxidada", "Weapon", 5, family: "Dagas"), 40, Ing("Hierro", 1), Ing("Piedra", 2), Ing("Colmillo de Cimarrón", 1)),
    Rcp(1, true,  Item(43, "Arco Corto de Sauce", "Weapon", 5, family: "Arcos"), 40, Ing("Madera de Pino", 3), Ing("Tela Rasgada", 1)),
    Rcp(1, true,  Item(44, "Grimorio Desgastado", "Weapon", 5, family: "Grimorios"), 40, Ing("Madera de Pino", 2), Ing("Pluma de Ñandú", 1)),
    Rcp(1, false, Item(45, "Hoja de Acero Puro", "Weapon", 15, family: "Espadas"), 180, Ing("Hierro", 3), Ing("Madera de Pino", 2), Ing("Colmillo de Jabalí", 1)),
    Rcp(1, false, Item(46, "Hacha de Hierro MK3", "Weapon", 35, family: "Espadas"), 150, Ing("Hierro", 5), Ing("Cuero Grueso", 3)),
    Rcp(1, false, Item(47, "Amuleto del Levantador", "Amulet", 10), 150, Ing("Piedra", 5), Ing("Colmillo de Jabalí", 1)),
    Rcp(1, false, Item(48, "Hombreras de Cuero Grueso", "Amulet", 20), 200, Ing("Cuero Grueso", 5), Ing("Piedra Caliente", 3)),
];

// (id de zona, nombre) -> lista de recetas visibles según RecipeCatalog.ViewFor; devuelve (nombres, zonaId?, esRespaldo)
(List<string> Names, int? ZoneId, bool Fallback) View(IEnumerable<object> recipes, string cls, int currentZone)
{
    var view = recipeCatalog.GetMethod("ViewFor")!.Invoke(null, [ListOf(recipeType, recipes.ToArray()), zones, cls, currentZone, false])!;
    var zone = view.GetType().GetProperty("Zone")!.GetValue(view);
    int? zid = zone is null ? null : (int)zone.GetType().GetProperty("ZoneId")!.GetValue(zone)!;
    bool fallback = (bool)view.GetType().GetProperty("IsFallback")!.GetValue(view)!;
    var names = ((IEnumerable)view.GetType().GetProperty("Recipes")!.GetValue(view)!).Cast<object>().Select(RName).ToList();
    return (names, zid, fallback);
}

string[] Generales = ["Hoja de Acero Puro", "Hacha de Hierro MK3"];
string[] Amuletos = ["Amuleto del Levantador", "Hombreras de Cuero Grueso"];

foreach (var (cls, afinidad) in new[] { ("Guerrero", "Espada de Madera"), ("Ninja", "Daga Oxidada"), ("Arquero", "Arco Corto de Sauce"), ("Hechicero", "Grimorio Desgastado") })
{
    var (names, zidV, fb) = View(Zone1Template(), cls, 1);
    var expected = new[] { afinidad }.Concat(Generales).Concat(Amuletos).OrderBy(x => x).ToList();
    Check($"molde: un {cls} ve 5 recetas: SU arma de afinidad + 2 generales + 2 amuletos", names.OrderBy(x => x).SequenceEqual(expected) && zidV == 1 && !fb, string.Join(", ", names));
}

var (n4, z4id, fb4) = View(Zone1Template(), "Guerrero", 4);
Check("molde: si tu zona (Bosque) todavía no tiene recetas, ves las de la anterior que sí (Zona 1) y se marca como respaldo", n4.Count == 5 && z4id == 1 && fb4, $"zona {z4id}, respaldo {fb4}");

var conZona4 = Zone1Template().Append(Rcp(4, true, Item(60, "Arco de Prueba", "Weapon", 60, family: "Arcos"), 500, Ing("Madera de Ébano", 2))).ToArray();
var (n4b, z4b, fb4b) = View(conZona4, "Arquero", 4);
Check("molde: si tu zona SÍ tiene recetas, ves solo las de tu zona (no las de Zona 1)", z4b == 4 && !fb4b && n4b.SequenceEqual(new[] { "Arco de Prueba" }), $"zona {z4b}: {string.Join(", ", n4b)}");
var (n1c, z1c, _) = View(conZona4, "Arquero", 1);
Check("molde: estando en Zona 1 no ves las de la Zona 4", z1c == 1 && !n1c.Contains("Arco de Prueba"), string.Join(", ", n1c));
var (nUnk, zUnk, _) = View(Zone1Template(), "Guerrero", 99);
Check("molde: una zona desconocida cae a la primera con recetas", zUnk == 1 && nUnk.Count == 5, $"zona {zUnk}");
var (nEmpty, zEmpty, _) = View(new List<object>(), "Guerrero", 1);
Check("molde: sin recetas cargadas no hay nada que mostrar", zEmpty is null && nEmpty.Count == 0, "sin zona");
var soloZ5 = new[] { Rcp(5, false, Item(61, "Cosa de Zona 5", "Weapon", 90, family: "Espadas"), 900, Ing("Hierro", 1)) };
var (nAlto, zAlto, _) = View(soloZ5, "Guerrero", 1);
Check("molde: recetas solo de una zona MÁS ALTA que la tuya no se muestran", zAlto is null && nAlto.Count == 0, "sin zona");
var exclusiva = Zone1Template().Append(Rcp(1, false, Item(62, "Filo del Ninja", "Weapon", 30, family: "Dagas", classReq: "Ninja"), 100, Ing("Hierro", 1))).ToArray();
Check("molde: una receta exclusiva de otra clase sigue oculta (Guerrero no ve el Filo del Ninja)", !View(exclusiva, "Guerrero", 1).Names.Contains("Filo del Ninja") && View(exclusiva, "Ninja", 1).Names.Contains("Filo del Ninja"), "ok");

var groupOf = recipeCatalog.GetMethod("GroupOf")!;
string G(object r) => groupOf.Invoke(null, [r])!.ToString()!;
var tpl = Zone1Template();
Check("molde: agrupa en ClassWeapon / GeneralWeapon / Amulet", G(tpl[0]) == "ClassWeapon" && G(tpl[4]) == "GeneralWeapon" && G(tpl[6]) == "Amulet", $"{G(tpl[0])}, {G(tpl[4])}, {G(tpl[6])}");

// ---------- /forge recipes (el recetario completo, v0.27.0: una línea por receta, todas las zonas) ----------
async Task<(List<(string Name, string Value)> Fields, string? Description, string? Footer, int Length)> RecipesEmbedFields(IEnumerable<object> recipeList, string playerClass, int? zoneId = null)
{
    var recipeRepo = FakeProxy.Create(recipeRepoType, (n, a) => n == "GetAllAsync" ? ListOf(recipeType, recipeList.ToArray()) : throw new NotSupportedException(n));
    var zoneRepo = FakeProxy.Create(zoneRepoType, (n, a) => n == "GetAllAsync" ? zones : throw new NotSupportedException(n));
    var task = (Task)forgeModule.GetMethod("BuildRecipesEmbed")!.Invoke(null, [recipeRepo, zoneRepo, playerClass, zoneId, null, null])!;
    await task;
    var embed = task.GetType().GetProperty("Result")!.GetValue(task)!;
    var description = (string?)embed.GetType().GetProperty("Description")!.GetValue(embed);
    var fields = (IEnumerable)embed.GetType().GetProperty("Fields")!.GetValue(embed)!;
    var footerObj = embed.GetType().GetProperty("Footer")!.GetValue(embed);
    string? footer = footerObj is null ? null : (string?)footerObj.GetType().GetProperty("Text")!.GetValue(footerObj);
    int length = (int)embed.GetType().GetProperty("Length")!.GetValue(embed)!;
    return (fields.Cast<object>().Select(x => ((string)x.GetType().GetProperty("Name")!.GetValue(x)!, (string)x.GetType().GetProperty("Value")!.GetValue(x)!)).ToList(), description, footer, length);
}

var (gf, gDesc, gFoot, gLen) = await RecipesEmbedFields(Zone1Template(), "Guerrero");
Check("recetario: la zona es el título de su campo (con el nivel) y la descripción nombra la clase", gf.Count == 1 && gf[0].Name.Contains("Zona 1 · Praderas del Mate · nivel ") && gDesc!.Contains("Guerrero"), gf.FirstOrDefault().Name ?? "(sin campos)");
var gl = gf[0].Value.Split('\n');
Check("recetario: una línea por receta, en orden: arma de clase, armas generales (de menor a mayor poder) y amuletos",
    gl.Length == 5 && gl[0].StartsWith("**Espada de Madera**") && gl[1].StartsWith("**Hoja de Acero Puro**") && gl[2].StartsWith("**Hacha de Hierro MK3**") && gl[3].StartsWith("**Amuleto del Levantador**") && gl[4].StartsWith("**Hombreras de Cuero Grueso**"), string.Join(" | ", gl.Select(l => l.Split('`')[0].Trim())));
Check("recetario: cada línea dice lo que suma, los ingredientes con su cantidad y el oro (sin ingredientes uno por línea ni ✅/❌)", gl[0] == "**Espada de Madera** `+5 ATQ` → 3 Madera de Pino + 1 Hierro · 💰 40" && gl[3] == "**Amuleto del Levantador** `+10 DEF` → 5 Piedra + 1 Colmillo de Jabalí · 💰 150", gl[0] + " // " + gl[3]);
Check("recetario: sin leyenda al pie, sin campos en línea, y muy por debajo de los límites de Discord", gFoot is null && gLen < 2000 && gf.All(x => x.Value.Length <= 1024 && x.Name.Length <= 256), $"largo total {gLen}");
Check("recetario: sin sinergia no hay «(+8 con tu clase ⭐)» ni etiquetas de tipo (_arma de tu clase_)", !gf[0].Value.Contains("⭐") && !gf[0].Value.Contains("con tu clase") && !gf[0].Value.Contains("_arma"), gf[0].Value);

var (nf, _, _, _) = await RecipesEmbedFields(Zone1Template(), "Ninja");
Check("recetario: un Ninja ve la Daga Oxidada como arma de clase y NO la Espada", nf[0].Value.Split('\n')[0].StartsWith("**Daga Oxidada** `+5 ATQ`") && !nf[0].Value.Contains("Espada de Madera"), nf[0].Value.Split('\n')[0]);

var withZone = Zone1Template().Append(Rcp(4, true, Item(60, "Arco de Prueba", "Weapon", 60, family: "Arcos"), 500, Ing("Madera de Ébano", 2))).ToArray();
var (zf, _, _, _) = await RecipesEmbedFields(withZone, "Arquero", 4);
var (af, _, _, _) = await RecipesEmbedFields(withZone, "Arquero");
Check("recetario: con una zona pedida muestra SOLO esa; sin zona, todas (en orden de dificultad)", zf.Count == 1 && zf[0].Name.Contains("Zona 4") && zf[0].Value.StartsWith("**Arco de Prueba**") && af.Count == 2 && af[0].Name.Contains("Zona 1") && af[1].Name.Contains("Zona 4"), string.Join(" | ", af.Select(x => x.Name)));
var (nof, nofDesc, _, _) = await RecipesEmbedFields(Zone1Template(), "Guerrero", 4);
Check("recetario: una zona sin recetas lo dice en vez de romper", nof.Count == 0 && nofDesc!.Contains("zona 4"), nofDesc ?? "(null)");

var sinZona = Zone1Template().Append(Rcp(null, false, Item(70, "Hoja de Prueba", "Weapon", 9), 40, Ing("Hierro", 2), Ing("Madera de Pino", 10))).ToArray();
var (sf, _, _, _) = await RecipesEmbedFields(sinZona, "Guerrero");
Check("recetario: la receta SIN ZONA va en su propia parte al final: «Sin zona específica»", sf.Count == 2 && sf[1].Name == "🧰 Sin zona específica" && sf[1].Value == "**Hoja de Prueba** `+9 ATQ` → 2 Hierro + 10 Madera de Pino · 💰 40", string.Join(" | ", sf.Select(x => x.Name)));
var (sz, _, _, _) = await RecipesEmbedFields(sinZona, "Guerrero", 1);
Check("recetario: pidiendo una zona NO aparece «Sin zona específica»", sz.Count == 1 && sz[0].Name.Contains("Zona 1"), string.Join(" | ", sz.Select(x => x.Name)));

var (vacio, vacioDesc, _, _) = await RecipesEmbedFields(new List<object>(), "Guerrero");
Check("recetario: sin recetas cargadas avisa en vez de romper", vacio.Count == 0 && vacioDesc!.Contains("no hay recetas cargadas"), vacioDesc ?? "(null)");
var soloOtraClase = new[] { Rcp(1, true, Item(62, "Filo del Ninja", "Weapon", 30, family: "Dagas", classReq: "Ninja"), 100, Ing("Hierro", 1)) };
var (otra, otraDesc, _, _) = await RecipesEmbedFields(soloOtraClase, "Guerrero");
Check("recetario: si no hay nada para tu clase lo dice", otra.Count == 0 && otraDesc!.Contains("No hay recetas para vos"), otraDesc ?? "(null)");

// La vista del menú (ViewFor) suma la receta sin zona a la de la zona; y el menú de /forge la esconde si ya llevás un arma puesta.
var (nSin, zSin, _) = View(sinZona, "Guerrero", 1);
Check("molde: la receta sin zona la ve cualquiera, en cualquier zona", nSin.Contains("Hoja de Prueba") && zSin == 1 && View(sinZona, "Guerrero", 4).Names.Contains("Hoja de Prueba"), string.Join(", ", nSin));

// ---------- /forge make (lista desplegable) ----------
List<(string Name, string Value)> RunForge(IEnumerable<object> recipes, object player, Dictionary<string, int> owned, string typed)
{
    var result = (IEnumerable)forgeChoices.GetMethod("For")!.Invoke(null, [ListOf(recipeType, recipes.ToArray()), zones, player, owned, typed])!;
    return result.Cast<object>().Select(r => ((string)r.GetType().GetProperty("Name")!.GetValue(r)!, (string)r.GetType().GetProperty("Value")!.GetValue(r)!)).ToList();
}

var invent = new Dictionary<string, int> { ["Madera de Pino"] = 5, ["Hierro"] = 1, ["Piedra"] = 2 };
var f = RunForge(Zone1Template(), PlayerAt("Guerrero", 100, 1), invent, "");
Check("lista: son las 5 de tu clase y zona (no las armas de afinidad de otras clases)",
    f.Count == 5 && !f.Any(x => x.Value is "Daga Oxidada" or "Arco Corto de Sauce" or "Grimorio Desgastado"), Show(f));
Check("lista: lo que YA podés forjar va primero, con ✅, +ATQ y costo", f[0].Name == "✅ Espada de Madera (+5 ATQ) — 40 oro", f[0].Name);
Check("lista: después de menor a mayor lo que falta (Hoja 3 unidades, Levantador 4, Hacha 7, Hombreras 8)",
    f.Select(x => x.Value).SequenceEqual(new[] { "Espada de Madera", "Hoja de Acero Puro", "Amuleto del Levantador", "Hacha de Hierro MK3", "Hombreras de Cuero Grueso" }), Show(f));
Check("lista: lo que no podés dice qué falta (materiales y oro)", f[1].Name.StartsWith("❌ Hoja de Acero Puro (+15 ATQ) — falta:") && f[1].Name.Contains("2 Hierro") && f[1].Name.Contains("1 Colmillo de Jabalí") && f[1].Name.Contains("80 oro"), f[1].Name);
Check("lista: el valor de cada opción es el nombre exacto del ítem", f.All(x => x.Value.Length > 0 && !x.Value.Contains("✅") && !x.Value.Contains("(")), string.Join(",", f.Select(x => x.Value)));

var ninjaList = RunForge(Zone1Template(), PlayerAt("Ninja", 100, 1), invent, "");
Check("lista: un Ninja ve la Daga Oxidada y no la Espada de Madera", ninjaList.Any(x => x.Value == "Daga Oxidada") && !ninjaList.Any(x => x.Value == "Espada de Madera"), Show(ninjaList));
var filtrada = RunForge(Zone1Template(), PlayerAt("Guerrero", 100, 1), invent, "HOJA");
Check("lista: filtra por texto sin importar mayúsculas", filtrada.Count == 1 && filtrada[0].Value == "Hoja de Acero Puro", Show(filtrada));
var respaldo = RunForge(Zone1Template(), PlayerAt("Guerrero", 100, 4), invent, "");
Check("lista: desde una zona sin recetas muestra las de la zona anterior (igual que /forge recipes)", respaldo.Count == 5, Show(respaldo));
var rico = RunForge(Zone1Template(), PlayerAt("Guerrero", 1000, 1), new Dictionary<string, int> { ["Madera de Pino"] = 9, ["Hierro"] = 9, ["Piedra"] = 9, ["Tela Rasgada"] = 9, ["Colmillo de Jabalí"] = 9, ["Cuero Grueso"] = 9, ["Piedra Caliente"] = 9 }, "");
Check("lista: con todo en la mochila las 5 salen ✅", rico.Count == 5 && rico.All(x => x.Name.StartsWith("✅")), Show(rico));
var pobre = RunForge(Zone1Template(), PlayerAt("Guerrero", 0, 1), new Dictionary<string, int>(), "");
Check("lista: sin nada ninguna es ✅ pero aparecen las 5 (sirve de guía)", pobre.Count == 5 && pobre.All(x => x.Name.StartsWith("❌")), Show(pobre));
var sinOro = RunForge(Zone1Template(), PlayerAt("Guerrero", 10, 1), invent, "espada");
Check("lista: con los materiales pero sin oro suficiente NO es ✅", !sinOro[0].Name.StartsWith("✅") && sinOro[0].Name.Contains("30 oro"), sinOro[0].Name);

Console.WriteLine(fails == 0 ? "\nRESULTADO: OK" : $"\nRESULTADO: FALLA ({fails})");

// DispatchProxy mínimo para simular repositorios sin referenciar sus tipos en compilación.
public class FakeProxy : DispatchProxy
{
    private Func<string, object?[], object?> _h = null!;

    public static object Create(Type iface, Func<string, object?[], object?> h)
    {
        var create = typeof(DispatchProxy).GetMethod("Create", 2, [])!.MakeGenericMethod(iface, typeof(FakeProxy));
        var proxy = create.Invoke(null, null)!;
        ((FakeProxy)proxy)._h = h;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? m, object?[]? a)
    {
        object? r = _h(m!.Name, a ?? []);
        var rt = m.ReturnType;
        if (rt == typeof(Task)) return Task.CompletedTask;
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(rt.GetGenericArguments()[0]).Invoke(null, [r]);
        return r;
    }
}
