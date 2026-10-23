using System.Globalization;
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

// Etapa 2b: comida reducida a 6 + banquetes con buff de ataque. Contra la base REAL con un usuario descartable.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var zones = new ZoneRepository(factory);
var monsters = new MonsterRepository(factory);
var shop = new ShopRepository(factory);
var eventsRepo = new GameEventRepository(factory);
var events = new GameEventService(eventsRepo);
var combat = new CombatSessionService(users);
await using var db = new NpgsqlConnection(conn);

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000002201UL;
const string Banquete1 = "Asado Completo del Domingo en Familia";
const string Banquete2 = "Mate Dulce de la Abuela";
const string Mate = "Mate Amargo";
async Task Cleanup() => await db.ExecuteAsync(
    "DELETE FROM player_buffs WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id;", new { id = (long)U });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });
async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task SetHp(int hp) => await db.ExecuteAsync("UPDATE users SET current_hp = @hp WHERE discord_id = @id", new { id = (long)U, hp });
async Task SetGold(int g) => await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)U, g });
async Task<(int hp, int maxHp, int gold)> Stats() => await db.QuerySingleAsync<(int, int, int)>("SELECT current_hp, max_hp, gold FROM users WHERE discord_id = @id", new { id = (long)U });
async Task ClearBuff() => await db.ExecuteAsync("DELETE FROM player_buffs WHERE discord_id = @id", new { id = (long)U });
async Task ClearCooldowns() => await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });

var stubTarget = new StubTarget();

