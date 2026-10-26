using BotDsRpg.GameData;
using Dapper;
using Npgsql;

// v0.28.0: la XP al estilo de EPIC RPG (el dueño, 2026-10-26: «me gusta su niveleo con esa subida por FN, acomodala a las zonas que tenemos»).
//   · La EXP para pasar de nivel pasa de 100 × nivel^1,5 a 40 × nivel² (EPIC usa 125 × nivel²: con nuestros premios de EXP llegar al nivel 25 del Fogón tomaba 58 h).
//   · El bono de EXP de las peleas por Fuego Nuevo pasa de +10 % lineal a la fórmula de EPIC (99 + x) × x / 2 %: +50 % la primera vuelta, +545 % la décima.
// La segunda parte juega «el juego perfecto» (cacería cada minuto, viaje cada 30 min ×30, jefe cada 5 h ×15, siempre en la zona más alta que el nivel permite, sin mascotas ni bendiciones)
// con el valor real de los monstruos de la base LOCAL y comprueba el ritmo de nivel por hora que se midió al elegir la curva.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

Console.WriteLine("== 1. La curva de nivel ==");
Check(LevelingCalculator.RequiredXpForLevel(1) == 40 && LevelingCalculator.RequiredXpForLevel(5) == 1000 && LevelingCalculator.RequiredXpForLevel(10) == 4000
      && LevelingCalculator.RequiredXpForLevel(20) == 16000 && LevelingCalculator.RequiredXpForLevel(50) == 100000, "EXP para pasar de nivel = 40 × nivel² (1: 40, 5: 1.000, 10: 4.000, 20: 16.000, 50: 100.000)");
Check(Enumerable.Range(1, 300).All(l => LevelingCalculator.RequiredXpForLevel(l + 1) > LevelingCalculator.RequiredXpForLevel(l)), "cada nivel pide más que el anterior (hasta el 300)");
var up = LevelingCalculator.ApplyXpGain(level: 5, xp: 900, maxHp: 100, currentHp: 40, xpGained: 100);
Check(up.Level == 6 && up.Xp == 0 && up.LevelsGained == 1 && up.CurrentHp == up.MaxHp, "5 → 6 con justo 1.000 de EXP, y se cura entera al subir");
var many = LevelingCalculator.ApplyXpGain(level: 1, xp: 0, maxHp: 100, currentHp: 100, xpGained: 40 + 160 + 360 + 5);
Check(many.Level == 4 && many.Xp == 5 && many.LevelsGained == 3, "un premio grande sube varios niveles de una (1 → 4) y deja lo que sobra");
Check((int)(LevelingCalculator.RequiredXpForLevel(20) * 10 / 100.0) == 1600, "los premios en % del nivel (daily, misiones, logros, Arena: MissionRewards.Resolve usa esta misma cuenta) siguen la curva: 10 % del nivel 20 = 1.600");

Console.WriteLine("\n== 2. El bono de EXP por Fuego Nuevo (fórmula de EPIC RPG) ==");
Check(Near(FuegoNuevoRules.XpMultiplier(0), 1.0, 1e-9) && Near(FuegoNuevoRules.XpMultiplier(1), 1.5, 1e-9) && Near(FuegoNuevoRules.XpMultiplier(2), 2.01, 1e-9) && Near(FuegoNuevoRules.XpMultiplier(5), 3.6, 1e-9)
      && Near(FuegoNuevoRules.XpMultiplier(10), 6.45, 1e-9) && Near(FuegoNuevoRules.XpMultiplier(25), 16.5, 1e-9), "FN 0/1/2/5/10/25 = ×1 / ×1,5 / ×2,01 / ×3,6 / ×6,45 / ×16,5 ((99 + x) × x / 2 % sobre la base)");
Check(FuegoNuevoRules.PercentText(FuegoNuevoRules.XpMultiplier(1)) == "+50 %" && FuegoNuevoRules.PercentText(FuegoNuevoRules.XpMultiplier(10)) == "+545 %", "y se muestra como «+50 %» la primera vuelta y «+545 %» la décima");
Check(Enumerable.Range(0, 100).All(x => FuegoNuevoRules.XpMultiplier(x + 1) - FuegoNuevoRules.XpMultiplier(x) >= 0.5 - 1e-9), "cada vuelta suma al menos +50 % más que la anterior: la subida se acelera (no es pareja como antes)");
Check(FuegoNuevoRules.XpMultiplier(-3) == 1.0, "un número de vueltas raro (negativo) no rompe: ×1");

