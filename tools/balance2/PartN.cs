using BotDsRpg.GameData;

// ======================= PARTE N (v0.47.0): las zonas 6 a 10 y el Asador escalado =======================
// Calibra con el CombatTurnResolver real (no hay datos en la base todavía: la escalera de equipo es la de AsadorScaling/ZoneLadder de abajo).
//   · Monstruo común de cada zona: con el equipo de la zona ANTERIOR al nivel mínimo una pelea cuesta ~40 % de la vida; con el de la zona, al nivel mínimo + 3, ~6 %.
//     Se busca el daño (bisección) que da el 6 % con el equipo propio, con la vida (HP del monstruo) que hace que el equipo anterior pierda el 40 %.
//   · El Asador desde la zona tope N: con el equipo de N, nivel mínimo + 3, llegando con el 80 % de la vida, sin comida y con la habilidad, ~30 % de derrota;
//     la llamarada (20 % de los turnos) pega ×1,75. Vida = ~10 golpes del jugador promedio.
//   uso: dotnet run --project tools/balance2 -- N
public static class PartN
{
    // Zonas 6..10 (índice 0..4) y la Zona 5 (de la base) como «equipo anterior» de la 6.
    public static readonly int[] MinLevel = [30, 35, 40, 45, 50];
    public static readonly int[] WeaponAtk = [180, 270, 400, 600, 900];
    public static readonly int[] AmuletDef = [165, 215, 280, 365, 475];

    static (int W, int A) Gear(int k) => k < 0 ? (120, 120) : (WeaponAtk[k], AmuletDef[k]);

    static double MeanLost(int level, (int W, int A) gear, int hp, int dmg, int n)
    {
        var m = new MonRow { Name = "x", MinHp = (int)(hp * 0.85), MaxHp = (int)(hp * 1.15), MinDmg = (int)(dmg * 0.8), MaxDmg = (int)(dmg * 1.2) };
        double lost = 0;
        foreach (var cls in World.Classes)
        {
            lost += Sim.Compute(cls, level, gear.W, true, gear.A, [m], Policy.AttackOnly, 0, 1.0, false, n).MeanLost;
        }

        return lost / World.Classes.Length;
    }

    static int Bisect(Func<int, double> f, double target, int lo, int hi)
    {
        for (int i = 0; i < 22 && hi - lo > 1; i++)
        {
            int mid = (lo + hi) / 2;
            if (f(mid) < target) lo = mid; else hi = mid;
        }

        return hi;
    }

    // uso: dotnet run --project tools/balance2 -- N hp [n]
    // Cuánto pierde un jugador contra el Asador de cada zona tope si se le multiplica la vida (daño sin tocar, llega con el 80 % o con el 100 %, sin comida, con la habilidad):
    // con el equipo de la zona y con el de la ANTERIOR, a nivel mínimo + 3 (el caso de diseño) y bastante por encima (+10, +20, +30), que es donde el dueño lo liquidó sin equipo de la zona.
    static void RunAsadorHp(string[] args)
    {
        int n = args.Length > 2 && int.TryParse(args[2], out var nn) ? nn : 300;
        double[] factors = [1.0, 1.5, 2.0, 3.0];
        int[] offsets = [3, 10, 20, 30];
        foreach (double start in new[] { 0.8, 1.0 })
        {
            Console.WriteLine($"\n===== llega con el {start:P0} de la vida =====");
            for (int k = 0; k < 5; k++)
            {
                var s = AsadorScaling.BaseStatsFor(k + 6)!.Value;
                int hp = (s.MinHp + s.MaxHp) / 2, dmg = (s.MinDamage + s.MaxDamage) / 2;
                Console.WriteLine($"\nTope {k + 6} (vida base {hp}, daño {dmg}) — % de derrota con la vida ×{string.Join(" | ×", factors.Select(f => f.ToString("0.0")))}");
                foreach (var (label, gear) in new[] { ("equipo de la zona   ", Gear(k)), ("equipo de la anterior", Gear(k - 1)) })
                {
                    foreach (int off in offsets)
                    {
                        var row = string.Join(" | ", factors.Select(f => $"{AsadorDefeat(MinLevel[k] + off, gear, (int)(hp * f), dmg, n, start),4:P0}"));
                        Console.WriteLine($"  {label} nivel {MinLevel[k] + off,2} (min+{off,-2}): {row}");
                    }
                }
            }
        }
    }

