using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.17.1: más vida a los seis jefes (daño igual) y el raid con la MISMA dificultad de antes (sus multiplicadores se dividieron por el mismo factor).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

string conn = TestEnv.Connection();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

Console.WriteLine("== 1. La vida y el daño de los jefes en la base ==");
var rows = (await db.QueryAsync<(string Name, int MinHp, int MaxHp, int MinDmg, int MaxDmg)>("SELECT name, min_hp, max_hp, min_damage, max_damage FROM monsters WHERE is_boss ORDER BY min_hp")).ToList();
(string, int, int, int, int)[] expected =
[
    ("Rey Jabalí", 175, 245, 23, 37), ("Lobisón Alfa", 624, 794, 71, 96), ("Capataz de Hierro", 929, 1161, 108, 142),
    ("Señor del Volcán", 1340, 1638, 151, 188), ("Soberano de la Escoria", 1925, 2309, 202, 248), ("El Asador Eterno", 2503, 3001, 233, 289),
];
Check(rows.Count == 6 && rows.Select(r => (r.Name, r.MinHp, r.MaxHp, r.MinDmg, r.MaxDmg)).SequenceEqual(expected.Select(e => (e.Item1, e.Item2, e.Item3, e.Item4, e.Item5))), "los seis jefes tienen la vida nueva (×1,25) y el daño de la v0.15.1 sin tocar");
(string Name, int MinHp, int MaxHp)[] old =
[
    ("Rey Jabalí", 140, 196), ("Lobisón Alfa", 499, 635), ("Capataz de Hierro", 743, 929), ("Señor del Volcán", 1072, 1310), ("Soberano de la Escoria", 1540, 1847), ("El Asador Eterno", 2002, 2401),
];
Check(old.All(o => { var n = rows.Single(r => r.Name == o.Name); return Math.Abs(n.MinHp - o.MinHp * 1.25) <= 0.5 && Math.Abs(n.MaxHp - o.MaxHp * 1.25) <= 0.5; }), "cada jefe tiene exactamente ×1,25 de lo que tenía (redondeado)");
Check(rows.All(r => r.MinHp < r.MaxHp), "mínimo menor que máximo en todos");
var nonBossHp = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM monsters WHERE NOT is_boss AND (min_hp > 400 AND zone_id = 1)");
Check(nonBossHp == 0, "los monstruos comunes y de viaje NO se tocaron (solo los jefes)");

Console.WriteLine("\n== 2. El raid queda igual de difícil ==");
double[] oldMult = [2.2, 1.5, 1.5, 1.5, 1.5];
bool same = true; string worst = "";
for (int rank = 1; rank <= 5; rank++)
{
    var (oMin, oMax) = (old[rank - 1].MinHp, old[rank - 1].MaxHp);
    var (nMin, nMax) = (rows.Single(r => r.Name == old[rank - 1].Name).MinHp, rows.Single(r => r.Name == old[rank - 1].Name).MaxHp);
    for (int players = 1; players <= 4; players++)
        foreach (var (o, n) in new[] { (oMin, nMin), (oMax, nMax) })
        {
            double before = Math.Round(o * oldMult[rank - 1] * (1 + 0.5 * (players - 1)));
            int after = RaidDifficulty.BossHp(n, players, rank);
            if (Math.Abs(before - after) > 2) { same = false; worst = $"rango {rank}, {players} jug.: {before} -> {after}"; }
        }
}
Check(same, $"la vida del jefe de raid (1 a 4 jugadores, las 5 zonas) es la de antes con ±2 de redondeo {worst}");
Check(RaidDifficulty.ForZoneRank(1).HpMultiplier == 1.76 && RaidDifficulty.ForZoneRank(2).HpMultiplier == 1.2 && RaidDifficulty.ForZoneRank(5).HpMultiplier == 1.2 && RaidDifficulty.ForZoneRank(1).DamageMultiplier == 1.35 && RaidDifficulty.ForZoneRank(3).DamageMultiplier == 1.1,
    "los multiplicadores de VIDA del raid se dividieron por 1,25 (2,2 → 1,76 y 1,5 → 1,2); los de DAÑO no cambiaron");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
