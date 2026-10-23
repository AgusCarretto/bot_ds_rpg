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

// v0.25.3: LA MISMA VIDA EN TODAS LAS PANTALLAS (el dueño, 2026-10-23: «necesito que siempre sea la misma vida… sea la clase que sea, mostrá la vida que debería tener: si es ×0,95, mostrá el resultado»). Hasta la
// v0.25.2 la pelea mostraba la vida con el factor de clase (un Ninja de 340 veía 323) y la victoria, el perfil, /heal y /use la real (340), así que el máximo «cambiaba». Ahora todo muestra la vida con el factor.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Console.WriteLine("== 1. La cuenta pura ==");
User Make(string cls, int max, int cur) => new() { Class = cls, MaxHp = max, CurrentHp = cur };
Check(Near(PlayerCombatProfileCalculator.HpMultiplier(Make("Ninja", 340, 340)), 0.95, 1e-9) && Near(PlayerCombatProfileCalculator.HpMultiplier(Make("Arquero", 340, 340)), 1.15, 1e-9) && Near(PlayerCombatProfileCalculator.HpMultiplier(Make("Guerrero", 340, 340)), ClassPassives.For("Guerrero").MaxHpMultiplier, 1e-9),
    "el factor de cada clase contra monstruos: Ninja ×0,95, Arquero ×1,15, Guerrero el de su pasiva");
Check(HpDisplay.Scale(340, 0.95) == 323 && HpDisplay.Scale(257, 0.95) == 244 && HpDisplay.Scale(0, 0.95) == 0 && HpDisplay.Scale(1, 0.95) == 1,
    "el caso de las capturas: un Ninja de 340 se ve de 323 y 257 se ve 244 (los números que mostraba la pelea), y nunca queda en negativo");
var hpBlessing = new PlayerBonuses(PetBonuses.None, 0, new Dictionary<string, int> { [BlessingCatalog.HpKey] = 5 });
Check(Near(PlayerCombatProfileCalculator.HpMultiplier(Make("Ninja", 340, 340), hpBlessing), 0.95 * (1 + (BlessingCatalog.HpPerLevel * 5)), 1e-9), "con la bendición Corazón de Brasa V suma su vida al factor (×0,95 × 1,10 para un Ninja)");
bool same = true;
foreach (string cls in new[] { "Guerrero", "Hechicero", "Arquero", "Ninja" })
{
    var u = Make(cls, 337, 211);
    var fight = PlayerCombatProfileCalculator.Resolve(u, null, null, 0, hpBlessing);
    double mult = PlayerCombatProfileCalculator.HpMultiplier(u, hpBlessing);
    same &= fight.CombatMaxHp == HpDisplay.Scale(u.MaxHp, mult) && fight.CombatCurrentHp == HpDisplay.Scale(u.CurrentHp, mult);
}

Check(same, "la vida que se muestra sale de LA MISMA cuenta que la de la pelea (Resolve), en las cuatro clases: no pueden separarse");
var duel = PlayerCombatProfileCalculator.Resolve(Make("Ninja", 340, 340), null, null);
Check(duel.CombatMaxHp == HpDisplay.Scale(340, ClassPassives.For("Ninja").MaxHpMultiplier), "y el PvP no cambia: sin bonuses (duelos y Arena) la vida sigue sin el ajuste contra monstruos");

Console.WriteLine("\n== 2. La victoria ==");
var victoryUser = Make("Ninja", 340, 257);
string shown = AdventureModule.ShownHpLine(victoryUser, 0.95);
Check(shown.Contains("244/323") && !shown.Contains("340") && !shown.Contains("257"), $"la pantalla de victoria muestra 244/323 (lo mismo que la pelea) y no 257/340 ({shown.Replace("\n", " ")})");
Check(AdventureModule.ShownHpLine(victoryUser, 1.0).Contains("257/340"), "con factor 1,0 (una clase sin ajuste) muestra la vida de siempre");

Console.WriteLine("\n== 3. Contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var combat = new CombatSessionService(users);
var shop = new ShopRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000096001UL;
async Task Reset(int maxHp, int curHp)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET class = 'Ninja', level = 20, max_hp = @m, current_hp = @c WHERE discord_id = @id", new { id = (long)U, m = maxHp, c = curHp });
}

