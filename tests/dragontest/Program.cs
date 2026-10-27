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

// v0.30.0: el dragón (el «horse» de EPIC RPG hecho a nuestra manera): las reglas puras (tiers 1 a 10, bonos por tipo, chances de criar y piedad), la tabla player_dragons (comprar una sola vez y en una sola transacción,
// criar: mismo tier, cooldown, la piedad, 12 crías a la vez y cruzadas sin trabarse), las invitaciones, los bonos dentro de PlayerBonuses (oro, EXP, ataque y defensa), la pantalla, la compra desde la taberna, que el Fuego Nuevo
// no lo toque y el registro de los comandos con Discord.Net de verdad. Contra la base LOCAL (usuarios descartables).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong A = 999000000000099101UL, B = 999000000000099102UL, C = 999000000000099103UL, D = 999000000000099104UL, E = 999000000000099105UL, F = 999000000000099106UL, G = 999000000000099107UL, GHOST = 999000000000099199UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
ulong[] all = [A, B, C, D, E, F, G, GHOST];

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(DragonModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
var users = sp.GetRequiredService<IUserRepository>();
var dragons = sp.GetRequiredService<IDragonRepository>();
var offers = sp.GetRequiredService<IDragonBreedOfferService>();
await using var db = new NpgsqlConnection(conn);

async Task Clean()
{
    foreach (var id in all)
    {
        await db.ExecuteAsync("DELETE FROM player_dragons WHERE discord_id = @id; DELETE FROM inventory WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id;", new { id = (long)id });
    }
}

async Task Player(ulong id, int gold = 10000)
{
    await users.GetOrCreateUserAsync(id);
    await db.ExecuteAsync("UPDATE users SET gold = @g, level = 30, current_hp = max_hp WHERE discord_id = @id", new { id = (long)id, g = gold });
}

// Un dragón ya hecho, sin pasar por la tienda (para probar la cría).
async Task Dragon(ulong id, string kind, int tier, int streak = 0, bool rested = true) =>
    await db.ExecuteAsync("INSERT INTO player_dragons (discord_id, kind, tier, fail_streak, last_bred_at) VALUES (@id, @k, @t, @s, @l) ON CONFLICT (discord_id) DO UPDATE SET kind = @k, tier = @t, fail_streak = @s, last_bred_at = @l",
        new { id = (long)id, k = kind, t = tier, s = streak, l = rested ? (DateTime?)null : DateTime.UtcNow });

async Task SetFood(ulong id, int quantity)
{
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id AND item_id IN (SELECT item_id FROM items WHERE name = @n)", new { id = (long)id, n = DragonRules.FoodItemName });
    if (quantity > 0)
    {
        await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n", new { id = (long)id, q = quantity, n = DragonRules.FoodItemName });
    }
}

async Task<int> FoodRows(ulong id) => await db.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)id, n = DragonRules.FoodItemName });

async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });

await Clean();

Console.WriteLine("== 1. Las reglas puras ==");
bool curveOk = DragonRules.Kinds.All(k => Enumerable.Range(1, 9).All(t => DragonRules.BonusPercent(k, t) < DragonRules.BonusPercent(k, t + 1)) && DragonRules.BonusPercent(k, 10) == DragonRules.MaxPercent(k) && DragonRules.BonusPercent(k, 1) > 0 && DragonRules.BonusPercent(k, 1) < DragonRules.MaxPercent(k) * 0.1);
Check(curveOk && DragonRules.MaxPercent("fuerza") == 12 && DragonRules.MaxPercent("defensa") == 8 && DragonRules.MaxPercent("oro") == 30 && DragonRules.MaxPercent("especial") == 25, "el bono sube en cada tier (curva convexa), el tier 10 da el máximo de su tipo (12 ATQ / 8 DEF / 30 oro / 25 EXP) y el tier 1 menos del 10 % de eso");
Check(DragonRules.BonusesFor("fuerza", 3).AttackPercent == DragonRules.BonusPercent("fuerza", 3) && DragonRules.BonusesFor("defensa", 3).DefensePercent == DragonRules.BonusPercent("defensa", 3) && DragonRules.BonusesFor("oro", 3).GoldPercent == DragonRules.BonusPercent("oro", 3) && DragonRules.BonusesFor("especial", 3).XpPercent == DragonRules.BonusPercent("especial", 3)
    && DragonRules.BonusesFor("oro", 3).AttackPercent == 0 && DragonRules.BonusesFor("oro", 3).XpPercent == 0 && DragonRules.BonusesFor("fuerza", 3).GoldPercent == 0, "cada tipo mejora SOLO lo suyo (la estadística de su especialidad; los extras de tier son aparte)");
Check(DragonRules.TierName(1) == "Cría" && DragonRules.TierName(10) == "Eterno" && DragonRules.TierNumeral(4) == "IV" && Enumerable.Range(1, 10).Select(DragonRules.TierName).Distinct().Count() == 10, "diez nombres de tier distintos, de Cría a Eterno");
Check(DragonRules.BreedChance(1) == 1.0 && DragonRules.BreedChance(2) == 0.60 && DragonRules.BreedChance(9) == 0.0011 && DragonRules.BreedChance(10) == 0 && Enumerable.Range(1, 8).All(t => DragonRules.BreedChance(t) > DragonRules.BreedChance(t + 1)) && Enumerable.Range(1, 8).All(t => DragonRules.PityCap(t) < DragonRules.PityCap(t + 1)), "la chance de criar baja tier a tier (100 / 60 / 30 / 15 / 5 / 2 / 1 / 0,33 / 0,11 %) y el tope de fallos sube");
Check(DragonRules.RollBreed(1, 0, new FixedRandom(0.999)) && !DragonRules.RollBreed(2, 0, new FixedRandom(0.999)) && DragonRules.RollBreed(2, DragonRules.PityCap(2), new FixedRandom(0.999)) && DragonRules.RollBreed(9, DragonRules.PityCap(9), new FixedRandom(0.999)) && !DragonRules.RollBreed(10, 99, new FixedRandom(0)),
    "el tier 1 sube siempre; con mala suerte falla, pero al llegar al tope de fallos seguidos sale SEGURO (también en el tier 9); el tier 10 no sube más");
var stat = new Random(7); int wins = Enumerable.Range(0, 20000).Count(_ => DragonRules.RollBreed(3, 0, stat));
Check(wins is > 5700 and < 6300, $"el tier 3 sube el 30 % de las veces (en 20.000 intentos: {wins})");
double total = Enumerable.Range(1, 9).Sum(t => DragonRules.ExpectedAttempts(t));
Check(Math.Abs(DragonRules.ExpectedAttempts(2) - 1.56) < 0.01 && total is > 400 and < 450, $"con la piedad, subir de tier lleva en promedio 1,6 intentos del 2 al 3 y unos {total:0} intentos hasta el tier 10 (a 2 por día, ~{total / 2:0} días: una meta de meses)");
Check(DragonRules.PityCap(1) == 0 && DragonRules.PityCap(2) == 2 && DragonRules.PityCap(9) == 256 && Enumerable.Range(2, 7).All(t => DragonRules.PityCap(t + 1) == DragonRules.PityCap(t) * 2) && DragonRules.PityCap(10) == 0,
    "el tope de fallos seguidos crece EXPONENCIAL: cada tier pide el doble que el anterior (0 / 2 / 4 / 8 / 16 / 32 / 64 / 128 / 256)");
double soloTotal = Enumerable.Range(1, 9).Sum(t => DragonRules.ExpectedAttempts(t, DragonRules.SoloChanceFactor));
Check(Enumerable.Range(2, 8).All(t => DragonRules.SoloPityCap(t) == DragonRules.PityCap(t) * 3) && soloTotal > total * 2.8 && soloTotal < total * 3.2, $"criar solo lleva de verdad el triple: el tope de fallos también se triplica ({soloTotal:0} intentos contra {total:0} con pareja)");
var kindRoll = new Random(11); var counts = DragonRules.Kinds.ToDictionary(k => k, _ => 0); for (int i = 0; i < 20000; i++) counts[DragonRules.RollKind(kindRoll)]++;
Check(DragonRules.Kinds.Sum(DragonRules.ShopWeight) == 100 && counts["fuerza"] is > 5700 and < 6300 && counts["especial"] is > 1800 and < 2200, $"la tienda da fuerza / defensa / oro 30 % y especial 10 % ({counts["fuerza"]}, {counts["defensa"]}, {counts["oro"]}, {counts["especial"]} de 20.000)");
Check(DragonRules.Percent(12) == "12" && DragonRules.Percent(2.5) == "2,5" && DragonRules.Percent(0.33) == "0,33", "los porcentajes se escriben con coma, sin ceros de más");

