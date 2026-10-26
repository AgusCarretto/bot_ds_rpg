using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.9.0: banco, penalidad por muerte, desmantelar -> Polvo, encantamientos y logros nuevos.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== 1. Reglas puras ==");
Check(BankRules.AccountPrice == 1000, "la cuenta del banco cuesta 1.000 de oro");

Check(DeathPenalty.GoldPercent == 5 && DeathPenalty.GoldLost(0) == 0 && DeathPenalty.GoldLost(19) == 0 && DeathPenalty.GoldLost(20) == 1 && DeathPenalty.GoldLost(1000) == 50 && DeathPenalty.GoldLost(123456) == 6172, "penalidad: 5% del oro de la billetera, redondeado hacia abajo (19 → 0, 20 → 1, 1.000 → 50)");
Check(DeathPenalty.GoldLost(int.MaxValue) == (int)(int.MaxValue * 5L / 100) && DeathPenalty.GoldLost(-5) == 0, "sin desbordar con montos enormes y sin perder nada si el oro fuera negativo");

{
    var dq1 = DismantleRequest.Parse("30 Piedra"); var dq2 = DismantleRequest.Parse("todo Madera de Pino"); var dq3 = DismantleRequest.Parse("Madera de Pino");
    var dq4 = DismantleRequest.Parse("1.000 piedra"); var dq5 = DismantleRequest.Parse("  ALL   Hierro "); var dq6 = DismantleRequest.Parse("todo"); var dq7 = DismantleRequest.Parse("");
    Check(dq1 == ("30", "Piedra") && dq2 == ("todo", "Madera de Pino") && dq3 == ("1", "Madera de Pino") && dq4 == ("1.000", "piedra") && dq5 == ("ALL", "Hierro") && dq6 == ("todo", "") && dq7 == ("1", ""),
        "el pedido de texto: «30 Piedra», «todo Madera de Pino», «Madera de Pino» (1), «1.000 piedra», «ALL Hierro», «todo» solo (sin ítem) y vacío");
    Check(AmountParser.IsAllWord("TODO") && AmountParser.IsAllWord(" max ") && !AmountParser.IsAllWord("30") && !AmountParser.IsAllWord(null) && DismantleRequest.LooksLikeQuantity("1.000") && !DismantleRequest.LooksLikeQuantity("Piedra"), "qué es una cantidad y qué una palabra de «todo»");
}
Check(new[] { "Madera", "Mineral", "Material" }.All(Dismantling.CanDismantle) && !new[] { "Caja", "Consumable", "Weapon", "Amulet", "Algo" }.Any(Dismantling.CanDismantle), "solo se desmantelan materiales: madera, minerales y drops (nunca cajas, comida ni equipo)");
Check(Dismantling.DustPerUnit("Común") == 1 && Dismantling.DustPerUnit("Raro") == 6 && Dismantling.DustPerUnit("Épico") == 24 && Dismantling.DustPerUnit("Legendario") == 70 && Dismantling.DustPerUnit("Mítico") == 500 && Dismantling.DustPerUnit("???") == 0, "Polvo por unidad: 1 / 6 / 24 / 70 / 500 según la rareza");
Check(Dismantling.DustFor("Épico", 3) == 72 && Dismantling.DustFor("Común", 5) == 5, "cantidad × Polvo por unidad");
// El Polvo vale ~0,5 por minuto de farmeo para toda rareza (los mismos ritmos del modelo de ritmos): nadie desmantela "lo más eficiente" y rompe el costo de encantar.
double[] farmMinutes = [2.45, 11.9, 47.6, 143, 1000];
string[] rarities = ["Común", "Raro", "Épico", "Legendario", "Mítico"];
var perMinute = rarities.Select((r, i) => Dismantling.DustPerUnit(r) / farmMinutes[i]).ToList();
Check(perMinute.All(p => p is > 0.38 and < 0.56), $"el Polvo rinde parecido por minuto de farmeo en todas las rarezas ({string.Join(" / ", perMinute.Select(p => p.ToString("0.00")))})");

Check(Enchantments.MaxTier == 5 && Enchantments.TierName(1) == "Tibio" && Enchantments.TierName(5) == "Soberano" && Enchantments.TierName(0) == "Sin encantar", "5 tiers: Tibio, Al Rojo, Ardiente, Incandescente, Soberano");
Check(Enumerable.Range(1, 5).Select(Enchantments.BonusPercent).SequenceEqual([4, 8, 13, 19, 26]) && Enchantments.BonusPercent(0) == 0, "bonus +4 / +8 / +13 / +19 / +26 %");
Check(Enchantments.Apply(80, 5) == 101 && Enchantments.Apply(80, 0) == 80 && Enchantments.Apply(9, 1) == 10 && Enchantments.Apply(0, 5) == 0, "se aplica sobre el stat de la pieza (80 en tier 5 → 101) y un encantamiento siempre suma al menos 1 (9 en tier 1 → 10)");
Check(Enchantments.Label("weapon", 3) == "Filo Ardiente" && Enchantments.Label("amulet", 3) == "Guarda Ardiente" && Enchantments.Label("weapon", 0) == "Sin encantar", "el arma lleva un «Filo» y el amuleto una «Guarda»");
var rng = new Random(77);
var counts = new int[6];
for (int i = 0; i < 200000; i++) counts[Enchantments.Roll(rng)]++;
Check(counts[0] == 0 && Math.Abs(counts[1] / 2000.0 - 42) < 1.2 && Math.Abs(counts[2] / 2000.0 - 30) < 1.2 && Math.Abs(counts[3] / 2000.0 - 18) < 1 && Math.Abs(counts[4] / 2000.0 - 9) < 0.8 && Math.Abs(counts[5] / 2000.0 - 1) < 0.3, $"el sorteo da 42 / 30 / 18 / 9 / 1 % ({string.Join(" / ", counts.Skip(1).Select(c => (c / 2000.0).ToString("0.0")))})");
var cost1 = Enchantments.Cost(1); var cost5 = Enchantments.Cost(5);
Check(cost1.Gold == MissionRewards.GoldUnit(1) * 8 && cost5.Gold == MissionRewards.GoldUnit(5) * 8 && cost1.Dust == 10 && cost5.Dust == 60 && Enumerable.Range(1, 4).All(r => Enchantments.Cost(r).Gold < Enchantments.Cost(r + 1).Gold && Enchantments.Cost(r).Dust < Enchantments.Cost(r + 1).Dust), $"cada intento cuesta más cuanto más alta la zona de la pieza (Zona 1: {cost1.Gold} oro + {cost1.Dust} Polvo; Zona 5: {cost5.Gold} + {cost5.Dust})");
Check(Enchantments.GearRank("Común") == 1 && Enchantments.GearRank("Raro") == 2 && Enchantments.GearRank("Mítico") == 5 && Enchantments.GearRank("???") == 1, "la zona de la pieza sale de su rareza (Común = Zona 1 ... Mítico = Zona 5)");


Console.WriteLine("\n== 2. Repositorios contra la base (usuarios descartables) ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var bank = new BankRepository(factory);
var dustRepo = new DustRepository(factory);
var shop = new ShopRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000009100UL;
async Task Reset(int gold = 50, int xp = 0, int level = 1, int dust = 0, int bankGold = 0, bool hasBank = false)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET gold = @gold, xp = @xp, level = @level, dust = @dust, bank_gold = @bankGold, has_bank = @hasBank WHERE discord_id = @id", new { id = (long)U, gold, xp, level, dust, bankGold, hasBank });
}
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;
async Task Give(string name, int q) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q });
async Task<int> Have(string name) => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = name });

// -- penalidad por muerte
await Reset(gold: 1000, xp: 50, level: 3, bankGold: 500, hasBank: true);
var pen = await users.ApplyDeathPenaltyAsync(U);
var afterPen = await Me();
Check(pen is { GoldLost: 50, XpLost: 50, GoldAfter: 950 } && afterPen.Gold == 950 && afterPen.Xp == 0 && afterPen.Level == 3, $"penalidad: de 1.000 de oro y 50 de EXP queda en 950 y 0, sin bajar de nivel ({pen})");
Check(afterPen.BankGold == 500 && afterPen.HasBank, "el oro del banco no se toca");
await Reset(gold: 19, xp: 0);
var pen2 = await users.ApplyDeathPenaltyAsync(U);
Check(pen2 is { GoldLost: 0, XpLost: 0, GoldAfter: 19 } && (await Me()).Gold == 19, "con menos de 20 de oro y la EXP en 0 no se pierde nada");
Check(await users.ApplyDeathPenaltyAsync(999000000000009199UL) is null, "un jugador que no existe: sin penalidad (null)");
await Reset(gold: 100000, xp: 10);
await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => users.ApplyDeathPenaltyAsync(U)));
long expectedGold = 100000; for (int i = 0; i < 10; i++) expectedGold -= expectedGold * 5 / 100;
Check((await Me()).Gold == expectedGold, $"10 derrotas simultáneas se aplican una por una, sin pisarse (oro {(await Me()).Gold}, esperado {expectedGold})");

// -- banco
await Reset(gold: 999);
var o1 = await bank.OpenAccountAsync(U, BankRules.AccountPrice);
Check(o1.Status == BankStatus.NotEnoughGold && (await Me()).Gold == 999 && !(await Me()).HasBank, "abrir la cuenta con 999 de oro: no alcanza y no se cobra nada");
await db.ExecuteAsync("UPDATE users SET gold = 1500 WHERE discord_id = @id", new { id = (long)U });
var o2 = await bank.OpenAccountAsync(U, BankRules.AccountPrice);
Check(o2.Status == BankStatus.Ok && (await Me()) is { Gold: 500, HasBank: true, BankGold: 0 }, "con 1.500 se abre: cuesta 1.000 y quedan 500");
var o3 = await bank.OpenAccountAsync(U, BankRules.AccountPrice);
Check(o3.Status == BankStatus.AlreadyHasAccount && (await Me()).Gold == 500, "abrirla de nuevo: ya la tenés y no se cobra otra vez");
Check((await bank.DepositAsync(U, 600)).Status == BankStatus.NotEnoughGold && (await Me()).Gold == 500, "depositar más de lo que tenés en la billetera: no pasa nada");
var d1 = await bank.DepositAsync(U, 300);
Check(d1.Status == BankStatus.Ok && d1.User is { Gold: 200, BankGold: 300 }, "depositar 300: billetera 200, banco 300");
Check((await bank.WithdrawAsync(U, 400)).Status == BankStatus.NotEnoughBank && (await Me()).BankGold == 300, "retirar más de lo que hay en el banco: no pasa nada");
var w1 = await bank.WithdrawAsync(U, 100);
Check(w1.Status == BankStatus.Ok && w1.User is { Gold: 300, BankGold: 200 }, "retirar 100: billetera 300, banco 200");
await Reset(gold: 800);
Check((await bank.DepositAsync(U, 10)).Status == BankStatus.NoAccount && (await bank.WithdrawAsync(U, 10)).Status == BankStatus.NoAccount, "sin cuenta no se puede depositar ni retirar");
Check((await bank.DepositAsync(999000000000009199UL, 10)).Status == BankStatus.NoPlayer, "un jugador que no existe: lo dice");
await Reset(gold: 1000, hasBank: true);
var parallelDeposits = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => bank.DepositAsync(U, 100)));
var finalBank = await Me();
Check(parallelDeposits.Count(r => r.Status == BankStatus.Ok) == 10 && finalBank is { Gold: 0, BankGold: 1000 }, $"20 depósitos simultáneos de 100 con 1.000 en mano: pasan exactamente 10 (billetera {finalBank.Gold}, banco {finalBank.BankGold})");

