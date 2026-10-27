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

async Task<int> Gold(ulong id) => await db.ExecuteScalarAsync<int>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)id });

await Clean();

Console.WriteLine("== 1. Las reglas puras ==");
bool curveOk = DragonRules.Kinds.All(k => Enumerable.Range(1, 9).All(t => DragonRules.BonusPercent(k, t) < DragonRules.BonusPercent(k, t + 1)) && DragonRules.BonusPercent(k, 10) == DragonRules.MaxPercent(k) && DragonRules.BonusPercent(k, 1) > 0 && DragonRules.BonusPercent(k, 1) < DragonRules.MaxPercent(k) * 0.1);
Check(curveOk && DragonRules.MaxPercent("fuerza") == 12 && DragonRules.MaxPercent("defensa") == 8 && DragonRules.MaxPercent("oro") == 30 && DragonRules.MaxPercent("especial") == 25, "el bono sube en cada tier (curva convexa), el tier 10 da el máximo de su tipo (12 ATQ / 8 DEF / 30 oro / 25 EXP) y el tier 1 menos del 10 % de eso");
Check(DragonRules.BonusesFor("fuerza", 10).AttackPercent == 12 && DragonRules.BonusesFor("defensa", 10).DefensePercent == 8 && DragonRules.BonusesFor("oro", 10).GoldPercent == 30 && DragonRules.BonusesFor("especial", 10).XpPercent == 25 && DragonRules.BonusesFor("oro", 5).AttackPercent == 0, "cada tipo mejora SOLO lo suyo");
Check(DragonRules.TierName(1) == "Cría" && DragonRules.TierName(10) == "Eterno" && DragonRules.TierNumeral(4) == "IV" && Enumerable.Range(1, 10).Select(DragonRules.TierName).Distinct().Count() == 10, "diez nombres de tier distintos, de Cría a Eterno");
Check(DragonRules.BreedChance(1) == 1.0 && DragonRules.BreedChance(2) == 0.60 && DragonRules.BreedChance(9) == 0.0011 && DragonRules.BreedChance(10) == 0 && Enumerable.Range(1, 8).All(t => DragonRules.BreedChance(t) > DragonRules.BreedChance(t + 1)) && Enumerable.Range(1, 8).All(t => DragonRules.PityCap(t) < DragonRules.PityCap(t + 1)), "la chance de criar baja tier a tier (100 / 60 / 30 / 15 / 5 / 2 / 1 / 0,33 / 0,11 %) y el tope de fallos sube");
Check(DragonRules.RollBreed(1, 0, new FixedRandom(0.999)) && !DragonRules.RollBreed(2, 0, new FixedRandom(0.999)) && DragonRules.RollBreed(2, DragonRules.PityCap(2), new FixedRandom(0.999)) && DragonRules.RollBreed(9, DragonRules.PityCap(9), new FixedRandom(0.999)) && !DragonRules.RollBreed(10, 99, new FixedRandom(0)),
    "el tier 1 sube siempre; con mala suerte falla, pero al llegar al tope de fallos seguidos sale SEGURO (también en el tier 9); el tier 10 no sube más");
var stat = new Random(7); int wins = Enumerable.Range(0, 20000).Count(_ => DragonRules.RollBreed(3, 0, stat));
Check(wins is > 5700 and < 6300, $"el tier 3 sube el 30 % de las veces (en 20.000 intentos: {wins})");
double total = Enumerable.Range(1, 9).Sum(DragonRules.ExpectedAttempts);
Check(Math.Abs(DragonRules.ExpectedAttempts(2) - 1.56) < 0.01 && total is > 130 and < 175, $"con la piedad, subir de tier lleva en promedio 1,6 intentos del 2 al 3 y unos {total:0} intentos hasta el tier 10 (a 2 por día, ~{total / 2:0} días: una meta de meses)");
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
Check(first.Status == DragonBreedStatus.Bred && first.Success && first.Tier == 1 && a1 is { Tier: 2, FailStreak: 0 } && b1 is { Tier: 2, FailStreak: 0 } && a1.Kind == "oro" && b1.Kind == "fuerza" && !a1.CanBreed && !b1.CanBreed && a1.Wait > TimeSpan.FromHours(11),
    "el tier 1 sube seguro: los DOS dragones pasan al tier 2, cada uno conserva su tipo y los dos descansan ~12 h");
