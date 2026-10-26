using System.Text;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using Dapper;
using Npgsql;

// Revisión de balance completa (v0.15.0, lap 1): (A) matriz de combate por clase con las reglas reales (habilidad, comida), (B) economía: oro, XP, comida,
// (C) recorrido Monte Carlo de la escalera entera con drops, recolección y crafteo. Todo contra la base local y el CombatTurnResolver real.
//   uso: dotnet run -- A | B | C
string part = args.Length > 0 ? args[0] : "A";
string conn = TestEnv.Connection();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
await World.LoadAsync(db);

switch (part)
{
    case "A": PartA.Run(); break;
    case "B": PartB.Run(); break;
    case "C": PartC.Run(args); break;
    case "D": PartD.Run(args); break;
    case "E": PartE.Run(); break;
    case "F": PartF.Run(args); break;
    case "G": PartG.Run(args); break;
    case "H": PartH.Run(args); break;
    case "I": PartI.Run(args); break;
    case "J": PartJ.Run(args); break;
    case "K": PartK.Run(args); break;
    case "L": PartL.Run(args); break;
    case "M": PartM.Run(args); break;
    default: Console.WriteLine("A | B | C"); break;
}

// ======================= datos =======================
public class ZoneRow { public int ZoneId { get; set; } public int MinLevel { get; set; } public string Name { get; set; } = ""; }
public class MonRow
{
    public string Name { get; set; } = ""; public string Kind { get; set; } = ""; public int ZoneId { get; set; }
    public int MinHp { get; set; } public int MaxHp { get; set; } public int MinDmg { get; set; } public int MaxDmg { get; set; }
    public int GoldB { get; set; } public int XpB { get; set; } public string Drops { get; set; } = "";
    public string[] DropNames => Drops.Length == 0 ? [] : Drops.Split('|');
}
public class ItemRow
{
    public string Name { get; set; } = ""; public string Type { get; set; } = ""; public string Rarity { get; set; } = "";
    public int Stat { get; set; } public int Sell { get; set; } public int Buy { get; set; } public string? Family { get; set; }
}
public class RecipeRow
{
    public int RecipeId { get; set; } public string Result { get; set; } = ""; public string Type { get; set; } = ""; public string? Family { get; set; }
    public int Stat { get; set; } public int Sell { get; set; } public int Gold { get; set; } public int ZoneId { get; set; }
}
public class IngRow { public int RecipeId { get; set; } public string Name { get; set; } = ""; public int Quantity { get; set; } }

public sealed record Recipe(string Result, string Type, string? Family, int Stat, int Sell, int Gold, int Zi, Dictionary<string, int> Ingredients);

public static class World
{
    public static List<ZoneRow> Zones = [];
    public static MonRow[][] Hunt = [], Travel = [], Boss = [];
    public static List<Recipe> Recipes = [];
    public static Dictionary<string, List<string>> GatherPool = new(); public static Dictionary<string, int> Sell = new(); // "Madera|Raro" -> nombres
    public static HashSet<string> MythicNames = new(); public static string[] Classes = ["Guerrero", "Hechicero", "Ninja", "Arquero"];
    public static string FamilyOf(string cls) => ClassCatalog.All.First(c => c.Name == cls).WeaponType;