// -- desmantelar
await Reset();
await Give("Madera de Pino", 7);
var pinoId = (await items.GetByNameAsync("Madera de Pino"))!.ItemId;
var dm1 = await dustRepo.DismantleAsync(U, pinoId, 5, 5);
Check(dm1 is { DustAfter: 5, QuantityLeft: 2 } && await Have("Madera de Pino") == 2 && (await Me()).Dust == 5, $"desmantelar 5 de 7 Pinos: +5 Polvo y quedan 2 ({dm1})");
Check(await dustRepo.DismantleAsync(U, pinoId, 5, 5) is null && await Have("Madera de Pino") == 2 && (await Me()).Dust == 5, "pedir 5 teniendo 2: no hace nada (ni Polvo, ni gasta nada)");
var dm2 = await dustRepo.DismantleAsync(U, pinoId, 2, 2);
Check(dm2 is { DustAfter: 7, QuantityLeft: 0 } && await Have("Madera de Pino") == 0, "desmantelar lo último: el ítem sale de la mochila");
await Reset(); await Give("Piedra", 5);
var piedraId = (await items.GetByNameAsync("Piedra"))!.ItemId;
var parallelDismantle = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => dustRepo.DismantleAsync(U, piedraId, 1, 1)));
Check(parallelDismantle.Count(r => r is not null) == 5 && (await Me()).Dust == 5 && await Have("Piedra") == 0, "12 desmantelados simultáneos con 5 piedras: pasan 5, sin Polvo de más");

// -- encantar
var sword = (await items.GetByNameAsync("Espada de Madera"))!;
var talisman = (await items.GetByNameAsync("Hombreras de Cuero Grueso"))!;
await Reset(gold: 5000, dust: 100);
Check((await dustRepo.TryEnchantAsync(U, "weapon", 3, 100, 10)).Status == EnchantStatus.NoGear && (await Me()) is { Gold: 5000, Dust: 100 }, "sin arma puesta: no se puede encantar y no se cobra");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId, a = talisman.ItemId });
Check((await dustRepo.TryEnchantAsync(U, "weapon", 3, 6000, 10)).Status == EnchantStatus.NotEnoughGold && (await dustRepo.TryEnchantAsync(U, "weapon", 3, 100, 101)).Status == EnchantStatus.NotEnoughDust && (await Me()) is { Gold: 5000, Dust: 100 }, "sin oro o sin Polvo suficientes: lo dice y no se cobra");
var e1 = await dustRepo.TryEnchantAsync(U, "weapon", 3, 100, 10);
Check(e1 is { Status: EnchantStatus.Ok, PreviousTier: 0, RolledTier: 3, NewTier: 3 } && (await Me()) is { Gold: 4900, Dust: 90, WeaponEnchant: 3 }, "primer intento: sale tier 3, queda puesto y se cobra oro y Polvo");
var e2 = await dustRepo.TryEnchantAsync(U, "weapon", 2, 100, 10);
Check(e2 is { Status: EnchantStatus.Ok, PreviousTier: 3, RolledTier: 2, NewTier: 3 } && (await Me()) is { Gold: 4800, Dust: 80, WeaponEnchant: 3 }, "si sale uno peor (tier 2) se queda el que tenías (3), pero el intento se paga");
var e3 = await dustRepo.TryEnchantAsync(U, "weapon", 5, 100, 10);
Check(e3 is { NewTier: 5 } && (await Me()).WeaponEnchant == 5 && (await Me()).AmuletEnchant == 0, "uno mejor (tier 5) reemplaza, y el amuleto sigue sin encantar");
var e4 = await dustRepo.TryEnchantAsync(U, "amulet", 4, 100, 10);
Check(e4 is { Status: EnchantStatus.Ok, NewTier: 4 } && (await Me()).AmuletEnchant == 4 && (await Me()).WeaponEnchant == 5, "el amuleto se encanta aparte");
Check((await shop.SellEquippedAsync(U, sword.ItemId, 10)) is { WeaponId: null, WeaponEnchant: 0, AmuletEnchant: 4 }, "vender el arma puesta borra SU encantamiento (el del amuleto queda)");
await Reset(gold: 1000, dust: 100);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId });
var parallelEnchants = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => dustRepo.TryEnchantAsync(U, "weapon", 2, 300, 30)));
Check(parallelEnchants.Count(r => r.Status == EnchantStatus.Ok) == 3 && (await Me()) is { Gold: 100, Dust: 10 }, "10 intentos simultáneos con recursos para 3: pasan 3 (no se gasta de más)");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 3. Los comandos (/bank, /dismantle, /enchant) ==");
var eventsRepo = new GameEventRepository(factory);
var events = new GameEventService(eventsRepo);
async Task CleanEvents() => await db.ExecuteAsync("DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id", new { id = (long)U });
async Task<long> Stat(string kind) => (await eventsRepo.GetStatsAsync(U)).GetValueOrDefault(kind);
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)));

// -- banco
await Reset(gold: 400); await CleanEvents();
var v0 = await BankModule.BuildViewAsync(users, U);
Check(v0.Embed is not null && AllText(v0.Embed).Contains("1.000") && AllText(v0.Embed).Contains("/bank open") && v0.Embed.Fields.Any(f => f.Value.Contains("400")), "/bank sin cuenta: dice cuánto cuesta (1.000), cómo abrirla y tu billetera");
Check((await BankModule.ExecuteOpenAsync(users, bank, events, U)).PlainMessage!.Contains("1.000") && !(await Me()).HasBank, "abrirla sin oro: avisa el precio y no cobra");
await db.ExecuteAsync("UPDATE users SET gold = 1500 WHERE discord_id = @id", new { id = (long)U });
var open = await BankModule.ExecuteOpenAsync(users, bank, events, U);
Check(open.Embed is not null && open.Embed.Title.Contains("Cuenta abierta") && (await Me()) is { HasBank: true, Gold: 500 } && await Stat("bank_opened") == 1, "abrirla con oro: queda abierta, se cobra y se registra el evento");
Check((await BankModule.ExecuteOpenAsync(users, bank, events, U)).PlainMessage!.Contains("Ya tenés") && (await Me()).Gold == 500 && await Stat("bank_opened") == 1, "abrirla otra vez: ya la tenés (y no suma el evento ni cobra)");
var dep = await BankModule.ExecuteDepositAsync(users, bank, events, U, "all");
Check(dep.Embed is not null && (await Me()) is { Gold: 0, BankGold: 500 } && await Stat("bank_deposit") == 500, "depositar «all»: guarda toda la billetera y registra el monto");
Check((await BankModule.ExecuteDepositAsync(users, bank, events, U, "1")).PlainMessage!.Contains("tenés **0**") || (await BankModule.ExecuteDepositAsync(users, bank, events, U, "1")).PlainMessage!.Contains("billetera"), "depositar sin oro en mano: lo dice");
Check((await BankModule.ExecuteDepositAsync(users, bank, events, U, "hola")).PlainMessage!.Contains("No entendí"), "una cantidad que no se entiende: lo dice");
var wd = await BankModule.ExecuteWithdrawAsync(users, bank, U, "200");
Check(wd.Embed is not null && (await Me()) is { Gold: 200, BankGold: 300 }, "retirar 200: billetera 200, banco 300");
Check((await BankModule.ExecuteWithdrawAsync(users, bank, U, "1.000")).PlainMessage!.Contains("tenés **300**"), "retirar más de lo que hay: dice cuánto hay (y no toca nada)");
Check((await BankModule.ExecuteWithdrawAsync(users, bank, U, "todo")).Embed is not null && (await Me()) is { Gold: 500, BankGold: 0 }, "retirar «todo»: vuelve todo a la billetera");
Check((await BankModule.ExecuteWithdrawAsync(users, bank, U, "all")).PlainMessage!.Contains("No tenés oro en el banco"), "retirar «all» con el banco vacío: lo dice");
await Reset(gold: 10);
Check((await BankModule.ExecuteDepositAsync(users, bank, events, U, "5")).PlainMessage!.Contains("/bank open") && (await BankModule.ExecuteWithdrawAsync(users, bank, U, "5")).PlainMessage!.Contains("/bank open"), "sin cuenta, depositar y retirar mandan a abrirla");

// -- desmantelar
await Reset(); await CleanEvents();
await Give("Madera de Roble", 6); await Give("Cajón de Pino", 1); await Give("Mate Amargo", 1); await Give("Colmillo de Jabalí", 3);
var dis = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "madera de roble", 3);
Check(dis.Embed is not null && AllText(dis.Embed).Contains("+18") && (await Me()).Dust == 18 && await Have("Madera de Roble") == 3 && await Stat("dismantle") == 3, "desmantelar 3 Robles (Raro, 6 c/u): +18 Polvo, quedan 3 y se registra (aunque lo escribas en minúsculas)");
Check(dis.Embed!.Thumbnail?.Url == ItemDisplay.ImageUrl((await items.GetByNameAsync("Madera de Roble"))!.Emoji), "el material desmantelado va de miniatura grande");
Check((await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Cajón de Pino", 1)).PlainMessage!.Contains("no se puede desmantelar") && (await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Mate Amargo", 1)).PlainMessage!.Contains("no se puede desmantelar") && (await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Espada de Madera", 1)).PlainMessage!.Contains("no se puede desmantelar") && (await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Cosa que no existe", 1)).PlainMessage!.Contains("no se puede desmantelar") && await Have("Cajón de Pino") == 1, "cajas, comida, equipo y cosas que no existen: no se desmantelan (y no se gasta nada)");
Check((await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Roble", 0)).PlainMessage!.Contains("al menos 1") && (await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Roble", 101)).PlainMessage!.Contains("No tenés") && (await Me()).Dust == 18, "cero se rechaza («al menos 1») y 101 con solo 3 también («No tenés»): el tope de 100 ya no existe, el límite es la mochila");
Check((await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Roble", 4)).PlainMessage!.Contains("No tenés") && await Have("Madera de Roble") == 3 && (await Me()).Dust == 18, "pedir más de los que tenés: lo dice y no cambia nada");
var drop = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Colmillo de Jabalí", 2);
int colmilloDust = Dismantling.DustPerUnit((await items.GetByNameAsync("Colmillo de Jabalí"))!.Rarity, (await items.GetByNameAsync("Colmillo de Jabalí"))!.DustValue);
Check(drop.Embed is not null && (await Me()).Dust == 18 + 2 * colmilloDust, $"un drop de monstruo también se desmantela (Colmillo de Jabalí: +{colmilloDust} c/u)");
var inv = await new InventoryRepository(factory).GetByDiscordIdAsync(U);
var choices = ItemChoices.ForDismantle(inv, "");
Check(choices.Select(c => c.Value.ToString()).OrderBy(x => x).SequenceEqual(new[] { "Colmillo de Jabalí", "Madera de Roble" }.OrderBy(x => x)) && choices.All(c => c.Name.Length <= 100 && c.Name.Contains("Polvo c/u")), "la lista de /dismantle ofrece SOLO materiales que tenés (no la caja ni la comida), con el Polvo de cada uno: " + string.Join(" | ", choices.Select(c => c.Name)));
Check(ItemChoices.ForDismantle(inv, "ROBLE").Count == 1 && ItemChoices.ForDismantle(inv, "zzz").Count == 0, "y filtra al escribir sin importar mayúsculas");

// -- encantar
var swordRarity = (await items.GetByNameAsync("Espada de Madera"))!.Rarity;
var swordCost = Enchantments.Cost(Enchantments.GearRank(swordRarity));
await Reset(gold: 100000, dust: 1000); await CleanEvents();
Check((await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, null)).Embed!.Fields.Any(f => f.Name.Contains("Arma") && f.Value.Contains("Sin arma puesto")), "/enchant sin elegir y sin arma: muestra el estado");
Check((await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "weapon")).PlainMessage!.Contains("/forge"), "encantar sin arma puesta: manda a forjar y no cobra");
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a WHERE discord_id = @id", new { id = (long)U, w = sword.ItemId, a = talisman.ItemId });
var info = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "");
Check(AllText(info.Embed!).Contains($"{swordCost.Dust} Polvo") && AllText(info.Embed).Contains("Sin encantar") && AllText(info.Embed).Contains("1.000"), "/enchant sin elegir: arma y amuleto con su tier, lo que cuesta el próximo intento y tu Polvo");
var low = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "arma", new FixedRandom(0));     // tier 1
Check(low.Embed is not null && (await Me()) is { WeaponEnchant: 1 } && (await Me()).Gold == 100000 - swordCost.Gold && (await Me()).Dust == 1000 - swordCost.Dust && await Stat("enchant") == 1, $"/enchant arma: sale Tibio, queda puesto, cobra {swordCost.Gold} oro + {swordCost.Dust} Polvo y registra el intento");
Check(AllText(low.Embed!).Contains("Filo Tibio") && AllText(low.Embed).Contains("Mejoró"), "el mensaje cuenta qué salió y que mejoró");
var worse = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "weapon", new FixedRandom(0));   // tier 1 otra vez
Check(AllText(worse.Embed!).Contains("justo el que ya tenías") && (await Me()).WeaponEnchant == 1 && (await Me()).Dust == 1000 - 2 * swordCost.Dust, "si sale igual: lo dice, queda igual y el intento se paga");
var best = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "weapon", new FixedRandom(999));    // tier 5 (v0.22.1: los pesos suman 1000)
Check(AllText(best.Embed!).Contains("Filo Soberano") && (await Me()).WeaponEnchant == 5 && best.Embed.Fields.Any(f => f.Name.Contains("ATQ") && f.Value.Contains("➜")), "tier 5: Soberano, y muestra el ATQ del arma de antes ➜ ahora");
var keep = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "arma", new FixedRandom(0));
Check(AllText(keep.Embed!).Contains("que es mejor") && (await Me()).WeaponEnchant == 5, "si sale peor: «ya tenías uno mejor», te quedás con el tuyo");
var am = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "amuleto", new FixedRandom(800));    // tier 3
Check(AllText(am.Embed!).Contains("Guarda Ardiente") && (await Me()) is { AmuletEnchant: 3, WeaponEnchant: 5 } && am.Embed.Fields.Any(f => f.Name.Contains("DEF")), "el amuleto: «Guarda Ardiente», aparte del arma");
Check((await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "casco")).PlainMessage!.Contains("arma"), "una pieza que no existe: pregunta cuál");
await db.ExecuteAsync("UPDATE users SET gold = 5, dust = 1000 WHERE discord_id = @id", new { id = (long)U });
Check((await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "weapon")).PlainMessage!.Contains("oro"), "sin oro: lo dice");
await db.ExecuteAsync("UPDATE users SET gold = 100000, dust = 1 WHERE discord_id = @id", new { id = (long)U });
var noDust = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, "weapon");
Check(noDust.PlainMessage!.Contains("Polvo") && noDust.PlainMessage.Contains("/dismantle") && (await Me()).Gold == 100000, "sin Polvo: lo dice y manda a desmantelar (sin cobrar el oro)");