async Task Give(string name, int qty) => await db.ExecuteAsync("INSERT INTO inventory (discord_id, item_id, quantity) SELECT @id, item_id, @q FROM items WHERE name = @n ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = @q", new { id = (long)U, n = name, q = qty });
async Task<(int Cur, int Max)> Real() { var r = await db.QueryFirstAsync("SELECT current_hp AS \"Cur\", max_hp AS \"Max\" FROM users WHERE discord_id = @id", new { id = (long)U }); return ((int)r.Cur, (int)r.Max); }

await Reset(340, 200);
var profile = await GameModule.BuildProfileEmbedAsync(users, items, zonesRepo, buffs, U, "T", "https://example.com/a.png");
string vida = profile.Fields.First(f => f.Name == "Vida").Value;
Check(vida.Contains("190 / 323 HP") && !vida.Contains("340"), $"el perfil de un Ninja de 340 (con 200) muestra 190 / 323, la vida de la pelea ({vida.Replace("\n", " ")})");

await Give("Mate Amargo", 30);
var svc = new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), new GameEventRepository(factory), users, zonesRepo);
var heal = await TavernModule.ExecuteHealCoreAsync(users, inventory, combat, buffs, U, bonusService: svc);
Check(heal.Description.Contains("323/323") && !heal.Description.Contains("340") && heal.Description.Contains("Recuperaste **133**"), $"/heal: «ahora tenés 323/323» y recuperó 133 (190 → 323), con las cifras que ve el jugador ({heal.Title})");
var full = await TavernModule.ExecuteHealCoreAsync(users, inventory, combat, buffs, U, bonusService: svc);
Check(full.Description.Contains("323/323") && !full.Description.Contains("340"), "/heal al máximo dice 323/323");
var healNoSvc = await TavernModule.ExecuteHealCoreAsync(users, inventory, combat, buffs, U);
Check(healNoSvc.Description.Contains("323/323"), "sin el servicio de bonus (las pruebas viejas) igual aplica el factor de la clase");

await Reset(340, 300);
await Give("Mate Amargo", 5);
var used = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, "Mate Amargo", bonusService: svc);
Check(used.Embed is not null && used.Embed.Description.Contains("300/323") && !used.Embed.Description.Contains("340"), $"/use fuera de combate: el Mate Amargo cura 15 de la vida que ves (285 → 300 de 323) y a la base le suman 16 reales ({used.Embed?.Description})");
Check((await Real()).Cur == 316, $"y a la base le entran 16 de vida real, no 15: 15 / 0,95 ({(await Real()).Cur})");
await Reset(340, 340);
var usedFull = await UseModule.ExecuteUseAsync(users, items, inventory, combat, buffs, U, "Mate Amargo", bonusService: svc);
Check(usedFull.Embed is not null && usedFull.Embed.Description.Contains("323/323"), "/use al máximo dice 323/323");

await Reset(340, 100);
var tooHurt = await AutoHuntModule.ExecuteAsync(null!, users, items, null!, null!, U, svc);
Check(tooHurt.Embed is not null && tooHurt.Embed.Description.Contains("**95/323**") && !tooHurt.Embed.Description.Contains("340"), $"/autohunt con poca vida habla en la misma vida: 95/323 ({tooHurt.Embed?.Description.Split('\n')[0]})");

await Reset(340, 100);
await Give("Mate Amargo", 30);
string talk = await TabernaModule.ExecuteActionAsync("taberna_eat", users, items, inventory, shop, buffs, combat, null!, U, "__heal_full__", null, null, svc);
Check(talk.Contains("323/323") && !talk.Contains("340"), "la taberna («Curarme del todo») también");

var created = typeof(OnboardingModule).GetMethod("BuildConfirmationEmbed", AnyS)!;
var newNinja = Make("Ninja", 100, 100);
var onboarding = (Embed)created.Invoke(null, [ "T", newNinja, ClassCatalog.All.First(c => c.Name == "Ninja"), false ])!;
Check(onboarding.Fields.First(f => f.Name.Contains("Estado inicial")).Value.Contains("95/95 HP"), "el alta de un Ninja nuevo dice 95/95 HP (con el factor), no 100/100");

