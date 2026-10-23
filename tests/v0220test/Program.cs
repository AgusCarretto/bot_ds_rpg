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
using Npgsql;

// v0.22.0: encantamientos con EFECTO (el dueño: «efectos nuevos me gusta»; «que se elija»). Arma: Filo (el de siempre), Crítico, Sangre. Amuleto: Guarda (el de siempre), Vida, Esquive. El jugador ELIGE el efecto
// y el tier se sortea; cambiar de efecto reemplaza el que tenía (se confirma). Los cuatro nuevos valen SOLO contra monstruos. Catálogo, cuentas del perfil de combate, la cura de Sangre en el resolutor,
// el repositorio contra la base real, el flujo de /enchant con su confirmación, las pantallas, los comandos registrados y los reinicios (venta, Fuego Nuevo).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Console.WriteLine("== 1. El catálogo de efectos ==");
Check(Enchantments.Kinds.Count == 6 && Enchantments.KindsFor("weapon").Select(k => k.Key).SequenceEqual(["filo", "critico", "sangre"]) && Enchantments.KindsFor("amulet").Select(k => k.Key).SequenceEqual(["guarda", "vida", "esquive"]),
    "seis efectos: arma = Filo, Crítico, Sangre; amuleto = Guarda, Vida, Esquive");
Check(Enchantments.Kinds.All(k => k.ValuesByTier.Count == Enchantments.MaxTier && k.ValuesByTier.Zip(k.ValuesByTier.Skip(1)).All(p => p.First < p.Second)), "cada efecto trae un valor por tier y SUBE con el tier");
Check(Enchantments.FindKind("filo")!.ValuesByTier.SequenceEqual(Enumerable.Range(1, Enchantments.MaxTier).Select(Enchantments.BonusPercent)) && Enchantments.FindKind("filo")!.ValuesByTier.SequenceEqual([4, 8, 13, 19, 26]) && Enchantments.FindKind("guarda")!.ValuesByTier.SequenceEqual([3, 6, 9, 13, 18])
      && Enumerable.Range(1, Enchantments.MaxTier).All(t => Enchantments.StatBonusPercent("guarda", t) < Enchantments.StatBonusPercent("filo", t) && Enchantments.StatBonusPercent("critico", t) == 0 && Enchantments.StatBonusPercent(null, t) == 0) && Enchantments.StatBonusPercent("filo", 0) == 0,
    "el Filo sigue en +4/8/13/19/26 % (la tabla de tiers) y la Guarda tiene la suya, +3/6/9/13/18 % (v0.25.1, bajada un poco por ser muy fuerte): más baja que el Filo en todos los tiers, y los efectos de combate no suman al stat");
Check(Enchantments.Apply(100, "guarda", 5) == 118 && Enchantments.Apply(100, "filo", 5) == 126 && Enchantments.Apply(100, 5) == 126 && Enchantments.Apply(100, "vida", 5) == 100 && Enchantments.Apply(100, "guarda", 0) == 100 && Enchantments.Apply(5, "guarda", 1) == 6,
    "Apply(stat, efecto, tier): Guarda V sobre 100 de DEF da 118 (antes 126), el Filo 126, los efectos de combate no tocan el stat y siempre suma al menos 1");
Check(Enchantments.FindKind("critico")!.ValuesByTier.SequenceEqual([3, 6, 10, 15, 20]) && Enchantments.FindKind("sangre")!.ValuesByTier.SequenceEqual([1, 2, 3, 4, 5]) && Enchantments.FindKind("vida")!.ValuesByTier.SequenceEqual([4, 8, 13, 18, 24]) && Enchantments.FindKind("esquive")!.ValuesByTier.SequenceEqual([3, 6, 9, 12, 15]),
    "las magnitudes medidas con el simulador (parte L): Crítico 3-20 puntos, Sangre 1-5 % del daño, Vida 4-24 % y Esquive 3-15 puntos");
Check(Enchantments.Kinds.Where(k => k.PveOnly).Select(k => k.Key).OrderBy(x => x).SequenceEqual(["critico", "esquive", "sangre", "vida"]) && Enchantments.IsStatKind("filo") && Enchantments.IsStatKind("guarda") && !Enchantments.IsStatKind("critico"),
    "Crítico, Sangre, Vida y Esquive son solo contra monstruos; Filo y Guarda cambian el stat de la pieza");