Console.WriteLine("\n== 3. El ritmo con el juego perfecto (monstruos reales de la base local) ==");
string conn = TestEnv.Connection();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var zonesDb = (await db.QueryAsync<(int ZoneId, int MinLevel)>("SELECT zone_id, min_level FROM zones WHERE zone_id > 0 ORDER BY min_level")).ToList();
var xpBonus = (await db.QueryAsync<(int ZoneId, bool IsBoss, bool IsTravel, int Xp)>("SELECT zone_id, is_boss, is_travel, xp_reward FROM monsters WHERE zone_id > 0")).ToList();
int HuntBonus(int z) => xpBonus.First(m => m.ZoneId == z && !m.IsBoss && !m.IsTravel).Xp;
int BossBonus(int z) => xpBonus.First(m => m.ZoneId == z && m.IsBoss).Xp;

// Devuelve el nivel a cada hora de la lista y las horas hasta llegar a cada nivel de la lista, con la curva y el bono de EXP dados.
(Dictionary<int, int> LevelAt, Dictionary<int, double> HoursTo) Play(double xpMultiplier, int[] hours, int[] targets)
{
    int level = 1; double xp = 0; var levelAt = new Dictionary<int, int>(); var hoursTo = new Dictionary<int, double>();
    for (int minute = 1; minute <= hours.Max() * 60; minute++)
    {
        var zone = zonesDb.Last(z => z.MinLevel <= level);
        double hunt = (14 + level + HuntBonus(zone.ZoneId)) * CombatRewardCalculator.FightXpScale;       // la media de Random(8..20) es 14
        double gain = hunt;
        if (minute % 30 == 0) gain += hunt * CombatRewardCalculator.TravelRewardMultiplier;
        if (minute % 300 == 0) gain += (14 + level + BossBonus(zone.ZoneId)) * CombatRewardCalculator.BossRewardMultiplier * CombatRewardCalculator.FightXpScale;
        xp += gain * xpMultiplier;
        while (xp >= LevelingCalculator.RequiredXpForLevel(level))
        {
            xp -= LevelingCalculator.RequiredXpForLevel(level);
            level++;
            if (targets.Contains(level) && !hoursTo.ContainsKey(level)) hoursTo[level] = minute / 60.0;
        }

        foreach (int h in hours) if (minute == h * 60) levelAt[h] = level;
    }

    return (levelAt, hoursTo);
}

var fn0 = Play(1.0, [6, 12, 20, 31], [5, 10, 15, 20, 25, 30]);
Console.WriteLine($"   FN 0: nivel a las 6/12/20/31 h = {string.Join("/", new[] { 6, 12, 20, 31 }.Select(h => fn0.LevelAt[h]))}; horas hasta el nivel 5/10/15/20/25/30 = {string.Join("/", new[] { 5, 10, 15, 20, 25, 30 }.Select(l => fn0.HoursTo[l].ToString("0.0")))}");
Check(new[] { (6, 12), (12, 18), (20, 26), (31, 32) }.All(t => Math.Abs(fn0.LevelAt[t.Item1] - t.Item2) <= 1), $"sin vueltas, el nivel a las 6/12/20/31 h es ~12/18/26/32 (antes 13/22/33/43) — da {string.Join("/", new[] { 6, 12, 20, 31 }.Select(h => fn0.LevelAt[h]))}");
Check(Near(fn0.HoursTo[25], 19.0, 1.0), $"el nivel 25 del Fogón llega a las ~19 h de juego perfecto (da {fn0.HoursTo[25]:0.0} h; antes ~13,5)");
Check(zonesDb.Count == 5 && zonesDb.Skip(1).All(z => fn0.HoursTo[z.MinLevel] >= 1.0), "los niveles de las zonas 2 a 5 (5/10/15/20) no se alcanzan en la primera hora: la zona se gana, no se regala");
foreach (var (fn, expectedHours) in new[] { (1, 13.0), (3, 8.0), (5, 5.5), (10, 3.5) })
{
    var lap = Play(FuegoNuevoRules.XpMultiplier(fn), [60], [25]);
    Check(Near(lap.HoursTo[25], expectedHours, 0.8), $"con {fn} Fuego(s) Nuevo(s), el nivel 25 llega en ~{expectedHours:0.#} h (da {lap.HoursTo[25]:0.0} h)");
}

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