// -- el combate y el perfil usan el encantamiento
var player0 = new User { Class = "Guerrero", Level = 10, WeaponEnchant = 0, AmuletEnchant = 0 };
var player5 = new User { Class = "Guerrero", Level = 10, WeaponEnchant = 5, AmuletEnchant = 5 };
var hoja = (await items.GetByNameAsync("Hoja de Acero Puro"))!;     // arma de Espadas: con sinergia de Guerrero
var prof0 = PlayerCombatProfileCalculator.Resolve(player0, hoja, talisman);
var prof5 = PlayerCombatProfileCalculator.Resolve(player5, hoja, talisman);
int weaponWithSynergy = ClassWeaponSynergy.ApplyBonus(hoja.StatValue, "Guerrero", hoja.WeaponFamily);
Check(prof5.Damage - prof0.Damage == Enchantments.Apply(weaponWithSynergy, 5) - weaponWithSynergy && prof5.Damage > prof0.Damage, $"el combate pega con el encantamiento: ATQ {prof0.Damage} ➜ {prof5.Damage} (Soberano sobre el arma con sinergia de clase)");
Check(prof5.Defense - prof0.Defense == Enchantments.Apply(talisman.StatValue, "guarda", 5) - talisman.StatValue && prof5.Defense > prof0.Defense, $"y se defiende con el del amuleto (la Guarda, con su tabla propia desde la v0.25.1): DEF {prof0.Defense} ➜ {prof5.Defense}");
Check(PlayerCombatProfileCalculator.Resolve(player5, null, null).Damage == PlayerCombatProfileCalculator.Resolve(player0, null, null).Damage, "sin arma puesta, un encantamiento «fantasma» no suma nada");
await CleanEvents();
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 4. La penalidad por muerte: mensajes y /autohunt de punta a punta ==");
Check(AdventureModule.DeathPenaltyLine(new DeathPenaltyOutcome(50, 40, 950)) is { } l1 && l1.Contains("**50**") && l1.Contains("5 %") && l1.Contains("EXP") && l1.Contains("**0**"), "el aviso dice cuánto oro perdiste (el 5 %) y que la EXP del nivel volvió a 0");
Check(AdventureModule.DeathPenaltyLine(new DeathPenaltyOutcome(0, 30, 19)) is { } l2 && l2.Contains("EXP") && !l2.Contains("oro"), "con muy poco oro solo cuenta la EXP");
Check(AdventureModule.DeathPenaltyLine(new DeathPenaltyOutcome(30, 0, 600)) is { } l3 && l3.Contains("**30**") && !l3.Contains("EXP"), "con la EXP en 0 solo cuenta el oro");
Check(AdventureModule.DeathPenaltyLine(new DeathPenaltyOutcome(0, 0, 10)) is null && AdventureModule.DeathPenaltyLine(null) is null, "si no se perdió nada (o no se pudo aplicar), no se agrega nada");

CombatState St() => new("hunt", "Jabalí Rabioso", "🐗", 100, 60, 10, [], 0, 0, null, 100, 0, 100, 10, 2, 5, "Guerrero", ClassPassives.For("Guerrero"));
var lostTurn = new TurnResult(null, false, 12, 0, 0, 60, 100, new MonsterHitOutcome(false, 7), 0, default);
var penalty = new DeathPenaltyOutcome(50, 40, 950);
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
Embed CallS(Type t, string method, params object?[] args) => (Embed)t.GetMethod(method, AnyS)!.Invoke(null, args)!;
bool HasPenalty(Embed e) => e.Fields.Any(f => f.Name.Contains("Penalidad") && f.Value.Contains("**50**"));
var foodItem = (await items.GetByNameAsync("Mate Amargo"))!;
Check(HasPenalty(CallS(typeof(AdventureModule), "BuildDefeatEmbed", St(), lostTurn, penalty)) && !CallS(typeof(AdventureModule), "BuildDefeatEmbed", St(), lostTurn, null).Fields.Any(f => f.Name.Contains("Penalidad")), "la derrota de /hunt, /travel y /boss muestra la penalidad (y sin ella no inventa nada)");
Check(HasPenalty(CallS(typeof(UseModule), "BuildCombatDefeatEmbed", St(), foodItem, 5, penalty)), "la derrota mientras te curabas con /use también");
Check(HasPenalty(CallS(typeof(AutoHuntModule), "BuildDefeatEmbed", St(), penalty)), "y la de /autohunt");
var raidSession = new RaidSession { RaidId = Guid.NewGuid(), BossName = "Rey Jabalí", BossEmoji = "👑🐗", BossBaseHp = 100, ZoneRank = 1, BossMaxHp = 200, BossDamage = 20, BossDropItemNames = [], BossGoldBonus = 0, BossXpBonus = 0, ZoneId = 1, ZoneName = "Praderas del Mate", StarterId = 1 };
Check(AllText(CallS(typeof(RaidModule), "BuildWipeEmbed", raidSession, "log")).Contains("5 %") && AllText(CallS(typeof(RaidModule), "BuildWipeEmbed", raidSession, "log")).Contains("banco"), "si el raid cae entero, el mensaje avisa que quien cayó pierde el 5 % (y que el banco no se toca)");

// /autohunt de punta a punta: un jugador con 1 de vida pierde seguro; la penalidad se aplica una vez, el banco queda y se registra la derrota.
var cfgFull = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new Discord.Interactions.InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BankModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfgFull])!;
await Reset(gold: 1000, xp: 30, level: 1, bankGold: 500, hasBank: true); await CleanEvents();
// v0.9.3: con poca vida /autohunt ya no deja pelear (AutoHuntRules), así que la derrota se arma con la vida llena en la zona más difícil: un nivel 1 contra el Devorador de Almas no la cuenta.
await db.ExecuteAsync("UPDATE users SET current_zone_id = (SELECT zone_id FROM zones WHERE kind = 'normal' ORDER BY min_level DESC LIMIT 1) WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
var ah = await AutoHuntModule.ExecuteAsync(
    (IAdventureRepository)sp.GetService(typeof(IAdventureRepository))!, users, items, (IAdventureCombatStarter)sp.GetService(typeof(IAdventureCombatStarter))!, events, U);
var afterAh = await Me();
Check(ah.Embed is not null && ah.Embed.Title.Contains("Derrota") && HasPenaltyAny(ah.Embed), $"/autohunt perdido: el mensaje trae la penalidad ({ah.Embed?.Title})");
Check(afterAh.Gold == 950 && afterAh.Xp == 0 && afterAh.BankGold == 500 && afterAh.Level == 1 && await Stat("fight_lost") == 1, $"y la base quedó con 950 de oro, 0 de EXP, el banco intacto (500) y la derrota registrada (oro {afterAh.Gold}, EXP {afterAh.Xp}, banco {afterAh.BankGold})");
bool HasPenaltyAny(Embed e) => e.Fields.Any(f => f.Name.Contains("Penalidad") && f.Value.Contains("**50**") && f.Value.Contains("EXP"));
await CleanEvents();
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 5. Perfil y ayuda ==");
var zonesRepo = new ZoneRepository(factory); var buffsRepo = new BuffRepository(factory);
async Task<string> Profile() => AllText(await GameModule.BuildProfileEmbedAsync(users, items, zonesRepo, buffsRepo, U, "Tester", "https://example.com/a.png"));
await Reset(gold: 321, level: 1);
var p0 = await Profile();
bool HasNum(string text, string label, int n) => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\*\*{label}:\*\* {n}(?!\d)");
Check(HasNum(p0, "Oro", 321) && !p0.Contains("Banco") && !p0.Contains("Polvo") && !p0.Contains("✨ Filo") && !p0.Contains("Guarda"), "sin banco, sin Polvo y sin encantar: el perfil no suma líneas");
await Reset(gold: 321, dust: 45, bankGold: 700, hasBank: true);
var p1 = await Profile();
Check(HasNum(p1, "Oro", 321) && HasNum(p1, "Banco", 700) && HasNum(p1, "Polvo", 45), "con cuenta del banco y Polvo, el perfil muestra los dos (billetera, banco y Polvo)");
await Reset(gold: 321, bankGold: 0, hasBank: true);
Check(HasNum(await Profile(), "Banco", 0) && !(await Profile()).Contains("Polvo"), "con cuenta pero vacía muestra Banco 0; el Polvo solo aparece si hay");

var wpn = await db.QueryFirstAsync("SELECT item_id AS \"Id\", stat_value AS \"Stat\" FROM items WHERE type = 'Weapon' AND weapon_family IS NULL ORDER BY stat_value LIMIT 1");
var amu = await db.QueryFirstAsync("SELECT item_id AS \"Id\", stat_value AS \"Stat\" FROM items WHERE type = 'Amulet' ORDER BY stat_value LIMIT 1");
await Reset(gold: 321);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a WHERE discord_id = @id", new { id = (long)U, w = (int)wpn.Id, a = (int)amu.Id });
string plain = await Profile();
Check(HasNum(plain, "ATQ", CombatStats.TotalAttack(1, (int)wpn.Stat)) && HasNum(plain, "DEF", CombatStats.TotalDefense(1, (int)amu.Stat)) && !plain.Contains("Filo") && !plain.Contains("Guarda"), "arma y amuleto sin encantar: los números de siempre y ninguna línea de encantamiento");
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, amulet_enchant = 2 WHERE discord_id = @id", new { id = (long)U });
string ench = await Profile();
int expAtk = CombatStats.TotalAttack(1, Enchantments.Apply((int)wpn.Stat, 3)); int expDef = CombatStats.TotalDefense(1, Enchantments.Apply((int)amu.Stat, "guarda", 2));
Check(HasNum(ench, "ATQ", expAtk) && HasNum(ench, "DEF", expDef) && expAtk > CombatStats.TotalAttack(1, (int)wpn.Stat) && ench.Contains("✨+13 %") && ench.Contains("✨+6 %"), $"con encantamiento (Filo III +13 %, Guarda II +6 %) el perfil suma el bonus al ataque ({expAtk}) y a la defensa ({expDef}) y dice cuál tiene");
var userNow = await Me();
var itWpn = await items.GetByIdAsync((int)wpn.Id); var itAmu = await items.GetByIdAsync((int)amu.Id);
var resolved = PlayerCombatProfileCalculator.Resolve(userNow, itWpn, itAmu);
Check(resolved.Damage == expAtk && resolved.Defense == expDef, "y es exactamente lo que usa el combate real (PlayerCombatProfileCalculator)");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });

var infoEmbed = HelpModule.BuildInfoEmbed();
string infoText = AllText(infoEmbed);
Check(infoText.Contains("/bank") && infoText.Contains("/dismantle") && infoText.Contains("/enchant") && infoText.Contains("death"), "/info lista /bank, /dismantle, /enchant y el tema de la penalidad (death)");
Check(infoEmbed.Fields.All(f => f.Value.Length <= 1024) && infoEmbed.Length <= 6000, $"/info respeta los límites de Discord (campo más largo: {infoEmbed.Fields.Max(f => f.Value.Length)} / 1024; total {infoEmbed.Length} / 6000)");
var tut = HelpModule.BuildTutorialEmbed();
Check(AllText(tut).Contains("/bank") && tut.Fields.All(f => f.Value.Length <= 1024) && tut.Length <= 6000, "el tutorial también avisa de la penalidad y del banco, dentro de los límites");


Console.WriteLine("\n== 6. Logros nuevos, páginas y conteo de comandos y enemigos ==");
var allAch = AchievementCatalog.All;
Check(allAch.Count == 23 && allAch.Select(a => a.Key).Distinct().Count() == 23, $"hay 23 logros (17 + Domador y Criador de las mascotas + Asador del Fogón + Renacido del Fuego Nuevo - Coleccionista, retirado con los trofeos + Compañero del Asador y El que volvió de la historia + Racha, la racha más larga del /daily) con claves distintas ({allAch.Count})");
var pagedKeys = AchievementCatalog.Categories.SelectMany(c => c.Keys).ToList();
Check(pagedKeys.Count == allAch.Count && pagedKeys.OrderBy(k => k).SequenceEqual(allAch.Select(a => a.Key).OrderBy(k => k)), "cada logro está en exactamente una página, y las páginas no nombran logros que no existen");
Check(allAch.All(a => a.Tiers.Count == 3 && a.Tiers[0].Threshold < a.Tiers[1].Threshold && a.Tiers[1].Threshold < a.Tiers[2].Threshold), "todos tienen tres tramos crecientes");
string[] newAch = ["comandante", "exterminador", "misionero", "desmantelador", "encantador", "afortunado", "gladiador", "domador", "criador"];
Check(newAch.All(k => AchievementCatalog.Find(k) is { } a && a.Tiers.All(t => t.Reward.Box == BoxGrant.None)), "los 9 logros nuevos pagan solo oro y XP: nada de cajas (sus contadores se pueden inflar)");
Check(allAch.Where(a => a.Tiers.Any(t => t.Reward.Box == BoxGrant.Mythic)).Select(a => a.Key).OrderBy(k => k).SequenceEqual(["matajefes"]), "el Arca del Soberano ahora es solo de Matajefes (Coleccionista se retiró con los trofeos)");
Check(AchievementCatalog.Find("comandante")!.StatKind == "command_used" && AchievementCatalog.Find("exterminador")!.StatKind == "enemy_defeated", "Comandante cuenta command_used y Exterminador enemy_defeated");
Check(AchievementCatalog.ParsePage("2") == 1 && AchievementCatalog.ParsePage("oficios") == 1 && AchievementCatalog.ParsePage("Economía") == 2 && AchievementCatalog.ParsePage("econ") == 2 && AchievementCatalog.ParsePage("") is null && AchievementCatalog.ParsePage("9") is null && AchievementCatalog.ParsePage("xyz") is null, "«aa logros 2 / oficios / economía» elige la página (sin tildes ni mayúsculas); lo demás no");

// enemy_defeated sale de las victorias de combate y de nada más
var rec = new RecordingEvents();
var anyOutcome = new LevelUpOutcome(new User { DiscordId = (long)U, CurrentZoneId = 2, Level = 5 }, 0);
foreach (var k in new[] { "hunt_win", "travel_win", "boss_win", "raid_win" }) await rec.RecordVictoryAsync(U, k, anyOutcome);
await rec.RecordVictoryAsync(U, "daily_claim", anyOutcome);
Check(rec.Kinds.Count(k => k == "enemy_defeated") == 4 && rec.Kinds.Count(k => k == "daily_claim") == 1 && rec.Kinds.Count == 9, "cacería, viaje, jefe y raid suman un enemigo vencido; el /daily no");

// comandos de barra (con interacciones falsas) y de texto
var someUser = FakeProxy.Make<IUser>(new() { ["Id"] = U });
IInteractionContext Ctx(string commandName) => FakeProxy.Make<IInteractionContext>(new() { ["Interaction"] = FakeProxy.Make<IApplicationCommandInteraction>(new() { ["User"] = someUser, ["Data"] = FakeProxy.Make<IApplicationCommandInteractionData>(new() { ["Name"] = commandName }) }) });
var ok = FakeProxy.Make<Discord.Interactions.IResult>(new() { ["IsSuccess"] = true });
var bad = FakeProxy.Make<Discord.Interactions.IResult>(new() { ["IsSuccess"] = false });
var rec2 = new RecordingEvents();
await CommandCounter.RecordSlashAsync(rec2, Ctx("hunt"), ok);
await CommandCounter.RecordSlashAsync(rec2, Ctx("hunt"), bad);
await CommandCounter.RecordSlashAsync(rec2, Ctx("start"), ok);
await CommandCounter.RecordSlashAsync(rec2, FakeProxy.Make<IInteractionContext>(new() { ["Interaction"] = FakeProxy.Make<IComponentInteraction>(new() { ["User"] = someUser }) }), ok);
Check(rec2.Kinds.SequenceEqual(["command_used"]) && rec2.Users.All(u => u == U), "slash: cuenta el comando que se ejecutó; no el que falló, ni /start, ni los botones");
var rec3 = new RecordingEvents();
await CommandCounter.RecordTextAsync(rec3, U, "hunt", true);
await CommandCounter.RecordTextAsync(rec3, U, "  shop buy Pan", true);
await CommandCounter.RecordTextAsync(rec3, U, "hunt", false);
await CommandCounter.RecordTextAsync(rec3, U, "start", true);
await CommandCounter.RecordTextAsync(rec3, U, " START ahora", true);
Check(rec3.Kinds.Count == 2, "texto: cuenta «aa hunt» y «aa shop buy Pan»; no el que falló ni «aa start»");

// de punta a punta: el comando 100 desbloquea Comandante I y el aviso queda en la cola
var missionsRepo = new MissionRepository(factory);
var notifying = new GameEventService(eventsRepo, new ProgressNotifier(missionsRepo, zonesRepo));
await Reset(gold: 0); await CleanEvents();
await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, 'command_used', 99) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = 99", new { id = (long)U });
await CommandCounter.RecordTextAsync(notifying, U, "cd", true);
var notices = notifying.TakeNotices(U);
Check(await Stat("command_used") == 100 && notices.Any(n => (n.Text + (n.Embed is null ? "" : AllText(n.Embed))).Contains("Comandante I")), "el comando 100 suma al contador y deja el aviso «¡Logro desbloqueado!» de Comandante I");
await CommandCounter.RecordTextAsync(notifying, U, "cd", true);
Check(notifying.TakeNotices(U).Count == 0, "el 101 no repite el aviso");

// la pantalla de logros por páginas
var achRepo = new AchievementRepository(factory);
await Reset(gold: 0); await CleanEvents();
var ach0 = await AchievementsModule.BuildViewAsync(eventsRepo, achRepo, U);
Check(AllText(ach0.Embed).Contains("Combate") && AllText(ach0.Embed).Contains("Cazador") && AllText(ach0.Embed).Contains("Exterminador") && !AllText(ach0.Embed).Contains("Recolector") && ach0.Embed.Footer!.Value.Text.Contains("Página 1/4"), "la primera página es Combate: sus 5 logros y ninguno de otra página");
var v2 = await AchievementsModule.BuildViewAsync(eventsRepo, achRepo, U, 2);
Check(AllText(v2.Embed).Contains("Economía") && AllText(v2.Embed).Contains("Afortunado") && AllText(v2.Embed).Contains("Abridor") && !AllText(v2.Embed).Contains("Cazador"), "la página 3 es Economía");
var vBig = await AchievementsModule.BuildViewAsync(eventsRepo, achRepo, U, 99);
Check(AllText(vBig.Embed).Contains("Constancia") && vBig.Embed.Footer!.Value.Text.Contains("Página 4/4"), "una página fuera de rango se acota a la última");
bool within = true; for (int pg = 0; pg < 4; pg++) { var vv = await AchievementsModule.BuildViewAsync(eventsRepo, achRepo, U, pg); within &= vv.Embed.Length <= 6000 && vv.Embed.Fields.All(f => f.Value.Length <= 1024); }
Check(within, "ninguna página pasa los límites de Discord (6000 en total, 1024 por campo)");
var comps0 = ach0.Components!.Components.OfType<ActionRowComponent>().ToList();
var menu0 = comps0[0].Components.OfType<SelectMenuComponent>().Single();
Check(comps0.Count == 1 && menu0.CustomId == $"ach_page:{U}" && menu0.Options.Count == 4 && menu0.Options.Single(o => o.IsDefault == true).Value == "0", "sin nada para reclamar: solo el desplegable de páginas, con la actual marcada");
// con 100 comandos hay un tramo listo: aparece el botón de reclamar, que vuelve a la misma página
await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, 'command_used', 100) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = 100", new { id = (long)U });
var v3 = await AchievementsModule.BuildViewAsync(eventsRepo, achRepo, U, 3);
var comps3 = v3.Components!.Components.OfType<ActionRowComponent>().ToList();
var claimBtn = comps3.SelectMany(r => r.Components).OfType<ButtonComponent>().SingleOrDefault();
var optConst = comps3[0].Components.OfType<SelectMenuComponent>().Single().Options.Single(o => o.Value == "3");
Check(v3.ClaimableCount == 1 && claimBtn is { CustomId: var cid } && cid == $"ach_claim:{U}:3" && optConst.Description.Contains("1 para reclamar") && AllText(v3.Embed).Contains("🎁"), "con un tramo listo aparece «Reclamar (1)» (vuelve a la misma página) y la página de Constancia lo marca");
// reclamarlo: oro y XP chicos, sin caja, y una sola vez
await Reset(gold: 0); await CleanEvents();
await db.ExecuteAsync("INSERT INTO player_stats (discord_id, stat_key, value) VALUES (@id, 'command_used', 100) ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = 100", new { id = (long)U });
var claim1 = await AchievementsModule.ExecuteClaimAsync(users, zonesRepo, eventsRepo, achRepo, events, U);
var afterClaim = await Me();
int boxesHeld = await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.type = 'Caja'", new { id = (long)U });
Check(claim1.Embed is not null && AllText(claim1.Embed).Contains("Comandante I") && afterClaim.Gold == 4 * MissionRewards.GoldUnit(1) && boxesHeld == 0, $"reclamar Comandante I paga {afterClaim.Gold} de oro (4 cacerías de la Zona 1), algo de XP y ninguna caja");
Check((await AchievementsModule.ExecuteClaimAsync(users, zonesRepo, eventsRepo, achRepo, events, U)).PlainMessage!.Contains("No tenés logros") && (await Me()).Gold == afterClaim.Gold, "reclamar de nuevo no paga otra vez");