Check(Enchantments.DefaultKind("weapon") == "filo" && Enchantments.DefaultKind("amulet") == "guarda", "el efecto de siempre de cada pieza es Filo / Guarda");
Check(Enchantments.ParseKind("weapon", "Crítico") == "critico" && Enchantments.ParseKind("weapon", "CRITICO") == "critico" && Enchantments.ParseKind("weapon", " sangre ") == "sangre" && Enchantments.ParseKind("amulet", "vida") == "vida" && Enchantments.ParseKind("amulet", "Esquive") == "esquive"
    && Enchantments.ParseKind("weapon", "vida") is null && Enchantments.ParseKind("amulet", "filo") is null && Enchantments.ParseKind("weapon", "") is null && Enchantments.ParseKind("weapon", null) is null && Enchantments.ParseKind("weapon", "xyz") is null,
    "el efecto se lee sin mirar tildes ni mayúsculas y SOLO si es de esa pieza (vida en el arma o filo en el amuleto no existen)");
Check(Enchantments.StatTier("filo", 3) == 3 && Enchantments.StatTier("guarda", 5) == 5 && Enchantments.StatTier("critico", 5) == 0 && Enchantments.StatTier("sangre", 2) == 0 && Enchantments.StatTier("vida", 4) == 0 && Enchantments.StatTier(null, 3) == 0,
    "solo Filo y Guarda cuentan para el stat de la pieza");
Check(Enchantments.KindLabel("critico", 3) == "Crítico Ardiente" && Enchantments.KindLabel("filo", 5) == "Filo Soberano" && Enchantments.KindLabel("critico", 0) == "Sin encantar" && Enchantments.KindLabel("nada", 3) == "Sin encantar" && Enchantments.Label("weapon", 3) == "Filo Ardiente",
    "las etiquetas: «Crítico Ardiente», «Filo Soberano»; sin tier o con un efecto que no existe, «Sin encantar»; Label (el de antes) sigue igual");
Check(Enchantments.ValueText("filo", 3) == "+13 %" && Enchantments.ValueText("critico", 3) == "+10 puntos de crítico" && Enchantments.ValueText("sangre", 5) == "5 % del daño en vida" && Enchantments.ValueText("vida", 2) == "+8 % de vida" && Enchantments.ValueText("esquive", 4) == "+12 puntos de esquive",
    "el valor en palabras");
Check(Enchantments.ShortText("filo", 3) == "+13 %" && Enchantments.ShortText("critico", 3) == "Crít +10" && Enchantments.ShortText("sangre", 1) == "Sangre 1 %" && Enchantments.ShortText("vida", 5) == "Vida +24 %" && Enchantments.ShortText("esquive", 2) == "Esq +6",
    "el valor corto de la línea del perfil");
Check(Enchantments.DescribeValues(Enchantments.FindKind("filo")!) == "+4/8/13/19/26 % de ATQ de la pieza" && Enchantments.DescribeValues(Enchantments.FindKind("sangre")!) == "cura el 1/2/3/4/5 % del daño de cada golpe" && Enchantments.DescribeValues(Enchantments.FindKind("esquive")!).Contains("3/6/9/12/15"),
    "los cinco valores de cada efecto en palabras (para /enchant info)");