Console.WriteLine("== 0. Registro de módulos y dependencias ==");
{
    var client = new DiscordSocketClient();
    var interactions = new InteractionService(client.Rest);
    var texts = new CommandService();
    texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
    var sp = (ServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
    try
    {
        await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
        await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
        Check(sp.GetService<IBuffRepository>() is not null, "IBuffRepository está registrado en el DI real");
        var broken = new List<string>();
        foreach (var type in typeof(BoxModule).Assembly.GetTypes().Where(x => !x.IsAbstract && (typeof(IInteractionModuleBase).IsAssignableFrom(x) || typeof(IModuleBase).IsAssignableFrom(x))))
        {
            try { ActivatorUtilities.CreateInstance(sp, type); } catch (Exception ex) { broken.Add($"{type.Name}: {ex.Message.Split((char)10)[0]}"); }
        }

        Check(broken.Count == 0, $"todos los módulos se construyen con el DI real ({(broken.Count == 0 ? "ok" : string.Join(" | ", broken))})");
        Check(ActivatorUtilities.CreateInstance<AdventureCombatStarter>(sp) is not null, "AdventureCombatStarter se construye con el DI real");
    }
    catch (Exception ex) { Check(false, "registro de módulos: " + ex.Message); }
}

Console.WriteLine("\n== 1. Catálogo de comida ==");
var foods = (await items.GetAllByTypeAsync("Consumable")).OrderBy(i => i.StatValue).ThenBy(i => i.BuyPrice).ToList(); // el Cordero y el Asado Completo curan lo mismo (150) desde la v0.19.2: desempata el precio
var expected = new (string name, int hp, int buy, int sell)[]
{
    ("Mate Amargo", 15, 8, 6), ("Empanada de Carne", 40, 36, 27), ("Asado de Tira", 100, 160, 120), ("Cordero Patagónico", 150, 300, 225),
    (Banquete1, 150, 2100, 1575), (Banquete2, 200, 2800, 2100), // v0.19.2: curan 150 y 200 (eran 250 y 300); los precios no cambiaron
};
Check(foods.Count == 6 && foods.Zip(expected).All(p => p.First.Name == p.Second.name && p.First.StatValue == p.Second.hp && p.First.BuyPrice == p.Second.buy && p.First.SellPrice == p.Second.sell),
    $"quedan exactamente 6 comidas con los precios nuevos ({string.Join(", ", foods.Select(f => $"{f.Name}={f.BuyPrice}"))})");
foreach (var gone in new[] { "Pan Casero", "Choripán", "Vacío al Disco" })
{
    Check(await items.GetByNameAsync(gone) is null, $"{gone} ya no existe");
}

var itemBuffs = await buffs.GetItemBuffsAsync();
var idOf = foods.ToDictionary(f => f.Name, f => f.ItemId);
Check(itemBuffs.Count == 2 && itemBuffs.ContainsKey(idOf[Banquete1]) && itemBuffs.ContainsKey(idOf[Banquete2]), "solo los 2 banquetes Míticos dan buff");
Check(itemBuffs.Values.All(b => b is { AttackPercent: 15, Minutes: 30 }), "el buff es +15% de ataque por 30 minutos");
var one = await buffs.GetItemBuffAsync(idOf[Banquete2]);
Check(one is { AttackPercent: 15, Minutes: 30 } && await buffs.GetItemBuffAsync(idOf[Mate]) is null, "GetItemBuffAsync: el banquete sí, el mate no");
var boxNames = new[] { "Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro", "Arca del Soberano" };
var boxItems = new List<Item?>();
foreach (var n in boxNames) boxItems.Add(await items.GetByNameAsync(n));
Check(boxItems.All(b => b is { Type: "Caja" }), "las 5 cajas siguen en el catálogo");
// Nadie se quedó con un ítem de comida eliminado: el inventario no tiene referencias huérfanas (el FK las impediría igual).
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory inv LEFT JOIN items i USING (item_id) WHERE i.item_id IS NULL") == 0, "no hay inventario apuntando a ítems borrados");

Console.WriteLine("\n== 2. La cuenta pura del buff ==");
Check(AttackBuff.Apply(100, 15) == 115 && AttackBuff.Apply(100, 0) == 100 && AttackBuff.Apply(100, -5) == 100, "Apply: 100 +15% = 115; 0% y negativo no cambian nada");
Check(AttackBuff.Apply(7, 15) == 8 && AttackBuff.Apply(1, 15) == 1, "Apply redondea (7 -> 8) y nunca baja de lo que había");
Check(AttackBuff.Rescale(115, 15, 15) == 115, "Rescale: el mismo banquete otra vez NO apila (115 sigue en 115)");
Check(AttackBuff.Rescale(100, 0, 15) == 115 && AttackBuff.Rescale(115, 15, 30) == 130 && AttackBuff.Rescale(115, 15, 0) == 100, "Rescale: de 0 a 15 = 115, de 15 a 30 = 130, de 15 a 0 vuelve a 100");
for (int baseDmg = 1; baseDmg <= 400; baseDmg++)
{
    int buffed = AttackBuff.Apply(baseDmg, 15);
    if (AttackBuff.Rescale(buffed, 15, 15) != buffed || AttackBuff.Rescale(buffed, 15, 0) != baseDmg) { Check(false, $"Rescale no es estable con daño base {baseDmg}"); break; }
}
Check(true, "Rescale es estable (ida y vuelta exactas) para todos los ataques base de 1 a 400");

Console.WriteLine("\n== 3. Perfil de combate con buff ==");
await Cleanup();
var player = await users.GetOrCreateUserAsync(U);
var plain = PlayerCombatProfileCalculator.Resolve(player, null, null);
var boosted = PlayerCombatProfileCalculator.Resolve(player, null, null, 15);
Check(plain.AttackBuffPercent == 0 && boosted.AttackBuffPercent == 15 && boosted.Damage == AttackBuff.Apply(plain.Damage, 15) && boosted.Damage > plain.Damage, $"daño {plain.Damage} -> {boosted.Damage} con +15%");
Check(boosted.Defense == plain.Defense && boosted.CombatMaxHp == plain.CombatMaxHp && boosted.CombatCurrentHp == plain.CombatCurrentHp, "el buff toca SOLO el ataque (defensa y vida iguales)");

Console.WriteLine("\n== 4. Repositorio de buffs ==");
await ClearBuff();
Check(await buffs.GetActiveAttackAsync(U) is null, "sin buff: GetActiveAttackAsync = null");
var act = await buffs.ActivateAttackAsync(U, 15, 30, Banquete1);
var cur = await buffs.GetActiveAttackAsync(U);
Check(cur is { AttackPercent: 15, Source: Banquete1 } && act.ExpiresAt > DateTime.UtcNow.AddMinutes(29) && act.ExpiresAt < DateTime.UtcNow.AddMinutes(31) && cur.Remaining.TotalMinutes is > 29 and <= 30, $"activa: +15% hasta dentro de {cur?.Remaining.TotalMinutes:F1} min");
await buffs.ActivateAttackAsync(U, 20, 10, Banquete2);
cur = await buffs.GetActiveAttackAsync(U);
Check(cur is { AttackPercent: 20, Source: Banquete2 } && cur.Remaining.TotalMinutes < 10.1 && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_buffs WHERE discord_id = @id", new { id = (long)U }) == 1, "activar otro REEMPLAZA al anterior (una sola fila, no se acumulan)");
await db.ExecuteAsync("UPDATE player_buffs SET expires_at = now() - interval '1 second' WHERE discord_id = @id", new { id = (long)U });
Check(await buffs.GetActiveAttackAsync(U) is null, "vencido: ya no se devuelve (sin tareas de limpieza)");
var racing = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => buffs.ActivateAttackAsync(U, 15, 30, Banquete1)));
Check(racing.Length == 20 && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_buffs WHERE discord_id = @id", new { id = (long)U }) == 1, "20 activaciones simultáneas: sigue habiendo una sola fila");

Console.WriteLine("\n== 5. /use fuera de combate ==");
await ClearBuff();
await Give(Banquete1, 2);
await Give(Mate, 2);
await SetHp((await Stats()).maxHp); // al máximo
var r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Mate);
Check(r.Embed is not null && r.Embed.Title!.Contains("al máximo") && await Have(Mate) == 2 && await buffs.GetActiveAttackAsync(U) is null, "mate al máximo de HP: se rechaza y no se gasta (como siempre)");
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
var s = await Stats();
var ab = await buffs.GetActiveAttackAsync(U);
Check(r.Embed is not null && r.Embed.Description.Contains("+15% de ataque") && await Have(Banquete1) == 1 && ab is { AttackPercent: 15 } && s.hp == s.maxHp,
    $"banquete al máximo de HP: SÍ se usa (queda 1), buff +15% activo, HP sin cambios. Texto: «{r.Embed?.Description}»");