Console.WriteLine("\n== 2. Comprar ==");
await Player(A, 10000);
var ghostBuy = await dragons.BuyAsync(GHOST, DragonRules.ShopPrice, "oro");
Check(ghostBuy.Status == DragonBuyStatus.NoAccount, "sin cuenta no se compra");
await Player(B, 100);
var poor = await dragons.BuyAsync(B, DragonRules.ShopPrice, "oro");
Check(poor.Status == DragonBuyStatus.NotEnoughGold && await Gold(B) == 100 && await dragons.GetAsync(B) is null, "sin oro suficiente: no se cobra nada y no nace el dragón");
var bought = await dragons.BuyAsync(A, DragonRules.ShopPrice, "oro");
var mine = await dragons.GetAsync(A);
Check(bought.Status == DragonBuyStatus.Bought && mine is { Kind: "oro", Tier: 1, FailStreak: 0 } && mine.CanBreed && await Gold(A) == 10000 - DragonRules.ShopPrice, "comprar: descuenta el precio exacto y nace un dragón de tier 1, sin fallos y listo para criar");
var again = await dragons.BuyAsync(A, DragonRules.ShopPrice, "fuerza");
Check(again.Status == DragonBuyStatus.AlreadyHave && await Gold(A) == 10000 - DragonRules.ShopPrice && (await dragons.GetAsync(A))!.Kind == "oro", "una segunda compra se rechaza sin cobrar ni cambiar el dragón");
await Player(C, DragonRules.ShopPrice * 3);
var race = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => dragons.BuyAsync(C, DragonRules.ShopPrice, "defensa")));
Check(race.Count(r => r.Status == DragonBuyStatus.Bought) == 1 && race.Count(r => r.Status == DragonBuyStatus.AlreadyHave) == 9 && await Gold(C) == DragonRules.ShopPrice * 2, "10 compras a la vez con oro para tres: sale UN dragón y se cobra UNA vez");
bool threw = false; try { await dragons.BuyAsync(A, 1, "gato"); } catch (ArgumentException) { threw = true; }
Check(threw, "un tipo que no existe es un error de programación, no un dragón raro");

Console.WriteLine("\n== 3. Criar ==");
await Clean(); await Player(A); await Player(B); await Player(C);
await Dragon(A, "oro", 1); await Dragon(B, "fuerza", 1); await Dragon(C, "defensa", 2);
var self = await dragons.BreedAsync(A, A);
var noDrag = await dragons.BreedAsync(A, GHOST);
var mism = await dragons.BreedAsync(A, C);
Check(self.Status == DragonBreedStatus.NoDragon && noDrag.Status == DragonBreedStatus.NoDragon && noDrag.BlockedId == GHOST && mism.Status == DragonBreedStatus.TierMismatch && mism.Tier == 1, "consigo mismo, con alguien sin dragón o de otro tier: se rechaza");
var first = await dragons.BreedAsync(A, B, new FixedRandom(0.999));
var a1 = await dragons.GetAsync(A); var b1 = await dragons.GetAsync(B);
Check(first.Status == DragonBreedStatus.Bred && first.Success && first.SuccessB && first.Tier == 1 && a1 is { Tier: 2, FailStreak: 0 } && b1 is { Tier: 2, FailStreak: 0 } && first.TypeA is { From: "oro", FoodKept: false } && first.TypeB is { From: "fuerza", FoodKept: false } && a1.Kind == "especial" && b1.Kind == "especial" && !a1.CanBreed && !b1.CanBreed && a1.Wait > TimeSpan.FromHours(11),
    "el tier 1 sube seguro: los DOS dragones pasan al tier 2, cada uno se sortea un tipo nuevo (con el azar fijo en 0,999 sale Especial) y los dos descansan ~12 h");
var rest = await dragons.BreedAsync(A, B, new FixedRandom(0));
Check(rest.Status == DragonBreedStatus.OnCooldown && rest.Wait > TimeSpan.FromHours(11) && (await dragons.GetAsync(A))!.Tier == 2, "mientras descansan no se puede volver a criar y no pasa nada");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)A, (long)B } });
var fail = await dragons.BreedAsync(A, B, new FixedRandom(0.999));
Check(fail.Status == DragonBreedStatus.Bred && !fail.Success && !fail.SuccessB && fail.A is { Tier: 2, FailStreak: 1 } && fail.B is { Tier: 2, FailStreak: 1 } && !fail.A.CanBreed, "un intento fallido: siguen en el tier 2, los dos suman un fallo y descansan igual");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)A, (long)B } });
await db.ExecuteAsync("UPDATE player_dragons SET fail_streak = @cap WHERE discord_id = @id", new { id = (long)B, cap = DragonRules.PityCap(2) });
var pity = await dragons.BreedAsync(A, B, new FixedRandom(0.999));
Check(!pity.Success && pity.SuccessB && pity.A is { Tier: 2, FailStreak: 2 } && pity.B is { Tier: 3, FailStreak: 0 } && !pity.A.CanBreed && !pity.B.CanBreed, "cada dragón con su propio contador: el que llegó al tope (B) sube SEGURO aunque la suerte sea mala y el otro (A, con un solo fallo) se queda; los dos descansan");
await Dragon(A, "oro", 10); await Dragon(B, "oro", 10);
Check((await dragons.BreedAsync(A, B)).Status == DragonBreedStatus.MaxTier, "dos dragones del tier 10 no tienen nada más que criar");
await Dragon(A, "oro", 4); await Dragon(B, "oro", 4);
var raceBreed = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? dragons.BreedAsync(A, B, new FixedRandom(0.999)) : dragons.BreedAsync(B, A, new FixedRandom(0.999))));
Check(raceBreed.Count(r => r.Status == DragonBreedStatus.Bred) == 1 && raceBreed.Count(r => r.Status == DragonBreedStatus.OnCooldown) == 11 && (await dragons.GetAsync(A))!.FailStreak + (await dragons.GetAsync(B))!.FailStreak == 2,
    $"12 crías a la vez (la mitad cruzadas, A con B y B con A): sin trabarse, se hace UNA sola y las demás encuentran el descanso ({raceBreed.Count(r => r.Status == DragonBreedStatus.Bred)} hechas, {raceBreed.Count(r => r.Status == DragonBreedStatus.OnCooldown)} en descanso)");

Console.WriteLine("\n== 4. Los bonos en PlayerBonuses ==");
var none = PlayerBonuses.None; var withGold = none with { Dragon = DragonRules.BonusesFor("oro", 10) };
var withXp = none with { Dragon = DragonRules.BonusesFor("especial", 10) }; var withAtk = none with { Dragon = DragonRules.BonusesFor("fuerza", 10) }; var withDef = none with { Dragon = DragonRules.BonusesFor("defensa", 10) };
Check(Math.Abs(withGold.GoldMultiplier / none.GoldMultiplier - (1 + DragonRules.BonusesFor("oro", 10).GoldPercent / 100)) < 1e-9 && Math.Abs(withXp.XpMultiplier / none.XpMultiplier - (1 + DragonRules.BonusesFor("especial", 10).XpPercent / 100)) < 1e-9 && Math.Abs(withAtk.AttackMultiplier - (1 + DragonRules.BonusesFor("fuerza", 10).AttackPercent / 100)) < 1e-9 && Math.Abs(withDef.DefenseMultiplier - (1 + DragonRules.BonusesFor("defensa", 10).DefensePercent / 100)) < 1e-9
    && withGold.GoldMultiplier > 1.4, $"un dragón de tier 10 multiplica el oro, la EXP, el ataque o la defensa según su tipo, con la Llama eterna y las mejoras de tier (oro ×{withGold.GoldMultiplier:0.000})");
Check(withGold.AttackMultiplier == 1.0 + DragonRules.BonusesFor("oro", 10).AttackPercent / 100 && withGold.XpMultiplier / none.XpMultiplier == 1 + DragonRules.BonusesFor("oro", 10).XpPercent / 100 && !withGold.IsNone && none.IsNone && (none with { Dragon = DragonBonuses.None }).IsNone, "cada tipo toca solo lo suyo; con dragón ya no es «sin bonos»");
await Clean(); await Player(D); await dragons.BuyAsync(D, 1, "oro"); await db.ExecuteAsync("UPDATE player_dragons SET tier = 6 WHERE discord_id = @id", new { id = (long)D });
var svc = sp.GetRequiredService<IPlayerBonusService>();
var bD = await svc.GetAsync(D, 0); var bNo = await svc.GetAsync(GHOST, 0);
Check(bD.Dragon is not null && Math.Abs(bD.Dragon.GoldPercent - DragonRules.BonusPercent("oro", 6)) < 1e-9 && bD.GoldMultiplier > 1.1 && bNo.Dragon is null && bNo.GoldMultiplier == 1.0, $"el servicio de bonos lee el dragón de la base (tier 6 de oro: +{DragonRules.BonusPercent("oro", 6)} %) y sin dragón queda en ×1");

Console.WriteLine("\n== 5. La pantalla ==");
var empty = await DragonModule.BuildDragonEmbedAsync(dragons, GHOST);
Check(empty.Description.Contains("/taberna") && empty.Description.Contains("3.000") && empty.Fields.Length == 0, "sin dragón: dice cómo conseguirlo (taberna, 3.000 de oro)");
var view = await DragonModule.BuildDragonEmbedAsync(dragons, D);
var ladder = view.Fields.Single(f => f.Name.Contains("mejora nueva")).Value.Split('\n');
Check(view.Description.Contains("Tier **6/10**") && view.Description.Contains("Curtido") && ladder.Length == 10 && ladder.Count(l => l.StartsWith("✅")) == 6 && ladder.Count(l => l.StartsWith("🔒")) == 4 && ladder[3].Contains("Aliento final") && view.Description.Contains("Nivel **0/60**") && view.Fields.Any(f => f.Name.Contains("Criar") && f.Value.Contains("/dragon-breed")) && view.Fields.Any(f => f.Name.Contains("Entrenar")) && view.Length < 6000,
    "con dragón: el tier, el nivel y su nombre, las 10 mejoras (✅ las 6 que ya desbloqueó, 🔒 las 4 que faltan), el entrenamiento y cómo va la cría");
