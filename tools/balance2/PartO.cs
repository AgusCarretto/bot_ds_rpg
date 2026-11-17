using BotDsRpg.GameData;

// Parte O (v0.57.0): la Aventura (docs/aventura.md). Cuántas corridas llegan al final, por zona y clase, con el arma de clase y el amuleto de la zona,
// SIN comida, con la vida que se arrastra de pelea en pelea. Dos estrategias: siempre segura (4 comunes y el Guardián) y siempre arriesgada
// (4 élites y el Guardián). Los monstruos y el Guardián salen de AdventureRules (los mismos multiplicadores del juego; v0.59.0: el daño del común ×1,56 y del élite ×1,69, la vida ×1,3).
// Uso: dotnet run --project tools/balance2 -- O [corridas por celda] [niveles sobre el mínimo de la zona]
public static class PartO
{
    private static double Monster = AdventureRules.MonsterMultiplier, Guardian = AdventureRules.GuardianMultiplier, GuardianDamage = AdventureRules.GuardianDamageMultiplier;

    public static void Run(string[] args)
    {
        int runs = args.Length > 1 && int.TryParse(args[1], out int r) ? r : 2000;
        int extra = args.Length > 2 && int.TryParse(args[2], out int e) ? e : 0;
        // Para probar otros números sin tocar el juego: [3] multiplicador de comunes y élites, [4] del Guardián.
        Monster = args.Length > 3 && double.TryParse(args[3], System.Globalization.CultureInfo.InvariantCulture, out double mm) ? mm : AdventureRules.MonsterMultiplier;
        Guardian = args.Length > 4 && double.TryParse(args[4], System.Globalization.CultureInfo.InvariantCulture, out double gm) ? gm : AdventureRules.GuardianMultiplier;
        // [5] el daño del Guardián (v0.57.2 es aparte de su vida: ×1,6 × 1,25 = ×2,0).
        GuardianDamage = args.Length > 5 && double.TryParse(args[5], System.Globalization.CultureInfo.InvariantCulture, out double gd) ? gd : AdventureRules.GuardianDamageMultiplier;
        Console.WriteLine($"Aventura: % de corridas que llegan al final (sin comida). Nivel = mínimo de la zona + {extra}. {runs} corridas por celda.");
        Console.WriteLine($"Comunes y élites ×{Monster} de vida; daño ×{Monster * AdventureRules.SafeDamageMultiplier / AdventureRules.MonsterMultiplier:0.##} el común y ×{Monster * AdventureRules.RiskyDamageMultiplier / AdventureRules.MonsterMultiplier:0.##} el élite; Guardián = élite ×{Guardian} de vida y ×{GuardianDamage} de daño.\n");
        Console.WriteLine($"{"Zona",-28} {"Nivel",5} " + string.Join(" ", World.Classes.Select(c => $"{c[..4],6}S {c[..4],6}A")));

        for (int zi = 0; zi < World.Zones.Count; zi++)
        {
            if (World.Hunt[zi].Length == 0 || World.Travel[zi].Length == 0)
            {
                continue;
            }

            int level = World.Zones[zi].MinLevel + extra;
            var cells = new List<string>();
            foreach (var cls in World.Classes)
            {
                Recipe weapon, amulet;
                try
                {
                    weapon = World.ClassWeapon(cls, zi);
                    amulet = World.Amulet(zi);
                }
                catch
                {
                    cells.Add($"{"-",7} {"-",7}");
                    continue;
                }

                double safe = Rate(cls, level, weapon.Stat, amulet.Stat, zi, risky: false, runs);
                double risky = Rate(cls, level, weapon.Stat, amulet.Stat, zi, risky: true, runs);
                cells.Add($"{safe,6:P0} {risky,7:P0}");
            }

            Console.WriteLine($"{World.Zones[zi].Name,-28} {level,5} " + string.Join(" ", cells));
        }

        Console.WriteLine("\nS = siempre seguro, A = siempre arriesgado. Lo ideal (propuesta): seguro ~60-75 % con el equipo de la zona, arriesgado ~35-50 %.");
    }

    private static MonRow Scaled(MonRow m, double k, double? damage = null) => new()
    {
        Name = m.Name, Kind = m.Kind, ZoneId = m.ZoneId,
        MinHp = (int)Math.Round(m.MinHp * k), MaxHp = (int)Math.Round(m.MaxHp * k),
        MinDmg = (int)Math.Round(m.MinDmg * (damage ?? k)), MaxDmg = (int)Math.Round(m.MaxDmg * (damage ?? k)),
    };

    private static double Rate(string cls, int level, int weaponStat, int amuletStat, int zi, bool risky, int runs)
    {
        int finished = 0;
        for (int run = 0; run < runs; run++)
        {
            double hp = 1.0;
            bool dead = false;
            for (int scene = 1; scene <= AdventureRules.Scenes && !dead; scene++)
            {
                MonRow monster = scene == AdventureRules.Scenes
                    ? Scaled(World.Travel[zi][Sim.Rng.Next(World.Travel[zi].Length)], Guardian, GuardianDamage)
                    : risky
                        ? Scaled(World.Travel[zi][Sim.Rng.Next(World.Travel[zi].Length)], Monster, Monster * (AdventureRules.RiskyDamageMultiplier / AdventureRules.MonsterMultiplier))
                        : Scaled(World.Hunt[zi][Sim.Rng.Next(World.Hunt[zi].Length)], Monster, Monster * (AdventureRules.SafeDamageMultiplier / AdventureRules.MonsterMultiplier));
                var fight = Sim.Fight(cls, level, weaponStat, true, amuletStat, monster, Policy.Ability, 0, hp, limited: true);
                dead = fight.Died;
                hp -= fight.Lost;
            }

            if (!dead)
            {
                finished++;
            }
        }

        return finished / (double)runs;
    }
}