await SetHp(10);
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
s = await Stats();
ab = await buffs.GetActiveAttackAsync(U);
Check(await Have(Banquete1) == 0 && s.hp == Math.Min(s.maxHp, 10 + 150) && ab is { AttackPercent: 15 }, $"segundo banquete: cura ({s.hp}/{s.maxHp}), buff sigue en 15% (no apila), vence {ab?.Remaining.TotalMinutes:F1} min");
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
Check(r.PlainMessage is not null && r.PlainMessage.Contains("No tenés") && (await buffs.GetActiveAttackAsync(U))!.Remaining.TotalMinutes > 29, "sin banquetes en la mochila: no hay uso ni se renueva el buff gratis");
await SetHp(10);
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Mate);
Check(r.Embed is not null && !r.Embed.Description.Contains("ataque") && (await Stats()).hp == 25, "comida común: cura 15 y no menciona ningún buff");

Console.WriteLine("\n== 6. /use en pleno combate ==");
CombatState Fight(string cmd, int dmg, int buffPct = 0, bool healUsed = false) => new(
    cmd, "Toro Bravo", "🐂", 1000, 1000, 5, [], 0, 0, null, 400, 100, 100, dmg, 0, 1, "Guerrero", ClassPassives.For("Guerrero"),
    HealUsed: healUsed, AttackBuffPercent: buffPct);
async Task End() { var ses = combat.Peek(U); if (ses is not null) combat.TryAdvance(U, ses, null, null); }