Console.WriteLine("\n== 2. El perfil de combate (solo contra monstruos) ==");
var weaponItem = new Item { StatValue = 100, WeaponFamily = "Espadas" };
var amuletItem = new Item { StatValue = 100 };
User Player(string cls, string wk, int wt, string ak, int at) => new() { Class = cls, Level = 20, MaxHp = 400, CurrentHp = 400, WeaponEnchantKind = wk, WeaponEnchant = wt, AmuletEnchantKind = ak, AmuletEnchant = at };
var pve = PlayerBonuses.None;
var baseProfile = PlayerCombatProfileCalculator.Resolve(Player("Guerrero", "filo", 0, "guarda", 0), weaponItem, amuletItem, 0, pve);
var filoProfile = PlayerCombatProfileCalculator.Resolve(Player("Guerrero", "filo", 5, "guarda", 5), weaponItem, amuletItem, 0, pve);
Check(filoProfile.Damage > baseProfile.Damage && filoProfile.Defense > baseProfile.Defense && filoProfile.Passives == baseProfile.Passives, "Filo V y Guarda V suben el stat y no tocan las pasivas (como siempre)");
var critProfile = PlayerCombatProfileCalculator.Resolve(Player("Guerrero", "critico", 5, "guarda", 0), weaponItem, amuletItem, 0, pve);
Check(critProfile.Damage == baseProfile.Damage && Near(critProfile.Passives.CritChanceBonus, 0.20, 1e-9) && critProfile.Passives.DodgeChanceBonus == baseProfile.Passives.DodgeChanceBonus, "Crítico V NO suma al ataque (el stat de la pieza queda) y suma 20 puntos de chance de crítico");
var archerCrit = PlayerCombatProfileCalculator.Resolve(Player("Arquero", "critico", 3, "guarda", 0), weaponItem, amuletItem, 0, pve);
Check(Near(archerCrit.Passives.CritChanceBonus, 0.15 + 0.10, 1e-9), "se SUMA a lo de la clase: el Arquero (+15) con Crítico III (+10) queda en +25 puntos");
var bloodProfile = PlayerCombatProfileCalculator.Resolve(Player("Hechicero", "sangre", 4, "guarda", 0), weaponItem, amuletItem, 0, pve);
Check(Near(bloodProfile.Passives.LeechRatio, 0.04, 1e-9) && bloodProfile.Passives.LifestealChance == 0.30 && bloodProfile.Passives.LifestealRatio == 0.50, "Sangre IV cura el 4 % de cada golpe y NO pisa el Sifón de Almas del Hechicero");
var lifeProfile = PlayerCombatProfileCalculator.Resolve(Player("Ninja", "filo", 0, "vida", 5), weaponItem, amuletItem, 0, pve);
var lifeBase = PlayerCombatProfileCalculator.Resolve(Player("Ninja", "filo", 0, "guarda", 0), weaponItem, amuletItem, 0, pve);
Check(Near(lifeProfile.CombatMaxHp / (double)lifeBase.CombatMaxHp, 1.24, 0.01) && lifeProfile.Defense == lifeBase.Defense, $"Vida V sube la vida de combate un 24 % ({lifeBase.CombatMaxHp} → {lifeProfile.CombatMaxHp}) y no toca la defensa");
var dodgeProfile = PlayerCombatProfileCalculator.Resolve(Player("Ninja", "filo", 0, "esquive", 5), weaponItem, amuletItem, 0, pve);
Check(Near(dodgeProfile.Passives.DodgeChanceBonus, 0.15 + 0.15, 1e-9), "Esquive V suma 15 puntos de esquive a los 15 del Ninja");
var both = PlayerCombatProfileCalculator.Resolve(Player("Arquero", "sangre", 2, "esquive", 1), weaponItem, amuletItem, 0, pve);
Check(Near(both.Passives.LeechRatio, 0.02, 1e-9) && Near(both.Passives.DodgeChanceBonus, 0.03, 1e-9), "arma y amuleto con efecto a la vez: los dos cuentan");
var pvp = PlayerCombatProfileCalculator.Resolve(Player("Arquero", "critico", 5, "esquive", 5), weaponItem, amuletItem, 0);
var pvpBase = PlayerCombatProfileCalculator.Resolve(Player("Arquero", "filo", 0, "guarda", 0), weaponItem, amuletItem, 0);
Check(pvp.Passives == pvpBase.Passives && pvp.Damage == pvpBase.Damage, "sin PlayerBonuses (los duelos y la Arena) los efectos de combate NO cuentan: las pasivas son las de siempre");
var pvpFilo = PlayerCombatProfileCalculator.Resolve(Player("Guerrero", "filo", 5, "guarda", 5), weaponItem, amuletItem, 0);
Check(pvpFilo.Damage > pvpBase.Damage && pvpFilo.Defense > PlayerCombatProfileCalculator.Resolve(Player("Guerrero", "filo", 0, "guarda", 0), weaponItem, amuletItem, 0).Defense, "Filo y Guarda SÍ valen en los duelos, como antes");
Check(PlayerCombatProfileCalculator.WeaponDamage(Player("Guerrero", "critico", 5, "guarda", 0), weaponItem) == PlayerCombatProfileCalculator.WeaponDamage(Player("Guerrero", "filo", 0, "guarda", 0), weaponItem), "con Crítico en el arma el ATQ de la pieza es el base (no conserva el Filo)");