await Dragon(D, "oro", 10);
var maxView = await DragonModule.BuildDragonEmbedAsync(dragons, D);
Check(maxView.Description.Contains("tier máximo") && !maxView.Fields.Any(f => f.Name.Contains("Criar")), "en el tier 10: lo dice y ya no ofrece criar");

Console.WriteLine("\n== 6. Invitar y criar ==");
await Clean(); await Player(A); await Player(B); await Player(C);
await Dragon(A, "oro", 1); await Dragon(B, "defensa", 1); await Dragon(C, "fuerza", 3);
Task<BreedInvite> Invite(ulong from, ulong to, bool bot = false) => DragonModule.PrepareBreedAsync(dragons, offers, from, to, bot);
Check((await Invite(A, A)).Error is not null && (await Invite(A, B, true)).Error!.Contains("bots") && (await Invite(GHOST, A)).Error!.Contains("/taberna") && (await Invite(A, GHOST)).Error!.Contains("no tiene dragón") && (await Invite(A, C)).Error!.Contains("mismo tier"),
    "las invitaciones imposibles se rechazan diciendo por qué: uno mismo, un bot, sin dragón (vos o el otro) y distinto tier");
var inv = await Invite(A, B);
var buttons = inv.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(inv.Error is null && inv.Content == $"<@{B}>" && buttons.Count == 2 && buttons[0].CustomId.StartsWith("dragon_accept:") && buttons[1].CustomId.StartsWith("dragon_decline:") && buttons[0].CustomId[14..] == buttons[1].CustomId[15..] && inv.Embed!.Description.Contains("100"), "una invitación válida pinguea al invitado y trae Aceptar y Rechazar con el mismo id de oferta");
var offerId = Guid.Parse(buttons[0].CustomId[14..]);
var peek = offers.Peek(offerId);
Check(peek is { } o && o.FromId == A && o.ToId == B, "la oferta existe en el servicio con los dos jugadores");
var takes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => offers.TryTake(offerId))));
Check(takes.Count(t => t is not null) == 1 && offers.Peek(offerId) is null, "20 clicks a la vez en «Aceptar»: la oferta la recibe UNO solo");
var inv2 = await Invite(A, B); var inv3 = await Invite(A, B);
Check(offers.Peek(Guid.Parse(inv2.Components!.Components.OfType<ActionRowComponent>().First().Components.OfType<ButtonComponent>().First().CustomId[14..])) is null, "una invitación nueva del mismo jugador reemplaza a la anterior");
var resolved = await DragonModule.ResolveBreedAsync(dragons, new DragonBreedOffer(Guid.NewGuid(), A, B, DateTime.UtcNow.AddMinutes(1)));
Check(resolved.Description.Contains("Salió bien") && resolved.Description.Contains("Tier 2") && (await dragons.GetAsync(A))!.Tier == 2 && (await dragons.GetAsync(B))!.Tier == 2, "al aceptar, el tier 1 sube seguro: el mensaje lo cuenta y los dos quedan en el tier 2");
var resolvedAgain = await DragonModule.ResolveBreedAsync(dragons, new DragonBreedOffer(Guid.NewGuid(), A, B, DateTime.UtcNow.AddMinutes(1)));
Check(resolvedAgain.Description.Contains("descansando"), "si algo cambió mientras esperaban (ya descansan), la cría no se hace y el mensaje lo dice");
Check((await Invite(A, B)).Error!.Contains("descansando"), "y tampoco se puede invitar a quien descansa");

Console.WriteLine("\n== 7. La taberna ==");
await Clean(); await Player(E, 5000); await Player(F, 100);
var items = sp.GetRequiredService<IItemRepository>(); var inventory = sp.GetRequiredService<IInventoryRepository>(); var buffs = sp.GetRequiredService<IBuffRepository>();
var boxCtx = sp.GetRequiredService<IBoxContextService>(); var exchange = sp.GetRequiredService<IDropExchangeRepository>();
List<SelectMenuOption>? Options(TabernaScene s, string prefix) => s.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().FirstOrDefault(m => m.CustomId.StartsWith(prefix))?.Options.ToList();
var sceneBefore = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, E, null, boxCtx, exchange, svc, dragons);
var boxOpts = Options(sceneBefore, "taberna_box");
Check(boxOpts is not null && boxOpts.Any(o => o.Value == DragonModule.ShopKey && o.Label.Contains("Dragón") && o.Description.Contains("3.000")) && boxOpts.Count <= 25, "la lista de cajas de la taberna ofrece el dragón (con su precio) mientras no tengas uno");
TabernaScene sceneEn; using (Loc.Use("en")) { sceneEn = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, E, null, boxCtx, exchange, svc, dragons); }
Check(Options(sceneEn, "taberna_box")!.Any(o => o.Label == "🐲 Breeding dragon" && o.Description.Contains("Once only")), "y en inglés");
var combat = sp.GetRequiredService<ICombatSessionService>(); var shop = sp.GetRequiredService<IShopRepository>(); var events = sp.GetRequiredService<IGameEvents>();
Task<string> Act(ulong id, string value) => TabernaModule.ExecuteActionAsync("taberna_box", users, items, inventory, shop, buffs, combat, events, id, value, boxCtx, null, svc, dragons);
var poorTalk = await Act(F, DragonModule.ShopKey);
Check(poorTalk.Contains("3.000") && await dragons.GetAsync(F) is null && await Gold(F) == 100, "sin oro: el tabernero lo dice y no cobra");
var buyTalk = await Act(E, DragonModule.ShopKey);
var owned = await dragons.GetAsync(E);
Check(buyTalk.Contains("Compraste un dragón") && owned is { Tier: 1 } && await Gold(E) == 5000 - DragonRules.ShopPrice && buyTalk.Contains(DragonRules.KindName(owned.Kind)), $"comprarlo desde la taberna: lo cobra, nace el dragón y dice de qué tipo salió ({owned?.Kind})");
var sceneAfter = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, E, null, boxCtx, exchange, svc, dragons);
Check(!(Options(sceneAfter, "taberna_box") ?? []).Any(o => o.Value == DragonModule.ShopKey), "con un dragón ya no se ofrece otro");
Check((await Act(E, DragonModule.ShopKey)).Contains("ya tenés un dragón") && await Gold(E) == 5000 - DragonRules.ShopPrice, "pedir otro igual: «ya tenés un dragón» y no cobra");

Console.WriteLine("\n== 8. El Fuego Nuevo no lo toca ==");
await Player(G); await dragons.BuyAsync(G, 1, "especial"); await SetFood(G, 4);
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id; UPDATE player_dragons SET tier = 7, fail_streak = 3 WHERE discord_id = @id", new { id = (long)G });
var renewed = await sp.GetRequiredService<IFuegoNuevoRepository>().RenewAsync(G, 0, "Ninja");
Check(renewed.Status == RenewStatus.Ok && await dragons.GetAsync(G) is { Kind: "especial", Tier: 7, FailStreak: 3 }, $"renacer deja el dragón como estaba (tipo, tier y fallos): dura toda la vida de la cuenta ({renewed.Status})");
Check(await dragons.CountFoodAsync(G) == 4, "y la Comida de Dragón tampoco se pierde al renacer (se quedan los huevos, la comida de las mascotas y la del dragón)");

