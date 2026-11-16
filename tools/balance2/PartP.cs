using BotDsRpg.GameData;
using BotDsRpg.Models;

// Parte P (v0.57.2): la matriz de duelos entre clases (DuelEngine.Simulate, el mismo motor de /fight y la Arena, sin la suerte de la Arena), para medir cómo mueve el PvP un cambio de habilidad o
// de pasiva. Cada clase con el arma de su clase y el amuleto de la zona (la DEF del amuleto es la de PvP, PvpTuning.Amulet) al nivel mínimo + 8 de las zonas 2 a 5; la habilidad se usa apenas está lista.
// Imprime el % que gana la clase de la FILA contra la de la COLUMNA y, al final, el promedio de cada clase contra las otras tres.
// Uso: dotnet run --project tools/balance2 -- P [duelos por cruce]
public static class PartP
{
    public static void Run(string[] args)
    {
        int n = args.Length > 1 && int.TryParse(args[1], out int r) ? r : 3000;
        var rng = new Random(7);
        var totals = World.Classes.ToDictionary(c => c, _ => new List<double>());
        Console.WriteLine($"Duelos: % que gana la FILA contra la COLUMNA, {n} duelos por cruce, equipo de la zona, nivel mínimo + 8.\n");

        for (int zi = 1; zi < Math.Min(5, World.Zones.Count); zi++)
        {
            int level = World.Zones[zi].MinLevel + 8;
            Console.WriteLine($"{World.Zones[zi].Name} (nivel {level})");
            Console.WriteLine($"{"",-10} " + string.Join(" ", World.Classes.Select(c => $"{c,9}")));
            foreach (var a in World.Classes)
            {
                var cells = new List<string>();
                foreach (var b in World.Classes)
                {
                    if (a == b)
                    {
                        cells.Add($"{"—",9}");
                        continue;
                    }

                    var fa = Fighter(1, a, level, zi);
                    var fb = Fighter(2, b, level, zi);
                    int wins = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (DuelEngine.Simulate(fa, fb, rng).Winner.Fighter.DiscordId == 1)
                        {
                            wins++;
                        }
                    }

                    double pct = 100.0 * wins / n;
                    totals[a].Add(pct);
                    cells.Add($"{pct,8:0}%");
                }

                Console.WriteLine($"{a,-10} " + string.Join(" ", cells));
            }

            Console.WriteLine();
        }

        Console.WriteLine("Promedio de cada clase contra las otras (todas las zonas): " +
            string.Join(" · ", World.Classes.Select(c => $"{c} {totals[c].Average():0} %")));
    }

    private static DuelFighter Fighter(ulong id, string cls, int level, int zi)
    {
        var weapon = World.ClassWeapon(cls, zi);
        var amulet = World.Amulet(zi);
        int amuletDef = PvpTuning.Amulet(new Item { Name = amulet.Result, StatValue = amulet.Stat })?.StatValue ?? amulet.Stat;
        var passives = ClassPassives.For(cls);
        int damage = CombatStats.TotalAttack(level, weapon.Stat);
        int defense = CombatStats.TotalDefense(level, amuletDef);
        int maxHp = PvpTuning.MaxHp((int)Math.Round((100 + 15 * (level - 1)) * passives.MaxHpMultiplier), cls);
        return new DuelFighter(id, cls, level, cls, new CombatantProfile(damage, defense, cls, passives), maxHp);
    }
}