// los botones y el desplegable llegan al método correcto (con el InteractionService de verdad)
await interactions.AddModuleAsync(typeof(AchievementsModule), sp);
Discord.IComponentInteraction Click(string id) => FakeProxy.Make<Discord.IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<Discord.IComponentInteractionData>(new() { ["CustomId"] = id }) });
string Route(string id) { var r = interactions.SearchComponentCommand(Click(id)); return r.IsSuccess ? r.Command.MethodName : "(ninguno)"; }
Check(Route($"ach_page:{U}") == "HandlePageAsync" && Route($"ach_claim:{U}:2") == "HandleClaimPageButtonAsync" && Route($"achievements_claim:{U}") == "HandleClaimButtonAsync", $"los ids de los controles llegan a su método: página → {Route($"ach_page:{U}")}, reclamar → {Route($"ach_claim:{U}:2")}, el botón viejo sigue andando → {Route($"achievements_claim:{U}")}");
var capt = interactions.SearchComponentCommand(Click($"ach_claim:{U}:2")).RegexCaptureGroups;
Check(capt is { Length: 2 } && capt[0] == U.ToString() && capt[1] == "2", $"y el id del botón de reclamar entrega el dueño y la página por separado ({string.Join(" | ", capt ?? [])})");
await CleanEvents();
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 7. Los comandos de barra nuevos cumplen los límites de Discord para registrarse ==");
var isvc2 = new Discord.Interactions.InteractionService(client.Rest);
await isvc2.AddModulesAsync(typeof(BankModule).Assembly, sp);
bool validName(string n) => n.Length is >= 1 and <= 32 && n == n.ToLowerInvariant() && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
var badCmds = isvc2.SlashCommands.Where(c => !validName(c.Name) || c.Description.Length is < 1 or > 100 || c.Parameters.Any(p => !validName(p.Name) || p.Description.Length is < 1 or > 100 || (p.Choices?.Count ?? 0) > 25)).Select(c => c.Name).ToList();
Check(badCmds.Count == 0, $"todo comando de barra: nombre válido (minúscula, ≤32), descripción de 1 a 100 caracteres y sus parámetros igual ({string.Join(", ", badCmds)})");
var badGroups = isvc2.Modules.Where(m => m.SlashGroupName is not null && (!validName(m.SlashGroupName) || (m.Description ?? "").Length is < 1 or > 100)).Select(m => m.SlashGroupName).ToList();
Check(badGroups.Count == 0, $"y los grupos de subcomandos ({string.Join(", ", badGroups)})");
int topLevel = isvc2.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count();
Check(topLevel <= 100, $"entran en el tope de 100 comandos de nivel superior ({topLevel})");
var bankSubs = isvc2.SlashCommands.Where(c => c.Module.SlashGroupName == "bank").Select(c => c.Name).OrderBy(n => n).ToList();
Check(bankSubs.SequenceEqual(["deposit", "open", "view", "withdraw"]), $"/bank tiene sus cuatro subcomandos ({string.Join(", ", bankSubs)})");
var enchantCmd = isvc2.SlashCommands.Single(c => c.Name == "enchant");
var dismantleCmd = isvc2.SlashCommands.Single(c => c.Name == "dismantle");
Check(enchantCmd.Parameters.Single(p => p.Name == "pieza").Choices.Select(ch => ch.Value.ToString()).SequenceEqual(["weapon", "amulet", "info"]) && dismantleCmd.Parameters.Any(p => p.Name == "item" && p.AutocompleteHandler is not null) && dismantleCmd.Parameters.Any(p => p.Name == "cantidad" && p.ParameterType == typeof(string) && !p.IsRequired && p.MaxValue > 100 && p.MinValue < 1), "/enchant ofrece Arma, Amuleto e Info, /dismantle tiene la lista de materiales y la cantidad SIN tope (texto: un número o «todo»)");
// Los comandos de texto nuevos no chocan con ninguno (CommandService falla al armar si hay ambigüedad)
var texts2 = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts2.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts2.AddModulesAsync(typeof(BankModule).Assembly, sp);
var textNames = textMods.SelectMany(x => x.Commands).SelectMany(c => c.Aliases).Select(a => a.ToLowerInvariant()).ToList();
Check(new[] { "bank", "banco", "dismantle", "desmantelar", "enchant", "encantar" }.All(textNames.Contains), "aa bank/banco, aa dismantle/desmantelar y aa enchant/encantar están registrados");


Console.WriteLine("\n== 8. Pantallas más prolijas: inventario, perfil, Polvo y la Arena en /cd ==");
string ZW = ((char)0x200B).ToString();
var inventoryRepo = new InventoryRepository(factory);
await Reset(gold: 1500, level: 5);
var dropNames = (await db.QueryAsync<string>("SELECT name FROM items WHERE type = 'Material' ORDER BY name LIMIT 8")).ToList();
foreach (var dn in dropNames) await Give(dn, 7);
await Give("Madera de Pino", 3); await Give("Piedra", 2);
var invE = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "Tester");
int dropsIdx = invE.Fields.ToList().FindIndex(f => f.Name.Contains("Drops de monstruo"));
var dropField = invE.Fields[dropsIdx];
var dropLinesShown = dropField.Value.Split('\n');
Check(dropsIdx == 3 && dropField.Inline && dropLinesShown.Length == 8 && dropLinesShown.All(l => l.Contains("**") && l.EndsWith(": 7")), $"los drops van UNO POR RENGLÓN, en la columna izquierda debajo de la madera ({dropLinesShown.Length} renglones para 8 drops)");
Check(!dropField.Value.Contains(' ') && !dropField.Value.Contains(' '), "sin las fichas de espacios que no se cortan (el ícono quedaba huérfano al final del renglón anterior)");
Check(invE.Fields.Length == 6 && invE.Fields.All(f => f.Inline) && invE.Fields.Where((f, i) => i % 3 == 2).All(f => f.Name == ZW && f.Value == ZW) && invE.Fields[0].Name.Contains("Madera") && invE.Fields[1].Name.Contains("Mineral"), "DOS columnas: Madera | Mineral y debajo Drops | (nada): todo en línea y con un tercer campo vacío que cierra cada fila (al costado no suma altura)");
// el peor caso: todo el catálogo a 9.999
await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, 9999 FROM items WHERE type IN ('Madera','Mineral','Material','Consumable','Caja') ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = 9999", new { id = (long)U });
int materialCount = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM items WHERE type = 'Material'");
var invBig = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "Tester");
var bigLeft = invBig.Fields.Where((f, i) => i % 3 == 0).ToList(); var bigRight = invBig.Fields.Where((f, i) => i % 3 == 1).ToList();
var bigDropFields = invBig.Fields.SkipWhile(f => !f.Name.Contains("Drops de monstruo")).TakeWhile((f, i) => i == 0 || f.Name == ZW).Where(f => f.Value != ZW).ToList();   // el campo de los drops y su continuación sin título
Check(bigLeft[0].Name.Contains("Madera") && bigRight[0].Name.Contains("Mineral") && bigLeft[1].Name.Contains("Drops") && bigRight[1].Name == ZW && bigRight[1].Value != ZW && bigLeft[2].Name.Contains("Consumibles") && invBig.Fields.All(f => f.Inline), "con todo (v0.26.3): Madera | Mineral, los drops en dos columnas y debajo la Comida (con las Cajas)");
int bigDropLines = bigDropFields.Sum(f => f.Value.Split('\n').Length);
Check(invBig.Length <= 6000 && invBig.Fields.All(f => f.Value.Length <= 1024) && bigDropLines == materialCount && bigDropFields.Skip(1).All(f => f.Name == ZW), $"con todo el catálogo a 9.999 entra ({invBig.Length} de 6000 caracteres), los {materialCount} drops van uno por renglón repartidos en {bigDropFields.Count} campos, los de abajo sin título");
await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
var invEmpty = await GameModule.BuildInventoryEmbedAsync(inventoryRepo, U, "Tester");
Check(invEmpty.Description.Contains("Todavía no tenés ningún material") && invEmpty.Fields.Length == 0, "inventario vacío: el aviso de siempre");

// perfil: todo apilado
await Reset(gold: 4321, dust: 12, bankGold: 7450, hasBank: true, level: 19);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, amulet_id = @a, weapon_enchant = 2, amulet_enchant = 0, daily_streak = 3 WHERE discord_id = @id", new { id = (long)U, w = (int)wpn.Id, a = (int)amu.Id });
async Task<Embed> ProfileEmbed() => await GameModule.BuildProfileEmbedAsync(users, items, zonesRepo, buffsRepo, U, "Tester", "https://example.com/a.png");
var pe = await ProfileEmbed();
var shownNames = pe.Fields.Where(f => f.Name != ZW).Select(f => f.Name).ToList();
Check(shownNames.SequenceEqual(["XP", "Vida"]) && pe.Fields.Length == 3, $"el perfil va apilado: XP, Vida y el bloque de números sin título (v0.25.2: ya no hay «Estado» ni «Habilidad») ({string.Join(" / ", shownNames)})");
Check(pe.Fields.All(f => !f.Inline) && pe.Fields.Count(f => f.Name == ZW) == 1 && pe.Fields.Single(f => f.Name == ZW).Value.Length > 100, "todo el perfil apilado, un campo debajo del otro: sin columnas (Polvo y Racha quedaban muy separados) y el único campo sin título es el bloque de números, con contenido (un campo VACÍO como separador dejaba un hueco enorme)");
string ProfBlock(Embed e) => ProfileProbe.Stats(e);
string ProfLine(Embed e, string label) => ProfileProbe.Line(e, label);
var atkField = ProfLine(pe, "ATQ");
Check(!System.Text.RegularExpressions.Regex.IsMatch(atkField, @"\(\+\d+\)") && atkField.Contains("✨+8 %") && !System.Text.RegularExpressions.Regex.IsMatch(ProfLine(pe, "DEF"), @"\(\+\d+\)") && !ProfLine(pe, "DEF").Contains("✨"), $"sin el «(+N)» del arma ni del amuleto, y el encantamiento queda como «✨+8 %» al final de la línea del arma ({atkField})");
Check(ProfLine(pe, "Oro") == "💰 **Oro:** 4.321" && ProfLine(pe, "Banco") == "🏦 **Banco:** 7.450" && ProfLine(pe, "Polvo") == "✨ **Polvo:** 12" && ProfLine(pe, "Racha") == "🎁 **Racha:** día 3", "oro y banco con separador de miles, Polvo y racha: una línea cada uno, con su ícono PRIMERO y el nombre con dos puntos (el ícono de siempre mientras no estén subidos los propios)");
var zonesAll = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
int expectedMax = zonesAll[ZoneRanking.MaxUnlockedRank(zonesAll, 19, 0) - 1].ZoneId;
string ZoneLine(Embed e) => ProfileProbe.ZoneLine(e);
var zoneIcon = zonesAll.First(z => z.ZoneId == 1).Emoji;
string zonePrefix = string.IsNullOrWhiteSpace(zoneIcon) ? string.Empty : zoneIcon + " ";
Check(ZoneLine(pe) == $"{zonePrefix}**Zona:** 1 (máx. {expectedMax})" && !ZoneLine(pe).Contains("Praderas") && !pe.Description.Contains("Praderas") && !pe.Fields.Any(f => f.Name.Contains("Zona actual")) && !ProfBlock(pe).Contains("**Zona:**"), $"la zona va en la descripción (arriba de la XP), SIN el nombre: «Zona 1 (máx. {expectedMax})» con el ícono de la zona PRIMERO ({ZoneLine(pe)})");
await db.ExecuteAsync("UPDATE users SET highest_zone_cleared = @z WHERE discord_id = @id", new { id = (long)U, z = zonesAll[2].ZoneId });
int expectedMax2 = zonesAll[ZoneRanking.MaxUnlockedRank(zonesAll, 19, zonesAll[2].ZoneId) - 1].ZoneId;
var pe2 = await ProfileEmbed();
Check(expectedMax2 > expectedMax && ZoneLine(pe2).Contains($"(máx. {expectedMax2})"), $"al derrotar jefes el máximo sube: (máx. {expectedMax2})");
await Reset(gold: 50);
var peBare = await ProfileEmbed();
Check(ProfLine(peBare, "Banco") == string.Empty && ProfLine(peBare, "Polvo") == string.Empty && ProfLine(peBare, "Racha") == string.Empty && ProfLine(peBare, "Oro").Length > 0, "sin banco, sin Polvo y sin racha no salen esas líneas");