Console.WriteLine("\n== 9. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(DragonModule).Assembly, sp);
var slashDragon = interactions.SlashCommands.Single(c => c.Name == "dragon"); var slashBreed = interactions.SlashCommands.Single(c => c.Name == "dragon-breed");
Check(slashDragon.Parameters.Count == 0 && slashBreed.Parameters.Count == 1 && slashBreed.Parameters[0].Name == "amigo" && slashDragon.Description.Length <= 100 && slashBreed.Description.Length <= 100, $"/dragon y /dragon-breed existen, con descripciones de {slashDragon.Description.Length} y {slashBreed.Description.Length} caracteres (≤ 100)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var rAccept = interactions.SearchComponentCommand(Click($"dragon_accept:{Guid.NewGuid()}")); var rDecline = interactions.SearchComponentCommand(Click($"dragon_decline:{Guid.NewGuid()}"));
var rTrain = interactions.SearchComponentCommand(Click($"dragon_train:{A}"));
Check(rAccept.IsSuccess && rAccept.Command.MethodName == "HandleAcceptAsync" && rDecline.IsSuccess && rDecline.Command.MethodName == "HandleDeclineAsync" && rTrain.IsSuccess && rTrain.Command.MethodName == "HandleTrainButtonAsync", "los botones dragon_accept / dragon_decline / dragon_train llegan a su método (sin que el grupo les cambie el id)");
var slashTrain = interactions.SlashCommands.Single(c => c.Name == "dragon-train");
Check(slashTrain.Parameters.Count == 1 && slashTrain.Parameters[0].Name == "veces" && !slashTrain.Parameters[0].IsRequired && slashTrain.Description.Length <= 100, $"/dragon-train existe con «veces» opcional y {slashTrain.Description.Length} caracteres de descripción (≤ 100)");
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "dragon", "dragón", "dragones" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Any(c => c.Command.Name == "dragon")) && texts.Search("dragon breed @amigo").Commands.Any(c => c.Command.Name == "dragon breed") && texts.Search("dragon criar @amigo").Commands.Any(c => c.Command.Name == "dragon breed") && texts.Search("dragon train 3").Commands.Any(c => c.Command.Name == "dragon train") && texts.Search("dragon entrenar").Commands.Any(c => c.Command.Name == "dragon train"), "aa dragon (y dragón / dragones), aa dragon breed @amigo (o «criar») y aa dragon train [veces] (o «entrenar») existen");
Check(!FightLock.IsAllowed("dragon") && !FightLock.IsAllowed("dragon-breed") && !FightLock.IsAllowed("dragon-train"), "en plena pelea los comandos del dragón quedan bloqueados (como todo lo que no es de la pelea)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(DragonModule)); ActivatorUtilities.CreateInstance(sp, typeof(TabernaModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built && HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("`/dragon`")), "los módulos se construyen con el DI real y /info nombra /dragon");

Console.WriteLine("\n== 10. Una mejora nueva en cada tier ==");
var kinds = DragonRules.Kinds;
bool perksGrow = kinds.All(k => Enumerable.Range(1, 9).All(t => { var lo = DragonRules.BonusesFor(k, t); var hi = DragonRules.BonusesFor(k, t + 1); return hi.AttackPercent >= lo.AttackPercent && hi.DefensePercent >= lo.DefensePercent && hi.GoldPercent >= lo.GoldPercent && hi.XpPercent >= lo.XpPercent && hi.GatherPercent >= lo.GatherPercent && hi.DropPercent >= lo.DropPercent && hi.HpPercent >= lo.HpPercent && (hi.LastStand || !lo.LastStand); }));
var t1 = DragonRules.BonusesFor("fuerza", 1); var t2 = DragonRules.BonusesFor("fuerza", 2); var t3 = DragonRules.BonusesFor("fuerza", 3); var t4 = DragonRules.BonusesFor("fuerza", 4); var t5 = DragonRules.BonusesFor("fuerza", 5);
var t6 = DragonRules.BonusesFor("fuerza", 6); var t7 = DragonRules.BonusesFor("fuerza", 7); var t8 = DragonRules.BonusesFor("fuerza", 8); var t9 = DragonRules.BonusesFor("fuerza", 9);
Check(perksGrow && DragonRules.Perks.Count == 10 && DragonRules.Perks.Select(p => p.Name).Distinct().Count() == 10 && DragonRules.Perks.Select(p => p.Tier).SequenceEqual(Enumerable.Range(1, 10)), "cada tier tiene su mejora (10 distintas) y los extras solo suben: nunca se pierde lo que dio un tier anterior");
Check(t1.DropPercent == 0 && t1.GatherPercent == 0 && !t1.LastStand && t2.DropPercent == 4 && t2.GatherPercent == 0 && t3.GatherPercent == 5 && !t3.LastStand && t4.LastStand && t4.HpPercent == 0 && t5.HpPercent == 4 && t5.XpPercent == 0 && t6.XpPercent == 5 && t6.GoldPercent == 0 && t7.GoldPercent == 5
    && t7.DefensePercent == 0 && t8.DefensePercent == 3 && t8.AttackPercent == DragonRules.BonusPercent("fuerza", 8) + 3 && t9.DropPercent == 7 && t9.GatherPercent == 8 && t9.GoldPercent == 8 && t9.XpPercent == 8,
    "lo que desbloquea cada tier: II drop +4 %, III recolección +5 %, IV Aliento final, V vida +4 %, VI EXP +5 %, VII oro +5 %, VIII ATQ y DEF +3 %, IX +3 % a drop, recolección, oro y EXP");
var eterno = DragonRules.BonusesFor("oro", 10, 0);
Check(DragonRules.TypePercent("oro", 10, 0) == Math.Round(30 * DragonRules.EternalMultiplier, 1) && eterno.GoldPercent == DragonRules.TypePercent("oro", 10, 0) + 5 + 3 && DragonRules.TypePercent("oro", 9, 0) == DragonRules.BonusPercent("oro", 9), "en el tier 10 la Llama eterna hace rendir ×1,25 el bono de su tipo (y solo ahí)");
var perkNone = PlayerBonuses.None;
Check(Math.Abs((perkNone with { Dragon = t2 }).HuntDropMultiplier / perkNone.HuntDropMultiplier - 1.04) < 1e-9 && Math.Abs((perkNone with { Dragon = t2 }).TravelDropMultiplier / perkNone.TravelDropMultiplier - 1.04) < 1e-9 && Math.Abs((perkNone with { Dragon = t3 }).ChopMultiplier / perkNone.ChopMultiplier - 1.05) < 1e-9
    && Math.Abs((perkNone with { Dragon = t3 }).MineMultiplier / perkNone.MineMultiplier - 1.05) < 1e-9 && Math.Abs((perkNone with { Dragon = t5 }).MaxHpMultiplier - 1.04) < 1e-9 && (perkNone with { Dragon = t1 }).HuntDropMultiplier == perkNone.HuntDropMultiplier,
    "las mejoras entran en PlayerBonuses: drop de /hunt y /travel, cantidad de /chop y /mine y vida máxima");

Console.WriteLine("\n== 11. Entrenar ==");
Check(DragonRules.LevelCap(1) == 10 && DragonRules.LevelCap(5) == 50 && DragonRules.LevelCap(10) == 100 && DragonRules.MaxLevel == 100 && DragonRules.LevelBonusPercent("oro", 100) == 15 && DragonRules.LevelBonusPercent("fuerza", 100) == 6 && DragonRules.LevelBonusPercent("oro", 0) == 0
    && DragonRules.TypePercent("oro", 5, 50) > DragonRules.TypePercent("oro", 5, 0) && DragonRules.BonusesFor("oro", 1, 99).GoldPercent == DragonRules.TypePercent("oro", 1, 10), "el nivel máximo es tier × 10, cada nivel suma al bono de su tipo y un nivel de más (pasado el tope del tier) no cuenta");
bool dearer = Enumerable.Range(0, 99).All(l => DragonRules.TrainCost(l) < DragonRules.TrainCost(l + 1));
long fullPrice = DragonRules.TrainCostBetween(0, 100);
Check(dearer && DragonRules.TrainCost(0) == 50 && DragonRules.TrainCost(9) is > 3500 and < 4500 && DragonRules.TrainCost(99) is > 280000 and < 350000 && fullPrice is > 9_000_000 and < 14_000_000 && DragonRules.TrainCostBetween(0, 10) is > 10_000 and < 25_000,
    $"cada nivel sale más caro que el anterior (nivel 1: {DragonRules.TrainCost(0)}, nivel 10: {DragonRules.TrainCost(9)}, nivel 100: {DragonRules.TrainCost(99)}); hasta el 10 son {DragonRules.TrainCostBetween(0, 10):N0} y hasta el 100 unos {fullPrice:N0} de oro");
await Clean(); await Player(A, 100000); await Player(B, 10); await Player(C, 1_000_000);
await Dragon(A, "oro", 1);
Check((await dragons.TrainAsync(GHOST, 1)).Status == DragonTrainStatus.NoDragon && (await dragons.TrainAsync(B, 1)).Status == DragonTrainStatus.NoDragon, "sin cuenta o sin dragón no se entrena");
var one = await dragons.TrainAsync(A, 1);
Check(one.Status == DragonTrainStatus.Trained && one.LevelsGained == 1 && one.GoldSpent == DragonRules.TrainCost(0) && one.Dragon!.Level == 1 && await Gold(A) == 100000 - DragonRules.TrainCost(0) && one.NextCost == DragonRules.TrainCost(1), "entrenar un nivel: cobra lo que dice la tabla, sube el nivel y avisa lo que cuesta el próximo");
var five = await dragons.TrainAsync(A, 5);
long expectFive = Enumerable.Range(1, 5).Sum(l => (long)DragonRules.TrainCost(l));
Check(five.LevelsGained == 5 && five.GoldSpent == expectFive && five.Dragon!.Level == 6 && await Gold(A) == 100000 - DragonRules.TrainCost(0) - (int)expectFive, "entrenar varios niveles juntos cobra cada uno a su precio (cada nivel más caro)");
var capped = await dragons.TrainAsync(A, 50);
Check(capped.Status == DragonTrainStatus.Trained && capped.LevelsGained == 4 && capped.Dragon!.Level == 10 && capped.NextCost == 0, "pidiendo de más, se entrena hasta el tope del tier (nivel 10 en el tier 1) y no pasa de ahí");
int goldBefore = await Gold(A);
var atCap = await dragons.TrainAsync(A, 1);
Check(atCap.Status == DragonTrainStatus.AtCap && await Gold(A) == goldBefore && (await dragons.GetAsync(A))!.Level == 10, "en el tope del tier no se entrena ni se cobra");
await db.ExecuteAsync("UPDATE player_dragons SET tier = 2 WHERE discord_id = @id", new { id = (long)A });
Check((await dragons.TrainAsync(A, 1)).Status == DragonTrainStatus.Trained && (await dragons.GetAsync(A))!.Level == 11, "al subir de tier se abren 10 niveles más");
await Dragon(B, "fuerza", 3); await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)B, g = DragonRules.TrainCost(0) - 1 });
var poorTrain = await dragons.TrainAsync(B, 1);
Check(poorTrain.Status == DragonTrainStatus.NotEnoughGold && poorTrain.NextCost == DragonRules.TrainCost(0) && poorTrain.GoldLeft == DragonRules.TrainCost(0) - 1 && (await dragons.GetAsync(B))!.Level == 0, "sin oro para el siguiente nivel: no cobra, no entrena y dice cuánto falta");
int threeLevels = DragonRules.TrainCost(0) + DragonRules.TrainCost(1) + DragonRules.TrainCost(2);
await Dragon(C, "defensa", 5); await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id", new { id = (long)C, g = threeLevels });
var partial = await dragons.TrainAsync(C, 10);
Check(partial.LevelsGained == 3 && partial.GoldSpent == threeLevels && await Gold(C) == 0 && partial.Dragon!.Level == 3, "con oro para 3 niveles y pidiendo 10: entrena 3 y deja el oro en cero (nunca en negativo)");
await db.ExecuteAsync("UPDATE users SET gold = @g WHERE discord_id = @id; UPDATE player_dragons SET level = 0 WHERE discord_id = @id", new { id = (long)C, g = threeLevels });
var trainRace = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => dragons.TrainAsync(C, 1)));
Check(trainRace.Count(r => r.Status == DragonTrainStatus.Trained) == 3 && trainRace.Count(r => r.Status == DragonTrainStatus.NotEnoughGold) == 7 && await Gold(C) == 0 && (await dragons.GetAsync(C))!.Level == 3, "10 entrenamientos a la vez con oro para tres niveles: se entrenan exactamente 3 y el oro no queda en negativo");
Check((await DragonModule.ExecuteTrainAsync(dragons, GHOST, 1)).Contains("/taberna") && (await DragonModule.ExecuteTrainAsync(dragons, B, 1)).Contains("Te falta oro") && (await DragonModule.ExecuteTrainAsync(dragons, A, 1)).Contains("nivel 12/20"), "los mensajes del entrenamiento: sin dragón, sin oro y éxito (con el nivel nuevo y el tope del tier)");
var trainView = await DragonModule.BuildDragonViewAsync(dragons, B);
var trainButton = trainView.Components!.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Single();
await Dragon(B, "fuerza", 1); await db.ExecuteAsync("UPDATE player_dragons SET level = 10 WHERE discord_id = @id", new { id = (long)B });
Check(trainButton.CustomId == $"dragon_train:{B}" && trainButton.Label.StartsWith("Entrenar (") && (await DragonModule.BuildDragonViewAsync(dragons, B)).Components is null, "la pantalla trae el botón «Entrenar (costo)» y en el tope del tier ya no lo trae");

