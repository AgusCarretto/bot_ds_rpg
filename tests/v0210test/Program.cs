using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// v0.21.0: el MINIBUFF DEL BANCO (el dueño, 2026-10-20: «un minibuff al banco para tener plata en el banco un mínimo al menos, que sea como una curva exponencial hasta el 5 %: tener más siempre te
// da más, pero cada vez menos»). El oro guardado da hasta +5 % de oro en las peleas, medido en cacerías de oro de la zona del jugador. Reglas puras, el servicio de bonus contra la base real, la
// recompensa de una pelea, la pantalla de /bank, la ayuda y la inyección de dependencias.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Console.WriteLine("== 1. La curva ==");
Check(BankRules.MaxGoldBonusPercent == 5.0 && Near(BankRules.GoldBonusHunts * MissionRewards.GoldUnit(5), 25600, 1e-6), "tope de 5 % y τ = 25.600 de oro en la zona 5 (68,27 cacerías de oro)");
Check(Enumerable.Range(1, 5).All(z => BankRules.GoldBonusPercent(0, z) == 0 && BankRules.GoldBonusPercent(-500, z) == 0), "sin saldo (o negativo) no hay bonus, en ninguna zona");
double z5(long gold) => BankRules.GoldBonusPercent(gold, 5);
Check(Near(z5(100_000), 4.90, 0.01), $"el ejemplo del dueño: 100.000 de oro en la zona 5 dan {z5(100_000):0.000} % (4,90)");
Console.WriteLine($"   zona 5: 1K {z5(1_000):0.00} % · 10K {z5(10_000):0.00} % · 25K {z5(25_000):0.00} % · 50K {z5(50_000):0.00} % · 100K {z5(100_000):0.00} % · 200K {z5(200_000):0.00} % · 1M {z5(1_000_000):0.000} %");
bool monotonic = true, bounded = true;
foreach (int zone in Enumerable.Range(1, 5))
{
    double prev = 0;
    foreach (long gold in new long[] { 1, 10, 100, 1_000, 5_000, 10_000, 50_000, 100_000, 300_000, 1_000_000, 100_000_000 })
    {
        double v = BankRules.GoldBonusPercent(gold, zone);
        monotonic &= v >= prev;
        bounded &= v >= 0 && v <= 5.0;
        prev = v;
    }
}
Check(monotonic && bounded, "en todas las zonas sube siempre (o se queda en el tope) y nunca pasa del 5 %");
Check(z5(2_000) > z5(1_000) * 1.9 && z5(200_000) - z5(100_000) < 0.11 && z5(1_000_000) < 5.0 + 1e-9, "arriba de 100K la curva casi no se mueve (la diferencia entre 100K y 200K es de centésimas) y al principio sube casi parejo");
Check(Near(BankRules.GoldBonusPercent((long)Math.Round(100_000 / 375.0 * 14), 1), z5(100_000), 0.03) && Near(BankRules.GoldBonusPercent((long)Math.Round(100_000 / 375.0 * 58), 2), z5(100_000), 0.03) && Near(BankRules.GoldBonusPercent((long)Math.Round(100_000 / 375.0 * 215), 4), z5(100_000), 0.03),
    "se mide por zona: lo mismo en cacerías de oro da lo mismo (≈ 3.700 de oro en la zona 1, ≈ 15.500 en la 2 y ≈ 57.000 en la 4 equivalen a 100.000 de la zona 5)");
Check(BankRules.GoldBonusPercent(3_700, 1) > 4.8 && BankRules.GoldBonusPercent(3_700, 5) < 1.0, $"y por eso el banco sirve desde la zona 1: 3.700 de oro valen +{BankRules.GoldBonusPercent(3_700, 1):0.00} % en la zona 1 y solo +{BankRules.GoldBonusPercent(3_700, 5):0.00} % en la 5");
Check(BankRules.BonusText(4.9) == "+4,9 %" && BankRules.BonusText(3.125) == "+3,13 %" && BankRules.BonusText(5.0) == "+5 %" && BankRules.BonusText(0.004) == "+0 %", "el texto lleva coma decimal y dos decimales como mucho");

