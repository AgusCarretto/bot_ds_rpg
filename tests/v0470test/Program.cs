using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.47.0: las Zonas 6 a 10 (el dueño, 2026-11-01: «que se tenga que farmear hasta el FN 5… cada más FN más zonas… que pasar de zona sea una mazmorra tipo acertijo… y el Asador más fuerte»).
// La escalera por Fuego Nuevo (zones.min_fuego_nuevo, ZoneRanking.ForPlayer), la puerta que se corre, los datos de las zonas nuevas, las mazmorras y el Asador escalado. Contra la base LOCAL.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var monsters = new MonsterRepository(factory);
var adventure = new AdventureRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const long Base = 999000000000047000L;
ulong Id(int n) => (ulong)(Base + n);

async Task Make(int n, int level, int fn, int zone, int cleared)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id;", new { id = Base + n });
    await users.GetOrCreateUserAsync(Id(n));
    await db.ExecuteAsync("UPDATE users SET level = @l, fuego_nuevo = @fn, current_zone_id = @z, highest_zone_cleared = @c, gate_cleared = false, in_gate = false, class = 'Guerrero' WHERE discord_id = @id",
        new { id = Base + n, l = level, fn, z = zone, c = cleared });
}

Console.WriteLine("== 1. La escalera por Fuego Nuevo (pura, con las zonas de la base) ==");
var all = await zonesRepo.GetAllAsync();
Check(all.Count == 10 && all.Count(z => z.MinFuegoNuevo > 0) == 5, $"la base tiene 10 zonas normales, 5 de ellas con Fuego Nuevo ({all.Count})");
Check(new[] { (0, 5), (4, 5), (5, 6), (7, 6), (8, 7), (11, 8), (14, 9), (16, 9), (17, 10), (99, 10) }.All(p => ZoneRanking.ForPlayer(all, p.Item1).Count == p.Item2),
    "FN 0/4 → 5 zonas, 5/7 → 6, 8 → 7, 11 → 8, 14/16 → 9, 17 en adelante → las 10");
Check(all.Where(z => z.ZoneId <= 5).All(z => !ZoneRanking.IsDungeonZone(z)) && all.Where(z => z.ZoneId >= 6).All(ZoneRanking.IsDungeonZone), "las zonas 6 a 10 son de mazmorra; las 1 a 5 no");
Check(FogonRules.IsGateOpen(ZoneRanking.ForPlayer(all, 0), 5) && !FogonRules.IsGateOpen(ZoneRanking.OrderByDifficulty(all), 5),
    "con FN 0 la Zona 5 abre el Fogón con la escalera DEL JUGADOR (con la lista entera no se abriría nunca: por eso todo pasa por ForPlayer)");
Check(ZoneRanking.MaxUnlockedRank(ZoneRanking.ForPlayer(all, 0), 60, 5) == 5 && ZoneRanking.MaxUnlockedRank(ZoneRanking.ForPlayer(all, 5), 60, 5) == 6 && ZoneRanking.MaxUnlockedRank(ZoneRanking.ForPlayer(all, 8), 60, 5) == 6,
    "un nivel 60 con FN 0 llega hasta la 5; con FN 5 y la 5 vencida, a la 6; con FN 8 sigue en la 6 hasta pasar su mazmorra");
Check(ZoneRanking.TopZone(all, 5)?.ZoneId == 6 && ZoneRanking.TopZone(all, 0)?.ZoneId == 5 && ZoneRanking.NextLockedByFuegoNuevo(all, 5)?.ZoneId == 7 && ZoneRanking.NextLockedByFuegoNuevo(all, 17) is null,
    "la zona tope y la próxima que se abre");
Check(MissionRewards.GoldUnit(6) == 560 && MissionRewards.GoldUnit(10) == 2840 && MissionRewards.GoldUnit(11) == 2840, "la unidad de oro de las zonas nuevas (560 … 2840)");