await Give(Banquete1, 3);
await ClearBuff();
await SetHp(100);
Check(combat.TryStart(U, Fight("travel", 100), stubTarget), "arranca una pelea /travel con ataque 100");
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
var st = combat.Peek(U)?.State;
Check(st is { PlayerDamage: 115, AttackBuffPercent: 15, HealUsed: true } && await Have(Banquete1) == 2 && (await buffs.GetActiveAttackAsync(U)) is { AttackPercent: 15 },
    $"banquete mid-fight: el ataque pasa de 100 a {st?.PlayerDamage} YA, queda registrado el 15%, gasta la cura única y activa el buff. Texto: «{r.Embed?.Description}»");
Check(st is not null && st.PlayerCurrentHp >= 1 && r.Embed!.Description.Contains("+15% de ataque"), $"HP del jugador tras curarse + contraataque: {st?.PlayerCurrentHp}/400");
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
Check(r.PlainMessage is not null && r.PlainMessage.Contains("una vez") && await Have(Banquete1) == 2 && combat.Peek(U)!.State.PlayerDamage == 115, "segundo banquete en la MISMA pelea de /travel: se rechaza (una cura por pelea, el banquete no es puerta trasera)");
await End();

// /hunt (v0.16.1): una sola cura por pelea, igual que travel/boss; el segundo banquete se rechaza y el buff no apila.
await ClearBuff();
await SetHp(100);
Check(combat.TryStart(U, Fight("hunt", 100), stubTarget), "arranca una pelea /hunt con ataque 100");
await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
r = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete1);
st = combat.Peek(U)?.State;
Check(st is { PlayerDamage: 115, AttackBuffPercent: 15, HealUsed: true } && r.PlainMessage is not null && r.PlainMessage.Contains("una vez") && await Have(Banquete1) == 1, $"/hunt: dos banquetes seguidos -> el 2º se rechaza (una cura por pelea), ataque {st?.PlayerDamage} (no 132) y solo se gastó el primero");
await End();

// Una pelea que arranca YA con el buff aplicado (daño 115, 15%): otro banquete no lo vuelve a multiplicar.
await Give(Banquete2, 1);
Check(combat.TryStart(U, Fight("hunt", 115, 15), stubTarget), "arranca una pelea /hunt que ya traía el buff aplicado (ataque 115)");
await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Banquete2);
st = combat.Peek(U)?.State;
Check(st is { PlayerDamage: 115, AttackBuffPercent: 15 }, $"banquete encima de un buff ya aplicado: el ataque sigue en {st?.PlayerDamage}");
await End();

// Mate común en combate: no toca el ataque.
await Give(Mate, 1);
await SetHp(100);
Check(combat.TryStart(U, Fight("hunt", 100), stubTarget), "arranca otra pelea /hunt");
await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, Mate);
st = combat.Peek(U)?.State;
Check(st is { PlayerDamage: 100, AttackBuffPercent: 0 }, "un mate en combate no cambia el ataque ni registra buff");
await End();

Console.WriteLine("\n== 7. /heal fuera de combate no gasta banquetes ==");
await ClearBuff();
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
await Give(Banquete1, 1);
await SetHp(10);
var heal = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(heal.Title!.Contains("nada para comer") && await Have(Banquete1) == 1 && (await Stats()).hp == 10 && await buffs.GetActiveAttackAsync(U) is null, "solo con un banquete: /heal dice que no tenés nada y NO lo gasta");
await Give(Mate, 3);
heal = await TavernModule.ExecuteHealAsync(users, inventory, combat, buffs, U);
Check(heal.Title!.Contains("provecho") || heal.Title!.Contains("todo lo que tenías"), "con banquete + mates: /heal se come los mates que haga falta (ahora cura de una) y deja el banquete");
Check(await Have(Mate) == 0 && await Have(Banquete1) == 1 && (await Stats()).hp == Math.Min((await Stats()).maxHp, 10 + 45), "...los 3 mates (45 HP) y el banquete sigue ahí");

