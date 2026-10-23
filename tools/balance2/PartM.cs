using System.Globalization;
using BotDsRpg.GameData;

// Parte M (v0.22.1): el jefe de una zona contra un jugador que entra SIN el equipo de esa zona (el de la zona anterior o el de dos antes), con encantamientos y comida buena, a distintos NIVELES.
// La pregunta del dueño: «un jugador pasó el jefe de la zona 5 sin nada de la zona 5, con comida buena y los dos encantados: ¿hay que subir la dificultad?»
//   M [zi] [niveles] [N]   -> zi = índice de la zona (0..4, por defecto 4 = zona 5); niveles separados por coma (por defecto mín+3, +8, +15, +25, +40).
// Cada fila es una combinación de equipo (ENTRANDO con el de la zona anterior, o con el de dos zonas antes), encantamientos (ninguno, Filo V + Guarda V) y comida (Cordero de 150, o el banquete
// Mate Dulce de 200 con +15 % de ataque). Muestra «% de PERDER / % de vida que cuesta», promedio de las 4 clases (y por clase al final).
public static class PartM
{
    static (int W, bool CW, int A) Gear(string cls, int zi, string which) => which switch
    {
        "own" => (World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat),
        "prev" => zi == 0 ? (0, false, 0) : (World.ClassWeapon(cls, zi - 1).Stat, true, World.Amulet(zi - 1).Stat),
        "prev2" => zi < 2 ? (0, false, 0) : (World.ClassWeapon(cls, zi - 2).Stat, true, World.Amulet(zi - 2).Stat),
        _ => (0, false, 0),
    };

    sealed record Row(string Label, string Gear, bool Enchanted, bool Banquet, int WeaponTier = 5, int AmuletTier = 5);

    static (double Die, double Lost) Measure(Row row, int zi, int level, MonRow boss, int n, string? only)
    {
        Sim.Enchant = row.Enchanted ? ("filo", row.WeaponTier, "guarda", row.AmuletTier) : ("filo", 0, "guarda", 0);
        Sim.AttackBuff = row.Banquet ? 15 : 0;
        // La comida más cara que se banca: el Cordero (150) o el banquete (200); el resto de la lista queda como siempre.
        var original = Sim.Foods[3];
        Sim.Foods[3] = row.Banquet ? ("Mate Dulce de la Abuela", 200, 2800) : original;
        Sim.Profiles.Clear();
        double die = 0, lost = 0; int k = 0;
        foreach (var cls in World.Classes)
        {
            if (only is not null && cls != only) continue;
            var (w, cw, a) = Gear(cls, zi, row.Gear);
            var cell = Sim.Compute(cls, level, w, cw, a, [boss], Policy.AbilityHeal, tier: 4, startFrac: 1.0, limited: true, n);
            die += cell.DieRate; lost += cell.MeanLost; k++;
        }

        Sim.Foods[3] = original;
        Sim.Enchant = ("filo", 0, "guarda", 0);
        Sim.AttackBuff = 0;
        Sim.Profiles.Clear();
        return (die / k, lost / k);
    }

    public static void Run(string[] args)
    {
        int zi = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 4;
        int min = World.Zones[zi].MinLevel;
        int[] levels = args.Length > 2 && args[2] != "-" ? args[2].Split(',').Select(int.Parse).ToArray() : [min + 3, min + 8, min + 15, min + 25, min + 40];
        int n = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1500;
        var boss = World.Boss[zi][0];
        // Factores de vida y daño del jefe para probar escalarlo (por ejemplo «1.2 1.2»): args[5] y args[6].
        double hpF = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 1.0, dmgF = args.Length > 6 ? double.Parse(args[6], CultureInfo.InvariantCulture) : 1.0;
        if (hpF != 1.0 || dmgF != 1.0)
        {
            boss = new MonRow { Name = boss.Name + $" (vida x{hpF}, daño x{dmgF})", Kind = boss.Kind, ZoneId = boss.ZoneId, MinHp = (int)Math.Round(boss.MinHp * hpF), MaxHp = (int)Math.Round(boss.MaxHp * hpF), MinDmg = (int)Math.Round(boss.MinDmg * dmgF), MaxDmg = (int)Math.Round(boss.MaxDmg * dmgF), GoldB = boss.GoldB, XpB = boss.XpB, Drops = boss.Drops };
        }

        string P(double v) => $"{100 * v,5:F1}";

        // «decomp»: cuánto aporta cada pieza encantada (zona anterior + banquete), para saber qué mueve el resultado.
        bool decomp = args.Length > 4 && args[4] == "decomp";
        Row[] rows = decomp
        ?
        [
            new("zona anterior + banquete, SIN encantar              ", "prev", false, true),
            new("zona anterior + banquete + SOLO Filo V (arma)       ", "prev", true, true, 5, 0),
            new("zona anterior + banquete + SOLO Guarda V (amuleto)  ", "prev", true, true, 0, 5),
            new("zona anterior + banquete + Filo V y Guarda V        ", "prev", true, true, 5, 5),
            new("zona anterior + banquete + Filo III y Guarda III    ", "prev", true, true, 3, 3),
        ]
        :
        [
            new("equipo de la zona (propio), sin encantar, Cordero   ", "own", false, false),
            new("equipo de la zona (propio), Filo V+Guarda V, banquete", "own", true, true),
            new("equipo de la zona ANTERIOR, sin encantar, Cordero   ", "prev", false, false),
            new("zona anterior + Filo V + Guarda V, Cordero          ", "prev", true, false),
            new("zona anterior + Filo V + Guarda V, banquete (200+15%)", "prev", true, true),
            new("de DOS zonas antes + Filo V + Guarda V, banquete    ", "prev2", true, true),
        ];

        Console.WriteLine($"=== {boss.Name} (zona {zi + 1}; vida {boss.MinHp}-{boss.MaxHp}, daño {boss.MinDmg}-{boss.MaxDmg}, nivel mín {min}): % de PERDER / % de vida que cuesta, promedio de las 4 clases ===");
        Console.WriteLine(new string(' ', 56) + "|" + string.Join("|", levels.Select(l => $" nivel {l,-3}      ")));
        foreach (var row in rows)
        {
            var line = $"{row.Label,-56}|";
            foreach (int lv in levels)
            {
                var (d, l) = Measure(row, zi, lv, boss, n, null);
                line += $" {P(d)} / {P(l)} |";
            }

            Console.WriteLine(line);
        }

        Console.WriteLine("\npor clase (zona ANTERIOR + Filo V + Guarda V + banquete):");
        foreach (var cls in World.Classes)
        {
            var line = $"  {cls,-54}|";
            foreach (int lv in levels)
            {
                var (d, l) = Measure(rows[4], zi, lv, boss, n, cls);
                line += $" {P(d)} / {P(l)} |";
            }

            Console.WriteLine(line);
        }
    }
}