Console.WriteLine("\n== 2. PlayerBonuses ==");
var none = PlayerBonuses.None;
var bankOnly = new PlayerBonuses(PetBonuses.None, 0, null, null, 4.9);
Check(none.GoldMultiplier == 1.0 && Near(bankOnly.GoldMultiplier, 1.049, 1e-9) && !bankOnly.IsNone && PlayerBonuses.None.IsNone, "el bonus del banco entra en el multiplicador de oro y ya no es «sin nada»");
Check(Near(new PlayerBonuses(PetBonuses.None, 0, null, null, 99).GoldMultiplier, 1.05, 1e-9) && new PlayerBonuses(PetBonuses.None, 0, null, null, -3).GoldMultiplier == 1.0, "un valor fuera de rango se acota entre 0 y 5 %");
var pets = new PetBonuses(5, 0, 0, 0);
Check(Near(new PlayerBonuses(pets, 0, null, null, 5).GoldMultiplier, 1.05 * 1.05, 1e-9) && Near(new PlayerBonuses(new PetBonuses(0, 0, 0, 0), 0, new Dictionary<string, int> { [BlessingCatalog.GoldKey] = 5 }, null, 5).GoldMultiplier, (1 + (BlessingCatalog.GoldPerLevel * 5)) * 1.05, 1e-9),
    "se MULTIPLICA con las mascotas y con la bendición Bolsillo Hondo, como todo lo de PlayerBonuses");
Check(PlayerBonuses.Scale(1000, bankOnly.GoldMultiplier) == 1049 && PlayerBonuses.Scale(1000, none.GoldMultiplier) == 1000, "sobre 1.000 de oro de una pelea suma 49");
long totalPlain = 0, totalBank = 0; var five = new PlayerBonuses(PetBonuses.None, 0, null, null, 5.0);
for (int i = 0; i < 20000; i++) { totalPlain += CombatRewardCalculator.RollHuntReward(10).Gold; totalBank += CombatRewardCalculator.RollHuntReward(10, 0, 0, five).Gold; }
Check(Near(totalBank / (double)totalPlain, 1.05, 0.01), $"en las recompensas de pelea (RollHuntReward) el oro sube ~5 %: ×{totalBank / (double)totalPlain:0.000} con el tope");