Console.WriteLine("\n== 3. La cura de Sangre y las chances en el resolutor ==");
var passives0 = ClassPassives.For("Guerrero");
var withLeech = new CombatantProfile(100, 0, "Guerrero", passives0 with { LeechRatio = 0.05 });
int missingHp = 300;
var strike = CombatTurnResolver.ResolveStrike(withLeech, default, PlayerAction.Attack, 100, 400);
Check(strike.LeechHeal == Math.Max(1, (int)Math.Round(strike.Damage * 0.05)) && strike.LifestealHeal == strike.LeechHeal && strike.HpAfterStrike == 100 + strike.LeechHeal && strike.Damage > 0, $"cada golpe cura el 5 % de lo que pega ({strike.Damage} de daño ⇒ {strike.LeechHeal} de vida)");
var tiny = new CombatantProfile(5, 0, "Guerrero", passives0 with { LeechRatio = 0.01 });
Check(CombatTurnResolver.ResolveStrike(tiny, default, PlayerAction.Attack, 100, 400).LeechHeal >= 1, "con un golpe chico igual cura al menos 1 (el tier I se nota)");
var capped = CombatTurnResolver.ResolveStrike(withLeech, default, PlayerAction.Attack, 399, 400);
Check(capped.HpAfterStrike <= 400, "la cura nunca pasa de la vida máxima");
var noLeech = CombatTurnResolver.ResolveStrike(new CombatantProfile(100, 0, "Guerrero", passives0), default, PlayerAction.Attack, 100, 400);
Check(noLeech.LeechHeal == 0 && noLeech.LifestealHeal == 0, "sin Sangre no cura");
var mage = new CombatantProfile(100, 0, "Hechicero", ClassPassives.For("Hechicero") with { LeechRatio = 0.05, LifestealChance = 1.0, LifestealRatio = 0.5 });
var mageStrike = CombatTurnResolver.ResolveStrike(mage, default, PlayerAction.Attack, 100, 1000);
Check(mageStrike.LifestealHeal == (int)Math.Round(mageStrike.Damage * 0.5) + mageStrike.LeechHeal && mageStrike.LeechHeal > 0, "con el Sifón (100 % de chance en la prueba) las dos curas se SUMAN y LeechHeal dice cuánto fue de Sangre");
var turn = CombatTurnResolver.ResolveTurn(withLeech, default, PlayerAction.Attack, 100, 400, 10_000, 5);
Check(turn.LeechHeal > 0 && turn.LifestealHeal == turn.LeechHeal, "ResolveTurn pasa la cura de Sangre al resultado del turno");
// Probabilidades: el crítico y el esquive son puntos de chance sobre la base (10 % y 5 %)
int N = 40000, crits = 0, dodges = 0;
var critFighter = new CombatantProfile(100, 0, "Guerrero", passives0 with { CritChanceBonus = 0.20 });
var dodgeFighter = new CombatantProfile(100, 0, "Guerrero", passives0 with { DodgeChanceBonus = 0.15 });
for (int i = 0; i < N; i++)
{
    if (CombatTurnResolver.ResolveStrike(critFighter, default, PlayerAction.Attack, 100, 400).CritCount > 0) crits++;
    if (CombatTurnResolver.ResolveCounterTurn(dodgeFighter, default, 50).Hit.Dodged) dodges++;
}
Check(Near(crits / (double)N, 0.30, 0.01) && Near(dodges / (double)N, 0.20, 0.01), $"Crítico V da {100.0 * crits / N:0.0} % de crítico (10 + 20) y Esquive V {100.0 * dodges / N:0.0} % de esquive (5 + 15)");

Console.WriteLine("\n== 4. El repositorio contra la base real ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var dustRepo = new DustRepository(factory);
var shopRepo = new ShopRepository(factory);
var eventRepo = new GameEventRepository(factory);
var events = new GameEventService(eventRepo);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000096001UL;
int weaponId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = 'Weapon' AND rarity = 'Común' ORDER BY item_id LIMIT 1");
int amuletId = await db.ExecuteScalarAsync<int>("SELECT item_id FROM items WHERE type = 'Amulet' ORDER BY item_id LIMIT 1");
async Task Reset(int gold = 1_000_000, int dust = 5_000)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET gold = @g, dust = @d, weapon_id = @w, amulet_id = @a WHERE discord_id = @id", new { id = (long)U, g = gold, d = dust, w = weaponId, a = amuletId });
}
async Task<User> Me() => (await users.GetByDiscordIdAsync(U))!;
await Reset();
var fresh = await Me();
Check(fresh.WeaponEnchantKind == "filo" && fresh.AmuletEnchantKind == "guarda" && fresh.WeaponEnchant == 0, "un jugador nuevo arranca con Filo y Guarda (el de siempre) y sin tier");
var o1 = await dustRepo.TryEnchantAsync(U, "weapon", 2, 100, 10, "critico");
var u1 = await Me();
Check(o1.Status == EnchantStatus.Ok && o1.NewKind == "critico" && o1.PreviousKind == "filo" && u1.WeaponEnchantKind == "critico" && u1.WeaponEnchant == 2 && u1.Gold == 1_000_000 - 100 && u1.Dust == 5_000 - 10, "elegir un efecto sobre una pieza SIN encantar lo pone directo (no hay nada que reemplazar) y cobra");
var o2 = await dustRepo.TryEnchantAsync(U, "weapon", 1, 100, 10);
var u2 = await Me();
Check(o2.Status == EnchantStatus.Ok && u2.WeaponEnchantKind == "critico" && u2.WeaponEnchant == 2 && o2.NewTier == 2, "sin elegir efecto queda el de la pieza y el tier solo SUBE (salió 1, se queda el 2)");
var o3 = await dustRepo.TryEnchantAsync(U, "weapon", 4, 100, 10, "critico");
Check(o3.Status == EnchantStatus.Ok && (await Me()).WeaponEnchant == 4 && o3.PreviousTier == 2, "el mismo efecto con un tier mejor lo sube");
long goldBefore = (await Me()).Gold; int dustBefore = (await Me()).Dust;
var o4 = await dustRepo.TryEnchantAsync(U, "weapon", 3, 100, 10, "sangre");
var u4 = await Me();
Check(o4.Status == EnchantStatus.NeedsConfirm && o4.PreviousKind == "critico" && o4.NewKind == "sangre" && u4.Gold == goldBefore && u4.Dust == dustBefore && u4.WeaponEnchantKind == "critico" && u4.WeaponEnchant == 4,
    "pedir OTRO efecto sobre una pieza con tier, sin confirmar: no cobra y no cambia nada");
