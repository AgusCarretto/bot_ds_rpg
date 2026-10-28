using System.Reflection;
using BotDsRpg.GameData;
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

// v0.34.0: «aa admingive», el comando del administrador para dar oro, Polvo, EXP o cualquier ítem SIN tenerlos (probar cosas, sorteos). Lo que se prueba: el lector de lo que escribe el administrador, quién es administrador según
// Admin__UserIds, y contra la base LOCAL (usuarios descartables): que quien NO es administrador no recibe respuesta ni cambia nada, que dar oro / Polvo / EXP / cajas / materiales funciona y suma, que el equipo y las herramientas se PONEN,
// los topes, que no crea cuentas, el rastro en game_events, las 10 entregas a la vez, y que el comando se registra con Discord.Net de verdad y no vale en plena pelea.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong ADMIN = 999000000000088101UL, ADMIN2 = 999000000000088102UL, P1 = 999000000000088103UL, P2 = 999000000000088104UL, NOACC = 999000000000088105UL, OUTSIDER = 999000000000088106UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [ADMIN, ADMIN2, P1, P2, NOACC, OUTSIDER];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x", ["Admin:UserIds"] = $"{ADMIN}, {ADMIN2}" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(AdminAccess).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var items = sp.GetRequiredService<IItemRepository>();
var progression = sp.GetRequiredService<IProgressionRepository>();
var adminRepo = sp.GetRequiredService<IAdminRepository>();
var events = sp.GetRequiredService<IGameEvents>();
var access = sp.GetRequiredService<IAdminAccess>();
await using var db = new NpgsqlConnection(conn);

async Task Clean()
{
    foreach (var id in all)
    {
        await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM player_tools WHERE discord_id = @id; DELETE FROM inventory WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id;", new { id = (long)id });
    }
}

async Task Player(ulong id, int gold = 50)
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET gold = @g, dust = 0, level = 1, xp = 0 WHERE discord_id = @id", new { id = (long)id, g = gold });
}