Console.WriteLine("\n== 12. El Aliento final (tier 4) en el resolutor de combate ==");
var ghostProfile = new CombatantProfile(50, 0, "Guerrero", ClassPassives.For("Guerrero") with { LastStand = true });
var plainProfile = new CombatantProfile(50, 0, "Guerrero", ClassPassives.For("Guerrero"));
TurnResult Hit(CombatantProfile p, AbilityState st, int hp, PlayerAction action = PlayerAction.Attack)
{
    // El esquive es al azar: se repite hasta que el monstruo pegue (con 500 de daño siempre mata si pega).
    for (int i = 0; i < 200; i++) { var r = CombatTurnResolver.ResolveTurn(p, st, action, hp, 100, 100000, 500); if (r.MonsterHit is { Damage: > 0 }) return r; }
    throw new InvalidOperationException("el monstruo nunca pegó");
}
var saved = Hit(ghostProfile, default, 5);
Check(saved.PlayerHpAfter == 1 && saved.LastStand && saved.StatusAfter.LastStandUsed, "el golpe que te mataba te deja con 1 de vida y gasta el Aliento final");
var secondHit = Hit(ghostProfile, saved.StatusAfter, 1);
Check(secondHit.PlayerHpAfter == 0 && !secondHit.LastStand, "es UNA vez por pelea: con el aliento ya gastado, el siguiente golpe mata");
var noPerk = Hit(plainProfile, default, 5);
Check(noPerk.PlayerHpAfter == 0 && !noPerk.LastStand, "sin dragón de tier 4 el golpe mata como siempre");
var abilityKeeps = ClassAbilities.For("Guerrero") is not null ? Hit(new CombatantProfile(50, 0, "Guerrero", ClassPassives.For("Guerrero") with { LastStand = true }), new AbilityState(0, 0, true), 5, PlayerAction.Ability) : null;
Check(abilityKeeps is not null && abilityKeeps.StatusAfter.LastStandUsed && abilityKeeps.PlayerHpAfter == 0, "usar la habilidad de clase no «recarga» el Aliento final (el estado gastado se conserva)");
var survives = CombatTurnResolver.ResolveTurn(ghostProfile, default, PlayerAction.Attack, 100, 100, 100000, 3);
Check(!survives.LastStand && survives.PlayerHpAfter > 0 && !survives.StatusAfter.LastStandUsed, "si el golpe no te mataba, el aliento no se gasta");
var weapon = (await sp.GetRequiredService<IItemRepository>().GetByNameAsync("Espada de Madera"))!;
await Player(D); await Dragon(D, "oro", 4); var pD = (await users.GetByDiscordIdAsync(D))!;
var profile4 = PlayerCombatProfileCalculator.Resolve(pD, weapon, null, 0, PlayerBonuses.None with { Dragon = DragonRules.BonusesFor("oro", 4) });
var profile3 = PlayerCombatProfileCalculator.Resolve(pD, weapon, null, 0, PlayerBonuses.None with { Dragon = DragonRules.BonusesFor("oro", 3) });
var duel = PlayerCombatProfileCalculator.Resolve(pD, weapon, null);
Check(profile4.Passives.LastStand && !profile3.Passives.LastStand && !duel.Passives.LastStand, "el perfil de combate trae el Aliento final desde el tier 4 y SOLO contra monstruos (sin bonos, como en duelos y Arena, no)");
var turnEmbedSuffix = typeof(AdventureModule).GetMethod("LastStandSuffix", BindingFlags.NonPublic | BindingFlags.Static)!;
string lastLine = (string)turnEmbedSuffix.Invoke(null, [saved])!; string lastLineEn; using (Loc.Use("en")) { lastLineEn = (string)turnEmbedSuffix.Invoke(null, [saved])!; }
Check(lastLine.Contains("Aliento final") && lastLineEn.Contains("Last breath") && (string)turnEmbedSuffix.Invoke(null, [noPerk])! == string.Empty, "el texto del turno avisa el Aliento final (en español y en inglés) solo cuando saltó");

Console.WriteLine("\n== 13. Los emojis propios del dragón (se levantan por nombre) ==");
string beforeGold = DragonRules.KindEmoji("oro"), beforeDragon = UiEmojis.Dragon;
UiEmojis.Set([("dragon_oro", "<:dragon_oro:123456789012345678>"), ("ui_dragon", "<:ui_dragon:123456789012345679>")]);
Check(beforeGold == "💰" && beforeDragon == "🐲" && DragonRules.KindEmoji("oro") == "<:dragon_oro:123456789012345678>" && DragonRules.KindEmoji("fuerza") == "🗡️" && DragonRules.KindEmoji("defensa") == "🛡️" && DragonRules.KindEmoji("especial") == "✨" && UiEmojis.Dragon == "<:ui_dragon:123456789012345679>",
    "sin emojis subidos valen los de siempre; con «dragon_oro» y «ui_dragon» subidos esos se usan solos y los demás tipos siguen con el suyo de siempre");
UiEmojis.Set([]);

Console.WriteLine("\n== 14. El nombre, el perfil, /cd y la pantalla del dragón ==");
Check(DragonRules.CleanName("  Chispa  ") == "Chispa" && DragonRules.CleanName("Fuego   Eterno") == "Fuego Eterno" && DragonRules.CleanName("Ñandú 2") == "Ñandú 2" && DragonRules.CleanName("a") is null && DragonRules.CleanName(new string('a', 25)) is null && DragonRules.CleanName(new string('a', 24)) is not null
    && DragonRules.CleanName("@everyone") is null && DragonRules.CleanName("<:x:123>") is null && DragonRules.CleanName("**hola**") is null && DragonRules.CleanName("`x`") is null && DragonRules.CleanName("   ") is null && DragonRules.CleanName(null) is null,
    "el nombre: 2 a 24 caracteres (sin espacios de más), con letras, números, espacios y . - ' !; nada de menciones, emojis, negritas ni símbolos");