// /cd con la Arena
var cooldownsRepo = new CooldownRepository(factory);
await Reset(gold: 50);
var inList = await CooldownModule.BuildStatusEmbedAsync(cooldownsRepo, users, U, new FakeArena([U], TimeSpan.FromMinutes(95)));
var outList = await CooldownModule.BuildStatusEmbedAsync(cooldownsRepo, users, U, new FakeArena([], TimeSpan.FromMinutes(95)));
string[] inLines = inList.Description.Split('\n');
Check(inLines[^1].Contains("Arena") && inLines[^1].Contains("anotado ✅") && inLines[^1].Contains("1h 35m") && inLines.Length == CooldownCatalog.All.Count + 2, $"/cd: ya anotado → «{inLines[^1]}» (la última línea, después de los cooldowns y el diario)");
string[] outLines = outList.Description.Split('\n');
Check(outLines[^1].Contains("todavía no te anotaste") && outLines[^1].Contains("/arena join") && outLines[^1].Contains("1h 35m"), $"/cd: sin anotarte → «{outLines[^1]}»");
var noArena = await CooldownModule.BuildStatusEmbedAsync(cooldownsRepo, users, U);
var brokenArena = await CooldownModule.BuildStatusEmbedAsync(cooldownsRepo, users, U, new FakeArena(null, TimeSpan.Zero));
Check(!noArena.Description.Contains("Arena") && !brokenArena.Description.Contains("Arena") && brokenArena.Description.Contains("Diario"), "sin servicio de Arena, o si falla, /cd sale igual sin esa línea");
var realArena = (IArenaService)sp.GetService(typeof(IArenaService))!;
var viaReal = await CooldownModule.BuildStatusEmbedAsync(cooldownsRepo, users, U, realArena);
Check(viaReal.Description.Split('\n')[^1].Contains("todavía no te anotaste"), "con el servicio real (solo lee el torneo de hoy), un jugador que no se anotó ve el aviso");
Check(CooldownModule.ArenaLine(false, TimeSpan.FromHours(30)).Contains("1d 6h 0m") || CooldownModule.ArenaLine(false, TimeSpan.FromHours(30)).Contains("30"), "el tiempo que falta usa el formato de siempre de los cooldowns");

// Polvo y encantamientos más aireados
await Reset(gold: 321, dust: 45);
await db.ExecuteAsync("UPDATE users SET weapon_id = @w, weapon_enchant = 3 WHERE discord_id = @id", new { id = (long)U, w = (int)wpn.Id });
var enchantInfo = DustModule.BuildInfoEmbed(await Me(), await items.GetByIdAsync((int)wpn.Id), null);
Check(enchantInfo.Fields.All(f => !f.Inline && f.Name != ZW) && enchantInfo.Description.Split("\n\n").Length == 4 && enchantInfo.Fields.Single(f => f.Name == "🎲 Tiers posibles").Value.Split('\n').Length == 2, "/enchant sin elegir: párrafos separados y los campos apilados (arma, amuleto, Polvo, tiers en dos renglones)");
var dismantleEmbed = (await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", 1));
Check(dismantleEmbed.PlainMessage is not null || dismantleEmbed.Embed is not null, "/dismantle sigue contestando");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 9. /enchant info: opciones, chances y bonus ==");
Check(Math.Abs(Enumerable.Range(1, 5).Sum(Enchantments.ChancePercent) - 100) < 1e-9 && Enchantments.ChancePercent(1) == 42 && Enchantments.ChancePercent(5) == 1 && Enchantments.ChancePercent(0) == 0, "las chances por tier suman 100 % (42 / 30 / 18 / 9 / 1)");
Check(Enchantments.AttemptsToReach(1) == 1 && Math.Abs(Enchantments.AttemptsToReach(2) - 1 / 0.58) < 1e-9 && Math.Abs(Enchantments.AttemptsToReach(3) - 1 / 0.28) < 1e-9 && Math.Abs(Enchantments.AttemptsToReach(4) - 10) < 1e-9 && Math.Abs(Enchantments.AttemptsToReach(5) - 100) < 1e-9, "los intentos promedio hasta cada tier: 1 / ~1,7 / ~3,6 / ~10 / ~100");
var optEmbed = DustModule.BuildOptionsEmbed();
string optText = AllText(optEmbed);
Check(optEmbed.Fields.All(f => !f.Inline) && optEmbed.Fields.All(f => f.Value.Length <= 1024) && optEmbed.Length <= 6000, $"la pantalla de opciones va apilada y entra en los límites de Discord ({optEmbed.Length} de 6000)");
Check(optText.Contains("Tibio") && optText.Contains("42 %") && optText.Contains("Soberano") && optText.Contains("1 %") && optText.Contains("~100 intentos") && optText.Contains("~1,7 intentos") && optText.Contains("+4/8/13/19/26 % de ATQ") && optText.Contains("+3/6/9/13/18 % de DEF"), "dice cada tier con su chance (Tibio 42 % ... Soberano 1 %) y cuántos intentos llevan, y el bonus de cada efecto en su lista (Filo +4/8/13/19/26, Guarda +3/6/9/13/18)");
Check(optText.Contains("112 oro + 10 Polvo") && optText.Contains("3.000 oro + 60 Polvo") && optText.Contains("Zona 5") && optText.Contains("Mítico 500") && optText.Contains("Común 1") && optText.Contains("/dismantle"), "dice lo que cuesta cada intento por zona de la pieza y de dónde sale el Polvo");
Check(optText.Contains("nunca baja") && optText.Contains("pierde su encantamiento"), "explica que el tier nunca baja y que vender la pieza borra el encantamiento");
await Reset(gold: 10);
foreach (var word in new[] { "info", " INFO ", "opciones", "ayuda" })
{
    var viaInfo = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, word);
    Check(viaInfo.Embed is not null && viaInfo.Embed.Title.Contains("opciones") && (await Me()).Gold == 10 && (await Me()).Dust == 0, $"«enchant {word.Trim()}» muestra las opciones y no cobra nada");
}
var infoNoAccount = await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, 999000000000009199UL, "info");
Check(infoNoAccount.Embed is not null, "las opciones se ven aunque no tengas cuenta (es la misma para todos)");
var statusEmbed = (await DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, null)).Embed!;
Check(statusEmbed.Footer!.Value.Text.Contains("/enchant info"), "sin elegir pieza (tu estado) el pie manda a /enchant info");
Check(HelpTopics.Find("enchant")?.Key == "enchant" && HelpTopics.Find(" Encantar ")?.Key == "enchant" && HelpTopics.Find("encantamientos")?.Key == "enchant" && HelpTopics.Find("")?.Key is null && HelpTopics.Find(null)?.Key is null && HelpTopics.Find("banco")?.Key == "bank", "los temas de «info»: encantamientos se encuentra por su clave, su nombre y sus alias");
Check(HelpModule.BuildTopicEmbed("enchant").Title.Contains("opciones") && HelpModule.BuildTopicEmbed("encantar").Title.Contains("opciones") && HelpModule.BuildTopicEmbed("").Title.Contains("Comandos") && HelpModule.BuildTopicEmbed("cosa rara").Title.Contains("Ayuda por tema") && HelpModule.BuildTopicEmbed(null).Title.Contains("Comandos"), "«/info enchant» y «aa info enchant» abren las opciones; sin tema, la lista de comandos; con un tema que no existe, la lista de temas");
Check(texts2.Search("info enchant").IsSuccess && texts2.Search("info").IsSuccess && texts2.Search("enchant info").IsSuccess && texts2.Search("encantar info").IsSuccess, "los comandos de texto «aa info», «aa info enchant», «aa enchant info» y «aa encantar info» se reconocen");
Check(isvc2.SlashCommands.Single(c => c.Name == "enchant").Parameters.Single(p => p.Name == "pieza").Choices.Select(ch => ch.Value.ToString()).SequenceEqual(["weapon", "amulet", "info"]) && isvc2.SlashCommands.Single(c => c.Name == "info").Parameters.Single() is { AutocompleteHandler: not null } topicParam && topicParam.Choices.Count == 0, "/enchant ofrece Arma, Amuleto e Info; /info tiene la lista desplegable de temas");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 10. /autohunt con poca vida (calibración con la penalidad) ==");
Check(AutoHuntRules.MinHpPercent == 65 && !AutoHuntRules.IsTooHurt(100, 100) && !AutoHuntRules.IsTooHurt(65, 100) && AutoHuntRules.IsTooHurt(64, 100) && AutoHuntRules.IsTooHurt(1, 100) && !AutoHuntRules.IsTooHurt(0, 100) && !AutoHuntRules.IsTooHurt(221, 340) && AutoHuntRules.IsTooHurt(220, 340), "la regla: con el 65 % de la vida o más se puede auto-cazar; por debajo no (y con 0 HP el mensaje es el de siempre)");
var tooHurtEmbed = AutoHuntModule.BuildTooHurtEmbed(212, 340);
Check(tooHurtEmbed.Description.Contains("212/340") && tooHurtEmbed.Description.Contains("62 %") && tooHurtEmbed.Description.Contains("65 %") && tooHurtEmbed.Description.Contains("/heal") && tooHurtEmbed.Description.Contains("/hunt") && tooHurtEmbed.Description.Contains("No se gastó el cooldown"), "el aviso dice cuánta vida tenés, el mínimo, qué hacer (/heal o /hunt) y que no se gastó el cooldown");
var ahStarter = (IAdventureCombatStarter)sp.GetService(typeof(IAdventureCombatStarter))!;
var ahRepo = (IAdventureRepository)sp.GetService(typeof(IAdventureRepository))!;
await Reset(gold: 1000, xp: 30, level: 5); await CleanEvents();
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp / 2 WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
var hurt = await AutoHuntModule.ExecuteAsync(ahRepo, users, items, ahStarter, events, U);
var afterHurt = await Me();
int cooldownRows = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM cooldowns WHERE discord_id = @id", new { id = (long)U });
Check(hurt.Embed is not null && hurt.Embed.Title.Contains("Muy herido") && cooldownRows == 0 && afterHurt.Gold == 1000 && afterHurt.Xp == 30 && afterHurt.CurrentHp == afterHurt.MaxHp / 2 && await Stat("fight_lost") == 0, "a la mitad de la vida: no pelea, no gasta el cooldown, no toca oro ni EXP ni vida y no registra nada");
await db.ExecuteAsync("UPDATE users SET current_hp = max_hp * 8 / 10 WHERE discord_id = @id", new { id = (long)U });
var healthy = await AutoHuntModule.ExecuteAsync(ahRepo, users, items, ahStarter, events, U);
Check(healthy.Embed is not null && !healthy.Embed.Title.Contains("Muy herido") && await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM cooldowns WHERE discord_id = @id", new { id = (long)U }) == 1, $"con el 80 % de la vida pelea como siempre y se gasta el cooldown ({healthy.Embed?.Title})");
await CleanEvents();
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id; DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 11. Desmantelar sin tope por vez (el 100 ya no existe) ==");
await Reset(gold: 10); await CleanEvents();
await Give("Madera de Pino", 250);
var big100 = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", 100);
Check(big100.Embed is not null && AllText(big100.Embed).Contains("+100") && (await Me()).Dust == 100 && await Have("Madera de Pino") == 150 && await Stat("dismantle") == 100, "100 de golpe: +100 de Polvo (Común = 1 c/u), quedan 150 y se registran las 100 unidades");
await Give("Madera de Roble", 3);
var tooMany = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Roble", 100);
var zeroQty = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", 0);
Check(tooMany.PlainMessage is not null && tooMany.PlainMessage.Contains("No tenés") && zeroQty.PlainMessage!.Contains("al menos 1") && (await Me()).Dust == 100 && await Have("Madera de Roble") == 3 && await Have("Madera de Pino") == 150, "pedir más de lo que tenés (100 con 3) o cero: se rechaza y no se toca nada");
// dos desmantelamientos de 100 a la vez con solo 150: la resta es guardada, paga uno solo
var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", 100)));
Check(race.Count(r => r.Embed is not null) == 1 && race.Count(r => r.PlainMessage is not null) == 1 && (await Me()).Dust == 200 && await Have("Madera de Pino") == 50, "dos de 100 a la vez con solo 150: se paga uno solo y nunca queda negativo (50 de Madera, 200 de Polvo)");
// el caso más grande: 100 unidades de lo más raro (Mítico = 500 c/u = 50.000 de Polvo) entra en la columna
await Give("Corteza del Árbol de Vida", 100);
var mythic = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Corteza del Árbol de Vida", 100);
Check(mythic.Embed is not null && (await Me()).Dust == 200 + 50000 && AllText(mythic.Embed).Contains("50.000") && await Have("Corteza del Árbol de Vida") == 0, "100 unidades Míticas: +50.000 de Polvo, sin desbordar");
var viaText = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "madera de pino", 50);
Check(viaText.Embed is not null && await Have("Madera de Pino") == 0 && (await Me()).Dust == 50250, "el nombre en minúsculas también anda, hasta dejar la mochila en 0");
{
    await Give("Madera de Pino", 1500);
    var big = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "1.000");
    Check(big.Embed is not null && await Have("Madera de Pino") == 500 && (await Me()).Dust == 50250 + 1000, "1.000 de golpe, escrito con punto de miles (más de 100: el tope ya no existe): +1.000 de Polvo y quedan 500");
    var allPino = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "todo");
    Check(allPino.Embed is not null && AllText(allPino.Embed).Contains("+500") && await Have("Madera de Pino") == 0 && (await Me()).Dust == 51250 + 500, "«todo»: se desmantelan las 500 que quedaban y la mochila queda en 0");
    var nothingLeft = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "todo");
    Check(nothingLeft.PlainMessage is not null && nothingLeft.PlainMessage.Contains("No tenés") && (await Me()).Dust == 51750, "«todo» sin tener ninguna: lo dice y no toca nada");
    var inexistente = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Cosa Inexistente", "todo");
    var caja = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Cajón de Pino", "todo");
    Check(inexistente.PlainMessage!.Contains("no se puede desmantelar") && caja.PlainMessage!.Contains("no se puede desmantelar") && (await Me()).Dust == 51750, "«todo» de algo que no existe o que no se desmantela (una caja): lo explica y no toca nada");
    var sinItem = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "  ", "todo");
    Check(sinItem.PlainMessage!.Contains("Qué querés desmantelar"), "«aa desmantelar todo» sin ítem: pregunta cuál");
    await Give("Madera de Pino", 5);
    var cero = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "0");
    var basura = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "muchos");
    var negativo = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "-5");
    var enorme = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "99999999999");
    Check(cero.PlainMessage!.Contains("al menos 1") && basura.PlainMessage!.Contains("No entendí") && negativo.PlainMessage!.Contains("No entendí") && enorme.PlainMessage!.Contains("demasiado grande") && await Have("Madera de Pino") == 5 && (await Me()).Dust == 51750,
        "cantidad 0, «muchos», «-5» o un número enorme: se rechaza con su motivo y no se toca nada");
    await Give("Corteza del Árbol de Vida", 3);
    var desborda = await DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Corteza del Árbol de Vida", 5_000_000);
    Check(desborda.PlainMessage!.Contains("demasiado grande") && await Have("Corteza del Árbol de Vida") == 3 && (await Me()).Dust == 51750, "5 millones de Míticos (2.500 millones de Polvo) no desbordan: se rechaza antes de tocar la base");
    await db.ExecuteAsync("DELETE FROM inventory WHERE discord_id = @id", new { id = (long)U });
    await Give("Madera de Pino", 200);
    var raceAll = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => DustModule.ExecuteDismantleAsync(users, items, dustRepo, events, U, "Madera de Pino", "todo")));
    Check(raceAll.Count(r => r.Embed is not null) >= 1 && (await Me()).Dust == 51750 + 200 && await Have("Madera de Pino") == 0 && raceAll.All(r => r.Embed is not null || r.PlainMessage!.Contains("No tenés")),
        "dos «todo» a la vez con 200: el Polvo se paga UNA sola vez (+200) y la mochila nunca queda negativa");
}
await CleanEvents();
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });


