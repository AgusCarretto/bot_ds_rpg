using System.Globalization;
using BotDsRpg.GameData;

// Parte L (v0.22.0): cuánto vale cada ENCANTAMIENTO DE EFECTO (Crítico, Sangre, Vida, Esquive) contra los de siempre (Filo, Guarda), para calibrar sus magnitudes.
//   L                  -> la tabla: jefe de las zonas 2 a 5 con el equipo PROPIO a nivel mín+3 y mín+8 (con comida y habilidad, como un jugador) y las cacerías comunes de la zona (solo ataque, como /autohunt),
//                         promedio de las 4 clases, con el efecto al tier 5 en la pieza que lo lleva; cada efecto contra el mismo equipo sin encantar.
//   L t                -> lo mismo con el tier t (1..5).
//   L t clase          -> solo esa clase.
// Mide «% de PERDER el jefe / % de vida que cuesta» y «% de vida que cuesta una cacería común». Un efecto está bien calibrado si su ganancia es parecida a la del Filo o la Guarda del mismo tier.
public static class PartL
{
    static readonly string[] Cases = ["sin", "filo", "critico", "sangre", "guarda", "vida", "esquive"];

    static (string WK, int WT, string AK, int AT) Config(string name, int tier) => name switch
    {
        "filo" => ("filo", tier, "guarda", 0),
        "critico" => ("critico", tier, "guarda", 0),
        "sangre" => ("sangre", tier, "guarda", 0),
        "guarda" => ("filo", 0, "guarda", tier),
        "vida" => ("filo", 0, "vida", tier),
        "esquive" => ("filo", 0, "esquive", tier),
        _ => ("filo", 0, "guarda", 0),
    };

    static (int W, bool CW, int A) Own(string cls, int zi) => (World.ClassWeapon(cls, zi).Stat, true, World.Amulet(zi).Stat);

    // Promedio de las clases: (% de perder, % de vida perdida) contra un monstruo o un grupo, con esa configuración de encantamientos.
    static (double Die, double Lost) Measure(string name, int tier, int zi, int level, MonRow[] pool, Policy policy, bool limited, int n, string? only)
    {
        Sim.Enchant = Config(name, tier);
        Sim.Profiles.Clear();
        double die = 0, lost = 0; int k = 0;
        foreach (var cls in World.Classes)
        {
            if (only is not null && cls != only) continue;
            var (w, cw, a) = Own(cls, zi);
            var cell = Sim.Compute(cls, level, w, cw, a, pool, policy, tier: policy == Policy.AbilityHeal ? 4 : 0, startFrac: 1.0, limited, n);
            die += cell.DieRate; lost += cell.MeanLost; k++;
        }

        Sim.Enchant = ("filo", 0, "guarda", 0);
        Sim.Profiles.Clear();
        return (die / k, lost / k);
    }

    public static void Run(string[] args)
    {
        int tier = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5;
        string? only = args.Length > 2 && args[2] != "-" ? args[2] : null;

        // Pruebas de magnitudes sin recompilar: «critico=3,6,9,12,15» pisa los valores por tier de ese efecto (solo en esta corrida; los arreglos son mutables a propósito).
        foreach (var arg in args.Skip(3).Where(a => a.Contains('=')))
        {
            var parts = arg.Split('=');
            var values = parts[1].Split(',').Select(int.Parse).ToArray();
            var kind = Enchantments.FindKind(parts[0]) ?? throw new ArgumentException("efecto desconocido: " + parts[0]);
            var target = (int[])kind.ValuesByTier;
            for (int i = 0; i < target.Length; i++) target[i] = values[i];
            Console.WriteLine($"   {kind.Name} por tier: {string.Join(", ", target)}");
        }

        const int N = 1200;
        string P(double v) => $"{100 * v,5:F1}";

        foreach (var (title, offsets) in new[] { ("JEFE (equipo propio, con comida y habilidad)", new[] { 3, 8 }) })
        {
            Console.WriteLine($"=== {title}: % de PERDER / % de vida que cuesta, tier {tier}{(only is null ? ", promedio de las 4 clases" : ", " + only)} ===");
            Console.WriteLine("zona / nivel         |" + string.Join("|", Cases.Select(c => $" {c,-13}")));
            for (int zi = 1; zi < World.Zones.Count; zi++)
            {
                var boss = World.Boss[zi];
                foreach (int off in offsets)
                {
                    int level = World.Zones[zi].MinLevel + off;
                    var row = $"Z{zi + 1} nivel {level,-3} (mín+{off,-2}) |";
                    foreach (var name in Cases)
                    {
                        var (d, l) = Measure(name, tier, zi, level, boss, Policy.AbilityHeal, true, N, only);
                        row += $" {P(d)} / {P(l)} |";
                    }

                    Console.WriteLine(row);
                }
            }
        }

        Console.WriteLine($"\n=== CACERÍA COMÚN (equipo propio, solo ataque como /autohunt): % de vida que cuesta, tier {tier} ===");
        Console.WriteLine("zona / nivel         |" + string.Join("|", Cases.Select(c => $" {c,-7}")));
        for (int zi = 1; zi < World.Zones.Count; zi++)
        {
            int level = World.Zones[zi].MinLevel + 3;
            var row = $"Z{zi + 1} nivel {level,-3}         |";
            foreach (var name in Cases)
            {
                var (_, l) = Measure(name, tier, zi, level, World.Hunt[zi], Policy.AttackOnly, false, N, only);
                row += $" {P(l),7} |";
            }

            Console.WriteLine(row);
        }
    }
}