await Clean(); await Player(A, 20000); await Player(B, 100); await Player(C, DragonRules.RenamePrice * 3); await Player(D, 50000);
await Dragon(A, "fuerza", 2); await Dragon(B, "oro", 1); await Dragon(C, "defensa", 3);
var rn1 = await dragons.RenameAsync(A, "Chispa", DragonRules.RenamePrice);
Check(rn1.Status == DragonRenameStatus.Renamed && rn1.Dragon!.Name == "Chispa" && rn1.GoldLeft == 20000 - DragonRules.RenamePrice && await Gold(A) == 20000 - DragonRules.RenamePrice && (await dragons.GetAsync(A))!.Name == "Chispa", "ponerle nombre cobra el precio exacto (5.000) y lo guarda");
var rnSame = await dragons.RenameAsync(A, "Chispa", DragonRules.RenamePrice);
Check(rnSame.Status == DragonRenameStatus.SameName && await Gold(A) == 20000 - DragonRules.RenamePrice, "ponerle el mismo nombre no cobra nada");
var rn2 = await dragons.RenameAsync(A, "Brasa", DragonRules.RenamePrice);
Check(rn2.Status == DragonRenameStatus.Renamed && (await dragons.GetAsync(A))!.Name == "Brasa" && await Gold(A) == 20000 - 2 * DragonRules.RenamePrice, "cambiárselo de nuevo cuesta lo mismo");
Check((await dragons.RenameAsync(GHOST, "Chispa", DragonRules.RenamePrice)).Status == DragonRenameStatus.NoDragon && (await dragons.RenameAsync(D, "Chispa", DragonRules.RenamePrice)).Status == DragonRenameStatus.NoDragon && await Gold(D) == 50000, "sin cuenta o sin dragón no se cobra");
var rnPoor = await dragons.RenameAsync(B, "Chispa", DragonRules.RenamePrice);
Check(rnPoor.Status == DragonRenameStatus.NotEnoughGold && rnPoor.GoldLeft == 100 && await Gold(B) == 100 && (await dragons.GetAsync(B))!.Name is null, "sin oro: no cobra ni cambia el nombre");
var rnRace = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => dragons.RenameAsync(C, $"Nombre{i}", DragonRules.RenamePrice)));
Check(rnRace.Count(r => r.Status == DragonRenameStatus.Renamed) == 3 && rnRace.Count(r => r.Status == DragonRenameStatus.NotEnoughGold) == 7 && await Gold(C) == 0, "10 renombres a la vez con oro para tres: se cobran exactamente tres y el oro no queda en negativo");
Check((await DragonModule.ExecuteRenameAsync(dragons, A, "@everyone")).Contains("no sirve") && (await DragonModule.ExecuteRenameAsync(dragons, A, "Brasa")).Contains("no te cobro") && (await DragonModule.ExecuteRenameAsync(dragons, A, "Ceniza")).Contains("Ceniza") && (await DragonModule.ExecuteRenameAsync(dragons, B, "Chispa")).Contains("Te falta oro") && (await DragonModule.ExecuteRenameAsync(dragons, D, "Chispa")).Contains("/taberna"),
    "los mensajes de /dragon-name: nombre inválido, mismo nombre, éxito, sin oro y sin dragón");

var viewNamed = await DragonModule.BuildDragonViewAsync(dragons, A);
var viewUnnamed = await DragonModule.BuildDragonViewAsync(dragons, B);
Check(viewNamed.Embed.Description.Contains("**Ceniza** (Dragoncito)") && !viewNamed.Embed.Description.Contains("/dragon-name") && viewUnnamed.Embed.Description.Contains("**/dragon-name**"), "la pantalla muestra el nombre del dragón (con su tier) y, si no tiene, avisa cómo ponérselo");
string allFieldNames = string.Join(" ", viewNamed.Embed.Fields.Select(f => f.Name));
Check(!allFieldNames.Contains("🎁") && !allFieldNames.Contains("🏋") && !allFieldNames.Contains("🥚") && viewNamed.Embed.Fields.Any(f => f.Name == "Criar") && viewNamed.Embed.Fields.Any(f => f.Name == "Entrenar") && viewNamed.Embed.Fields.Any(f => f.Name == "Una mejora nueva en cada tier"), "los títulos de los campos ya no llevan 🎁, 🏋️ ni 🥚");
Check(!viewNamed.Embed.Description.Contains("Descansa") && !string.Join(" ", viewNamed.Embed.Fields.Select(f => f.Value)).Contains("Descansa") && !string.Join(" ", viewNamed.Embed.Fields.Select(f => f.Value)).Contains("⏳"), "el descanso de la cría ya no está en /dragon: vive en /cd");
Check(viewNamed.Embed.Title == "🐲 Tu dragón" && viewNamed.Embed.Thumbnail is null, "sin el emoji de su tipo subido: el 🐲 de siempre en el título y sin miniatura");
UiEmojis.Set([("dragon_fuerza", "<:dragon_fuerza:123456789012345670>")]);
var viewIcon = await DragonModule.BuildDragonViewAsync(dragons, A);
Check(viewIcon.Embed.Title == "Tu dragón" && viewIcon.Embed.Thumbnail?.Url is { } thumbUrl && thumbUrl.Contains("123456789012345670") && viewIcon.Embed.Description.Contains("<:dragon_fuerza:123456789012345670>"), "con el emoji de su tipo subido: el título sin el 🐲 verde y SU dragón grande de miniatura (y en la descripción)");

var zonesRepo = sp.GetRequiredService<IZoneRepository>();
await db.ExecuteAsync("UPDATE users SET dust = 50 WHERE discord_id = @id", new { id = (long)A });
Task<Embed> Profile(ulong id, bool withDragons = true) => GameModule.BuildProfileEmbedAsync(users, items, zonesRepo, buffs, id, "T", "https://example.com/a.png", null, null, withDragons ? dragons : null);
var profA = await Profile(A);
string statsA = profA.Fields.Single(f => f.Name == GameModule.ProfileStatsFieldName).Value;
Check(statsA.Contains("**Polvo:**") && statsA.Contains("**Ceniza** (II) lvl 0") && statsA.IndexOf("**Ceniza**", StringComparison.Ordinal) > statsA.IndexOf("**Polvo:**", StringComparison.Ordinal) && statsA.Contains("<:dragon_fuerza:123456789012345670> **Ceniza**") && !statsA.Contains("Dragón:") && !statsA.Contains("tier") && !statsA.Contains("nivel"), "el perfil muestra el dragón justo debajo del Polvo como «emoji nombre (II) lvl 0»: sin el rótulo «Dragón:» ni las palabras tier y nivel");
Embed profEn; using (Loc.Use("en")) { profEn = await Profile(A); }
string statsEn = profEn.Fields.Single(f => f.Name == GameModule.ProfileStatsFieldName).Value;
Check(statsEn.Contains("**Ceniza** (II) lvl 0") && !statsEn.Contains("Dragon"), "y en inglés es lo mismo (el número romano y «lvl» no se traducen)");
var profB = await Profile(B); string statsB = profB.Fields.Single(f => f.Name == GameModule.ProfileStatsFieldName).Value;
var profD = await Profile(D); var profNone = await Profile(A, false);
Check(statsB.Contains("**Cría** (I) lvl 0") && !profD.Fields.Single(f => f.Name == GameModule.ProfileStatsFieldName).Value.Contains("lvl") && !profNone.Fields.Single(f => f.Name == GameModule.ProfileStatsFieldName).Value.Contains("lvl"), "sin nombre muestra el de su tier (Cría, I); sin dragón, o sin el repositorio, el perfil no cambia");
UiEmojis.Set([]);

Task<Embed> Cd(ulong id) => CooldownModule.BuildStatusEmbedAsync(sp.GetRequiredService<ICooldownRepository>(), users, id, null, null, false, null, dragons);
var cdReady = await Cd(A);
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = now() WHERE discord_id = @id", new { id = (long)A });
var cdWait = await Cd(A);
await Dragon(B, "oro", 10);
Embed cdEn; using (Loc.Use("en")) { cdEn = await Cd(A); }
Check(cdReady.Description.Contains("**Dragón (criar)**") && cdReady.Description.Contains("¡Listo!") && cdReady.Description.Contains("/dragon-breed") && cdWait.Description.Contains("**Dragón (criar)**") && (cdWait.Description.Contains("11h") || cdWait.Description.Contains("12h")) && cdWait.Description.Contains("restantes") && cdEn.Description.Contains("**Dragon (breed)**"),
    "/cd muestra cuándo puede volver a criar el dragón: «¡Listo!» o lo que falta (también en inglés)");
Check(!(await Cd(B)).Description.Contains("Dragón") && !(await Cd(D)).Description.Contains("Dragón") && CooldownModule.DragonLine(null) is null, "en el tier máximo (nada que criar) o sin dragón, /cd no trae la línea");

var slashName = interactions.SlashCommands.Single(c => c.Name == "dragon-name");
Check(slashName.Parameters.Count == 1 && slashName.Parameters[0].Name == "nombre" && slashName.Description.Length <= 100 && texts.Search("dragon name Chispa Roja").Commands.Any(c => c.Command.Name == "dragon name") && !FightLock.IsAllowed("dragon-name"), "/dragon-name y «aa dragon name <nombre>» existen (bloqueados en plena pelea)");

Console.WriteLine("\n== 15. Criar solo, /shop buy dragon y la pantalla ==");
Check(Math.Abs(DragonRules.SoloChanceFactor - 1.0 / 3.0) < 1e-12 && Math.Abs(DragonRules.SoloBreedChance(1) - 1.0 / 3.0) < 1e-12 && Math.Abs(DragonRules.SoloBreedChance(2) - 0.20) < 1e-12 && Math.Abs(DragonRules.SoloBreedChance(9) - DragonRules.BreedChance(9) / 3) < 1e-12 && DragonRules.SoloPityCap(1) == 1 && DragonRules.SoloPityCap(5) == DragonRules.PityCap(5) * 3 && DragonRules.ExpectedAttempts(3, DragonRules.SoloChanceFactor) > DragonRules.ExpectedAttempts(3),
    "criar solo: la chance de subir es UN TERCIO de la del tier (en el tier 1, 33 %; en el 2, 20 %) y en promedio lleva más intentos");