var o5 = await dustRepo.TryEnchantAsync(U, "weapon", 1, 100, 10, "sangre", allowSwitch: true);
var u5 = await Me();
Check(o5.Status == EnchantStatus.Ok && u5.WeaponEnchantKind == "sangre" && u5.WeaponEnchant == 1 && o5.PreviousTier == 4 && o5.NewTier == 1 && u5.Gold == goldBefore - 100, "confirmado: REEMPLAZA con el tier que salió (aunque sea menor: 4 → 1) y cobra");
bool threw = false;
try { await dustRepo.TryEnchantAsync(U, "weapon", 1, 100, 10, "vida"); } catch (ArgumentException) { threw = true; }
Check(threw && (await Me()).WeaponEnchantKind == "sangre", "un efecto que no es de esa pieza es un error de programación (tira) y no toca nada");
var a1 = await dustRepo.TryEnchantAsync(U, "amulet", 3, 100, 10, "vida");
var a2 = await dustRepo.TryEnchantAsync(U, "amulet", 5, 100, 10, "esquive");
var a3 = await dustRepo.TryEnchantAsync(U, "amulet", 5, 100, 10, "esquive", allowSwitch: true);
var ua = await Me();
Check(a1.Status == EnchantStatus.Ok && a2.Status == EnchantStatus.NeedsConfirm && a3.Status == EnchantStatus.Ok && ua.AmuletEnchantKind == "esquive" && ua.AmuletEnchant == 5 && ua.WeaponEnchantKind == "sangre", "el amuleto anda igual (Vida → pide confirmar → Esquive) y no toca el arma");
await Reset(gold: 50, dust: 5_000);
var poor = await dustRepo.TryEnchantAsync(U, "weapon", 3, 100, 10, "critico");
Check(poor.Status == EnchantStatus.NotEnoughGold && (await Me()).WeaponEnchant == 0 && (await Me()).WeaponEnchantKind == "filo", "sin oro no cambia nada (ni el efecto)");

// 8 intentos a la vez sobre la misma pieza: se hacen uno detrás del otro, cada uno cobra una vez y el tier solo sube
await Reset(gold: 1_000_000, dust: 5_000);
var race = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => Task.Run(() => dustRepo.TryEnchantAsync(U, "weapon", (i % 5) + 1, 100, 10, "critico"))));
var uRace = await Me();
Check(race.All(r => r.Status == EnchantStatus.Ok) && uRace.Gold == 1_000_000 - (8 * 100) && uRace.Dust == 5_000 - (8 * 10) && uRace.WeaponEnchantKind == "critico" && uRace.WeaponEnchant == 5, $"8 intentos a la vez: cada uno cobra UNA vez (oro {uRace.Gold}) y el tier final es el mejor que salió ({uRace.WeaponEnchant})");