Console.WriteLine("\n== 3. El servicio contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var petsRepo = new PetRepository(factory);
var blessings = new BlessingRepository(factory);
var eventRepo = new GameEventRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000095001UL;
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
await users.GetOrCreateUserAsync(U);
var zones = ZoneRanking.OrderByDifficulty(await zonesRepo.GetAllAsync());
async Task Put(bool hasBank, int bank, int zoneRank) =>
    await db.ExecuteAsync("UPDATE users SET has_bank = @h, bank_gold = @b, current_zone_id = @z WHERE discord_id = @id", new { id = (long)U, h = hasBank, b = bank, z = zones[zoneRank - 1].ZoneId });
var service = new PlayerBonusService(petsRepo, blessings, eventRepo, users, zonesRepo);
await Put(true, 100_000, 5);
var b5 = await service.GetAsync(U, 0);
Check(Near(b5.BankGoldPercent, 4.90, 0.01) && Near(b5.GoldMultiplier, 1.049, 0.0002), $"100.000 en el banco parado en la zona 5: +{b5.BankGoldPercent:0.000} % de oro");
await Put(true, 100_000, 1);
var b1 = await service.GetAsync(U, 0);
Check(b1.BankGoldPercent > 4.99 && b1.BankGoldPercent <= 5.0, $"los mismos 100.000 parado en la zona 1 ya están en el tope (+{b1.BankGoldPercent:0.000} %): el saldo se mide en cacerías de oro de la zona donde está");
await Put(true, 2_000, 1);
var bSmall = await service.GetAsync(U, 0);
Check(Near(bSmall.BankGoldPercent, BankRules.GoldBonusPercent(2_000, 1), 1e-9) && bSmall.BankGoldPercent is > 3 and < 5, $"2.000 de oro en la zona 1 son +{bSmall.BankGoldPercent:0.00} %");
await Put(true, 0, 5);
Check((await service.GetAsync(U, 0)).BankGoldPercent == 0, "con la cuenta abierta pero vacía no hay bonus");
await Put(false, 5_000, 5);
Check((await service.GetAsync(U, 0)).BankGoldPercent == 0, "sin cuenta del banco no hay bonus");
await Put(true, 100_000, 5);
var noRepos = new PlayerBonusService(petsRepo, blessings, eventRepo);
Check((await noRepos.GetAsync(U, 0)).BankGoldPercent == 0, "un servicio armado sin usuarios ni zonas (como en otras pruebas) no suma nada y no tira");
var broken = new PlayerBonusService(petsRepo, blessings, eventRepo, DispatchProxy.Create<IUserRepository, ThrowingProxy>(), zonesRepo);
var bBroken = await broken.GetAsync(U, 2);
Check(bBroken.BankGoldPercent == 0 && bBroken.FuegoNuevo == 2, "si falla la lectura del usuario el servicio NO tira: sigue sin el bonus del banco (y con lo demás)");

Console.WriteLine("\n== 4. Pantallas ==");
await Put(true, 100_000, 5);
var view = await BankModule.BuildViewAsync(users, U, zonesRepo);
Check(view.Embed is not null && AllText(view.Embed).Contains("Lo que rinde") && AllText(view.Embed).Contains("+4,9 %") && AllText(view.Embed).Contains("+5 %") && view.Embed.Length <= 6000, $"/bank view dice cuánto rinde hoy («{view.Embed?.Fields.FirstOrDefault(f => f.Name.Contains("rinde")).Value}»)");
var viewNoZones = await BankModule.BuildViewAsync(users, U);
Check(viewNoZones.Embed is not null && !AllText(viewNoZones.Embed).Contains("Lo que rinde"), "sin el repositorio de zonas (los llamadores viejos) la pantalla queda como antes");
await Put(true, 0, 5);
var viewEmpty = await BankModule.BuildViewAsync(users, U, zonesRepo);
Check(viewEmpty.Embed is not null && AllText(viewEmpty.Embed).Contains("hasta **+5 %**") && !AllText(viewEmpty.Embed).Contains("+0 %"), "con el banco vacío explica el tope en vez de mostrar «+0 %»");
await Put(false, 0, 5);
var viewNoAccount = await BankModule.BuildViewAsync(users, U, zonesRepo);
Check(viewNoAccount.Embed is not null && !AllText(viewNoAccount.Embed).Contains("Lo que rinde"), "sin cuenta no sale esa línea (la pantalla invita a abrirla)");
var topic = HelpTopics.Find("banco")!; var te = topic.Build(); string tt = AllText(te);
Check(topic.Key == "bank" && tt.Contains("+5 %") && tt.Contains("/bank view") && tt.Contains("tu zona") && !tt.Contains("Sin interés ni tope por ahora") && te.Length <= 6000 && te.Fields.All(f => f.Value.Length <= 1024), $"el tema de ayuda del banco explica el minibuff con el tope leído de las constantes ({te.Length})");

Console.WriteLine("\n== 5. Inyección de dependencias (InteractionService real) ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BankModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await Put(true, 100_000, 5);
var viaDi = await sp.GetRequiredService<IPlayerBonusService>().GetAsync(U, 0);
Check(Near(viaDi.BankGoldPercent, 4.90, 0.01), $"el contenedor arma el servicio CON usuarios y zonas: +{viaDi.BankGoldPercent:0.000} % para 100.000 en la zona 5");
await interactions.AddModulesAsync(typeof(BankModule).Assembly, sp);
Check(ActivatorUtilities.CreateInstance(sp, typeof(BankModule)) is BankModule && interactions.SlashCommands.Count(c => c.Module.SlashGroupName == "bank") == 4, "BankModule se arma con la inyección y sus 4 subcomandos siguen registrados");
await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un IUserRepository que tira en cualquier llamada (para probar que el servicio de bonus nunca se cae).
public class ThrowingProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("simulado");
}
