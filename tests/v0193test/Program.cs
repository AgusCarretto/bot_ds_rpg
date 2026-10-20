using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.19.3 (dos pedidos del dueño, 2026-10-20):
//   1) «hay comandos que muestran en vez del emoji que es de ellos muestran otra cosa» (el Cofre de Escoria en aa drops...): las pantallas que solo tienen el NOMBRE de un ítem (premios, cofre del daily y del voto,
//      huevos, comida de mascotas, mini-evento) escribían un emoji común (📦, 🥚, 🦴). Ahora piden el emoji propio a GameData/ItemEmojiRegistry, que carga Services/ItemEmojiRefresher desde el catálogo.
//   2) el ranking (/leaderboard, aa leaderboard) ordena primero por Fuego Nuevo y después por nivel, y muestra el FN de cada uno.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }
string AllText(Embed e) => (e.Title + "\n" + e.Description + "\n" + string.Join("\n", e.Fields.Select(f => f.Name + " " + f.Value)) + "\n" + (e.Footer?.Text ?? ""));

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();

const string Roble = "<:cofre_raro:1555578515754061944>";
const string Piedra = "<:piedra_test:111>";

Console.WriteLine("== 1. El registro de emojis por nombre ==");
ItemEmojiRegistry.Set([]);
Check(ItemEmojiRegistry.IconOf("Baúl de Roble", "📦") == "📦" && ItemEmojiRegistry.Label("Baúl de Roble", "📦") == "📦 Baúl de Roble" && ItemEmojiRegistry.Label("Baúl de Roble", string.Empty) == "Baúl de Roble",
    "vacío (o antes de cargar): cae al emoji común y, sin fallback, al nombre solo");
ItemEmojiRegistry.Set([("Baúl de Roble", Roble), ("Sin Arte", null), ("Espacios", "  "), ("", "<:x:1>")]);
Check(ItemEmojiRegistry.IconOf("Baúl de Roble", "📦") == Roble && ItemEmojiRegistry.IconOf("baúl de roble", "📦") == Roble, "con el ítem cargado devuelve SU emoji (sin mirar mayúsculas)");
Check(ItemEmojiRegistry.IconOf("Sin Arte", "📦") == "📦" && ItemEmojiRegistry.IconOf("Espacios", "🥚") == "🥚" && ItemEmojiRegistry.IconOf("", "📦") == "📦", "un ítem sin emoji (null, en blanco o sin nombre) no entra: usa el fallback");
Check(ItemEmojiRegistry.Label("Baúl de Roble", "📦") == $"{Roble} Baúl de Roble", "Label arma «emoji Nombre»");
ItemEmojiRegistry.Set([("Piedra", Piedra)]);
Check(ItemEmojiRegistry.IconOf("Baúl de Roble", "📦") == "📦", "Set reemplaza TODA la tabla (lo que ya no está, deja de estar)");

Console.WriteLine("\n== 2. Los premios muestran el emoji propio de la caja ==");
var roble = new ResolvedReward(84, 120, "Baúl de Roble", 1);
var roble3 = new ResolvedReward(0, 0, "Baúl de Roble", 3);
ItemEmojiRegistry.Set([]);
Check(MissionRewards.Describe(roble).Contains("📦 Baúl de Roble") && !MissionRewards.Describe(roble).Contains("<:"), "sin registro, el premio sigue con el 📦 de siempre");
ItemEmojiRegistry.Set([("Baúl de Roble", Roble)]);
Check(MissionRewards.Describe(roble).Contains($"{Roble} Baúl de Roble") && !MissionRewards.Describe(roble).Contains("📦"), "con el registro, el premio de misión/logro usa el emoji del Baúl de Roble");
Check(MissionRewards.Describe(roble3).Contains($"{Roble} 3× Baúl de Roble") && MissionRewards.Describe(new ResolvedReward(5, 5, null, 0)) == "💰 5 oro · ⭐ 5 XP", "varias cajas llevan «3×» y sin caja no cambia nada");
var totals = new RewardTotals();
totals.Add(new RewardReceipt(roble, 100, 0, 5));
totals.Add(new RewardReceipt(roble3, 100, 0, 5));
Check(totals.Describe().Contains($"{Roble} 4× Baúl de Roble") && !totals.Describe().Contains("📦"), "el recibo con varios premios suma las cajas y lleva el emoji propio");
ItemEmojiRegistry.Set([("Piedra", Piedra)]);
Check(MiniEventRules.Describe(new MiniEventReward("Piedra", 4, 0)) == $"4× {Piedra} Piedra" && MiniEventRules.Describe(new MiniEventReward(null, 0, 70)) == "💰 70 oro",
    "el premio del mini-evento (piedras/madera) lleva el emoji del material; la plata no cambia");