Console.WriteLine("\n== 5. El reinicio del efecto ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, weapon_enchant_kind = 'sangre', amulet_enchant = 2, amulet_enchant_kind = 'vida' WHERE discord_id = @id", new { id = (long)U });
var sold = await shopRepo.SellEquippedAsync(U, weaponId, 10);
var uSold = await Me();
Check(sold is not null && uSold.WeaponId is null && uSold.WeaponEnchant == 0 && uSold.WeaponEnchantKind == "filo" && uSold.AmuletEnchantKind == "vida" && uSold.AmuletEnchant == 2, "vender el arma vuelve su efecto a Filo y su tier a 0, y no toca el amuleto");
await Reset();
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, weapon_enchant_kind = 'critico', amulet_enchant = 2, amulet_enchant_kind = 'esquive', gate_cleared = true WHERE discord_id = @id", new { id = (long)U });
var preview = await new FuegoNuevoRepository(factory).GetPreviewAsync(U);
Check(preview is not null && preview.WeaponEnchantKind == "critico" && preview.AmuletEnchantKind == "esquive" && preview.WeaponEnchant == 3, "la vista previa del Fuego Nuevo sabe el efecto de cada pieza");
var renewed = await new FuegoNuevoRepository(factory).RenewAsync(U, 0, "Ninja");
var uRenew = await Me();
Check(renewed.Status == RenewStatus.Ok && uRenew.WeaponEnchant == 0 && uRenew.AmuletEnchant == 0 && uRenew.WeaponEnchantKind == "filo" && uRenew.AmuletEnchantKind == "guarda", "el Fuego Nuevo borra los encantamientos Y devuelve los efectos a Filo / Guarda");

Console.WriteLine("\n== 6. /enchant: elegir el efecto y confirmar el cambio ==");
await Reset();
var bonusSvc = new PlayerBonusService(new PetRepository(factory), new BlessingRepository(factory), eventRepo);
Task<DustModule.DustResult> Enchant(string slot, string? effect = null, bool confirmed = false, Random? rng = null, bool advanced = false) =>
    DustModule.ExecuteEnchantAsync(users, items, dustRepo, events, U, slot, rng, bonusSvc, advanced, effect, confirmed);
var bad = await Enchant("arma", "vida");
Check(bad.PlainMessage is not null && bad.PlainMessage.Contains("no es un efecto del arma") && bad.PlainMessage.Contains("Filo") && bad.PlainMessage.Contains("Crítico") && bad.PlainMessage.Contains("Sangre") && (await Me()).Gold == 1_000_000, "un efecto de la otra pieza se rechaza diciendo cuáles hay y sin cobrar");
var bad2 = await Enchant("amuleto", "critico");
Check(bad2.PlainMessage is not null && bad2.PlainMessage.Contains("no es un efecto del amuleto") && bad2.PlainMessage.Contains("Guarda") && bad2.PlainMessage.Contains("Vida") && bad2.PlainMessage.Contains("Esquive"), "y al revés: Crítico en el amuleto");
var first = await Enchant("arma", "Crítico", rng: new FixedRandom(800));   // tier 3
var uFirst = await Me();
Check(first.Embed is not null && first.Components is null && uFirst.WeaponEnchantKind == "critico" && uFirst.WeaponEnchant == 3 && AllText(first.Embed).Contains("Crítico Ardiente") && AllText(first.Embed).Contains("Mejoró") && AllText(first.Embed).Contains("🎯 Efecto") && AllText(first.Embed).Contains("Solo cuenta contra monstruos"),
    "elegir Crítico sobre una pieza sin encantar lo hace directo, dice el efecto y que solo cuenta contra monstruos");
long goldMid = (await Me()).Gold;
var ask = await Enchant("arma", "sangre", rng: new FixedRandom(0));
var comps = ask.Components!;
var buttons = comps.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(ask.Embed is not null && AllText(ask.Embed).Contains("¿Cambiar de efecto?") && AllText(ask.Embed).Contains("Crítico Ardiente") && AllText(ask.Embed).Contains("Sangre") && (await Me()).Gold == goldMid && (await Me()).WeaponEnchantKind == "critico"
    && buttons.Count == 2 && buttons[0].CustomId == $"enchant_switch:{U}:weapon:sangre:0" && buttons[1].CustomId == $"enchant_cancel:{U}" && buttons.All(b => b.CustomId.Length <= 100),
    "cambiar de efecto sobre una pieza encantada PREGUNTA (nombra lo que se pierde) con dos botones y no cobra nada");