var rest = await dragons.BreedAsync(A, B, new FixedRandom(0));
Check(rest.Status == DragonBreedStatus.OnCooldown && rest.Wait > TimeSpan.FromHours(11) && (await dragons.GetAsync(A))!.Tier == 2, "mientras descansan no se puede volver a criar y no pasa nada");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)A, (long)B } });
var fail = await dragons.BreedAsync(A, B, new FixedRandom(0.999));
Check(fail.Status == DragonBreedStatus.Bred && !fail.Success && fail.A is { Tier: 2, FailStreak: 1 } && fail.B is { Tier: 2, FailStreak: 1 } && !fail.A.CanBreed, "un intento fallido: siguen en el tier 2, los dos suman un fallo y descansan igual");
await db.ExecuteAsync("UPDATE player_dragons SET last_bred_at = NULL WHERE discord_id = ANY(@ids)", new { ids = new long[] { (long)A, (long)B } });
await db.ExecuteAsync("UPDATE player_dragons SET fail_streak = @cap WHERE discord_id = @id", new { id = (long)B, cap = DragonRules.PityCap(2) });
var pity = await dragons.BreedAsync(A, B, new FixedRandom(0.999));
Check(pity.Success && pity.A is { Tier: 3, FailStreak: 0 } && pity.B is { Tier: 3, FailStreak: 0 }, "la piedad es la del que más fallos lleva: con el compañero en el tope, el intento sale SEGURO aunque la suerte sea mala");
await Dragon(A, "oro", 10); await Dragon(B, "oro", 10);
Check((await dragons.BreedAsync(A, B)).Status == DragonBreedStatus.MaxTier, "dos dragones del tier 10 no tienen nada más que criar");
await Dragon(A, "oro", 4); await Dragon(B, "oro", 4);
var raceBreed = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 2 == 0 ? dragons.BreedAsync(A, B, new FixedRandom(0.999)) : dragons.BreedAsync(B, A, new FixedRandom(0.999))));
Check(raceBreed.Count(r => r.Status == DragonBreedStatus.Bred) == 1 && raceBreed.Count(r => r.Status == DragonBreedStatus.OnCooldown) == 11 && (await dragons.GetAsync(A))!.FailStreak + (await dragons.GetAsync(B))!.FailStreak == 2,
    $"12 crías a la vez (la mitad cruzadas, A con B y B con A): sin trabarse, se hace UNA sola y las demás encuentran el descanso ({raceBreed.Count(r => r.Status == DragonBreedStatus.Bred)} hechas, {raceBreed.Count(r => r.Status == DragonBreedStatus.OnCooldown)} en descanso)");

Console.WriteLine("\n== 4. Los bonos en PlayerBonuses ==");
var none = PlayerBonuses.None; var withGold = none with { Dragon = DragonRules.BonusesFor("oro", 10) };
var withXp = none with { Dragon = DragonRules.BonusesFor("especial", 10) }; var withAtk = none with { Dragon = DragonRules.BonusesFor("fuerza", 10) }; var withDef = none with { Dragon = DragonRules.BonusesFor("defensa", 10) };
Check(Math.Abs(withGold.GoldMultiplier / none.GoldMultiplier - 1.30) < 1e-9 && Math.Abs(withXp.XpMultiplier / none.XpMultiplier - 1.25) < 1e-9 && Math.Abs(withAtk.AttackMultiplier - 1.12) < 1e-9 && Math.Abs(withDef.DefenseMultiplier - 1.08) < 1e-9, "un dragón de tier 10 multiplica el oro ×1,30, la EXP ×1,25, el ataque ×1,12 o la defensa ×1,08 según su tipo");
Check(withGold.XpMultiplier == none.XpMultiplier && withXp.GoldMultiplier == none.GoldMultiplier && withAtk.DefenseMultiplier == none.DefenseMultiplier && !withGold.IsNone && none.IsNone && (none with { Dragon = DragonBonuses.None }).IsNone, "cada tipo toca solo lo suyo; con dragón ya no es «sin bonos»");
await Clean(); await Player(D); await dragons.BuyAsync(D, 1, "oro"); await db.ExecuteAsync("UPDATE player_dragons SET tier = 6 WHERE discord_id = @id", new { id = (long)D });
var svc = sp.GetRequiredService<IPlayerBonusService>();
var bD = await svc.GetAsync(D, 0); var bNo = await svc.GetAsync(GHOST, 0);
Check(bD.Dragon is not null && Math.Abs(bD.Dragon.GoldPercent - DragonRules.BonusPercent("oro", 6)) < 1e-9 && bD.GoldMultiplier > 1.1 && bNo.Dragon is null && bNo.GoldMultiplier == 1.0, $"el servicio de bonos lee el dragón de la base (tier 6 de oro: +{DragonRules.BonusPercent("oro", 6)} %) y sin dragón queda en ×1");