    public static void Run(string[] args)
    {
        if (args.Length > 1 && args[1] == "hp") { RunAsadorHp(args); return; }
        int n = args.Length > 1 && int.TryParse(args[1], out var nn) ? nn : 600;
        Console.WriteLine("Zona | nivel | vida del común | daño del común | previo nivel min | propio nivel min+3");
        var commons = new List<(int Hp, int Dmg)>();
        for (int k = 0; k < 5; k++)
        {
            int lvl = MinLevel[k];
            // Para cada daño, la vida que hace que el equipo anterior pierda el 40 %; después, el daño que deja al propio en 6 %.
            // La vida del común: ~4 golpes del jugador promedio con el equipo de la zona (como en las zonas 1 a 5). El daño: el que hace que entrar con el equipo anterior cueste el 40 %.
            double ownDamage = World.Classes.Average(c => Sim.Profile(c, lvl + 3, Gear(k).W, true, Gear(k).A).Damage);
            int hp = (int)Math.Round(ownDamage * 4);
            int dmg = Bisect(d => MeanLost(lvl, Gear(k - 1), hp, d, n / 3), 0.40, 1, 20000);
            commons.Add((hp, dmg));
            Console.WriteLine($"  {k + 6}  |  {lvl}  |  {hp}  |  {dmg}  |  {MeanLost(lvl, Gear(k - 1), hp, dmg, n):P0}  |  {MeanLost(lvl + 3, Gear(k), hp, dmg, n):P0}");
        }

        Console.WriteLine("\nAsador por zona tope (equipo propio, nivel min+3, llega con 80 %, sin comida, habilidad; llamarada 20 % ×1,75):");
        for (int k = 0; k < 5; k++)
        {
            int lvl = MinLevel[k] + 3;
            var gear = Gear(k);
            double avgDamage = World.Classes.Average(c => Sim.Profile(c, lvl, gear.W, true, gear.A).Damage);
            int hp = (int)Math.Round(avgDamage * 10);
            int dmg = Bisect(d => AsadorDefeat(lvl, gear, hp, d, n), 0.30, 1, 8000);
            Console.WriteLine($"  tope {k + 6}: vida {hp}, daño {dmg} → derrota {AsadorDefeat(lvl, gear, hp, dmg, n * 2):P0} (llegando al 100 %: {AsadorDefeat(lvl, gear, hp, dmg, n * 2, 1.0):P0}, al 60 %: {AsadorDefeat(lvl, gear, hp, dmg, n * 2, 0.6):P0})");
        }
    }

    public static double AsadorDefeat(int level, (int W, int A) gear, int hp, int dmg, int n, double start = 0.8)
    {
        int deaths = 0, total = 0;
        foreach (var cls in World.Classes)
        {
            var prof = Sim.Profile(cls, level, gear.W, true, gear.A);
            var cp = new CombatantProfile(prof.Damage, prof.Defense, cls, prof.Passives);
            for (int i = 0; i < n; i++)
            {
                int max = prof.CombatMaxHp, php = Math.Max(1, (int)Math.Round(max * start));
                int mhp = Sim.Rng.Next((int)(hp * 0.9), (int)(hp * 1.1) + 1), mdmg = Sim.Rng.Next((int)(dmg * 0.9), (int)(dmg * 1.1) + 1);
                var st = default(AbilityState);
                bool died = true;
                for (int t = 0; t < 300; t++)
                {
                    bool ability = CombatTurnResolver.CheckAbility(cls, st) == AbilityAvailability.Ready;
                    int hit = AsadorScaling.RollsFlare(Sim.Rng) ? AsadorScaling.FlareDamage(mdmg) : mdmg;
                    var r = CombatTurnResolver.ResolveTurn(cp, st, ability ? PlayerAction.Ability : PlayerAction.Attack, php, max, mhp, hit);
                    php = r.PlayerHpAfter; mhp = r.MonsterHpAfter; st = r.StatusAfter;
                    if (mhp <= 0) { died = false; break; }
                    if (php <= 0) break;
                }

                total++; if (died) deaths++;
            }
        }

        return deaths / (double)total;
    }
}