var sameKind = await Enchant("arma", "critico", rng: new FixedRandom(0));
Check(sameKind.Embed is not null && sameKind.Components is null && (await Me()).WeaponEnchant == 3 && (await Me()).Gold < goldMid && AllText(sameKind.Embed).Contains("Ya tenías"), "pedir el MISMO efecto no pregunta (solo sube) y el intento se paga igual");
var noEffect = await Enchant("arma", rng: new FixedRandom(0));
Check(noEffect.Components is null && (await Me()).WeaponEnchantKind == "critico", "sin elegir efecto queda el de la pieza (aa enchant arma sigue andando como antes)");
var confirmed = await Enchant("arma", "sangre", confirmed: true, rng: new FixedRandom(500));   // tier 2
var uConf = await Me();
Check(confirmed.Embed is not null && uConf.WeaponEnchantKind == "sangre" && uConf.WeaponEnchant == 2 && AllText(confirmed.Embed).Contains("Cambiaste de efecto") && AllText(confirmed.Embed).Contains("Crítico Ardiente") && AllText(confirmed.Embed).Contains("Sangre Al Rojo"),
    "confirmado: reemplaza (Crítico Ardiente ➜ Sangre Al Rojo, con el tier que salió) y lo cuenta");
var advLocked = await Enchant("arma", "critico", advanced: true);
Check(advLocked.PlainMessage is not null && advLocked.PlainMessage.Contains("se desbloquea"), "el modo avanzado sigue pidiendo el oficio al 100, también con efecto");
var ev = await db.ExecuteScalarAsync<string>("SELECT detail FROM game_events WHERE discord_id = @id AND kind = 'enchant' ORDER BY event_id DESC LIMIT 1", new { id = (long)U });
Check(ev == "weapon:2:sangre", $"el evento de cada intento anota la pieza, el tier y el efecto («{ev}»)");
await Reset();
var amu = await Enchant("amuleto", "esquive", rng: new FixedRandom(999));   // tier 5
Check(amu.Embed is not null && (await Me()).AmuletEnchantKind == "esquive" && (await Me()).AmuletEnchant == 5 && AllText(amu.Embed).Contains("Esquive Soberano") && AllText(amu.Embed).Contains("+15 puntos de esquive"), "el amuleto con Esquive V: «+15 puntos de esquive»");

Console.WriteLine("\n== 7. Pantallas ==");
await Reset();
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, weapon_enchant_kind = 'critico', amulet_enchant = 4, amulet_enchant_kind = 'vida' WHERE discord_id = @id", new { id = (long)U });
var zonesRepo = new ZoneRepository(factory); var buffRepo = new BuffRepository(factory);
var prof = await GameModule.BuildProfileEmbedAsync(users, items, zonesRepo, buffRepo, U, "T", "https://example.com/a.png");
string State = prof.Fields.Single(f => f.Name == "Estado").Value;
Check(State.Split('\n').First(l => l.StartsWith("**ATQ**")).Contains("✨Crít +10") && State.Split('\n').First(l => l.StartsWith("**DEF**")).Contains("✨Vida +18 %"), "el perfil marca el efecto de cada pieza: «✨Crít +10» y «✨Vida +18 %»");
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, weapon_enchant_kind = 'filo', amulet_enchant = 2, amulet_enchant_kind = 'guarda' WHERE discord_id = @id", new { id = (long)U });
var prof2 = await GameModule.BuildProfileEmbedAsync(users, items, zonesRepo, buffRepo, U, "T", "https://example.com/a.png");
string State2 = prof2.Fields.Single(f => f.Name == "Estado").Value;
Check(State2.Split('\n').First(l => l.StartsWith("**ATQ**")).Contains("✨+13 %") && State2.Split('\n').First(l => l.StartsWith("**DEF**")).Contains("✨+6 %"), "con Filo y Guarda la marca sale de la tabla de cada uno («✨+13 %» el Filo III, «✨+6 %» la Guarda II)");
await db.ExecuteAsync("UPDATE users SET weapon_enchant = 3, weapon_enchant_kind = 'sangre', amulet_enchant = 0, amulet_enchant_kind = 'guarda' WHERE discord_id = @id", new { id = (long)U });
var infoE = DustModule.BuildInfoEmbed(await Me(), await items.GetByIdAsync(weaponId), await items.GetByIdAsync(amuletId));
Check(AllText(infoE).Contains("Sangre Ardiente") && AllText(infoE).Contains("3 % del daño en vida") && AllText(infoE).Contains("efecto:") && infoE.Length <= 6000 && infoE.Fields.All(f => f.Value.Length <= 1024), "/enchant sin pieza muestra el efecto que tenés y cómo elegirlo");
var options = DustModule.BuildOptionsEmbed();
string optText = AllText(options);
Check(Enchantments.Kinds.All(k => optText.Contains($"**{k.Name}**: {Enchantments.DescribeValues(k)}")) && optText.Contains("reemplaza el que tenés") && optText.Contains("solo contra monstruos") && options.Length <= 6000 && options.Fields.All(f => f.Value.Length <= 1024),
    $"/enchant info lista los seis efectos con sus cinco valores (leídos de la tabla), avisa que cambiar reemplaza y que son solo contra monstruos, y entra en los límites ({options.Length})");