Console.WriteLine("\n== 12. /info por tema: la ayuda de cómo funciona cada cosa ==");
var topicsAll = HelpTopics.All;
Check(topicsAll.Count >= 15 && topicsAll.Select(t => t.Key).Distinct().Count() == topicsAll.Count, $"hay {topicsAll.Count} temas con claves distintas ({string.Join(", ", topicsAll.Select(t => t.Key))})");
// ningún nombre ni alias lleva a otro tema: cada uno se encuentra a sí mismo
var collisions = topicsAll.SelectMany(t => new[] { t.Key, t.Name }.Concat(t.Aliases).Select(term => (term, t.Key))).Where(x => HelpTopics.Find(x.term)?.Key != x.Key).Select(x => $"{x.term}→{HelpTopics.Find(x.term)?.Key ?? "nada"}").ToList();
Check(collisions.Count == 0, $"cada clave, nombre y alias lleva a SU tema y a ningún otro ({string.Join(", ", collisions)})");
var broken = new List<string>();
string slashNames = string.Join("|", isvc2.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct());
var knownCommands = isvc2.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).ToHashSet();
foreach (var t in topicsAll)
{
    try
    {
        var e = t.Build();
        string all = AllText(e) + " " + (e.Footer?.Text ?? "");
        if (e.Length > 6000 || e.Fields.Any(f => f.Value.Length > 1024 || f.Name.Length > 256) || (e.Description?.Length ?? 0) > 4096 || string.IsNullOrWhiteSpace(e.Title)) broken.Add($"{t.Key}: límites ({e.Length})");
        // todo comando de barra que el tema nombra tiene que existir
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(all, @"(?<![\w/])/([a-z]+)"))
            if (!knownCommands.Contains(m.Groups[1].Value)) broken.Add($"{t.Key}: nombra /{m.Groups[1].Value}, que no existe");
    }
    catch (Exception ex) { broken.Add($"{t.Key}: tiró {ex.GetType().Name}"); }
}
Check(broken.Count == 0, $"todos los temas se arman, entran en los límites de Discord y nombran solo comandos que existen ({string.Join("; ", broken)})");
// el texto dice los números del juego
string T(string key) => AllText(HelpTopics.Find(key)!.Build()) + " " + (HelpTopics.Find(key)!.Build().Footer?.Text ?? "");
Check(T("death").Contains("5 %") && T("death").Contains("65 %") && T("death").Contains("20 de oro") && T("bank").Contains("1.000") && T("dismantle").Contains("sin tope") && T("dismantle").Contains("todo") && T("dismantle").Contains("Mítico 500"), "muerte: 5 % y 65 %; banco: 1.000; desmantelar: sin tope y 500 por Mítico");
Check(T("play").Contains("×5") && T("play").Contains("×2") && T("play").Contains("10") && T("hunt").Contains("1 min") && T("hunt").Contains("30 min") && T("hunt").Contains("6 %") && T("hunt").Contains("40 %"), "casino: ×5 y ×2 y apuesta mínima 10; combate: cooldowns 1 y 30 min, drops 6 % y 40 %");
Check(T("boss").Contains("5 h") && T("boss").Contains("30 min") && T("boss").Contains("15 cacerías") && T("boss").Contains("2 a 6") && T("boxes").Contains("2 h") && T("boxes").Contains("1 a 10") && T("daily").Contains("24 h") && T("daily").Contains("20 cacerías de oro") && T("daily").Contains("48 h") && T("arena").Contains("32") && T("trade").Contains("2 minutos"), "jefes: 5 h / 30 min / ×15 / 2 a 6; cajas: 2 h y 1 a 10; diario: 24 h, 20 cacerías de oro, 48 h; arena: 32; intercambio: 2 min");
Check(T("gather").Contains("Común 1 a 5") && T("gather").Contains("68 %") && T("gather").Contains("Mítico 0,5 %") && T("stats").Contains("15 de vida") && T("stats").Contains("100 × nivel^1,5") && T("forge").Contains("×1,5") && T("missions").Contains("23 logros") && T("missions").Contains("3 misiones por día"), "recolección 1 a 5 y 68 %; nivel: +15 de vida; herrería ×1,5; 23 logros y 3 misiones por día");
var classesText = T("classes");
Check(new[] { "Guerrero", "Ninja", "Arquero", "Hechicero", "Aguante", "Sombra", "Lluvia de Flechas", "Bola de Fuego", "Sifón de Almas" }.All(classesText.Contains), "clases: las cuatro con su pasiva y su habilidad");
// resolver: sin tema → comandos, tema → su ayuda, desconocido → la lista de temas avisando
Check(HelpTopics.Resolve(null).Title.Contains("Comandos") && HelpTopics.Resolve("play").Title.Contains("casino") && HelpTopics.Resolve("Casino ").Title.Contains("casino") && HelpTopics.Resolve("ench").Title.Contains("Encantamientos"), "«info play» y «info casino» abren el casino; «info ench» (único que empieza así) abre encantamientos");
var unknownEmbed = HelpTopics.Resolve("xyz");
Check(unknownEmbed.Title.Contains("Ayuda por tema") && unknownEmbed.Description.Contains("No encontré el tema **xyz**") && topicsAll.All(t => AllText(unknownEmbed).Contains($"**{t.Key}**")) && unknownEmbed.Fields.All(f => f.Value.Length <= 1024), "un tema que no existe responde con la lista de todos los temas, diciendo que no lo encontró");
Check(HelpTopics.Find("c") is null && HelpTopics.Find("enchant info")?.Key == "enchant" && HelpTopics.Find("ENCHANT")?.Key == "enchant" && HelpTopics.Find("info play") is null, "una letra ambigua no adivina; con dos palabras prueba la primera; no distingue mayúsculas");
Check(HelpTopics.Search("enc").First().Key == "enchant" && HelpTopics.Search("").Count == topicsAll.Count && HelpTopics.Search("").Take(AutocompleteText.MaxChoices).Count() == Math.Min(topicsAll.Count, AutocompleteText.MaxChoices) && HelpTopics.Search("votar").First().Key == "vote" && HelpTopics.Search("zzzz").Count == 0 && HelpTopics.Search("caceria").Any(t => t.Key == "hunt"), "la lista desplegable filtra por clave, nombre y alias sin importar tildes, y el desplegable se corta en las 25 opciones de Discord (el tema nuevo, vote, va último a propósito)");
var commandsList = HelpModule.BuildInfoEmbed();
Check(AllText(commandsList).Contains("/info tema:<tema>") && topicsAll.All(t => AllText(commandsList).Contains(t.Key)) && commandsList.Length <= 6000 && commandsList.Fields.All(f => f.Value.Length <= 1024), "la lista de comandos avisa que existe /info tema y nombra todos los temas");
Check(texts2.Search("info play").IsSuccess && texts2.Search("info enchant").IsSuccess && texts2.Search("in bank").IsSuccess, "«aa info play», «aa info enchant» y «aa in bank» se reconocen");
// MaxTier ya no es un número suelto: sale de la tabla y nada indexa fuera de ella (el error del dueño, MaxTier = 100)
Check(Enchantments.MaxTier == 5 && Enchantments.IsValidTier(1) && Enchantments.IsValidTier(5) && !Enchantments.IsValidTier(0) && !Enchantments.IsValidTier(6) && !Enchantments.IsValidTier(100) && Enchantments.TierName(6) == "Sin encantar" && Enchantments.BonusPercent(100) == 0 && Enchantments.Label("weapon", 100) == "Sin encantar" && Enchantments.ChancePercent(7) == 0 && Enchantments.AttemptsToReach(7) == 0, "los tiers fuera de la tabla (0, 6, 100) no rompen nada: «Sin encantar» y 0");


