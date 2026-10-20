using BotDsRpg.GameData;

// Parte K: el RAID por zona, a varios niveles y con 1 a 4 jugadores (equipo propio de la zona, sin comida porque el raid no tiene curación; promedio de las 4 clases en el grupo: Guerrero, Hechicero, Arquero, Ninja
// en ese orden). Complementa la parte C de PartI (que promedia las zonas 2 a 5). La vida del jefe y su daño salen de la base y de GameData/RaidDifficulty.cs, o sea lo que hoy corre.
//   K [offsets] [N] [factorVida] [heal]   offsets = niveles sobre el mínimo de la zona, separados por coma (por defecto 3,8,15); N = raids por celda (por defecto 400); factorVida = multiplica la vida GUARDADA del jefe
//   para probar un raid más blando o más duro sin tocar la base (por defecto 1,0 = lo que corre hoy)
public static class PartK
{
    public static void Run(string[] args)
    {
        int[] offs = args.Length > 1 ? args[1].Split(',').Select(int.Parse).ToArray() : [3, 8, 15];
        int n = args.Length > 2 ? int.Parse(args[2]) : 400;
        double hpFactor = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1.0;
        // 5.º argumento: heal = comida común una vez por jugador; bNNN = un banquete que cura NNN y da +15 % ATQ; buff = banquete que solo da +15 % ATQ (no cura).
        string mode = args.Length > 4 ? args[4] : string.Empty;
        bool heal = mode == "heal" || mode == "buff" || mode == "pre" || mode.StartsWith('b') && mode.Length > 1 && char.IsDigit(mode[1]);
        int banquet = mode == "pre" ? -2 : mode == "buff" ? -1 : mode.StartsWith('b') && mode.Length > 1 && char.IsDigit(mode[1]) ? int.Parse(mode[1..]) : 0;
        string P(double v) => $"{100 * v,4:F0}";

        Console.WriteLine($"=== RAID por zona: % de VICTORIA con 1 / 2 / 3 / 4 jugadores (equipo propio, sin comida, {n} raids por celda, vida guardada del jefe ×{hpFactor:0.00}) ===");
        Console.WriteLine("zona (jefe)                           | nivel     |   1 |   2 |   3 |   4 | vida del jefe para 1 / 2 / 4");
        for (int zi = 0; zi < World.Zones.Count; zi++)
        {
            var stored = World.Boss[zi][0];
            var boss = new MonRow { Name = stored.Name, Kind = stored.Kind, ZoneId = stored.ZoneId, MinHp = (int)Math.Round(stored.MinHp * hpFactor), MaxHp = (int)Math.Round(stored.MaxHp * hpFactor), MinDmg = stored.MinDmg, MaxDmg = stored.MaxDmg, GoldB = stored.GoldB, XpB = stored.XpB, Drops = stored.Drops };
            foreach (int off in offs)
            {
                int level = World.Zones[zi].MinLevel + off;
                var row = $"Z{zi + 1} {boss.Name,-32}| mín+{off,-2} ({level,2}) |";
                foreach (int size in new[] { 1, 2, 3, 4 })
                {
                    int wins = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (PartI.Raid(size, zi, level, boss, heal, banquet).Win) wins++;
                    }

                    row += $" {P(wins / (double)n)}% |";
                }

                int baseHp = (boss.MinHp + boss.MaxHp) / 2;
                row += $" {RaidDifficulty.BossHp(baseHp, 1, zi + 1)} / {RaidDifficulty.BossHp(baseHp, 2, zi + 1)} / {RaidDifficulty.BossHp(baseHp, 4, zi + 1)}";
                Console.WriteLine(row);
            }
        }
    }
}