var help = HelpTopics.Find("enchant")!.Build();
Check(AllText(help).Contains("Crítico") && help.Length <= 6000, "el tema de ayuda del encantamiento (el mismo embed) los explica");
var confirmEmbed = DustModule.BuildSwitchConfirmEmbed(new Item { Name = "Espada", Emoji = null }, "weapon", "critico", 3, "sangre", (200, 10));
Check(confirmEmbed.Description.Contains("Crítico Ardiente") && confirmEmbed.Description.Contains("reemplaza") && confirmEmbed.Description.Contains("Sangre") && confirmEmbed.Length <= 6000, "la pregunta de confirmar nombra lo que se reemplaza");

Console.WriteLine("\n== 8. Comandos y botones registrados (InteractionService real) ==");
var client = new Discord.WebSocket.DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var enchantCmd = interactions.SlashCommands.Single(c => c.Name == "enchant" && c.Module.SlashGroupName is null);
var effectParam = enchantCmd.Parameters.SingleOrDefault(p => p.Name == "efecto");
Check(effectParam is { IsRequired: false, Choices.Count: 6 } && effectParam.Description.Length is >= 1 and <= 100 && enchantCmd.Description.Length <= 100 && enchantCmd.Parameters.Count == 3 && enchantCmd.Parameters.All(p => p.Description.Length <= 100),
    $"/enchant tiene el parámetro opcional «efecto» con 6 opciones y respeta los límites de Discord (descripción de {effectParam?.Description.Length} caracteres)");
Check(effectParam!.Choices.Select(c => c.Value.ToString()).OrderBy(x => x).SequenceEqual(["critico", "esquive", "filo", "guarda", "sangre", "vida"]) && effectParam.Choices.All(c => c.Name.Length <= 100), "las opciones son las seis claves de Enchantments");
Discord.IComponentInteraction Click(string id) => FakeProxy.Make<Discord.IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<Discord.IComponentInteractionData>(new() { ["CustomId"] = id }) });
(string? Method, string[] Groups) Route(string id) { var r = interactions.SearchComponentCommand(Click(id)); return r.IsSuccess ? (r.Command.MethodName, r.Command.Module.Name == "DustModule" ? r.RegexCaptureGroups ?? [] : []) : (null, []); }
var rSwitch = Route($"enchant_switch:{U}:weapon:sangre:0"); var rCancel = Route($"enchant_cancel:{U}");
Check(rSwitch.Method == "HandleSwitchAsync" && rSwitch.Groups.SequenceEqual([U.ToString(), "weapon", "sangre", "0"]) && rCancel.Method == "HandleSwitchCancelAsync" && rCancel.Groups.SequenceEqual([U.ToString()]), "«enchant_switch:…» y «enchant_cancel:…» llegan a sus manejadores con sus capturas");
var texts = new Discord.Commands.CommandService(new Discord.Commands.CommandServiceConfig { CaseSensitiveCommands = false });
texts.AddTypeReader<IUser>(new GuildUserTypeReader(), true);
var textMods = await texts.AddModulesAsync(typeof(BoxModule).Assembly, sp);
var textEnchant = textMods.SelectMany(m => m.Commands).Single(c => c.Aliases.Contains("enchant"));
Check(textEnchant.Parameters.Count == 3 && textEnchant.Parameters.All(p => p.IsOptional) && textEnchant.Aliases.Contains("encantar"), "«aa enchant» acepta pieza, efecto y modo (los tres opcionales) y conserva su alias «encantar»");
Check(Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, typeof(DustModule)) is DustModule, "DustModule se arma con la inyección");

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Un Random que siempre devuelve el mismo valor (acotado): para elegir el tier del sorteo sin azar. Con los pesos 420/300/180/90/10 (suman 1000): 0 → tier 1, 500 → 2, 800 → 3, 950 → 4, 999 → 5.
sealed class FixedRandom(int value) : Random
{
    public override int Next(int maxValue) => Math.Min(value, maxValue - 1);
}

// Un proxy que implementa cualquier interfaz de Discord devolviendo los valores que se le den por nombre de propiedad.
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