Console.WriteLine("\n== 2. La puerta se corre (base real) ==");
await Make(1, 30, 0, 5, 4);
var v0 = await adventure.ApplyBossVictoryAsync(Id(1), 10, 10, 0, null, 0, 5);
Check(v0.Gate == GateEvent.Opened, "FN 0: ganarle al jefe de la Zona 5 abre el Fogón como siempre");
await Make(2, 30, 5, 5, 4);
var v5 = await adventure.ApplyBossVictoryAsync(Id(2), 10, 10, 0, null, 0, 5);
Check(v5.Gate == GateEvent.None && v5.Player.HighestZoneCleared == 5, "FN 5: ganarle al jefe de la Zona 5 ya NO abre el Fogón: sube la escalera (se abre la Zona 6)");
await Make(3, 35, 5, 6, 5);
var asador = await adventure.ApplyBossVictoryAsync(Id(3), 10, 10, 0, null, 0, FogonRules.GateZoneId);
Check(asador.Gate == GateEvent.Cleared && asador.Player.CurrentZoneId == 6 && asador.Player.GateCleared, "FN 5: ganarle al Asador te devuelve a TU zona tope (la 6), no a la 10");

Console.WriteLine("\n== 3. /zona y /zonas ==");
await Make(4, 40, 4, 5, 5);
var (msg4, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(4), 6);
Check(msg4 is not null && msg4.Contains("Fuego Nuevo 5"), $"/zona 6 con FN 4: «se abre con el Fuego Nuevo 5» ({msg4})");
await Make(5, 40, 5, 5, 5);
var (msg5, emb5) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(5), 6);
Check(msg5 is null && emb5 is not null && (await users.GetByDiscordIdAsync(Id(5)))!.CurrentZoneId == 6, "/zona 6 con FN 5, la 5 vencida y nivel 40: viaja");
await Make(6, 40, 5, 5, 4);
var (msg6, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(6), 6);
Check(msg6 is not null && msg6.Contains("Soberano"), "/zona 6 sin la Zona 5 vencida: pide su jefe");
await Make(7, 50, 8, 6, 5);
var (msg7, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(7), 7);
Check(msg7 is not null && msg7.Contains("mazmorra") && msg7.Contains("Salinas"), $"/zona 7 con FN 8 sin pasar la mazmorra de la 6: la pide ({msg7})");
var (gateMsg, _) = await ZoneModule.ExecuteTravelAsync(users, zonesRepo, monsters, Id(7), 0);
Check(gateMsg is not null && gateMsg.Contains("mazmorra"), "/zona 0 con la zona tope de mazmorra: se llega pasando la mazmorra, no caminando");
var list0 = AllText(await ZoneModule.BuildZoneListEmbedAsync(users, zonesRepo, Id(4)));
var list5 = AllText(await ZoneModule.BuildZoneListEmbedAsync(users, zonesRepo, Id(5)));
Check(list0.Contains("Fuego Nuevo 5") && !list0.Contains("mazmorra") && list5.Contains("Salinas") && list5.Contains("Selva de Humo") && list5.Contains("Fuego Nuevo 8") && list5.Contains("mazmorra"),
    "/zonas con FN 4 solo avisa que la 6 se abre en el FN 5; con FN 5 muestra la 6 (sin jefe, con mazmorra) y avisa que la 7 se abre en el FN 8");

Console.WriteLine("\n== 4. Los datos de las zonas 6 a 10 ==");
var mons = await monsters.GetAllAsync();
foreach (var z in all.Where(ZoneRanking.IsDungeonZone))
{
    var mine = mons.Where(m => m.ZoneId == z.ZoneId).ToList();
    Check(mine.Count(m => m.Kind == MonsterKind.Hunt) == 1 && mine.Count(m => m.Kind == MonsterKind.Travel) == 1 && mine.Count(m => m.Kind == MonsterKind.Boss) == 0 && mine.Single(m => m.Kind == MonsterKind.Hunt).Monster.DropItemNames.Count == 1,
        $"Zona {z.ZoneId}: un monstruo de caza con UN drop, uno de viaje y ningún jefe");
}

for (int n = 1; n <= 7; n++) await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id; DELETE FROM game_events WHERE discord_id = @id; DELETE FROM player_stats WHERE discord_id = @id; DELETE FROM cooldowns WHERE discord_id = @id;", new { id = Base + n });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