Console.WriteLine("\n== 8. El combate arranca con el buff aplicado ==");
{
    var cooldowns = new CooldownRepository(factory);
    var adventure = new AdventureRepository(factory);
    var raids = new RaidSessionService();
    var starter = new AdventureCombatStarter(cooldowns, adventure, users, items, monsters, zones, buffs, new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), new GameEventRepository(factory)), combat, raids);
    await ClearBuff(); await ClearCooldowns(); await SetHp((await Stats()).maxHp);
    var o0 = await starter.PrepareHuntAsync(U);
    Check(o0.Status == CombatStartStatus.Started && o0.State!.AttackBuffPercent == 0, $"sin buff: pelea normal (ataque {o0.State?.PlayerDamage}, buff {o0.State?.AttackBuffPercent}%)");
    int baseDamage = o0.State!.PlayerDamage;

    await ClearCooldowns();
    await buffs.ActivateAttackAsync(U, 15, 30, Banquete1);
    var o1 = await starter.PrepareHuntAsync(U);
    Check(o1.Status == CombatStartStatus.Started && o1.State!.AttackBuffPercent == 15 && o1.State.PlayerDamage == AttackBuff.Apply(baseDamage, 15) && o1.State.PlayerDamage > baseDamage,
        $"con banquete activo: ataque {baseDamage} -> {o1.State?.PlayerDamage} desde el primer golpe, buff registrado {o1.State?.AttackBuffPercent}%");

    await ClearCooldowns();
    await db.ExecuteAsync("UPDATE player_buffs SET expires_at = now() - interval '1 minute' WHERE discord_id = @id", new { id = (long)U });
    var o2 = await starter.PrepareTravelAsync(U);
    Check(o2.Status == CombatStartStatus.Started && o2.State!.AttackBuffPercent == 0 && o2.State.PlayerDamage == baseDamage, "buff vencido: /travel arranca sin él (ataque otra vez en " + o2.State?.PlayerDamage + ")");
    await ClearCooldowns();
}

Console.WriteLine("\n== 9. El raid toma el buff al unirse ==");
{
    await ClearBuff();
    var p0 = (await RaidModule.BuildSessionAsync(users, items, monsters, zones, buffs, U, "Test")).Participants.Single();
    await buffs.ActivateAttackAsync(U, 15, 30, Banquete1);
    var p1 = (await RaidModule.BuildSessionAsync(users, items, monsters, zones, buffs, U, "Test")).Participants.Single();
    Check(p1.Damage == AttackBuff.Apply(p0.Damage, 15) && p1.Damage > p0.Damage && p1.Defense == p0.Defense && p1.MaxHp == p0.MaxHp, $"el participante entra con ataque {p0.Damage} -> {p1.Damage} (defensa y vida iguales)");
    await ClearBuff();
}

