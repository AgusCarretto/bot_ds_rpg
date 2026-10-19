using System.Globalization;
using BotDsRpg.GameData;

// Parte J: el examen del jefe de una zona ENTRANDO con el equipo de la zona anterior (PREV: arma de clase + amuleto de la zona anterior), a distintos niveles, con la vida y el daño del jefe escalados.
//   J            -> tabla de todos los jefes con PREV al nivel mínimo / +3 / +8 / +15, para ver si el de la zona 3 es un caso aparte
//   J zi hs ds levels -> el jefe de la zona zi (índice 0..4) con factores de vida hs y de daño ds (listas separadas por coma) a los niveles dados
public static class PartJ
{
    static MonRow Scaled(MonRow m, double hp, double dmg) => new()
    {
        Name = m.Name, Kind = m.Kind, ZoneId = m.ZoneId, MinHp = (int)Math.Round(m.MinHp * hp), MaxHp = (int)Math.Round(m.MaxHp * hp),
        MinDmg = (int)Math.Round(m.MinDmg * dmg), MaxDmg = (int)Math.Round(m.MaxDmg * dmg), GoldB = m.GoldB, XpB = m.XpB, Drops = m.Drops,
    };

    static (int W, bool CW, int A) Prev(string cls, int zi) => zi == 0 ? (0, false, 0) : (World.ClassWeapon(cls, zi - 1).Stat, true, World.Amulet(zi - 1).Stat);
    static (int W, bool CW, int A) Own(string cls, int zi) => (World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat);

    static (double Die, double Lost, double Turns) Avg(MonRow boss, int level, Func<string, (int W, bool CW, int A)> gear, int n, string? only = null)
    {
        double die = 0, lost = 0, turns = 0; int k = 0;
        foreach (var cls in World.Classes)
        {
            if (only is not null && cls != only) continue;
            var (w, cw, a) = gear(cls);
            var cell = Sim.Compute(cls, level, w, cw, a, [boss], Policy.AbilityHeal, tier: 4, startFrac: 1.0, limited: true, n);
            die += cell.DieRate; lost += cell.MeanLost; turns += cell.MeanTurns; k++;
        }

        return (die / k, lost / k, turns / k);
    }

    public static void Run(string[] args)
    {
        const int N = 600;
        string P(double v) => $"{100 * v,5:F1}";
        if (args.Length <= 1)
        {
            Console.WriteLine("=== Cada jefe con el equipo de la zona ANTERIOR (arma de clase + amuleto), promedio de las 4 clases, con comida y habilidad: % de PERDER / % de vida que cuesta ===");
            Console.WriteLine("jefe                           | nivel mín      | mín+3          | mín+8          | mín+15");
            for (int zi = 1; zi < World.Zones.Count; zi++)
            {
                var boss = World.Boss[zi][0]; int min = World.Zones[zi].MinLevel;
                var row = $"Z{zi + 1} {boss.Name,-27}|";
                foreach (int off in new[] { 0, 3, 8, 15 })
                {
                    var (d, l, _) = Avg(boss, min + off, c => Prev(c, zi), N);
                    row += $" {P(d)}% / {P(l)}% |";
                }

                Console.WriteLine(row);
            }

            Console.WriteLine("\n=== Lo mismo con el equipo PROPIO de la zona (para comparar) ===");
            for (int zi = 1; zi < World.Zones.Count; zi++)
            {
                var boss = World.Boss[zi][0]; int min = World.Zones[zi].MinLevel;
                var row = $"Z{zi + 1} {boss.Name,-27}|";
                foreach (int off in new[] { 0, 3, 8, 15 })
                {
                    var (d, l, _) = Avg(boss, min + off, c => Own(c, zi), N);
                    row += $" {P(d)}% / {P(l)}% |";
                }

                Console.WriteLine(row);
            }

            return;
        }

        int z = int.Parse(args[1]);
        double[] hs = args.Length > 2 ? args[2].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray() : [1.0];
        double[] ds = args.Length > 3 ? args[3].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray() : [1.0];
        int[] levels = args.Length > 4 ? args[4].Split(',').Select(int.Parse).ToArray() : [10, 13, 18, 30, 60];
        bool own = args.Length > 5 && args[5] == "own";
        var b = World.Boss[z][0];
        Console.WriteLine($"=== {b.Name} (zona {z + 1}; vida {b.MinHp}-{b.MaxHp}, daño {b.MinDmg}-{b.MaxDmg}) con el equipo {(own ? "PROPIO" : "de la zona anterior")}: % de PERDER / % de vida que cuesta / turnos (promedio de las 4 clases) ===");
        Console.WriteLine("vida x / daño x |" + string.Join("|", levels.Select(l => $" nivel {l,-3}            ")));
        foreach (double h in hs)
            foreach (double d in ds)
            {
                var row = $"  {h,4:F2} / {d,4:F2}   |";
                foreach (int lv in levels)
                {
                    var (die, lost, t) = Avg(Scaled(b, h, d), lv, c => own ? Own(c, z) : Prev(c, z), N);
                    row += $" {P(die)}% / {P(lost)}% / {t,3:F1} |";
                }

                Console.WriteLine(row);
            }

        if (hs.Length == 1 && ds.Length == 1)
        {
            Console.WriteLine("\npor clase:");
            foreach (var cls in World.Classes)
            {
                var row = $"  {cls,-10}      |";
                foreach (int lv in levels)
                {
                    var (die, lost, t) = Avg(Scaled(b, hs[0], ds[0]), lv, c => own ? Own(c, z) : Prev(c, z), N, cls);
                    row += $" {P(die)}% / {P(lost)}% / {t,3:F1} |";
                }

                Console.WriteLine(row);
            }
        }
    }
}