Check(!DragonRules.RollBreed(2, 0, new FixedRandom(0.5), DragonRules.SoloChanceFactor) && DragonRules.RollBreed(2, 0, new FixedRandom(0.5)) && !DragonRules.RollBreed(1, 0, new FixedRandom(0.7), DragonRules.SoloChanceFactor) && DragonRules.RollBreed(1, 0, new FixedRandom(0.7)) && DragonRules.RollBreed(1, 1, new FixedRandom(0.7), DragonRules.SoloChanceFactor) && DragonRules.RollBreed(2, DragonRules.SoloPityCap(2), new FixedRandom(0.999), DragonRules.SoloChanceFactor) && !DragonRules.RollBreed(2, DragonRules.PityCap(2), new FixedRandom(0.999), DragonRules.SoloChanceFactor),
    "con la misma suerte, con amigo sube y solo no; el tier 1 (seguro con pareja) solo puede fallar una vez; y solo el seguro llega con el TRIPLE de fallos seguidos");
await Clean(); await Player(A, 100000); await Player(B, 1000); await Player(C, 1000);
await Dragon(A, "oro", 2);
var soloFail = await dragons.BreedSoloAsync(A, new FixedRandom(0.5));
Check(soloFail.Status == DragonBreedStatus.Bred && !soloFail.Success && soloFail.A is { Tier: 2, FailStreak: 1 } && !soloFail.A.CanBreed, "solo, con mala suerte: el dragón sigue en el tier 2, suma un fallo y descansa igual que criando con alguien");
Check((await dragons.BreedSoloAsync(A, new FixedRandom(0))).Status == DragonBreedStatus.OnCooldown, "y mientras descansa no puede criar solo");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL WHERE discord_id = @id", new { id = (long)A });
var soloWin = await dragons.BreedSoloAsync(A, new FixedRandom(0.1));
Check(soloWin.Success && soloWin.Tier == 2 && soloWin.A is { Tier: 3, FailStreak: 0 } && !soloWin.A.CanBreed, "con suerte (por debajo del tercio de la chance) sube de tier, reinicia los fallos y descansa");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL, fail_streak = @cap WHERE discord_id = @id", new { id = (long)A, cap = DragonRules.PityCap(3) });
Check(!(await dragons.BreedSoloAsync(A, new FixedRandom(0.999))).Success, "solo, el tope de fallos que ya alcanza con pareja todavía NO alcanza (hace falta el triple)");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL, fail_streak = @cap WHERE discord_id = @id", new { id = (long)A, cap = DragonRules.SoloPityCap(3) });
Check((await dragons.BreedSoloAsync(A, new FixedRandom(0.999))).Success, "la piedad cuenta también solo: con el tope de fallos seguidos (el triple) el intento sale seguro");
Check((await dragons.BreedSoloAsync(GHOST)).Status == DragonBreedStatus.NoDragon && (await dragons.BreedSoloAsync(B)).Status == DragonBreedStatus.NoDragon, "sin dragón no se cría solo");
await Dragon(B, "oro", 10);
Check((await dragons.BreedSoloAsync(B)).Status == DragonBreedStatus.MaxTier, "en el tier máximo tampoco");
await Dragon(C, "defensa", 4);
var soloRace = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => dragons.BreedSoloAsync(C, new FixedRandom(0.999))));
Check(soloRace.Count(r => r.Status == DragonBreedStatus.Bred) == 1 && soloRace.Count(r => r.Status == DragonBreedStatus.OnCooldown) == 11 && (await dragons.GetAsync(C))!.FailStreak == 1, "12 intentos solos a la vez: se hace UNO y los demás encuentran el descanso");
await db.ExecuteAsync("UPDATE player_dragons SET tier = 2, fail_streak = 0, last_bred_at = NULL WHERE discord_id = @id", new { id = (long)A });
var winEmbed = await DragonModule.ExecuteSoloBreedAsync(dragons, A, new FixedRandom(0.1));
await db.ExecuteAsync("UPDATE player_dragons SET fail_streak = 0, last_bred_at = NULL WHERE discord_id = @id", new { id = (long)A });
var failEmbed = await DragonModule.ExecuteSoloBreedAsync(dragons, A, new FixedRandom(0.9));
Check(winEmbed.Description.Contains("¡Lo lograste solo!") && winEmbed.Description.Contains("Tier 3") && failEmbed.Description.Contains("un tercio") && failEmbed.Description.Contains("Fallos seguidos") && (await DragonModule.ExecuteSoloBreedAsync(dragons, GHOST)).Description.Contains("/taberna") && (await DragonModule.ExecuteSoloBreedAsync(dragons, A)).Description.Contains("descansando"),
    "los mensajes de criar solo: éxito, fallo (dice que la chance es un tercio), sin dragón y descansando");
var viewSolo = await DragonModule.BuildDragonViewAsync(dragons, A);
Check(viewSolo.Embed.Fields.Single(f => f.Name == "Criar").Value.Contains("**solo**") && viewSolo.Embed.Fields.Single(f => f.Name == "Criar").Value.Contains("un tercio"), "la pantalla del dragón avisa que se puede criar solo y con un tercio de la chance");
var slashBreed2 = interactions.SlashCommands.Single(c => c.Name == "dragon-breed");
Check(slashBreed2.Parameters.Count == 1 && !slashBreed2.Parameters[0].IsRequired && slashBreed2.Description.Length <= 100 && texts.Search("dragon breed").Commands.Any(c => c.Command.Name == "dragon breed"), "/dragon-breed y «aa dragon breed» sin amigo son criar solo (el parámetro es opcional)");

await Player(E, 20000); await Player(F, 20000); await Player(G, 20000);
Task<ShopModule.ShopActionResult> Buy(ulong id, string name, int qty = 1, bool withRepo = true) => ShopModule.ExecuteBuyAsync(users, items, shop, combat, events, id, name, qty, boxCtx, withRepo ? dragons : null);
var buyDragon = await Buy(E, "Dragón");
Check(buyDragon.PlainMessage!.Contains("Compraste un dragón") && await dragons.GetAsync(E) is not null && await Gold(E) == 20000 - DragonRules.ShopPrice, "«/shop buy dragón» compra el dragón (mismo cobro y mismas reglas que desde la taberna)");
var buyAgain = await Buy(E, "dragon", 3);
Check(buyAgain.PlainMessage!.Contains("ya tenés un dragón") && await Gold(E) == 20000 - DragonRules.ShopPrice, "pedir otro (aunque sea «3»): «ya tenés un dragón» y no cobra");
Check((await Buy(F, "DRAGON DE CRÍA")).PlainMessage!.Contains("Compraste un dragón") && await dragons.GetAsync(F) is not null, "se entiende «dragon de cría» en mayúsculas y con tilde");
var noRepo = await Buy(G, "dragon", 1, withRepo: false);
Check((noRepo.PlainMessage ?? noRepo.Embed?.Description ?? "").Contains("no está disponible") && await dragons.GetAsync(G) is null, "sin el repositorio del dragón (arneses viejos), «dragon» es un nombre más de la tienda: no está");
Check(DragonModule.IsDragonName("dragon") && DragonModule.IsDragonName(" Dragón ") && DragonModule.IsDragonName("Dragon de cria") && !DragonModule.IsDragonName("dragones") && !DragonModule.IsDragonName("Mate Amargo") && DragonModule.MatchesDragon("") && DragonModule.MatchesDragon("dra") && DragonModule.MatchesDragon("dragón") && !DragonModule.MatchesDragon("espada"), "los nombres que valen para el dragón en /shop buy y lo que se ofrece en la lista mientras se escribe");

Console.WriteLine("\n== 16. En pareja, cada dragón tira su propia suerte ==");
await Clean(); await Player(A); await Player(B); await Player(C); await Player(D);
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2, streak: 1);
var mixed = await dragons.BreedAsync(A, B, new SequenceRandom(0.1, 0.999));
Check(mixed.Status == DragonBreedStatus.Bred && mixed.Success && !mixed.SuccessB && mixed.Tier == 2 && mixed.A is { Tier: 3, FailStreak: 0 } && mixed.B is { Tier: 2, FailStreak: 2 } && !mixed.A.CanBreed && !mixed.B.CanBreed,
    "cada dragón tira su suerte: sube el A (0,1 contra 60 %) y el B no (0,999); el A reinicia sus fallos, el B suma uno (de 1 a 2) y los dos descansan");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var mixed2 = await dragons.BreedAsync(A, B, new SequenceRandom(0.999, 0.1));
Check(!mixed2.Success && mixed2.SuccessB && mixed2.A is { Tier: 2, FailStreak: 1 } && mixed2.B is { Tier: 3, FailStreak: 0 }, "y al revés: puede subir el segundo y quedarse el primero");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var both = await dragons.BreedAsync(A, B, new SequenceRandom(0.1, 0.2));
Check(both.Success && both.SuccessB && both.A is { Tier: 3 } && both.B is { Tier: 3 }, "también pueden subir los dos");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
await dragons.BreedAsync(A, B, new SequenceRandom(0.1, 0.999));
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)A, (long)B } });
Check((await dragons.BreedAsync(A, B)).Status == DragonBreedStatus.TierMismatch, "si subió uno solo ya no tienen el mismo tier: para seguir criando cada uno necesita un compañero de SU tier");