Console.WriteLine("\n== 10. Perfil, tienda y desplegable de curar muestran el buff ==");
{
    await ClearBuff();
    var e0 = await GameModule.BuildProfileEmbedAsync(users, items, zones, buffs, U, "Test", "https://example.com/a.png");
    string ProfLine(Embed e, string label) => ProfileProbe.Line(e, label);
    string atk0 = ProfLine(e0, "ATQ");
    await buffs.ActivateAttackAsync(U, 15, 30, Banquete1);
    var e1 = await GameModule.BuildProfileEmbedAsync(users, items, zones, buffs, U, "Test", "https://example.com/a.png");
    string atk1 = ProfLine(e1, "ATQ");
    int n0 = ProfileProbe.Number(e0, "ATQ");
    int n1 = ProfileProbe.Number(e1, "ATQ");
    Check(!atk0.Contains("🍖") && atk1.Contains("🍖") && atk1.Contains("+15 %") && n1 == AttackBuff.Apply(n0, 15), $"/profile: «{atk0.Trim()}» -> «{atk1.Trim()}»");
    await ClearBuff();

    var shopItems = await ShopModule.LoadShopItemsAsync(items);
    var embed = ShopModule.BuildViewEmbed(shopItems, await buffs.GetItemBuffsAsync());
    string all = string.Join("\n", embed.Fields.Select(f => f.Name + " :: " + f.Value));
    int buffLines = all.Split('\n').Count(l => l.Contains("+15% ATQ 30 min"));
    Check(buffLines == 2 && all.Contains(Banquete1) && all.Contains(Banquete2), $"/shop view: 2 líneas con «+15% ATQ 30 min» (los banquetes), el resto sin buff ({buffLines})");
    Check(!all.Contains("Pan Casero") && !all.Contains("Choripán") && !all.Contains("Vacío al Disco") && all.Contains("Cajón de Pino"), "/shop view: sin la comida eliminada y con las cajas");
    Check(embed.Length < 6000, $"el embed de la tienda mide {embed.Length} caracteres (tope de Discord: 6000)");
    var legacy = ShopModule.BuildViewEmbed(shopItems);
    Check(legacy.Length > 0 && !string.Join("\n", legacy.Fields.Select(f => f.Value)).Contains("ATQ"), "BuildViewEmbed sin diccionario de buffs sigue funcionando (sin línea de buff)");

    var choices = ItemChoices.ForBuy(shopItems, 100000, "", await buffs.GetItemBuffsAsync());
    Check(choices.Count(c => c.Name.Contains("+15% ATQ")) == 2 && choices.All(c => c.Name.Length <= 100), "autocompletar /shop buy: los 2 banquetes dicen «+15% ATQ» y ninguna opción pasa de 100 caracteres");

    // Desplegable de curar (travel / boss).
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
    await Give(Banquete1, 1);
    await Give(Mate, 2);
    var state = Fight("travel", 100);
    var opts = await AdventureModule.LoadHealOptionsAsync(inventory, buffs, U, state);
    var banq = opts.FirstOrDefault(o => o.Name == Banquete1);
    Check(banq is { BuffPercent: 15 } && opts.First(o => o.Name == Mate).BuffPercent == 0 && opts[0].Name == Banquete1, $"opciones de curar: el banquete trae su 15% y va primero (cura más) -> {string.Join(", ", opts.Select(o => CombatHeal.Describe(o)))}");
    Check(CombatHeal.Describe(banq!).Contains("+15% ATQ") && !CombatHeal.Describe(opts.First(o => o.Name == Mate)).Contains("ATQ"), "el texto del desplegable muestra el buff solo en el banquete");
    var ui = AdventureModule.BuildCombatButtons(state, opts);
    var menu = ui.Components.OfType<ActionRowComponent>().SelectMany(row => row.Components).OfType<SelectMenuComponent>().FirstOrDefault();
    Check(menu is not null && menu.Options.Any(o => o.Description.Contains("+15% ATQ")) && menu.Options.All(o => o.Description.Length <= 100 && o.Label.Length <= 100), "el menú de Discord tiene las opciones del buff y respeta los 100 caracteres");
}

Console.WriteLine("\n== 11. Comprar un banquete ==");
{
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
    await SetGold(5000);
    var buy = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, Banquete1, 2);
    Check(buy.Embed is not null && await Have(Banquete1) == 2 && (await Stats()).gold == 5000 - 2 * 2100, $"se compran 2 banquetes por {2 * 2100} oro (oro ahora {(await Stats()).gold})");
    var poor = await ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, U, Banquete2, 2);
    Check(await Have(Banquete2) == 0 && (await Stats()).gold == 800, "sin oro suficiente: no compra y no cobra");
    var sell = await ShopModule.ExecuteSellAsync(items, shop, events, U, Banquete1, 1);
    Check(await Have(Banquete1) == 1 && (await Stats()).gold == 800 + 1575, $"vender uno devuelve 1575 (75%): oro {(await Stats()).gold}");
}

await Cleanup();
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE discord_id = @id", new { id = (long)U }) == 0 && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_buffs WHERE discord_id = @id", new { id = (long)U }) == 0, "usuario de prueba y sus buffs borrados");
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class StubTarget : ICombatReplyTarget
{
    public Task UpdateAsync(Embed embed, MessageComponent components) => Task.CompletedTask;
}