Task<string?> Give(ulong actor, string args, IAdminAccess? who = null) => AdminGiveHandler.ExecuteAsync(who ?? access, users, items, progression, adminRepo, events, actor, args);
async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });
async Task<int> Dust(ulong id) => await db.ExecuteScalarAsync<int>("SELECT dust FROM users WHERE discord_id = @id", new { id = (long)id });
async Task<int> Qty(ulong id, string item) => await db.ExecuteScalarAsync<int?>("SELECT inv.quantity FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = item }) ?? 0;

Console.WriteLine("== 1. Qué escribió el administrador ==");
var g1 = AdminGive.Parse("gold 1m");
Check(g1.Request is { Kind: AdminGiveKind.Gold, Amount: 1_000_000, TargetId: null }, "«gold 1m» = un millón de oro, a sí mismo");
Check(AdminGive.Parse("oro 1.500").Request is { Kind: AdminGiveKind.Gold, Amount: 1500 } && AdminGive.Parse("plata 10k").Request is { Amount: 10_000 } && AdminGive.Parse("GOLD 1,5m").Request is { Amount: 1_500_000 }, "los sinónimos y los formatos de monto: oro, plata, 1.500, 10k, 1,5m (sin importar mayúsculas)");
Check(AdminGive.Parse($"dust 500 <@{P1}>").Request is { Kind: AdminGiveKind.Dust, Amount: 500 } d && d.TargetId == P1 && AdminGive.Parse($"<@!{P2}> polvo 7").Request is { Kind: AdminGiveKind.Dust, Amount: 7 } d2 && d2.TargetId == P2, "Polvo, con la mención antes o después (con o sin «!»)");
Check(AdminGive.Parse("xp 5000").Request is { Kind: AdminGiveKind.Xp, Amount: 5000 } && AdminGive.Parse("exp 2k").Request is { Kind: AdminGiveKind.Xp, Amount: 2000 }, "EXP");
Check(AdminGive.Parse("item Cofre de Oro").Request is { Kind: AdminGiveKind.Item, Amount: 1, ItemName: "Cofre de Oro" }
    && AdminGive.Parse("item Cofre de Oro 3").Request is { Amount: 3, ItemName: "Cofre de Oro" }
    && AdminGive.Parse("item \"Cofre de Oro\" 3 <@" + P1 + ">").Request is { Amount: 3, ItemName: "Cofre de Oro", TargetId: P1 }
    && AdminGive.Parse("ítem Hierro 25").Request is { Amount: 25, ItemName: "Hierro" }
    && AdminGive.Parse("item Hierro").Request is { Amount: 1, ItemName: "Hierro" },
    "un ítem: el nombre con varias palabras, entre comillas o suelto, la cantidad al final es opcional y la mención va donde sea");
string[] bad = ["", "gold", "gold all", "gold abc", "gold 0", "gold 1m 2m", "item", "banana 3", "xp 101m", "item Hierro 100001", $"gold 5 <@{P1}> <@{P2}>"];
Check(bad.All(b => AdminGive.Parse(b).Request is null && !string.IsNullOrEmpty(AdminGive.Parse(b).Error)), "lo que no se entiende (sin cantidad, «todo», letras, cero, sobra algo, sin ítem, una palabra rara, más de un destino, pasarse del tope) se rechaza diciendo por qué");
Check(AdminGive.Parse("gold all").Error!.Contains("número") && AdminGive.Parse("xp 100m").Request is { Amount: 100_000_000 } && AdminGive.Parse("item Hierro 100k").Request is { Amount: 100_000 } && AdminGive.Parse("").Error == AdminGive.Usage, "«todo» no vale, los topes exactos pasan (100 M de EXP, 100.000 ítems) y sin nada muestra cómo se usa");

Console.WriteLine("\n== 2. Quién es administrador ==");
var ids = AdminAccess.Parse("123, 456;789\t x, 0 ,-5,12abc,456");
Check(ids.SetEquals([123UL, 456UL, 789UL]), "la variable acepta coma, punto y coma y espacios; lo que no es un ID (letras, 0, negativos) se ignora y los repetidos cuentan una vez");
Check(!new AdminAccess(null).HasAnyAdmin && !new AdminAccess("").HasAnyAdmin && !new AdminAccess(" , ").HasAnyAdmin && !new AdminAccess("abc").IsAdmin(1), "sin variable, vacía o mal escrita: no hay ningún administrador (nunca por accidente)");
Check(access.IsAdmin(ADMIN) && access.IsAdmin(ADMIN2) && !access.IsAdmin(P1) && !access.IsAdmin(OUTSIDER) && access.HasAnyAdmin, "el servicio real se arma con Admin:UserIds de la configuración: solo esos dos IDs");

Console.WriteLine("\n== 3. Dar de verdad (base local) ==");
await Clean(); await Player(ADMIN); await Player(P1); await Player(P2); await Player(OUTSIDER, gold: 77);

var denied = await Give(OUTSIDER, $"gold 1m <@{OUTSIDER}>");
Check(denied is null && await Gold(OUTSIDER) == 77, "un NO administrador no recibe respuesta (ni confirma que el comando existe) y no cambia nada");
var off = await Give(ADMIN, "gold 1m", new AdminAccess(null));
Check(off is not null && off.Contains("Admin__UserIds") && await Gold(ADMIN) == 50, "si NO hay ningún administrador configurado explica qué variable falta y tampoco da nada");
var offOther = await Give(OUTSIDER, "gold 1m", new AdminAccess(null));
Check(offOther is not null && offOther.Contains("Admin__UserIds") && await Gold(OUTSIDER) == 77, "(lo mismo para cualquiera cuando no hay administradores: no hay nada que robar)");

var self = await Give(ADMIN, "gold 1m");
Check(self is not null && self.Contains("1.000.000") && await Gold(ADMIN) == 1_000_050, "oro a uno mismo: se suma a lo que tenía y el mensaje dice cuánto");
var toP1 = await Give(ADMIN, $"gold 500 <@{P1}>");
Check(toP1 is not null && toP1.Contains($"<@{P1}>") && await Gold(P1) == 550 && await Gold(ADMIN) == 1_000_050, "oro a otro jugador con mención: le llega a él y el administrador no pierde ni gana");
await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)P1, g = int.MaxValue - 10 });
var capped = await Give(ADMIN, $"gold 1000 <@{P1}>");
Check(capped is not null && capped.Contains("tope") && await Gold(P1) == int.MaxValue, "pasarse del tope de la columna no rompe: queda en el máximo y lo avisa");
var dust = await Give(ADMIN, $"polvo 50 <@{P1}>");
Check(dust is not null && dust.Contains("Polvo") && await Dust(P1) == 50, "Polvo");
var xp = await Give(ADMIN, $"xp 100k <@{P1}>");
int lvl = await db.ExecuteScalarAsync<int>("SELECT level FROM users WHERE discord_id = @id", new { id = (long)P1 });
Check(xp is not null && xp.Contains("nivel") && lvl > 5, $"EXP: sube de nivel como cualquier ganancia (nivel {lvl})");