    public static async Task LoadAsync(NpgsqlConnection db)
    {
        Zones = (await db.QueryAsync<ZoneRow>("SELECT zone_id AS \"ZoneId\", min_level AS \"MinLevel\", name AS \"Name\" FROM zones WHERE kind = 'normal' ORDER BY min_level")).ToList();
        var mons = (await db.QueryAsync<MonRow>(@"
            SELECT m.name AS ""Name"", CASE WHEN m.is_boss THEN 'boss' WHEN m.is_travel THEN 'travel' ELSE 'hunt' END AS ""Kind"", m.zone_id AS ""ZoneId"",
                   m.min_hp AS ""MinHp"", m.max_hp AS ""MaxHp"", m.min_damage AS ""MinDmg"", m.max_damage AS ""MaxDmg"", m.gold_reward AS ""GoldB"", m.xp_reward AS ""XpB"",
                   COALESCE(string_agg(i.name, '|'), '') AS ""Drops""
            FROM monsters m JOIN zones z ON z.zone_id = m.zone_id AND z.kind = 'normal'
            LEFT JOIN monster_drops d ON d.monster_id = m.monster_id LEFT JOIN items i ON i.item_id = d.item_id
            GROUP BY m.monster_id, m.name, m.is_boss, m.is_travel, m.zone_id, m.min_hp, m.max_hp, m.min_damage, m.max_damage, m.gold_reward, m.xp_reward")).ToList();
        MonRow[] Pick(int zi, string kind) => mons.Where(m => m.ZoneId == Zones[zi].ZoneId && m.Kind == kind).OrderBy(m => m.Name).ToArray();
        Hunt = Enumerable.Range(0, Zones.Count).Select(i => Pick(i, "hunt")).ToArray();
        Travel = Enumerable.Range(0, Zones.Count).Select(i => Pick(i, "travel")).ToArray();
        Boss = Enumerable.Range(0, Zones.Count).Select(i => Pick(i, "boss")).ToArray();
        PartI.Asador = (await db.QueryAsync<MonRow>("SELECT m.name AS \"Name\", 'boss' AS \"Kind\", m.zone_id AS \"ZoneId\", m.min_hp AS \"MinHp\", m.max_hp AS \"MaxHp\", m.min_damage AS \"MinDmg\", m.max_damage AS \"MaxDmg\", m.gold_reward AS \"GoldB\", m.xp_reward AS \"XpB\" FROM monsters m WHERE m.name = 'El Asador Eterno'")).FirstOrDefault();

        var items = (await db.QueryAsync<ItemRow>("SELECT name AS \"Name\", type AS \"Type\", rarity AS \"Rarity\", stat_value AS \"Stat\", sell_price AS \"Sell\", buy_price AS \"Buy\", weapon_family AS \"Family\" FROM items")).ToList();
        Sell = items.ToDictionary(i => i.Name, i => i.Sell); MythicNames = items.Where(i => i.Rarity == "Mítico").Select(i => i.Name).ToHashSet(); foreach (var g in items.Where(i => i.Type is "Madera" or "Mineral").GroupBy(i => $"{i.Type}|{i.Rarity}")) GatherPool[g.Key] = g.Select(i => i.Name).ToList();

        var rr = (await db.QueryAsync<RecipeRow>(@"SELECT r.recipe_id AS ""RecipeId"", i.name AS ""Result"", i.type AS ""Type"", i.weapon_family AS ""Family"", i.stat_value AS ""Stat"", i.sell_price AS ""Sell"",
                r.gold_cost AS ""Gold"", r.zone_id AS ""ZoneId"" FROM recipes r JOIN items i ON i.item_id = r.result_item_id JOIN zones z ON z.zone_id = r.zone_id AND z.kind = 'normal'")).ToList();
        var ings = (await db.QueryAsync<IngRow>("SELECT ri.recipe_id AS \"RecipeId\", x.name AS \"Name\", ri.quantity AS \"Quantity\" FROM recipe_ingredients ri JOIN items x ON x.item_id = ri.item_id")).ToList();
        Recipes = rr.Select(r => new Recipe(r.Result, r.Type, r.Family, r.Stat, r.Sell, r.Gold, Zones.FindIndex(z => z.ZoneId == r.ZoneId),
            ings.Where(i => i.RecipeId == r.RecipeId).ToDictionary(i => i.Name, i => i.Quantity))).ToList();
    }

    // Receta del arma de clase / general / amuleto de una zona.
    public static Recipe ClassWeapon(string cls, int zi) => Recipes.Single(r => r.Zi == zi && r.Type == "Weapon" && r.Family == FamilyOf(cls));
    public static Recipe GeneralWeapon(int zi) => Recipes.Single(r => r.Zi == zi && r.Type == "Weapon" && r.Family is null);
    public static Recipe Amulet(int zi) => Recipes.Single(r => r.Zi == zi && r.Type == "Amulet");
}

// ======================= combate =======================
public enum Policy { AttackOnly = 0, Ability = 1, AbilityHeal = 2 }

public sealed class Cell
{
    public bool[] Died = []; public float[] Lost = []; public short[] Turns = []; public short[] Food = [];
    public double DieRate, MeanLost, MeanTurns, MeanFood;
}

public static class Sim
{
    public static Random Rng = new(20261008); public static Dictionary<string, double> ExtraHp = new();
    public static readonly (string Name, int Heal, int Price)[] Foods = [("Mate Amargo", 15, 8), ("Empanada de Carne", 40, 36), ("Asado de Tira", 100, 160), ("Cordero Patagónico", 150, 300)];
    public static readonly Dictionary<(string, int, int, bool, int, string, int, string, int, int), PlayerCombatProfile> Profiles = new();
    // Parte M (v0.22.1): el +% de ataque de un banquete activo (GameData/AttackBuff.cs) para todo el combate; 0 = sin banquete.
    public static int AttackBuff = 0;
    // Parte L (v0.22.0): los encantamientos de la pieza que lleva el jugador simulado (efecto y tier de arma y de amuleto). Sin encantar = ("filo", 0, "guarda", 0).
    public static (string WeaponKind, int WeaponTier, string AmuletKind, int AmuletTier) Enchant = ("filo", 0, "guarda", 0);

    public static PlayerCombatProfile Profile(string cls, int level, int weaponStat, bool classWeapon, int amuletStat)
    {
        var key = (cls, level, weaponStat, classWeapon, amuletStat, Enchant.WeaponKind, Enchant.WeaponTier, Enchant.AmuletKind, Enchant.AmuletTier, AttackBuff);
        if (Profiles.TryGetValue(key, out var cached)) return cached;
        int maxHp = 100 + (15 * (level - 1));
        var user = new User
        {
            Class = cls, Level = level, MaxHp = maxHp, CurrentHp = maxHp,
            WeaponEnchantKind = Enchant.WeaponKind, WeaponEnchant = Enchant.WeaponTier, AmuletEnchantKind = Enchant.AmuletKind, AmuletEnchant = Enchant.AmuletTier,
        };
        Item? w = weaponStat > 0 ? new Item { StatValue = weaponStat, WeaponFamily = classWeapon ? World.FamilyOf(cls) : null } : null;
        Item? a = amuletStat > 0 ? new Item { StatValue = amuletStat } : null;
        var profile = PlayerCombatProfileCalculator.Resolve(user, w, a, AttackBuff, PlayerBonuses.None); // bonuses no nulos = peleas contra monstruos (incluye PveTuning)
        if (ExtraHp.TryGetValue(cls, out var extra)) profile = profile with { CombatMaxHp = (int)Math.Round(profile.CombatMaxHp * extra), CombatCurrentHp = (int)Math.Round(profile.CombatCurrentHp * extra) };
        Profiles[key] = profile;
        return profile;
    }

    // tier = cuántas comidas puede pagar (0..4); limited = /travel y /boss (una sola curación por pelea).
    public static (bool Died, double Lost, int Turns, int FoodGold) Fight(
        string cls, int level, int weaponStat, bool classWeapon, int amuletStat, MonRow m, Policy policy, int tier, double startFrac, bool limited)
    {
        var prof = Profile(cls, level, weaponStat, classWeapon, amuletStat);
        var cp = new CombatantProfile(prof.Damage, prof.Defense, cls, prof.Passives);
        int max = prof.CombatMaxHp;
        int hp0 = Math.Max(1, (int)Math.Round(max * startFrac));
        int hp = hp0;
        int mHp = Rng.Next(m.MinHp, m.MaxHp + 1), mDmg = Rng.Next(m.MinDmg, m.MaxDmg + 1);
        var state = default(AbilityState);
        int turns = 0, foodGold = 0, healsLeft = limited ? 1 : 6;

        while (turns < 400)
        {
            if (policy == Policy.AbilityHeal && tier > 0 && healsLeft > 0 && hp * 100 <= 40 * max)
            {
                int missing = max - hp, pick = 0;
                for (int i = 0; i < tier; i++) if (Foods[i].Heal <= missing * 1.25) pick = i;
                if (Foods[pick].Heal * 1.25 >= mDmg) // curarse tiene que valer más que el golpe que viene
                {
                    hp = Math.Min(max, hp + Foods[pick].Heal);
                    var counter = CombatTurnResolver.ResolveCounterTurn(cp, state, mDmg);
                    state = counter.Status;
                    hp -= counter.Hit.Damage;
                    turns++; healsLeft--; foodGold += Foods[pick].Price;
                    if (hp <= 0) return (true, 1.0, turns, foodGold);
                    continue;
                }
            }

            bool useAbility = policy != Policy.AttackOnly && CombatTurnResolver.CheckAbility(cls, state) == AbilityAvailability.Ready;
            var r = CombatTurnResolver.ResolveTurn(cp, state, useAbility ? PlayerAction.Ability : PlayerAction.Attack, hp, max, mHp, mDmg);
            turns++;
            hp = r.PlayerHpAfter; mHp = r.MonsterHpAfter; state = r.StatusAfter;
            if (mHp <= 0) return (false, Math.Max(0.0, (hp0 - hp) / (double)max), turns, foodGold);
            if (hp <= 0) return (true, 1.0, turns, foodGold);
        }

        return (true, 1.0, turns, foodGold);
    }

    public static Cell Compute(string cls, int level, int weaponStat, bool classWeapon, int amuletStat, MonRow[] pool, Policy policy, int tier, double startFrac, bool limited, int n)
    {
        var c = new Cell { Died = new bool[n], Lost = new float[n], Turns = new short[n], Food = new short[n] };
        double die = 0, lost = 0, turns = 0, food = 0;
        for (int i = 0; i < n; i++)
        {
            var (d, l, t, f) = Fight(cls, level, weaponStat, classWeapon, amuletStat, pool[i % pool.Length], policy, tier, startFrac, limited);
            c.Died[i] = d; c.Lost[i] = (float)l; c.Turns[i] = (short)t; c.Food[i] = (short)Math.Min(f, short.MaxValue);
            if (d) die++; lost += l; turns += t; food += f;
        }

        c.DieRate = die / n; c.MeanLost = lost / n; c.MeanTurns = turns / n; c.MeanFood = food / n;
        return c;
    }

    // /heal real (GameData/HealPlanner): qué cuesta curar "missing" HP con las 4 comidas comunes en la mochila.
    public static int HealCost(int missing, bool thrifty = false)
    {
        if (missing <= 0) return 0;
        if (thrifty) return (int)Math.Ceiling(missing / 15.0) * 8;
        var stock = Foods.Select((f, i) => new FoodStock(i, f.Name, null, f.Heal, 99)).ToList();
        var plan = HealPlanner.Plan(missing, stock);
        return plan.Foods.Sum(p => p.Quantity * Foods[p.Food.ItemId].Price);
    }

    public static int MaxHpAt(int level) => 100 + (15 * (level - 1));
}

// ======================= PARTE A: matriz de combate =======================
public static class PartA
{
    sealed record Scenario(string Code, string Label);
    static readonly Scenario[] Scenarios =
    [
        new("NG", "sin equipo"), new("PREV", "equipo de la zona anterior"), new("OWNW", "arma de clase propia, sin amuleto"),
        new("GEN", "arma general + amuleto propio"), new("OWN", "equipo de clase propio"),
    ];

    // (arma, esDeClase, amuleto) de una zona (índice 0..4) según el escenario.
    static (int W, bool CW, int A) Gear(string cls, int zi, string code)
    {
        (int, bool, int) None() => (0, false, 0);
        switch (code)
        {
            case "NG": return None();
            case "PREV": return zi == 0 ? None() : (World.ClassWeapon(cls, zi - 1).Stat, true, World.Amulet(zi - 1).Stat);
            case "OWNW": return (World.ClassWeapon(cls, zi).Stat, true, 0);
            case "GEN": return (World.GeneralWeapon(zi).Stat, false, World.Amulet(zi).Stat);
            default: return (World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat);
        }
    }

    public static void Run()
    {
        const int N = 900;
        int[] offsets = [0, 3, 6, 10];
        string[] kinds = ["común", "élite", "jefe"];
        var sb = new StringBuilder();

        // acumuladores: [policy][class][kind][scenario] -> (die sum, lost sum, count)
        var acc = new Dictionary<(int, string, int, string), (double D, double L, double F, double T, int N)>();
        // detalle por zona (política AbilityHeal, nivel min+3)
        var detail = new Dictionary<(string, int, int, string), (double D, double L, double F)>();

        foreach (var pol in new[] { Policy.AttackOnly, Policy.Ability, Policy.AbilityHeal })
            foreach (var cls in World.Classes)
                for (int zi = 0; zi < World.Zones.Count; zi++)
                    foreach (var sc in Scenarios)
                    {
                        if (sc.Code == "PREV" && zi == 0) continue;
                        var (w, cw, a) = Gear(cls, zi, sc.Code);
                        foreach (int off in offsets)
                        {
                            int level = World.Zones[zi].MinLevel + off;
                            for (int k = 0; k < 3; k++)
                            {
                                var pool = k == 0 ? World.Hunt[zi] : k == 1 ? World.Travel[zi] : World.Boss[zi];
                                // comida disponible: la que un jugador de ese nivel puede pagar (hasta Cordero), solo en la política con comida
                                var cell = Sim.Compute(cls, level, w, cw, a, pool, pol, tier: 4, startFrac: 1.0, limited: k > 0, N);
                                var key = ((int)pol, cls, k, sc.Code);
                                acc.TryGetValue(key, out var cur);
                                acc[key] = (cur.D + cell.DieRate, cur.L + cell.MeanLost, cur.F + cell.MeanFood, cur.T + cell.MeanTurns, cur.N + 1);
                                if (pol == Policy.AbilityHeal && off == 3)
                                    detail[(cls, zi, k, sc.Code)] = (cell.DieRate, cell.MeanLost, cell.MeanFood);
                            }
                        }
                    }

        string P(double v) => $"{100 * v,5:F1}";
        foreach (var pol in new[] { Policy.AttackOnly, Policy.Ability, Policy.AbilityHeal })
        {
            sb.AppendLine($"\n=== POLÍTICA: {pol} — % de PERDER (promedio de las 5 zonas × 4 niveles), por tipo de pelea y equipo ===");
            sb.AppendLine("Clase     | común: NG   PREV  OWNW  GEN   OWN  | élite: NG   PREV  OWNW  GEN   OWN | jefe: NG   PREV  OWNW  GEN   OWN");
            foreach (var cls in World.Classes)
            {
                var row = new StringBuilder($"{cls,-9} |        ");
                for (int k = 0; k < 3; k++)
                {
                    foreach (var sc in Scenarios)
                    {
                        var v = acc[((int)pol, cls, k, sc.Code)];
                        row.Append($" {P(v.D / v.N)}");
                    }

                    row.Append(k < 2 ? "  |        " : string.Empty);
                }

                sb.AppendLine(row.ToString());
            }
        }

        sb.AppendLine("\n=== Vida que cuesta una pelea COMÚN (% de la vida máxima), política AbilityHeal, con equipo PROPIO / ZONA ANTERIOR / SIN EQUIPO ===");
        foreach (var cls in World.Classes)
            sb.AppendLine($"{cls,-9} OWN {P(Avg(acc, 2, cls, 0, "OWN", 'L'))}  PREV {P(Avg(acc, 2, cls, 0, "PREV", 'L'))}  NG {P(Avg(acc, 2, cls, 0, "NG", 'L'))}   turnos OWN {Avg(acc, 2, cls, 0, "OWN", 'T'):F1}");

        sb.AppendLine("\n=== EFECTO DE LA HABILIDAD y DE LA COMIDA (jefe, equipo PREVIO, promedio): AttackOnly → Ability → AbilityHeal ===");
        foreach (var cls in World.Classes)
            sb.AppendLine($"{cls,-9} {P(Avg(acc, 0, cls, 2, "PREV", 'D'))} → {P(Avg(acc, 1, cls, 2, "PREV", 'D'))} → {P(Avg(acc, 2, cls, 2, "PREV", 'D'))}   | con equipo PROPIO: {P(Avg(acc, 0, cls, 2, "OWN", 'D'))} → {P(Avg(acc, 1, cls, 2, "OWN", 'D'))} → {P(Avg(acc, 2, cls, 2, "OWN", 'D'))}");

        sb.AppendLine("\n=== DETALLE POR ZONA (AbilityHeal, nivel = mínimo de la zona + 3): % de perder · oro de comida usada en la pelea ===");
        sb.AppendLine("Zona/nivel | Clase     | común OWN | común PREV | élite PREV | JEFE OWN | JEFE PREV | JEFE NG");
        for (int zi = 0; zi < World.Zones.Count; zi++)
            foreach (var cls in World.Classes)
            {
                string D(int k, string code) => detail.TryGetValue((cls, zi, k, code), out var v) ? $"{100 * v.D,5:F1}" : "   — ";
                string F(int k, string code) => detail.TryGetValue((cls, zi, k, code), out var v) ? $"{v.F,4:F0}" : "  — ";
                sb.AppendLine($"Z{zi + 1} L{World.Zones[zi].MinLevel + 3,-3}   | {cls,-9} | {D(0, "OWN")} ({F(0, "OWN")}g) | {D(0, "PREV")} ({F(0, "PREV")}g) | {D(1, "PREV")} ({F(1, "PREV")}g) | {D(2, "OWN")} ({F(2, "OWN")}g) | {D(2, "PREV")} ({F(2, "PREV")}g) | {D(2, "NG")} ({F(2, "NG")}g)");
            }

        Console.WriteLine(sb.ToString());
    }

    static double Avg(Dictionary<(int, string, int, string), (double D, double L, double F, double T, int N)> acc, int pol, string cls, int k, string code, char which)
    {
        var v = acc[(pol, cls, k, code)];
        double total = which switch { 'D' => v.D, 'L' => v.L, 'F' => v.F, _ => v.T };
        return total / v.N;
    }
}

// ======================= PARTE B: economía y XP =======================
public static class PartB
{
    public static void Run()
    {
        var sb = new StringBuilder();
        double[] hunt = new double[5];

        sb.AppendLine("=== NIVELES: XP para pasar de nivel y minutos de juego perfecto (cacería 1/min + viaje cada 30 min = x2 de XP) ===");
        sb.AppendLine("Nivel | XP p/subir | zona usada | XP/cacería | cacerías | min/nivel (solo hunt) | min/nivel (hunt+viaje) | minutos acumulados | horas acum.");
        double cum = 0;
        for (int level = 1; level <= 30; level++)
        {
            int zi = ZoneFor(level);
            double xpHunt = 14 + level + World.Hunt[zi].Average(m => m.XpB);
            double need = LevelingCalculator.RequiredXpForLevel(level);
            double huntsNeeded = need / xpHunt;
            double minutesBoth = need / (xpHunt * 2.0); // el viaje paga x30 por cada 30 min: otra cacería por minuto
            cum += minutesBoth;
            sb.AppendLine($"{level,5} | {need,10:F0} | Z{zi + 1,-9} | {xpHunt,10:F0} | {huntsNeeded,8:F1} | {huntsNeeded,21:F0} | {minutesBoth,22:F0} | {cum,18:F0} | {cum / 60,6:F1}");
        }

        sb.AppendLine("\n=== ORO: ingresos por hora de juego perfecto vs lo que cuestan las cosas (nivel = mínimo de la zona + 3) ===");
        sb.AppendLine("Zona | nivel | oro/cacería | oro/viaje | oro/jefe | oro/hora (hunt+viaje+jefe) | receta arma clase | amuleto | set completo | horas de ORO del set | cajón? (precio caja de la zona) | caja/oro-hora");
        string[] boxName = ["Cajón de Pino", "Baúl de Roble", "Arcón de Hierro", "Cofre de Oro", "Cofre de Escoria"];
        int[] boxPrice = [2300, 14000, 42000, 120000, 0];
        for (int zi = 0; zi < 5; zi++)
        {
            int level = World.Zones[zi].MinLevel + 3;
            double gb = World.Hunt[zi].Average(m => m.GoldB);
            double gHunt = 10 + (2 * level) + gb;
            double gTravel = (10 + (2 * level) + World.Travel[zi][0].GoldB) * CombatRewardCalculator.TravelRewardMultiplier;
            double gBoss = (10 + (2 * level) + World.Boss[zi][0].GoldB) * CombatRewardCalculator.BossRewardMultiplier;
            double perHour = (gHunt * 60) + (gTravel * 2) + (gBoss / 5.0);
            int weapon = World.ClassWeapon("Guerrero", zi).Gold, amulet = World.Amulet(zi).Gold;
            double setHours = (weapon + amulet) / perHour;
            sb.AppendLine($"Z{zi + 1}   | {level,5} | {gHunt,11:F0} | {gTravel,9:F0} | {gBoss,8:F0} | {perHour,26:F0} | {weapon,17} | {amulet,7} | {weapon + amulet,12} | {setHours,20:F2} | {boxName[zi],-16} {boxPrice[zi],7} | {(boxPrice[zi] > 0 ? (boxPrice[zi] / perHour).ToString("F1") + " h" : "(premio)")}");
        }

        sb.AppendLine("\n=== COMIDA: lo que cuesta curarse tras una pelea común con equipo PROPIO (nivel = mín+3), /heal real vs Mate Amargo suelto ===");
        sb.AppendLine("Zona | Clase     | vida perdida % | HP reales | /heal (oro) | Mate Amargo (oro) | oro por cacería | gasto en comida / ingreso % (heal) | % (mate)");
        for (int zi = 0; zi < 5; zi++)
            foreach (var cls in World.Classes)
            {
                int level = World.Zones[zi].MinLevel + 3;
                var cell = Sim.Compute(cls, level, World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat, World.Hunt[zi], Policy.Ability, 0, 1.0, false, 1500);
                int maxHp = Sim.MaxHpAt(level);
                int lost = (int)Math.Round(cell.MeanLost * maxHp);
                int heal = Sim.HealCost(lost), mate = Sim.HealCost(lost, true);
                double income = 10 + (2 * level) + World.Hunt[zi].Average(m => m.GoldB);
                sb.AppendLine($"Z{zi + 1}   | {cls,-9} | {100 * cell.MeanLost,14:F1} | {lost,9} | {heal,11} | {mate,17} | {income,15:F0} | {100.0 * heal / income,34:F0} | {100.0 * mate / income,6:F0}");
            }

        sb.AppendLine("\n=== PRIMERAS PELEAS (sin equipo, kit de 5 Mate Amargo, 50 de oro): nivel 1..4, zona 1 ===");
        sb.AppendLine("Nivel | HP | Clase     | % perder (sin comer) | vida que cuesta % | oro/cacería | XP/cacería | cacerías p/subir | oro de curarse (Mate)");
        for (int level = 1; level <= 4; level++)
            foreach (var cls in World.Classes)
            {
                var cell = Sim.Compute(cls, level, 0, false, 0, World.Hunt[0], Policy.Ability, 0, 1.0, false, 3000);
                int maxHp = Sim.MaxHpAt(level);
                int lost = (int)Math.Round(cell.MeanLost * maxHp);
                sb.AppendLine($"{level,5} | {maxHp,3} | {cls,-9} | {100 * cell.DieRate,20:F1} | {100 * cell.MeanLost,17:F0} | {10 + (2 * level),11} | {14 + level,10} | {LevelingCalculator.RequiredXpForLevel(level) / (14.0 + level),16:F1} | {Sim.HealCost(lost, true),6}");
            }

        Console.WriteLine(sb.ToString());
    }

    // la zona más alta cuyo nivel mínimo ya alcanzó (suponiendo el jefe anterior vencido)
    public static int ZoneFor(int level)
    {
        int zi = 0;
        for (int i = 0; i < World.Zones.Count; i++) if (level >= World.Zones[i].MinLevel) zi = i;
        return zi;
    }
}

// ======================= PARTE C: recorrido Monte Carlo =======================
public static class PartC
{
    public static void Run(string[] args)
    {
        int runs = args.Length > 1 ? int.Parse(args[1]) : 60;
        int capHours = args.Length > 2 ? int.Parse(args[2]) : 600;
        var sb = new StringBuilder();
        var modes = new (string Label, bool Bridge, bool Complete)[]
        {
            ("AVANCE LIBRE, SOLO ARMA DE CLASE (el jugador se va a la zona siguiente apenas puede)", false, false),
            ("AVANCE LIBRE, PUENTE (arma general primero, después la de clase)", true, false),
            ("SET COMPLETO ANTES DE AVANZAR (arma de clase + amuleto de cada zona, como diseñó la escalera)", false, true),
        };

        foreach (var mode in modes)
        {
            var summary = new Dictionary<string, List<Journey>>();
            foreach (var cls in World.Classes)
            {
                var list = new List<Journey>();
                for (int r = 0; r < runs; r++)
                {
                    var j = new Journey(cls, mode.Bridge, mode.Complete, new Random(1000 + r), capHours * 60);
                    j.Run();
                    list.Add(j);
                }

                summary[cls] = list;
            }

            sb.AppendLine($"\n##### {mode.Label} — {runs} recorridos por clase, tope {capHours} h #####");
            string[] marks = ["L5", "L10", "L15", "L20", "L25", "B1", "B2", "B3", "B4", "B5", "W1", "W2", "W3", "W4", "W5", "A1", "A2", "A3", "A4", "A5", "DONE"];
            sb.AppendLine("Hito (horas de juego perfecto): mediana [p10–p90]  (cuántos no llegaron en el tope)");
            foreach (var mark in marks)
            {
                var row = new StringBuilder($"{mark,-5}");
                foreach (var cls in World.Classes)
                {
                    var times = summary[cls].Select(j => j.Marks.TryGetValue(mark, out int t) ? (double?)t / 60.0 : null).ToList();
                    var ok = times.Where(t => t.HasValue).Select(t => t!.Value).OrderBy(t => t).ToList();
                    string text = ok.Count == 0 ? "—" : $"{Pct(ok, 0.5),5:F1} [{Pct(ok, 0.1):F1}–{Pct(ok, 0.9):F1}]";
                    int fail = times.Count - ok.Count;
                    row.Append($" | {cls[..3]} {text,-19}{(fail > 0 ? $"({fail} no)" : string.Empty),-8}");
                }

                sb.AppendLine(row.ToString());
            }

            sb.AppendLine("\nPor clase: muertes (mediana) · oro en comida · oro ganado · oro gastado en recetas · oro final · nivel final · intentos de jefe 1/2/3/4/5 · % de horas en descanso");
            foreach (var cls in World.Classes)
            {
                var l = summary[cls];
                sb.AppendLine($"{cls,-9} muertes {Med(l.Select(j => (double)j.Deaths)):F0} | comida {Med(l.Select(j => (double)j.FoodSpent)):F0} | ganado {Med(l.Select(j => (double)j.GoldEarned)):F0} | recetas {Med(l.Select(j => (double)j.RecipeGold)):F0} | final {Med(l.Select(j => (double)j.Gold)):F0} | nivel {Med(l.Select(j => (double)j.Level)):F0} | jefes {string.Join('/', Enumerable.Range(0, 5).Select(b => Med(l.Select(j => (double)j.BossTries[b])).ToString("F0")))} | descanso {Med(l.Select(j => 100.0 * j.RestMinutes / Math.Max(1, j.EndMinute))):F0}%");
            }

            sb.AppendLine("\nDesfase equipo/nivel: horas entre llegar al nivel de la zona siguiente y terminar el arma de clase de la zona (mediana; + = el equipo llega tarde)");
            foreach (var cls in World.Classes)
            {
                var parts = new List<string>();
                for (int zi = 0; zi < 4; zi++)
                {
                    var diffs = summary[cls].Select(j => j.Marks.TryGetValue($"W{zi + 1}", out int w) && j.Marks.TryGetValue($"L{World.Zones[zi + 1].MinLevel}", out int lv) ? (double?)(w - lv) / 60.0 : null)
                        .Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToList();
                    parts.Add($"Z{zi + 1}→Z{zi + 2}: {(diffs.Count == 0 ? "—" : Pct(diffs, 0.5).ToString("+0.0;-0.0"))}");
                }

                sb.AppendLine($"{cls,-9} {string.Join(" | ", parts)}");
            }
        }

        Console.WriteLine(sb.ToString());
    }

    static double Pct(List<double> sorted, double p) => sorted[Math.Clamp((int)Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
    static double Med(IEnumerable<double> values) { var s = values.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }
}

public sealed class Journey
{
    readonly string cls; readonly bool bridge, complete; readonly Random rng; readonly int cap;
    public int Level = 1; long xp; public long Gold = 50; int kit = 5;
    int realHp = 100;
    public int Highest; // zonas con jefe vencido
    readonly Dictionary<string, int> inv = new();
    int weaponStat; bool weaponClass; int amuletStat; int weaponSell;
    public int Deaths; public long FoodSpent, GoldEarned, RecipeGold; public int RestMinutes, EndMinute;
    public int[] BossTries = new int[5];
    public Dictionary<string, int> Marks = new();
    int bossReady; int t;
    readonly bool[] weaponDone = new bool[5], amuletDone = new bool[5];
    static readonly Dictionary<(string, int, int, bool, int, int, int, int, int), Cell> Cache = new();

    public Journey(string cls, bool bridge, bool complete, Random rng, int cap) { this.cls = cls; this.bridge = bridge; this.complete = complete; this.rng = rng; this.cap = cap; }

    int MaxHp => Sim.MaxHpAt(Level);
    double Sf => (double)realHp / MaxHp;

    Cell Get(int zi, int kind, double sf)
    {
        int bucket = sf >= 0.95 ? 0 : sf >= 0.7 ? 1 : sf >= 0.45 ? 2 : sf >= 0.25 ? 3 : 4;
        double startFrac = bucket switch { 0 => 1.0, 1 => 0.75, 2 => 0.5, 3 => 0.3, _ => 0.15 };
        int tier = Math.Min(4, Sim.Foods.Count(f => f.Price * 2 <= Gold));
        var key = (cls, Level, weaponStat, weaponClass, amuletStat, (zi * 3) + kind, tier, bucket, 0);
        if (Cache.TryGetValue(key, out var c)) return c;
        var pool = kind == 0 ? World.Hunt[zi] : kind == 1 ? World.Travel[zi] : World.Boss[zi];
        c = Sim.Compute(cls, Level, weaponStat, weaponClass, amuletStat, pool, Policy.AbilityHeal, tier, startFrac, kind > 0, 80);
        Cache[key] = c;
        return c;
    }

    // zonas (1..5) que puede pisar: nivel mínimo + jefe anterior vencido (+ en modo "set completo", solo hasta la primera zona con el set sin terminar)
    int MaxUnlocked()
    {
        int z = Math.Min(Highest + 1, 5);
        while (z > 1 && Level < World.Zones[z - 1].MinLevel) z--;
        if (complete)
            for (int i = 0; i < z - 1; i++)
                if (!(weaponDone[i] && amuletDone[i])) { z = i + 1; break; }
        return z;
    }

    // zona (índice) donde farmea: la más alta donde perder una cacería común es poco probable CON la vida que tiene ahora; -1 = hay que descansar
    int ChooseZone()
    {
        for (int z = MaxUnlocked(); z >= 1; z--)
            if (Get(z - 1, 0, Sf).DieRate <= (z == 1 ? 0.12 : 0.06)) return z - 1;
        return -1;
    }

    void Mark(string m) { if (!Marks.ContainsKey(m)) Marks[m] = t; }

    void Heal(double target)
    {
        int missing = MaxHp - realHp;
        if (missing <= 0 || Sf >= target) return;
        while (kit > 0 && missing > 0) { int h = Math.Min(15, missing); realHp += h; missing -= h; kit--; }
        if (missing <= 0) return;
        int cost = Sim.HealCost(missing);
        if (cost <= Gold) { Gold -= cost; FoodSpent += cost; realHp = MaxHp; return; }
        long units = Gold / 8; // sin plata para el /heal completo: compra mates con lo que tiene
        long healed = Math.Min(missing, units * 15);
        Gold -= units * 8; FoodSpent += units * 8; realHp += (int)healed;
    }

    // Sin vida y sin oro: lo que haría una persona, vender lo que juntó para comprar comida.
    void SellStock()
    {
        foreach (var kv in inv.ToList())
            if (World.Sell.TryGetValue(kv.Key, out int price) && kv.Value > 0) { Gold += (long)price * kv.Value; inv[kv.Key] = 0; }
    }

    void Win(MonRow m, int mult)
    {
        long g = (rng.Next(5, 16) + (2 * Level) + m.GoldB) * mult;
        long x = (rng.Next(8, 21) + Level + m.XpB) * mult;
        Gold += g; GoldEarned += g; xp += x;
        while (xp >= LevelingCalculator.RequiredXpForLevel(Level))
        {
            xp -= LevelingCalculator.RequiredXpForLevel(Level);
            Level++; realHp = MaxHp;
            if (Level is 5 or 10 or 15 or 20 or 25) Mark($"L{Level}");
        }
    }

    void Die() { Deaths++; xp = 0; Gold -= Gold * DeathPenalty.GoldPercent / 100; realHp = 0; }

    bool Resolve(Cell cell, MonRow[] pool, int mult, double dropChance, bool canDrop)
    {
        int i = rng.Next(cell.Died.Length);
        long foodGold = Math.Min(cell.Food[i], Gold);
        Gold -= foodGold; FoodSpent += foodGold;
        if (cell.Died[i]) { Die(); return false; }
        realHp = Math.Max(1, realHp - (int)Math.Round(cell.Lost[i] * MaxHp));
        var m = pool[rng.Next(pool.Length)];
        Win(m, mult);
        if (canDrop && rng.NextDouble() * 100 < dropChance && m.DropNames.Length > 0) inv[m.DropNames[0]] = inv.GetValueOrDefault(m.DropNames[0]) + 1;
        return true;
    }

    void Gather(string type)
    {
        string rarity = RarityCatalog.RollGatheringRarity();
        if (!World.GatherPool.TryGetValue($"{type}|{rarity}", out var pool) || pool.Count == 0) return;
        string name = pool[rng.Next(pool.Count)];
        inv[name] = inv.GetValueOrDefault(name) + GatheringYield.Roll(rarity);
    }

    int EffectiveWeapon(int stat, bool cw) => stat;   // desde la v0.27.0 el ×1,5 de la sinergia ya está en el ATQ del arma de clase (items.stat_value)

    bool CanMake(Recipe r) => Gold >= r.Gold && r.Ingredients.All(kv => inv.GetValueOrDefault(kv.Key) >= kv.Value);

    void Make(Recipe r)
    {
        foreach (var kv in r.Ingredients) inv[kv.Key] -= kv.Value;
        Gold -= r.Gold; RecipeGold += r.Gold;
    }

    void TryCraft()
    {
        int top = MaxUnlocked();
        for (int zi = top - 1; zi >= 0; zi--)
        {
            var cw = World.ClassWeapon(cls, zi);
            var gw = World.GeneralWeapon(zi);
            int current = weaponStat == 0 ? 0 : EffectiveWeapon(weaponStat, weaponClass);

            if (EffectiveWeapon(cw.Stat, true) > current && CanMake(cw))
            {
                Gold += weaponSell; Make(cw);
                weaponStat = cw.Stat; weaponClass = true; weaponSell = cw.Sell; weaponDone[zi] = true;
                Mark($"W{zi + 1}");
            }
            else if (bridge && EffectiveWeapon(gw.Stat, false) > current && CanMake(gw))
            {
                Gold += weaponSell; Make(gw);
                weaponStat = gw.Stat; weaponClass = false; weaponSell = gw.Sell;
                Mark($"G{zi + 1}");
            }

            var am = World.Amulet(zi);
            if (am.Stat > amuletStat && CanMake(am))
            {
                Make(am); amuletStat = am.Stat; amuletDone[zi] = true; Mark($"A{zi + 1}");
            }
        }
    }

    void TryBoss()
    {
        int b = Highest;
        if (b >= 5) return;
        if (b + 1 < 5 && Level < World.Zones[b + 1].MinLevel) return;
        if (MaxUnlocked() < b + 1) return;
        Heal(1.0);
        var est = Get(b, 2, Sf);
        if (est.DieRate > 0.40) return;
        BossTries[b]++;
        if (!Resolve(est, World.Boss[b], CombatRewardCalculator.BossRewardMultiplier, 0, false)) { bossReady = t + 30; return; }
        Highest = b + 1;
        Mark($"B{Highest}");
        bossReady = t + 300;
    }

    public void Run()
    {
        for (t = 0; t < cap; t++)
        {
            EndMinute = t;
            if (t % 5 == 0) { Gather("Madera"); Gather("Mineral"); }
            Heal(0.8);
            int zi = ChooseZone();
            if (zi < 0)
            {
                RestMinutes++;
                if (Gold < 60) SellStock();
                Heal(0.8);
            }
            else
            {
                Resolve(Get(zi, 0, Sf), World.Hunt[zi], 1, Cfg.Hunt, true);
                if (t % 30 == 15)
                {
                    Heal(1.0);
                    int tz = ChooseZoneForTravel(zi);
                    if (tz >= 0) Resolve(Get(tz, 1, Sf), World.Travel[tz], CombatRewardCalculator.TravelRewardMultiplier, Cfg.Travel, true);
                }

                if (t >= bossReady) TryBoss();
            }

            if (t % 3 == 0) TryCraft();
            if (Highest >= 5 && Marks.ContainsKey("W5") && Marks.ContainsKey("A5")) { Mark("DONE"); break; }
        }
    }

    // el élite solo se enfrenta si la chance de perder es baja (un viaje perdido cuesta XP y oro)
    int ChooseZoneForTravel(int zi) => Get(zi, 1, Sf).DieRate <= 0.08 ? zi : -1;
}

// ======================= PARTE D: escenarios de rebalanceo del crafteo =======================
public static class Cfg { public static double Hunt = CombatRewardCalculator.HuntDropChancePercent; public static double Travel = CombatRewardCalculator.TravelDropChancePercent; }

public static class PartD
{
    public static void Run(string[] args)
    {
        string scenario = args.Length > 1 ? args[1] : "base";
        int runs = args.Length > 2 ? int.Parse(args[2]) : 40;
        Apply(scenario);

        var results = new Dictionary<string, List<Journey>>();
        foreach (bool complete in new[] { true, false })
        {
            var all = new List<Journey>();
            foreach (var cls in World.Classes)
                for (int r = 0; r < runs; r++)
                {
                    var j = new Journey(cls, false, complete, new Random(5000 + r), 700 * 60);
                    j.Run();
                    all.Add(j);
                }

            results[complete ? "set" : "libre"] = all;
        }

        string Line(string mode)
        {
            var l = results[mode];
            double M(string mark, double p) { var v = l.Select(j => j.Marks.TryGetValue(mark, out int t) ? (double?)t / 60.0 : null).Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToList(); return v.Count == 0 ? double.NaN : v[Math.Clamp((int)Math.Round(p * (v.Count - 1)), 0, v.Count - 1)]; }
            string sets = string.Join(" ", new[] { "A1", "A2", "A3", "A4", "A5" }.Select(m => $"{M(m, 0.5),5:F1}"));
            return $"{mode,-5} FIN mediana {M("DONE", 0.5),5:F1} p10 {M("DONE", 0.1),5:F1} p90 {M("DONE", 0.9),5:F1} | arma Z5 mediana {M("W5", 0.5),5:F1} p90 {M("W5", 0.9),5:F1} | amuleto Z1..Z5 (mediana): {sets} | jefe5 {M("B5", 0.5),5:F1}";
        }

        Console.WriteLine($"[{scenario}] caza {Cfg.Hunt}% viaje {Cfg.Travel}%");
        Console.WriteLine("  " + Line("set"));
        Console.WriteLine("  " + Line("libre"));
    }

    // Cambia recetas y chances SOLO en memoria, para medir qué mueve cada palanca.
    static void Apply(string scenario)
    {
        var parts = scenario.Split('+');
        foreach (var p in parts)
        {
            switch (p)
            {
                case "base": break;
                case "t50": Cfg.Travel = 50; break;
                case "t60": Cfg.Travel = 60; break;
                case "h8": Cfg.Hunt = 8; break;
                case "need": // el drop de viaje que piden las recetas: arma de clase 3→2, amuleto 4→3
                    for (int zi = 1; zi < 5; zi++)
                    {
                        string drop = World.Travel[zi][0].DropNames[0];
                        foreach (var r in World.Recipes.Where(r => r.Zi == zi && r.Ingredients.ContainsKey(drop)))
                            if ((r.Type == "Weapon" && r.Family is not null) || r.Type == "Amulet") r.Ingredients[drop] = Math.Max(1, r.Ingredients[drop] - 1);
                    }

                    break;
                case "nomythic": // sin loterías de 0,5 %: el material Mítico de las armas de clase de la Zona 5 pasa a una Gema de Zafiro (Legendario)
                    foreach (var r in World.Recipes.Where(r => r.Zi == 4 && r.Type == "Weapon" && r.Family is not null))
                    {
                        var mythic = r.Ingredients.Keys.Where(k => World.MythicNames.Contains(k) && World.GatherPool.Values.Any(v => v.Contains(k))).ToList();
                        foreach (var m in mythic) { r.Ingredients.Remove(m); r.Ingredients["Gema de Zafiro"] = 1; }
                    }

                    break;
                case "amu2": // amuleto: 3 drops × 4 → 3 × 3 (y el de viaje, 3)
                    foreach (var r in World.Recipes.Where(r => r.Type == "Amulet" && r.Zi >= 1))
                        foreach (var k in r.Ingredients.Keys.ToList()) if (r.Ingredients[k] == 4 && !World.GatherPool.Values.Any(v => v.Contains(k))) r.Ingredients[k] = 3;
                    break;
                default: throw new ArgumentException(p);
            }
        }
    }
}

// ======================= PARTE E: examen del jefe según nivel, y barrido del Arquero =======================
public static class PartE
{
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== EXAMEN DEL JEFE según el nivel (AbilityHeal, % de perder): equipo SIN / ANTERIOR / PROPIO — promedio de las 4 clases ===");
        foreach (int zi in new[] { 1, 2, 3, 4 })
        {
            sb.AppendLine($"Jefe Z{zi + 1} (nivel mínimo de la zona {World.Zones[zi].MinLevel}):");
            sb.Append("  nivel   : "); for (int off = 0; off <= 16; off += 2) sb.Append($"{World.Zones[zi].MinLevel + off,6}"); sb.AppendLine();
            foreach (var sc in new[] { "NG", "PREV", "OWN" })
            {
                sb.Append($"  {sc,-8}: ");
                for (int off = 0; off <= 16; off += 2)
                {
                    double sum = 0;
                    foreach (var cls in World.Classes)
                    {
                        var (w, cw, a) = Gear(cls, zi, sc);
                        sum += Sim.Compute(cls, World.Zones[zi].MinLevel + off, w, cw, a, World.Boss[zi], Policy.AbilityHeal, 4, 1.0, true, 500).DieRate;
                    }

                    sb.Append($"{100 * sum / 4,6:F0}");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("\n=== ARQUERO: qué mueve un factor de vida PvE mayor (extra sobre el ×1,15 actual), promedio zonas 2-5, niveles mín+0/3/6/10, AbilityHeal ===");
        sb.AppendLine("factor total | jefe PROPIO | jefe ANTERIOR | élite ANTERIOR | común SIN EQUIPO   (referencia Ninja / Guerrero / Hechicero abajo)");
        foreach (double extra in new[] { 1.0, 1.0435, 1.0870, 1.1304 })
        {
            Sim.ExtraHp["Arquero"] = extra; Sim.Profiles.Clear();
            sb.AppendLine($"  x{1.15 * extra,4:F2}     | {Avg("Arquero", "OWN", 2):F1} | {Avg("Arquero", "PREV", 2),13:F1} | {Avg("Arquero", "PREV", 1),14:F1} | {Avg("Arquero", "NG", 0),16:F1}");
        }

        Sim.ExtraHp.Clear(); Sim.Profiles.Clear();
        sb.AppendLine("\n=== NINJA: factor de vida PvE total (actual ×0,95) — mismas columnas ===");
        foreach (double extra in new[] { 1.0, 0.9474, 0.8947 })
        {
            Sim.ExtraHp["Ninja"] = extra; Sim.Profiles.Clear();
            sb.AppendLine($"  x{0.95 * extra,4:F2}     | {Avg("Ninja", "OWN", 2):F1} | {Avg("Ninja", "PREV", 2),13:F1} | {Avg("Ninja", "PREV", 1),14:F1} | {Avg("Ninja", "NG", 0),16:F1}");
        }

        Sim.ExtraHp.Clear(); Sim.Profiles.Clear();
        foreach (var cls in new[] { "Ninja", "Guerrero", "Hechicero" })
            sb.AppendLine($"  {cls,-9} | {Avg(cls, "OWN", 2):F1} | {Avg(cls, "PREV", 2),13:F1} | {Avg(cls, "PREV", 1),14:F1} | {Avg(cls, "NG", 0),16:F1}");
        Console.WriteLine(sb.ToString());
    }

    static (int W, bool CW, int A) Gear(string cls, int zi, string code) => code switch
    {
        "NG" => (0, false, 0),
        "PREV" => (World.ClassWeapon(cls, zi - 1).Stat, true, World.Amulet(zi - 1).Stat),
        _ => (World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat),
    };

    // kind 0 común, 1 élite, 2 jefe
    static double Avg(string cls, string code, int kind)
    {
        double sum = 0; int n = 0;
        for (int zi = 1; zi < 5; zi++)
            foreach (int off in new[] { 0, 3, 6, 10 })
            {
                var (w, cw, a) = Gear(cls, zi, code);
                var pool = kind == 0 ? World.Hunt[zi] : kind == 1 ? World.Travel[zi] : World.Boss[zi];
                sum += Sim.Compute(cls, World.Zones[zi].MinLevel + off, w, cw, a, pool, Policy.AbilityHeal, 4, 1.0, kind > 0, 600).DieRate;
                n++;
            }

        return 100 * sum / n;
    }
}

// ======================= PARTE F: amuletos más fuertes =======================
public static class PartF
{
    static readonly int[] Current = [10, 18, 30, 46, 75];
    static int[] Neu = [10, 18, 30, 46, 75];

    static (int W, bool CW, int A) Gear(string cls, int zi, string code) => code switch
    {
        "NG" => (0, false, 0),
        "PREV" => (World.ClassWeapon(cls, zi - 1).Stat, true, Neu[zi - 1]),
        "OWNW" => (World.ClassWeapon(cls, zi).Stat, true, 0),
        _ => (World.ClassWeapon(cls, zi).Stat, true, Neu[zi]),
    };

    // promedio entre las 4 clases y niveles mín+3/6/10
    static (double Die, double Lost, double Food) Avg(int zi, string code, int kind, Policy pol)
    {
        double die = 0, lost = 0, food = 0; int n = 0;
        foreach (var cls in World.Classes)
            foreach (int off in new[] { 3, 6, 10 })
            {
                var (w, cw, a) = Gear(cls, zi, code);
                var pool = kind == 0 ? World.Hunt[zi] : kind == 1 ? World.Travel[zi] : World.Boss[zi];
                var c = Sim.Compute(cls, World.Zones[zi].MinLevel + off, w, cw, a, pool, pol, 4, 1.0, kind > 0, 500);
                die += c.DieRate; lost += c.MeanLost; food += c.MeanFood; n++;
            }

        return (100 * die / n, 100 * lost / n, food / n);
    }

    public static void Run(string[] args)
    {
        double target = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3.0;
        var sb = new StringBuilder();
        sb.AppendLine("=== HOY: vida que cuesta una cacería COMÚN con equipo propio, ataque básico (como /autohunt), % de la vida máxima — promedio de 4 clases y niveles mín+3/6/10 ===");
        for (int zi = 0; zi < 5; zi++)
        {
            var a = Avg(zi, "OWN", 0, Policy.AttackOnly);
            sb.AppendLine($"  Z{zi + 1}: amuleto {Neu[zi],3} → cuesta {a.Lost,5:F1} % · pierde {a.Die,4:F1} %");
        }

        sb.AppendLine($"\n=== AMULETO NECESARIO para que una común cueste ≤ {target} % (mismo cálculo) ===");
        for (int zi = 1; zi < 5; zi++)
        {
            int lo = Current[zi], hi = Current[zi] * 5;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                Neu[zi] = mid;
                if (Avg(zi, "OWN", 0, Policy.AttackOnly).Lost <= target) hi = mid; else lo = mid + 1;
            }

            Neu[zi] = lo;
            sb.AppendLine($"  Z{zi + 1}: {Current[zi]} → {Neu[zi]}  (x{(double)Neu[zi] / Current[zi]:F2})");
        }

        sb.AppendLine("\n=== CON LOS AMULETOS NUEVOS (AbilityHeal salvo la primera columna): común OWN (ataque básico) | común PREV | élite OWN | élite PREV | JEFE OWN | JEFE PREV | JEFE SIN EQUIPO — % de perder / vida que cuesta ===");
        foreach (bool neu in new[] { false, true })
        {
            var save = (int[])Neu.Clone();
            if (!neu) Array.Copy(Current, Neu, 5);
            sb.AppendLine(neu ? "NUEVOS" : "ACTUALES");
            for (int zi = 1; zi < 5; zi++)
            {
                var c0 = Avg(zi, "OWN", 0, Policy.AttackOnly);
                var c1 = Avg(zi, "PREV", 0, Policy.AttackOnly);
                var t0 = Avg(zi, "OWN", 1, Policy.AbilityHeal);
                var t1 = Avg(zi, "PREV", 1, Policy.AbilityHeal);
                var b0 = Avg(zi, "OWN", 2, Policy.AbilityHeal);
                var b1 = Avg(zi, "PREV", 2, Policy.AbilityHeal);
                var b2 = Avg(zi, "NG", 2, Policy.AbilityHeal);
                sb.AppendLine($"  Z{zi + 1} amuleto {Neu[zi],3}: común OWN {c0.Lost,4:F1}% | común PREV {c1.Lost,4:F1}% (pierde {c1.Die,3:F1}) | élite OWN {t0.Lost,4:F1}% | élite PREV {t1.Lost,4:F1}% | JEFE OWN {b0.Die,4:F1}% | JEFE PREV {b1.Die,4:F1}% | JEFE NG {b2.Die,4:F1}%");
            }

            Array.Copy(save, Neu, 5);
        }

        Console.WriteLine(sb.ToString());
    }
}

// ======================= PARTE G: amuletos nuevos + daño de jefe compensado =======================
public static class PartG
{
    static readonly int[] Old = [10, 18, 30, 46, 75];

    static MonRow Shift(MonRow m, int d) => new() { Name = m.Name, Kind = m.Kind, ZoneId = m.ZoneId, MinHp = m.MinHp, MaxHp = m.MaxHp, MinDmg = m.MinDmg + d, MaxDmg = m.MaxDmg + d, GoldB = m.GoldB, XpB = m.XpB, Drops = m.Drops };

    // modo: "old" (hoy), "none" (amuletos nuevos, jefes sin tocar), "own" (jefe + Δ de su zona), "mid" (jefe + la mitad entre Δ de su zona y Δ de la anterior)
    static (double Die, double Lost) Run1(string cls, int zi, int[] am, string mode, string code, int kind, Policy pol, int off)
    {
        int[] delta = am.Select((a, i) => a - Old[i]).ToArray();
        int d = mode switch { "own" => delta[zi], "mid" => zi == 0 ? delta[zi] : (delta[zi] + delta[zi - 1]) / 2, _ => 0 };
        var pool = kind == 0 ? World.Hunt[zi] : kind == 1 ? World.Travel[zi] : World.Boss[zi].Select(b => Shift(b, d)).ToArray();
        int amulet = code switch { "NG" => 0, "PREV" => am[zi - 1], _ => am[zi] };
        int weapon = code switch { "NG" => 0, "PREV" => World.ClassWeapon(cls, zi - 1).Stat, _ => World.ClassWeapon(cls, zi).Stat };
        var c = Sim.Compute(cls, World.Zones[zi].MinLevel + off, weapon, code != "NG", amulet, pool, pol, 4, 1.0, kind > 0, 500);
        return (c.DieRate, c.MeanLost);
    }

    static (double Die, double Lost) Mean(int zi, int[] am, string mode, string code, int kind, Policy pol)
    {
        double die = 0, lost = 0; int n = 0;
        foreach (var cls in World.Classes)
            foreach (int off in new[] { 3, 6, 10 }) { var r = Run1(cls, zi, am, mode, code, kind, pol, off); die += r.Die; lost += r.Lost; n++; }
        return (100 * die / n, 100 * lost / n);
    }

    public static void Run(string[] args)
    {
        int[] am = (args.Length > 1 ? args[1] : "10,35,60,85,120").Split(',').Select(int.Parse).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine($"Amuletos {string.Join('/', Old)} → {string.Join('/', am)}  (Δ {string.Join('/', am.Select((a, i) => a - Old[i]))})");
        sb.AppendLine("Zona | modo jefe | común OWN (ataque básico) % vida | élite OWN % vida | JEFE OWN % perder | JEFE PREV % perder | JEFE SIN EQUIPO % perder | jefe OWN: vida que cuesta %");
        for (int zi = 1; zi < 5; zi++)
            foreach (var mode in new[] { "old", "none", "own", "mid" })
            {
                int[] use = mode == "old" ? Old : am;
                var c0 = Mean(zi, use, mode, "OWN", 0, Policy.AttackOnly);
                var t0 = Mean(zi, use, mode, "OWN", 1, Policy.AbilityHeal);
                var b0 = Mean(zi, use, mode, "OWN", 2, Policy.AbilityHeal);
                var b1 = Mean(zi, use, mode, "PREV", 2, Policy.AbilityHeal);
                var b2 = Mean(zi, use, mode, "NG", 2, Policy.AbilityHeal);
                sb.AppendLine($"Z{zi + 1}   | {mode,-9} | {c0.Lost,5:F1} | {t0.Lost,5:F1} | {b0.Die,5:F1} | {b1.Die,5:F1} | {b2.Die,5:F1} | {b0.Lost,5:F1}");
            }

        Console.WriteLine(sb.ToString());
    }
}

// ======================= PARTE H: el Asador con el equipo del Fogón =======================
public static class PartH
{
    public static void Run(string[] args)
    {
        int brasa = args.Length > 1 ? int.Parse(args[1]) : 165, shift = args.Length > 2 ? int.Parse(args[2]) : 45, z5amulet = args.Length > 3 ? int.Parse(args[3]) : 120;
        var asOld = new MonRow { Name = "Asador", Kind = "boss", MinHp = 2002, MaxHp = 2401, MinDmg = 188, MaxDmg = 244 };
        var asNew = new MonRow { Name = "Asador", Kind = "boss", MinHp = 2002, MaxHp = 2401, MinDmg = 188 + shift, MaxDmg = 244 + shift };
        var sb = new StringBuilder("Asador (nivel 28, AbilityHeal, % de perder; promedio de 4 clases)\n");
        foreach (var (label, boss, fogA, z5A) in new[] { ("HOY (Brasa 120, amuleto Z5 75, Asador 188-244)", asOld, 120, 75), ($"NUEVO (Brasa {brasa}, amuleto Z5 {z5amulet}, Asador {188 + shift}-{244 + shift})", asNew, brasa, z5amulet) })
        {
            double fog = 0, z5 = 0, fogNoAm = 0;
            foreach (var cls in World.Classes)
            {
                fog += Sim.Compute(cls, 28, 128, false, fogA, [boss], Policy.AbilityHeal, 4, 1.0, true, 800).DieRate;
                z5 += Sim.Compute(cls, 28, World.ClassWeapon(cls, 4).Stat, true, z5A, [boss], Policy.AbilityHeal, 4, 1.0, true, 800).DieRate;
                fogNoAm += Sim.Compute(cls, 28, 128, false, 0, [boss], Policy.AbilityHeal, 4, 1.0, true, 800).DieRate;
            }

            sb.AppendLine($"  {label}: equipo del Fogón {100 * fog / 4:F1} % | equipo de Zona 5 (clase + amuleto) {100 * z5 / 4:F1} % | Trinche sin amuleto {100 * fogNoAm / 4:F1} %");
        }

        Console.WriteLine(sb.ToString());
    }
}

// ======================= PARTE I: más vida a TODOS los jefes (v0.17.1) =======================
public static class PartI
{
    static MonRow Scaled(MonRow m, double f) => new()
    {
        Name = m.Name, Kind = m.Kind, ZoneId = m.ZoneId, MinHp = (int)Math.Round(m.MinHp * f), MaxHp = (int)Math.Round(m.MaxHp * f),
        MinDmg = m.MinDmg, MaxDmg = m.MaxDmg, GoldB = m.GoldB, XpB = m.XpB, Drops = m.Drops,
    };

    public static MonRow? Asador;

    // (arma de clase propia, amuleto propio) de la zona zi; fogon = el par del Fogón (sin sinergia de clase).
    static (int W, bool CW, int A) Own(string cls, int zi) => (World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat);

    static (double Die, double Lost, double Turns) SoloAvg(MonRow boss, int level, Func<string, (int W, bool CW, int A)> gear, int n)
    {
        double die = 0, lost = 0, turns = 0;
        foreach (var cls in World.Classes)
        {
            var (w, cw, a) = gear(cls);
            var cell = Sim.Compute(cls, level, w, cw, a, [boss], Policy.AbilityHeal, tier: 4, startFrac: 1.0, limited: true, n);
            die += cell.DieRate; lost += cell.MeanLost; turns += cell.MeanTurns;
        }

        return (die / 4, lost / 4, turns / 4);
    }

    // heal = cada jugador puede comer UNA vez en la pelea (la misma regla del jefe solo: con 40 % de vida o menos, la comida más grande que no se desperdicie; cuesta el turno). El raid de hoy NO tiene curación;
    // esta variante es para medir qué pasaría si el jefe en equipo también dejara comer una vez por jugador.
    // banquet: 0 = comidas comunes (la más grande que no se desperdicie); N > 0 = un banquete que cura N y da +15 % de ataque desde que se come (con 40 % de vida o menos); -1 = banquete «solo buff»: se come en el primer turno, no cura y da +15 % de ataque.
    internal static (bool Win, int Alive) Raid(int size, int zi, int level, MonRow boss, bool heal = false, int banquet = 0)
    {
        var rng = Sim.Rng;
        int baseHp = rng.Next(boss.MinHp, boss.MaxHp + 1);
        int bossHp = RaidDifficulty.BossHp(baseHp, size, zi + 1);
        int bossDmg = RaidDifficulty.BossDamage(rng.Next(boss.MinDmg, boss.MaxDmg + 1), zi + 1);
        var cls = Enumerable.Range(0, size).Select(i => World.Classes[i % 4]).ToArray();
        var cps = new CombatantProfile[size]; var hp = new int[size]; var max = new int[size]; var st = new AbilityState[size]; var alive = Enumerable.Repeat(true, size).ToArray();
        for (int i = 0; i < size; i++)
        {
            var (w, cw, a) = Own(cls[i], zi);
            var prof = Sim.Profile(cls[i], level, w, cw, a);
            cps[i] = new CombatantProfile(prof.Damage, prof.Defense, cls[i], prof.Passives); max[i] = prof.CombatMaxHp; hp[i] = max[i];
        }

        // banquet == -2: el banquete se comió ANTES de la pelea (+15 % de ataque desde el primer turno, sin gastar turno) y en la pelea se come una comida común.
        if (banquet == -2)
        {
            for (int i = 0; i < size; i++) cps[i] = cps[i] with { Damage = AttackBuff.Apply(cps[i].Damage, 15) };
            banquet = 0;
        }

        var healsLeft = Enumerable.Repeat(heal ? 1 : 0, size).ToArray();
        for (int round = 0; round < 3000; round++)
        {
            for (int i = 0; i < size; i++)
            {
                if (!alive[i]) continue;
                if (healsLeft[i] > 0 && (banquet == -1 || hp[i] * 100 <= 40 * max[i]))
                {
                    int missing = max[i] - hp[i], pick = 0;
                    for (int f = 0; f < Sim.Foods.Length; f++) if (Sim.Foods[f].Heal <= missing * 1.25) pick = f;
                    int healAmount = banquet > 0 ? banquet : banquet == -1 ? 0 : Sim.Foods[pick].Heal;
                    if (banquet != 0 || Sim.Foods[pick].Heal * 1.25 >= bossDmg)
                    {
                        hp[i] = Math.Min(max[i], hp[i] + healAmount);
                        if (banquet != 0) cps[i] = cps[i] with { Damage = AttackBuff.Apply(cps[i].Damage, 15) };
                        var counter = CombatTurnResolver.ResolveCounterTurn(cps[i], st[i], bossDmg);
                        st[i] = counter.Status;
                        hp[i] -= counter.Hit.Damage;
                        healsLeft[i]--;
                        if (hp[i] <= 0) alive[i] = false;
                        continue;
                    }
                }

                bool ability = CombatTurnResolver.CheckAbility(cls[i], st[i]) == AbilityAvailability.Ready;
                var r = CombatTurnResolver.ResolveTurn(cps[i], st[i], ability ? PlayerAction.Ability : PlayerAction.Attack, hp[i], max[i], bossHp, bossDmg);
                hp[i] = r.PlayerHpAfter; bossHp = r.MonsterHpAfter; st[i] = r.StatusAfter;
                if (bossHp <= 0) return (true, alive.Count(x => x));
                if (hp[i] <= 0) alive[i] = false;
            }

            if (!alive.Any(x => x)) return (false, 0);
        }

        return (false, 0);
    }

    public static void Run(string[] args)
    {
        double[] fs = args.Length > 1 ? args[1].Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : [1.0, 1.25, 1.5, 2.0];
        const int N = 500;
        string P(double v) => $"{100 * v,5:F1}";

        int[] offs = args.Length > 2 ? args[2].Split(',').Select(int.Parse).ToArray() : [3];
        foreach (int off in offs)
        {
        Console.WriteLine($"=== A) EXAMEN con el equipo propio de la zona, nivel = mínimo + {off}: % de PERDER / % de vida que cuesta / turnos — promedio de las 4 clases, con comida (una vez) y habilidad ===");
        Console.WriteLine("zona (jefe)                    |" + string.Join("|", fs.Select(f => $" HP x{f,-4:F2}              ")));
        for (int zi = 0; zi < World.Zones.Count; zi++)
        {
            var boss = World.Boss[zi][0]; int level = World.Zones[zi].MinLevel + off; var row = $"{World.Zones[zi].Name,-14} ({boss.Name,-14}) |";
            foreach (double f in fs)
            {
                var (d, l, t) = SoloAvg(Scaled(boss, f), level, c => Own(c, zi), N);
                row += $" {P(d)}% / {P(l)}% / {t,4:F1}  |";
            }

            Console.WriteLine(row);
        }

        if (Asador is not null)
        {
            var row = $"{"Fogón",-14} ({Asador.Name,-14}) |";
            foreach (double f in fs) { var (d, l, t) = SoloAvg(Scaled(Asador, f), 25 + off, _ => (128, false, 165), N); row += $" {P(d)}% / {P(l)}% / {t,4:F1}  |"; }
            Console.WriteLine(row + $"  (nivel {25 + off}, Trinche + Brasa)");
        }
        Console.WriteLine();
        }
        if (args.Length > 3) return;

        Console.WriteLine("\n=== B) El jefe de la ZONA 5 y el ASADOR con MUCHO más nivel que el de la zona (equipo propio): % de PERDER / % de vida que cuesta / turnos ===");
        foreach (int level in new[] { 23, 30, 40, 50, 60 })
        {
            var row = $"nivel {level,2} vs {World.Boss[4][0].Name,-22} |";
            foreach (double f in fs) { var (d, l, t) = SoloAvg(Scaled(World.Boss[4][0], f), level, c => Own(c, 4), N); row += $" {P(d)}% / {P(l)}% / {t,4:F1}  |"; }
            Console.WriteLine(row);
        }

        if (Asador is not null)
        {
            foreach (int level in new[] { 28, 40, 50, 60 })
            {
                var row = $"nivel {level,2} vs {Asador.Name,-22} |";
                foreach (double f in fs) { var (d, l, t) = SoloAvg(Scaled(Asador, f), level, _ => (128, false, 165), N); row += $" {P(d)}% / {P(l)}% / {t,4:F1}  |"; }
                Console.WriteLine(row);
            }
        }

        Console.WriteLine("\n=== C) RAID (sin comida, cada uno con su vida, equipo propio de la zona, nivel = mínimo + 3): % de VICTORIA por cantidad de jugadores (promedio de las zonas 2 a 5; la zona 1 aparte) ===");
        Console.WriteLine("jugadores |" + string.Join("|", fs.Select(f => $" HP x{f,-4:F2} z1 / z2-5 ")));
        for (int size = 1; size <= 4; size++)
        {
            var row = $"    {size}     |";
            foreach (double f in fs)
            {
                double WinRate(int zi) { int wins = 0; for (int i = 0; i < 400; i++) if (Raid(size, zi, World.Zones[zi].MinLevel + 3, Scaled(World.Boss[zi][0], f)).Win) wins++; return wins / 400.0; }
                double z1 = WinRate(0); double rest = Enumerable.Range(1, 4).Average(WinRate);
                row += $"   {P(z1)}% / {P(rest)}%   |";
            }

            Console.WriteLine(row);
        }
    }
}