Console.WriteLine("\n== 13. El trueque con el tabernero: 3 drops por 1 de la misma zona ==");
var exRepo = new DropExchangeRepository(factory);
var exDrops = await exRepo.GetExchangeableDropsAsync();
var byZone = exDrops.GroupBy(d => d.ZoneId).OrderBy(g => g.First().MinLevel).ToList();
Check(DropExchange.GiveAmount == 3 && DropExchange.GetAmount == 1 && DropExchange.MaxTimes == 50 && exDrops.Count == 15 && byZone.Count == 5 && byZone.All(g => g.Count() == 3) && exDrops.All(d => d.MinLevel > 0) && exDrops.Select(d => d.Name).Distinct().Count() == 15, "la regla es 3 por 1 (hasta 50 juntos) y la lista tiene los 15 drops de monstruos: 3 por cada una de las 5 zonas");
Check(!exDrops.Any(d => d.Name is "Cajón de Pino" or "Cofre de Oro" or "Collar de Cuero Viejo" or "Madera de Pino" or "Hierro"), "no están los cofres de los jefes, ni los trofeos de las cajas, ni la madera ni los minerales");
var z2 = byZone[1].ToList(); var z3 = byZone[2].ToList();
var A = z2[0]; var B = z2[1]; var Tz = z2[2]; var X = z3[0];
await Reset(gold: 10); await CleanEvents();
await Give(A.Name, 7);
var ex1 = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name, B.Name, 1);
Check(ex1.Embed is not null && AllText(ex1.Embed).Contains("3×") && await Have(A.Name) == 4 && await Have(B.Name) == 1 && await Stat("drop_exchange") == 1, $"un cambio: entrega 3 de {A.Name} (quedan 4) y recibe 1 de {B.Name}; se registra el evento");
var ex2 = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name.ToLowerInvariant(), B.Name, 1);
Check(ex2.Embed is not null && await Have(A.Name) == 1 && await Have(B.Name) == 2 && await Stat("drop_exchange") == 2, "el nombre en minúsculas también anda; el segundo cambio deja 1 y 2");
var exNo = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name, B.Name, 1);
Check(exNo.PlainMessage is not null && exNo.PlainMessage.Contains("hacen falta **3**") && exNo.PlainMessage.Contains("tenés **1**") && await Have(A.Name) == 1 && await Have(B.Name) == 2 && await Stat("drop_exchange") == 2, "con menos de 3 no cambia nada y dice cuántos hacen falta y cuántos tenés");
await Give(A.Name, 10);
var ex3 = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name, Tz.Name, 3);
Check(ex3.Embed is not null && await Have(A.Name) == 1 && await Have(Tz.Name) == 3 && await Stat("drop_exchange") == 5, "3 cambios juntos: se entregan 9 y se reciben 3 (y el evento suma 3)");
await Give(X.Name, 5);
var exZone = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, X.Name, A.Name, 1);
var exSame = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name, A.Name, 1);
Check(exZone.PlainMessage is not null && exZone.PlainMessage.Contains(X.ZoneName) && exZone.PlainMessage.Contains(A.ZoneName) && exZone.PlainMessage.Contains("misma") && exSame.PlainMessage!.Contains("distinto") && await Have(X.Name) == 5 && await Have(A.Name) == 1, "de zonas distintas, o el mismo ítem: se rechaza diciendo por qué y no se toca nada");
await Give("Madera de Pino", 9); await Give("Cajón de Pino", 3);
var exWood = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, "Madera de Pino", A.Name, 1);
var exBox = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, X.Name, "Cajón de Pino", 1);
var exTrophy = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, "Mate Amargo", A.Name, 1);
Check(new[] { exWood, exBox, exTrophy }.All(r => r.PlainMessage is not null && r.PlainMessage.Contains("solo cambia **drops de monstruos**")) && await Have("Madera de Pino") == 9 && await Have("Cajón de Pino") == 3, "madera, cajas y comida no se cambian (los trofeos ya no existen) (ni como lo que das ni como lo que recibís)");
var exCap = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, X.Name, B.Name, 51);
var exZero = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, X.Name, B.Name, 0);
var exUnknown = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, "Cosa Rara", A.Name, 1);
var exNoAcct = await ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, 999000000000009198UL, A.Name, B.Name, 1);
Check(exCap.PlainMessage!.Contains("1 a 50") && exZero.PlainMessage!.Contains("1 a 50") && exUnknown.PlainMessage!.Contains("No conozco") && exNoAcct.PlainMessage!.Contains("no tenés cuenta"), "0 y 51 cambios, un ítem que no existe y un jugador sin cuenta: se rechazan con su mensaje");
// el inventario no deja filas en cero ni cantidades negativas, y dos pedidos a la vez con lo justo para uno cambian UNA vez
await Reset(gold: 10); await CleanEvents();
await Give(A.Name, 3);
var raceEx = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name, B.Name, 1)));
int aRows = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = @n", new { id = (long)U, n = A.Name });
Check(raceEx.Count(r => r.Embed is not null) == 1 && await Have(A.Name) == 0 && aRows == 0 && await Have(B.Name) == 1, "dos pedidos a la vez con solo 3: cambia uno (el otro dice que no alcanza), y no queda una fila en cero");
await Reset(gold: 10); await CleanEvents();
await Give(A.Name, 30);
var many = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ExchangeModule.ExecuteExchangeAsync(users, items, exRepo, events, U, A.Name, B.Name, 1)));
Check(many.Count(r => r.Embed is not null) == 10 && await Have(A.Name) == 0 && await Have(B.Name) == 10 && await Stat("drop_exchange") == 10, "20 pedidos a la vez con 30: salen exactamente 10 cambios (0 y 10), ni uno de más");
// las listas
var invFor = new List<InventoryEntry> { new() { ItemName = A.Name, Quantity = 4, Type = "Material" }, new() { ItemName = B.Name, Quantity = 2, Type = "Material" }, new() { ItemName = X.Name, Quantity = 3, Type = "Material" }, new() { ItemName = "Madera de Pino", Quantity = 50, Type = "Madera" } };
var forGive = ExchangeChoices.ForGive(invFor, exDrops, "");
Check(forGive.Select(r => r.Value.ToString()).SequenceEqual([A.Name, X.Name]) && forGive.All(r => r.Name.Length <= 100) && ExchangeChoices.ForGive(invFor, exDrops, A.Name[..4].ToLowerInvariant()).Count == 1, $"«dar» ofrece solo lo que tenés al menos 3 veces (no la madera ni lo que te falta), con su zona; y filtra por lo escrito");
var forGet = ExchangeChoices.ForGet(invFor, exDrops, A.Name, "");
Check(forGet.Select(r => r.Value.ToString()).OrderBy(n => n).SequenceEqual(new[] { B.Name, Tz.Name }.OrderBy(n => n)) && forGet.Any(r => r.Name.Contains("tenés 2")) && ExchangeChoices.ForGet(invFor, exDrops, null, "").Count == 15 && ExchangeChoices.ForGet(invFor, exDrops, "Madera de Pino", "").Count == 15, "«recibir» ofrece los OTROS dos drops de la zona de lo que das (y todos si todavía no elegiste qué das)");
// la taberna
await Reset(gold: 10); await CleanEvents();
var inventoryRepo13 = new InventoryRepository(factory);
var itemsRepo13 = items;
var buffs13 = new BuffRepository(factory);
var scene0 = await TabernaModule.BuildSceneAsync(users, itemsRepo13, inventoryRepo13, buffs13, U, null, null, exRepo);
Check(!(scene0.Components?.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<SelectMenuComponent>().Any(m => m.CustomId.StartsWith("taberna_swap:")) ?? false), "sin 3 de ningún drop, la taberna no muestra la lista de cambiar");
await Give(A.Name, 7); await Give(B.Name, 1);
var scene1 = await TabernaModule.BuildSceneAsync(users, itemsRepo13, inventoryRepo13, buffs13, U, null, null, exRepo);
var rows1 = scene1.Components!.Components.OfType<ActionRowComponent>().ToList();
var swapMenu = rows1.SelectMany(r => r.Components).OfType<SelectMenuComponent>().SingleOrDefault(m => m.CustomId == $"taberna_swap:{U}");
Check(swapMenu is not null && rows1.Count <= 5 && swapMenu.Options.Count == 1 && swapMenu.Options.Single().Value == A.Name && swapMenu.Options.Single().Label.Contains("×7") && swapMenu.Options.All(o => o.Emote is null && o.Label.Length <= 100 && o.Description.Length <= 100), "con 7 de un drop, la taberna ofrece «Cambiar drops» con ese drop (sin emojis personalizados, dentro de los límites, a lo sumo 5 filas)");
var question = await TabernaModule.BuildSwapQuestionAsync(itemsRepo13, inventoryRepo13, exRepo, U, A.Name);
var qMenu = question!.Value.Components.Components.OfType<ActionRowComponent>().Single().Components.OfType<SelectMenuComponent>().Single();
Check(qMenu.CustomId == $"taberna_swapget:{U}:{A.ItemId}" && qMenu.Options.Select(o => o.Value).OrderBy(n => n).SequenceEqual(new[] { B.Name, Tz.Name }.OrderBy(n => n)) && AllText(question.Value.Embed).Contains(A.ZoneName) && await TabernaModule.BuildSwapQuestionAsync(itemsRepo13, inventoryRepo13, exRepo, U, B.Name) is null && await TabernaModule.BuildSwapQuestionAsync(itemsRepo13, inventoryRepo13, exRepo, U, "Madera de Pino") is null, "el paso 2 pregunta por los otros dos drops de esa zona (con el id de lo que das); con menos de 3, o algo que no es un drop, no hay pregunta");
string Route2(string id) { var r = isvc2.SearchComponentCommand(Click(id)); return r.IsSuccess ? r.Command.MethodName : "(ninguno)"; }
Check(Route2($"taberna_swap:{U}") == "HandleSwapAsync" && Route2($"taberna_swapget:{U}:{A.ItemId}") == "HandleSwapGetAsync" && Route2($"taberna_sell:{U}") == "HandleSellAsync", "los ids de los dos pasos llegan a su método y no pisan a los de siempre");
// comandos
var exCmd = isvc2.SlashCommands.Single(c => c.Name == "exchange");
Check(exCmd.Parameters.Select(p => p.Name).SequenceEqual(["dar", "recibir", "veces"]) && exCmd.Parameters.Take(2).All(p => p.AutocompleteHandler is not null) && exCmd.Parameters[2].MaxValue == 50 && exCmd.Parameters[2].MinValue == 1 && texts2.Search("exchange").IsSuccess && texts2.Search("canje").IsSuccess, "/exchange tiene dar y recibir con lista y veces de 1 a 50; «aa exchange» y «aa canje» se reconocen");
Check(HelpTopics.Find("exchange")?.Key == "exchange" && HelpTopics.Find("tabernero")?.Key == "exchange" && AllText(HelpTopics.Find("exchange")!.Build()).Contains("3 drops de una zona por 1"), "«info exchange» explica el cambio");
await CleanEvents();
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un Random que siempre devuelve el mismo valor: para elegir el tier del sorteo sin azar.
sealed class FixedRandom(int value) : Random
{
    public override int Next(int maxValue) => Math.Min(value, maxValue - 1);
}

// Un IGameEvents que solo anota lo que le registran (para probar qué eventos salen sin tocar la base).
sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public List<ulong> Users { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Users.Add(discordId); Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}

// Un proxy que implementa cualquier interfaz de Discord devolviendo los valores que se le den por nombre de propiedad (y el valor por defecto del resto).
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

// Una Arena de mentira: lista fija de anotados (null = falla al consultar). Lo demás no se usa en /cd.
sealed class FakeArena(IReadOnlyList<ulong>? joined, TimeSpan untilPlay) : IArenaService
{
    public Task<ArenaListing> ListAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (joined is null) throw new InvalidOperationException("la Arena falló");
        return Task.FromResult(new ArenaListing(DateOnly.FromDateTime(utcNow), joined.Select(id => new ArenaEntry(id, "T", "Guerrero", 1, utcNow)).ToList(), untilPlay));
    }
    public Task<ArenaJoinResult> JoinAsync(ulong discordId, string displayName, ulong channelId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArenaResults?> GetLatestResultsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ArenaResolution>> ResolveDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ArenaResolution?> ResolveDayAsync(DateOnly day, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