var box = await Give(ADMIN, "item Cofre de Oro 3");
Check(box is not null && box.Contains("3×") && await Qty(ADMIN, "Cofre de Oro") == 3, "una caja a la mochila");
var boxMore = await Give(ADMIN, "item cofre de oro 2");
Check(boxMore is not null && await Qty(ADMIN, "Cofre de Oro") == 5, "otra vez se suma (y el nombre vale sin importar mayúsculas)");
var mat = await Give(ADMIN, $"item Hierro 40 <@{P2}>");
var egg = await Give(ADMIN, $"item Huevo de la Pradera <@{P2}>");
var food = await Give(ADMIN, $"item Comida de Dragón 4 <@{P2}>");
Check(mat is not null && egg is not null && food is not null && await Qty(P2, "Hierro") == 40 && await Qty(P2, "Huevo de la Pradera") == 1 && await Qty(P2, "Comida de Dragón") == 4, "materiales, huevos y comida de dragón van a la mochila del destinatario");
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = 'Hierro' ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)P2, q = int.MaxValue - 5 });
await Give(ADMIN, $"item Hierro 100 <@{P2}>");
Check(await Qty(P2, "Hierro") == int.MaxValue, "la mochila tampoco se rompe pasada del tope: queda en el máximo");

string weaponName = await db.ExecuteScalarAsync<string>("SELECT name FROM items WHERE type = 'Weapon' ORDER BY item_id LIMIT 1") ?? "";
string amuletName = await db.ExecuteScalarAsync<string>("SELECT name FROM items WHERE type = 'Amulet' ORDER BY item_id LIMIT 1") ?? "";
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, weapon_enchant_kind = 'critico' WHERE discord_id = @id", new { id = (long)P1 });
var weapon = await Give(ADMIN, $"item {weaponName} 5 <@{P1}>");
var worn = await db.QuerySingleAsync<(string? Weapon, int Enchant, string Kind)>("SELECT i.name, u.weapon_enchant, u.weapon_enchant_kind FROM users u JOIN items i ON i.item_id = u.weapon_id WHERE u.discord_id = @id", new { id = (long)P1 });
Check(weapon is not null && weapon.Contains("de arma") && worn.Weapon == weaponName && worn.Enchant == 0 && worn.Kind == "filo" && await Qty(P1, weaponName) == 0, $"un arma se PONE (pisa la que llevaba, sin encantar, y no va a la mochila aunque pida 5): {weaponName}");
var amulet = await Give(ADMIN, $"item {amuletName} <@{P1}>");
Check(amulet is not null && amulet.Contains("de amuleto") && await db.ExecuteScalarAsync<string>("SELECT i.name FROM users u JOIN items i ON i.item_id = u.amulet_id WHERE u.discord_id = @id", new { id = (long)P1 }) == amuletName, $"un amuleto se PONE: {amuletName}");
var axe = await Give(ADMIN, $"item Hacha de Escoria Templada <@{P1}>");
var axeBetter = await Give(ADMIN, $"item Hacha de Hierro Forjado <@{P1}>");
var pick = await Give(ADMIN, $"item Pico de Escoria Templada <@{P1}>");
var tools = (await db.QueryAsync<(string Kind, string Name)>("SELECT t.kind, i.name FROM player_tools t JOIN items i ON i.item_id = t.item_id WHERE t.discord_id = @id ORDER BY t.kind", new { id = (long)P1 })).ToList();
Check(axe is not null && axeBetter is not null && pick is not null && tools.Count == 2 && tools[0] == ("axe", "Hacha de Hierro Forjado") && tools[1] == ("pickaxe", "Pico de Escoria Templada"), "las herramientas se PONEN en su lugar (hacha y pico); la nueva pisa a la vieja aunque sea peor, que es para probar");