Console.WriteLine("\n== 3b. Curar y subir de nivel, en las mismas unidades ==");
Check(HpDisplay.ToReal(15, 0.95) == 16 && HpDisplay.ToReal(15, 1.0) == 15 && HpDisplay.ToReal(15, 1.2) == 13 && HpDisplay.ToReal(1, 1.2) == 1 && HpDisplay.ToReal(0, 0.95) == 0, "pasar vida que se ve a vida real: 15 son 16 para un Ninja, 15 sin factor y 13 para un Guerrero; nunca cero si cura algo");
bool roundTrip = true;
foreach (double mult in new[] { 0.95, 1.0, 1.15, 1.2, 1.045 })
{
    foreach (int real in new[] { 100, 233, 340, 1000 })
    {
        int shownBefore = HpDisplay.Scale(real, mult), shownAfter = HpDisplay.Scale(real + HpDisplay.ToReal(15, mult), mult);
        roundTrip &= Math.Abs((shownAfter - shownBefore) - 15) <= 1;
    }
}

Check(roundTrip, "una comida de 15 mueve la barra que se ve en 15 (con un punto de redondeo como mucho), para todos los factores de clase");
await Reset(340, 200);
await Give("Mate Amargo", 3);
var partial = await TavernModule.ExecuteHealCoreAsync(users, inventory, combat, buffs, U, bonusService: svc);
Check(partial.Title.Contains("todo lo que tenías") && partial.Description.Contains("Recuperaste **45**") && partial.Description.Contains("235/323") && partial.Description.Contains("faltan **88**"), $"con comida que no alcanza: 3 mates = 45 de lo que ves (190 → 235) y faltan 88 ({partial.Description.Split('\n').Last()})");
var zonesList = (await zonesRepo.GetAllAsync()).ToList();
var cardNinja = LevelUpCard.Compose(U, 7, 1, zonesList, new Random(1), 0.95);
var cardWarrior = LevelUpCard.Compose(U, 7, 1, zonesList, new Random(1), 1.2);
var cardPlain = LevelUpCard.Compose(U, 7, 1, zonesList, new Random(1));
string Gain(LevelUpCardContent c) => c.Fields.First(f => f.Name.Contains("Vida máxima")).Value;
Check(Gain(cardNinja) == "**+14**" && Gain(cardWarrior) == "**+18**" && Gain(cardPlain) == "**+15**", $"la tarjeta de nivel dice la vida que sube en las unidades que se ven: Ninja +14, Guerrero +18, sin factor +15 ({Gain(cardNinja)} {Gain(cardWarrior)} {Gain(cardPlain)})");
var notifier = new ProgressNotifier(new MissionRepository(factory), zonesRepo, users, svc);
var notices = await notifier.OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, DateTime.UtcNow, "21");
var levelNotice = notices.First(n => n.Embed is not null);
Check(levelNotice.Embed!.Fields.First(f => f.Name.Contains("Vida máxima")).Value == "**+14**", "y el aviso real (con el servicio y el usuario) lo hace para un Ninja: +14");
var noticesPlain = await new ProgressNotifier(new MissionRepository(factory), zonesRepo).OnEventAsync(U, GameEventKinds.LevelUp, 1, 1, DateTime.UtcNow, "21");
Check(noticesPlain.First(n => n.Embed is not null).Embed!.Fields.First(f => f.Name.Contains("Vida máxima")).Value == "**+15**", "sin usuarios ni servicio (como en las pruebas viejas) queda en la vida real, +15");

Console.WriteLine("\n== 4. El servicio de bonus ==");
var withBlessing = new StubBonuses(hpBlessing);
Check(Near(await ((IPlayerBonusService)withBlessing).HpMultiplierAsync(Make("Ninja", 340, 340)), 0.95 * 1.10, 1e-9), "con el servicio, la bendición de vida entra en lo que se muestra");
Check(Near(await ((IPlayerBonusService?)null).HpMultiplierAsync(Make("Ninja", 340, 340)), 0.95, 1e-9), "sin servicio cuenta la clase");
Check(Near(await new StubBonuses(null).HpMultiplierAsync(Make("Ninja", 340, 340)), 0.95, 1e-9), "si el servicio se cae NO tira: vuelve a la cuenta de la clase");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un servicio de bonus de mentira: devuelve lo que se le da, o se cae si no se le da nada.
public sealed class StubBonuses(PlayerBonuses? bonuses) : IPlayerBonusService
{
    public Task<PlayerBonuses> GetAsync(ulong discordId, int fuegoNuevo, CancellationToken cancellationToken = default) =>
        bonuses is null ? throw new InvalidOperationException("simulado") : Task.FromResult(bonuses);
}