ItemEmojiRegistry.Set([]);
Check(MiniEventRules.Describe(new MiniEventReward("Piedra", 4, 0)) == "4× Piedra", "mini-evento sin registro: queda el texto de siempre");

Console.WriteLine("\n== 3. El refresco lee el catálogo de la base ==");
ItemEmojiRegistry.Set([]);
await new ItemEmojiRefresher(items).RefreshAsync();
foreach (var name in new[] { "Cofre de Escoria", "Brasero del Fogón", "Huevo del Fogón", "Huevo de la Pradera", "Comida para Mascotas", "Baúl de Roble", "Arca del Soberano" })
{
    Check(ItemEmojiRegistry.IconOf(name, "FALLBACK").StartsWith("<:"), $"{name}: el registro cargado de la base tiene su emoji propio ({ItemEmojiRegistry.IconOf(name, "FALLBACK")})");
}
var missing = new List<string>();
foreach (var type in new[] { "Caja", "Huevo", "PetFood", "Consumable", "Madera", "Mineral", "Material" })
{
    foreach (var item in await items.GetAllByTypeAsync(type))
    {
        if (string.IsNullOrWhiteSpace(item.Emoji) || !item.Emoji.StartsWith("<:"))
        {
            missing.Add($"{type}/{item.Name}");
        }
    }
}
Check(missing.Count == 0, "todo ítem obtenible (cajas, huevos, comida, madera, minerales y drops) tiene emoji propio en la base" + (missing.Count == 0 ? string.Empty : " — SIN: " + string.Join(", ", missing)));

Console.WriteLine("\n== 4. Las pantallas que antes escribían el emoji común ==");
// El daily: la caja del día con su emoji.
var player = new User { DiscordId = 1, Level = 5, Class = "Guerrero" };
string escoria = ItemEmojiRegistry.IconOf("Cofre de Escoria", "?");
var daily = DailyModule.BuildResultEmbed(new DailyClaimCalculation(DailyClaimStatus.StreakContinued, 7, null), new LevelUpOutcome(player, 0), new RewardReceipt(new ResolvedReward(2900, 300, "Cofre de Escoria", 1), 1000, 0, 10));
Check(AllText(daily).Contains($"{escoria} Cofre de Escoria") && escoria.StartsWith("<:"), "el daily muestra la caja con el emoji del Cofre de Escoria");
// El voto: tema de ayuda.
string robleReal = ItemEmojiRegistry.IconOf(VoteRules.RewardBoxName, "?");
Check(AllText(HelpTopics.Find("vote")!.Build()).Contains($"{robleReal} {VoteRules.RewardBoxName}") && robleReal.StartsWith("<:"), "el tema de ayuda del voto muestra el emoji del Baúl de Roble");
// Mascotas: huevo sin abrir.
var pradera = new PetSpecies(1, 1, "Praderas del Mate", "Ñandusito", "🦤", "gold", 5, 1, "Huevo de la Pradera");
var view = PetModule.BuildViewEmbed(new PetModule.PetViewData([pradera], [], 3, new HashSet<string> { "Huevo de la Pradera" }, DateTime.UtcNow));
string huevoReal = ItemEmojiRegistry.IconOf("Huevo de la Pradera", "?");
Check(AllText(view).Contains($"{huevoReal} Huevo de la Pradera") && huevoReal.StartsWith("<:"), "/pet: el huevo sin abrir sale con su emoji (no con 🥚)");
// /drops: el cofre del jefe con SU emoji (sale de la base).
var zones = new ZoneRepository(factory);
var monsters = new MonsterRepository(factory);
var drops = await DropsModule.BuildDropsMessageAsync(users, zones, monsters, items, 999000000000093999UL);
string dropsText = string.Join("\n", drops.Embeds.Select(AllText));
Check(dropsText.Contains($"{escoria} Cofre de Escoria"), "/drops muestra el Cofre de Escoria con su emoji propio (si en la base viva sale otra cosa, falta correr apply_emojis.sql)");
Check(!dropsText.Contains("📦"), "/drops no escribe ningún 📦 genérico delante de un cofre");