await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var msgMixed = await DragonModule.ResolveBreedAsync(dragons, new DragonBreedOffer(Guid.NewGuid(), A, B, DateTime.UtcNow.AddMinutes(1)), new SequenceRandom(0.1, 0.999));
Check(msgMixed.Description.Contains($"<@{A}>") && msgMixed.Description.Contains($"<@{B}>") && msgMixed.Description.Contains("Tier 3") && msgMixed.Description.Contains("se queda") && msgMixed.Description.Contains("fallos seguidos") && msgMixed.Description.Contains("tiers distintos"),
    "el mensaje cuenta qué le pasó a CADA dragón (uno sube, el otro se queda con sus fallos) y avisa que ahora tienen tiers distintos");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var msgNone = await DragonModule.ResolveBreedAsync(dragons, new DragonBreedOffer(Guid.NewGuid(), A, B, DateTime.UtcNow.AddMinutes(1)), new SequenceRandom(0.999, 0.999));
Check(msgNone.Description.Contains("No se dio") && msgNone.Description.Contains("se queda") && !msgNone.Description.Contains("tiers distintos") && !msgNone.Description.Contains("Salió bien"), "si no sube ninguno: lo dice, los dos se quedan y no avisa de tiers distintos");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var msgBoth = await DragonModule.ResolveBreedAsync(dragons, new DragonBreedOffer(Guid.NewGuid(), A, B, DateTime.UtcNow.AddMinutes(1)), new SequenceRandom(0.1, 0.2));
Check(msgBoth.Description.Contains("Salió bien") && msgBoth.Description.Contains("Tier 3") && !msgBoth.Description.Contains("tiers distintos"), "si suben los dos: «Salió bien» y nada de tiers distintos");

await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var invite2 = await DragonModule.PrepareBreedAsync(dragons, offers, A, B, false);
Check(invite2.Embed!.Description.Contains("uno y el otro no") && invite2.Embed.Description.Contains("60"), "la invitación avisa que cada dragón tira su suerte (puede subir uno y el otro no)");
await Dragon(D, "oro", 6, streak: 5);
var viewD = (await DragonModule.BuildDragonViewAsync(dragons, D)).Embed.Fields.Single(f => f.Name == "Criar").Value;
Check(viewD.Contains("Fallos seguidos: **5**") && viewD.Contains($"**{DragonRules.PityCap(6)}**") && viewD.Contains($"**{DragonRules.SoloPityCap(6)}**"), "la pantalla muestra los fallos seguidos del dragón y los dos topes: con un amigo y solo (el triple)");

Console.WriteLine("\n== 17. Al subir de tier cambia el tipo; la Comida de Dragón lo conserva ==");
var foodItem = await items.GetByNameAsync(DragonRules.FoodItemName);
Check(foodItem is { Type: "DragonFood" } && ShopCatalog.IsForSale(foodItem) && foodItem.BuyPrice > 0, "la Comida de Dragón existe en la base (Database/add_dragon_food.sql), es de tipo DragonFood y se vende");
await Clean(); await Player(A, 100000); await Player(B); await Player(C); await Player(D);
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2); await SetFood(A, 2);
var kept = await dragons.BreedAsync(A, B, new SequenceRandom(0.1, 0.2, 0.95));
Check(kept.Success && kept.SuccessB && kept.A is { Tier: 3, Kind: "oro" } && kept.B is { Tier: 3, Kind: "especial" } && kept.TypeA is { From: "oro", FoodKept: true } && kept.TypeB is { From: "defensa", FoodKept: false } && await dragons.CountFoodAsync(A) == 1 && await dragons.CountFoodAsync(B) == 0,
    "al subir de tier el dragón cambia de tipo al azar (el B pasa de defensa a especial) y el que tenía Comida de Dragón conserva el suyo: se gastó UNA (le queda 1)");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var noChange = await dragons.BreedAsync(A, B, new SequenceRandom(0.999, 0.999));
Check(!noChange.Success && !noChange.SuccessB && noChange.TypeA is null && noChange.TypeB is null && noChange.A!.Kind == "oro" && noChange.B!.Kind == "defensa" && await dragons.CountFoodAsync(A) == 1, "si el intento falla no pasa nada con el tipo y la comida NO se gasta");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var lastFood = await dragons.BreedAsync(A, B, new SequenceRandom(0.1, 0.2, 0.05));
Check(lastFood.TypeA is { FoodKept: true } && lastFood.A!.Kind == "oro" && lastFood.B!.Kind == "fuerza" && await dragons.CountFoodAsync(A) == 0 && await FoodRows(A) == 0, "con la última comida: se gasta y la fila de la mochila desaparece (sin cantidades en cero)");

await Dragon(A, "oro", 2); await SetFood(A, 1);
var soloKeep = await dragons.BreedSoloAsync(A, new FixedRandom(0.1));
Check(soloKeep.Success && soloKeep.TypeA is { From: "oro", FoodKept: true } && soloKeep.A!.Kind == "oro" && await dragons.CountFoodAsync(A) == 0, "criando solo pasa lo mismo: sube, y con comida conserva el tipo");
await Dragon(A, "oro", 2);
var soloChange = await dragons.BreedSoloAsync(A, new FixedRandom(0.1));
Check(soloChange.Success && soloChange.TypeA is { From: "oro", FoodKept: false } && soloChange.A!.Kind == "fuerza", "y sin comida cambia de tipo");
await Dragon(A, "fuerza", 2);
var same = await dragons.BreedSoloAsync(A, new FixedRandom(0.1));
Check(same.TypeA is { From: "fuerza", FoodKept: false } && same.A!.Kind == "fuerza", "el sorteo puede devolver el mismo tipo: pasa y se cuenta como tal");
await Dragon(A, "oro", 2); await SetFood(A, 5);
var foodRace = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => dragons.BreedSoloAsync(A, new FixedRandom(0.1))));
Check(foodRace.Count(r => r.Status == DragonBreedStatus.Bred) == 1 && await dragons.CountFoodAsync(A) == 4, "12 intentos a la vez: se hace uno solo y se gasta UNA comida");

await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2); await SetFood(A, 1);
var msgType = await DragonModule.ResolveBreedAsync(dragons, new DragonBreedOffer(Guid.NewGuid(), A, B, DateTime.UtcNow.AddMinutes(1)), new SequenceRandom(0.1, 0.2, 0.95));
Check(msgType.Description.Contains("Gastó 1") && msgType.Description.Contains("Comida de Dragón") && msgType.Description.Contains("sigue siendo") && msgType.Description.Contains("Cambió de tipo") && msgType.Description.Contains("Especial"), "el mensaje de la cría cuenta qué pasó con el tipo de cada dragón (uno lo conservó con comida, el otro cambió)");
await Dragon(A, "oro", 2);
var soloMsg = await DragonModule.ExecuteSoloBreedAsync(dragons, A, new FixedRandom(0.1));
Check(soloMsg.Description.Contains("Cambió de tipo") && soloMsg.Description.Contains("Fuerza"), "y criando solo también");
await Dragon(A, "oro", 2); await Dragon(B, "defensa", 2);
var inviteFood = await DragonModule.PrepareBreedAsync(dragons, offers, A, B, false);
Check(inviteFood.Embed!.Description.Contains("cambia de tipo al azar") && inviteFood.Embed.Description.Contains("Comida de Dragón"), "la invitación avisa que el que suba cambia de tipo y que la comida lo evita");

await Dragon(D, "oro", 3); await SetFood(D, 2);
var typeFields = (await DragonModule.BuildDragonViewAsync(dragons, D)).Embed.Fields.Where(f => f.Name == "Tipo y comida").ToList();
Check(typeFields.Count == 1 && typeFields[0].Value.Contains("cambia de tipo") && typeFields[0].Value.Contains("tenés **2**"), "la pantalla del dragón explica el cambio de tipo y cuántas comidas tenés");
await SetFood(D, 0);
Check((await DragonModule.BuildDragonViewAsync(dragons, D)).Embed.Fields.Single(f => f.Name == "Tipo y comida").Value.Contains("no tenés"), "y si no tenés, manda a la taberna");
await Dragon(D, "oro", 10);
Check(!(await DragonModule.BuildDragonViewAsync(dragons, D)).Embed.Fields.Any(f => f.Name == "Tipo y comida"), "en el tier 10 ya no hay nada que criar: la pantalla no lo ofrece");

await Player(E, 100000);
await Buy(E, DragonRules.FoodItemName, 3);
Check(await dragons.CountFoodAsync(E) == 3 && await Gold(E) == 100000 - 3 * foodItem!.BuyPrice, "la Comida de Dragón se compra en la tienda (sin cooldown, de a varias) y va a la mochila");
var sceneFood = await TabernaModule.BuildSceneAsync(users, items, inventory, buffs, E, null, boxCtx, exchange, svc, dragons);
Check((Options(sceneFood, "taberna_buy") ?? []).Any(o => o.Value == DragonRules.FoodItemName && o.Description.Contains("dragón")), "la taberna la ofrece en «Comprar comida» diciendo que es para el dragón");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un Random que siempre devuelve el mismo valor: sortea lo que el arnés necesita (0 = siempre sale, 0,999 = nunca sale por suerte).
// Un Random que devuelve los valores en orden (el último se repite): para que cada dragón de una pareja reciba SU propia suerte.
sealed class SequenceRandom(params double[] values) : Random
{
    private int _next;
    private double Take() => values[Math.Min(_next++, values.Length - 1)];
    protected override double Sample() => Take();
    public override double NextDouble() => Take();
    public override int Next(int maxValue) => (int)(Take() * maxValue);
}

sealed class FixedRandom(double value) : Random
{
    protected override double Sample() => value;
    public override double NextDouble() => value;
    public override int Next(int maxValue) => (int)(value * maxValue);
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