Console.WriteLine("\n== 5. La pantalla ==");
var empty = await DragonModule.BuildDragonEmbedAsync(dragons, GHOST);
Check(empty.Description.Contains("/taberna") && empty.Description.Contains("3.000") && empty.Fields.Length == 0, "sin dragón: dice cómo conseguirlo (taberna, 3.000 de oro)");
var view = await DragonModule.BuildDragonEmbedAsync(dragons, D);
var ladder = view.Fields.Single(f => f.Name.Contains("Los tiers")).Value.Split('\n');
Check(view.Description.Contains("Tier **6/10**") && view.Description.Contains("Curtido") && ladder.Length == 10 && ladder.Count(l => l.StartsWith("▶️")) == 1 && ladder[5].StartsWith("▶️") && view.Fields.Any(f => f.Name.Contains("Criar") && f.Value.Contains("/dragon-breed")) && view.Length < 6000,
    "con dragón: el tier y su nombre, la escalera de 10 tiers con el actual marcado, y cómo va la cría");
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
await Player(G); await dragons.BuyAsync(G, 1, "especial");
await db.ExecuteAsync("UPDATE users SET gate_cleared = true WHERE discord_id = @id; UPDATE player_dragons SET tier = 7, fail_streak = 3 WHERE discord_id = @id", new { id = (long)G });
var renewed = await sp.GetRequiredService<IFuegoNuevoRepository>().RenewAsync(G, 0, "Ninja");
Check(renewed.Status == RenewStatus.Ok && await dragons.GetAsync(G) is { Kind: "especial", Tier: 7, FailStreak: 3 }, $"renacer deja el dragón como estaba (tipo, tier y fallos): dura toda la vida de la cuenta ({renewed.Status})");

Console.WriteLine("\n== 9. Registro con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(DragonModule).Assembly, sp);
var slashDragon = interactions.SlashCommands.Single(c => c.Name == "dragon"); var slashBreed = interactions.SlashCommands.Single(c => c.Name == "dragon-breed");
Check(slashDragon.Parameters.Count == 0 && slashBreed.Parameters.Count == 1 && slashBreed.Parameters[0].Name == "amigo" && slashDragon.Description.Length <= 100 && slashBreed.Description.Length <= 100, $"/dragon y /dragon-breed existen, con descripciones de {slashDragon.Description.Length} y {slashBreed.Description.Length} caracteres (≤ 100)");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var rAccept = interactions.SearchComponentCommand(Click($"dragon_accept:{Guid.NewGuid()}")); var rDecline = interactions.SearchComponentCommand(Click($"dragon_decline:{Guid.NewGuid()}"));
Check(rAccept.IsSuccess && rAccept.Command.MethodName == "HandleAcceptAsync" && rDecline.IsSuccess && rDecline.Command.MethodName == "HandleDeclineAsync", "los botones dragon_accept / dragon_decline llegan a su método (sin que el grupo les cambie el id)");
var texts = new CommandService();
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
await texts.AddModuleAsync<TextCommandModule>(sp);
Check(new[] { "dragon", "dragón", "dragones" }.All(a => texts.Search(a) is { IsSuccess: true } f && f.Commands.Any(c => c.Command.Name == "dragon")) && texts.Search("dragon breed @amigo").Commands.Any(c => c.Command.Name == "dragon breed") && texts.Search("dragon criar @amigo").Commands.Any(c => c.Command.Name == "dragon breed"), "aa dragon (y dragón / dragones) y aa dragon breed @amigo (o «criar») existen");
Check(!FightLock.IsAllowed("dragon") && !FightLock.IsAllowed("dragon-breed"), "en plena pelea los comandos del dragón quedan bloqueados (como todo lo que no es de la pelea)");
bool built = true; try { ActivatorUtilities.CreateInstance(sp, typeof(DragonModule)); ActivatorUtilities.CreateInstance(sp, typeof(TabernaModule)); ActivatorUtilities.CreateInstance(sp, typeof(TextCommandModule)); } catch (Exception ex) { built = false; Console.WriteLine("   " + ex.Message); }
Check(built && HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("`/dragon`")), "los módulos se construyen con el DI real y /info nombra /dragon");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un Random que siempre devuelve el mismo valor: sortea lo que el arnés necesita (0 = siempre sale, 0,999 = nunca sale por suerte).
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