Console.WriteLine("\n== 5. Ranking: primero Fuego Nuevo, después nivel ==");
ulong[] ids = [999000000000093001UL, 999000000000093002UL, 999000000000093003UL, 999000000000093004UL, 999000000000093005UL];
foreach (var id in ids)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    await users.GetOrCreateUserAsync(id);
}
async Task Put(ulong id, int fn, int level, int xp) =>
    await db.ExecuteAsync("UPDATE users SET fuego_nuevo = @fn, level = @level, xp = @xp WHERE discord_id = @id", new { id = (long)id, fn, level, xp });
// Mismo FN 995: desempata el nivel (7 > 3) y después el XP (50 > 10). FN 994 con nivel 60 va DESPUÉS de todos los 995 aunque sea de más nivel. FN 0 con nivel 99 cierra.
await Put(ids[0], 995, 3, 10);
await Put(ids[1], 995, 3, 50);
await Put(ids[2], 995, 7, 0);
await Put(ids[3], 994, 60, 0);
await Put(ids[4], 0, 99, 0);
var top = await users.GetTopPlayersAsync(10000);
var idSet = ids.Select(i => (long)i).ToHashSet();
var order = top.Select(t => t.DiscordId).Where(d => idSet.Contains(d)).ToList();
Check(order.SequenceEqual([(long)ids[2], (long)ids[1], (long)ids[0], (long)ids[3], (long)ids[4]]), "orden: FN 995 nivel 7, FN 995 nivel 3 con más XP, FN 995 nivel 3, FN 994 nivel 60, FN 0 nivel 99 (el FN manda sobre el nivel)");
Check(top.First(t => t.DiscordId == (long)ids[3]).FuegoNuevo == 994 && top.First(t => t.DiscordId == (long)ids[4]).FuegoNuevo == 0 && top.First(t => t.DiscordId == (long)ids[2]).Level == 7, "cada fila trae su Fuego Nuevo y su nivel");
var board = await LeaderboardModule.BuildLeaderboardEmbedAsync(users);
string boardText = board.Description;
int p7 = boardText.IndexOf($"<@{ids[2]}>"), p3b = boardText.IndexOf($"<@{ids[1]}>"), p3a = boardText.IndexOf($"<@{ids[0]}>"), p60 = boardText.IndexOf($"<@{ids[3]}>");
Check(p7 >= 0 && p7 < p3b && p3b < p3a && p3a < p60, "el embed del ranking respeta ese orden");
Check(boardText.Contains($"<@{ids[2]}> — 🔥 FN **995** · Nivel **7**") && boardText.Contains($"<@{ids[3]}> — 🔥 FN **994** · Nivel **60**"), "cada línea muestra el FN y el nivel de la persona");
string footer = board.Footer?.Text ?? string.Empty;
Check(footer.Contains("Fuego Nuevo") && !footer.Contains("ordena por nivel;"), "el pie explica el nuevo criterio");
var slashAttr = typeof(LeaderboardModule).GetMethod("HandleLeaderboardAsync")!.GetCustomAttributes(typeof(Discord.Interactions.SlashCommandAttribute), false).Cast<Discord.Interactions.SlashCommandAttribute>().Single();
Check(slashAttr.Description.Length <= 100 && slashAttr.Description.Contains("Fuego Nuevo"), $"la descripción del slash ({slashAttr.Description.Length} caracteres) entra en los 100 de Discord y dice el criterio");

foreach (var id in ids)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
}
ItemEmojiRegistry.Set([]);

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