var near = await Give(ADMIN, "item Cofre");
Check(near is not null && near.Contains("Cofre de Oro") && near.Contains("¿Quisiste decir"), "un nombre que no existe sugiere los parecidos");
var nothing = await Give(ADMIN, "item xyzzy inexistente");
Check(nothing is not null && nothing.Contains("No encontré") && !nothing.Contains("Quisiste"), "y si no hay nada parecido, solo dice que no lo encontró");
var noAccount = await Give(ADMIN, $"gold 10 <@{NOACC}>");
var noAccountItem = await Give(ADMIN, $"item Hierro 3 <@{NOACC}>");
Check(noAccount is not null && noAccount.Contains("no tiene cuenta") && noAccountItem is not null && noAccountItem.Contains("no tiene cuenta") && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE discord_id = @id", new { id = (long)NOACC }) == 0, "a quien no tiene cuenta no le da nada y NO le crea una");
var usage = await Give(ADMIN, "regalo");
Check(usage is not null && usage.Contains("Uso:"), "si no se entiende, el administrador ve cómo se usa");

var audit = (await db.QueryAsync<(string Kind, string? Detail)>("SELECT kind, detail FROM game_events WHERE discord_id = @id AND kind = 'admin_give' ORDER BY event_id", new { id = (long)P1 })).ToList();
Check(audit.Count >= 6 && audit.All(a => a.Detail!.Contains($"por {ADMIN}")) && audit.Any(a => a.Detail!.StartsWith("gold 500")) && audit.Any(a => a.Detail!.StartsWith("xp ")), $"cada entrega deja un evento admin_give con qué se dio y quién lo dio ({audit.Count} para P1)");
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM game_events WHERE discord_id = @id AND kind = 'admin_give'", new { id = (long)OUTSIDER }) == 0, "y el rechazado no deja nada");

await db.ExecuteAsync("UPDATE users SET gold = 0 WHERE discord_id = @id", new { id = (long)P2 });
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Give(ADMIN, $"gold 100 <@{P2}>")));
Check(race.All(r => r is not null) && await Gold(P2) == 1000, "10 entregas a la vez suman exacto (1.000), ninguna se pisa");

Console.WriteLine("\n== 4. Registro con Discord.Net de verdad ==");
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
await texts.AddModuleAsync<TextCommandModule>(sp);
var found = texts.Search("admingive gold 1m");
var slashNames = interactions.SlashCommands.Select(c => c.Name).ToList();
Check(found.IsSuccess && found.Commands.All(c => c.Command.Name == "admingive") && new[] { "admin-give", "admindar" }.All(a => texts.Search(a + " gold 1m").IsSuccess), "«aa admingive» (y sus alias) existe en CommandService");
Check(texts.Commands.Single(c => c.Name == "admingive").Summary is null && !FightLock.IsAllowed("admingive") && !slashNames.Any(n => n.Contains("admin", StringComparison.OrdinalIgnoreCase)), "no tiene resumen ni versión de barra (no figura en ninguna lista) y no vale en plena pelea");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
