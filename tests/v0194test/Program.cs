using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.19.4 (el dueño, 2026-10-20): «que el FN sea más personalizado que una llama, que el LV sea un emoji azul, que la experiencia sea XP, cambiar las barras».
// Los íconos propios de la interfaz (GameData/UiEmojis.cs) se leen por NOMBRE de los emojis de la aplicación (Services/UiEmojiRefresher.cs); sin ellos todo sale como siempre.
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

// Un código de emoji con la forma real de Discord para cada nombre (el id es inventado pero de 19 cifras).
string Code(string name, bool animated = false) => $"<{(animated ? "a" : string.Empty)}:{name}:{1000000000000000000L + Math.Abs(name.GetHashCode() % 100000)}>";
List<(string Name, string Code)> Full() => UiEmojis.AllNames.Select(n => (n, Code(n))).ToList();

// La barra de texto de ANTES, copiada tal cual: la nueva tiene que dar lo mismo cuando no hay emojis.
string OldRender(int current, int max, int segments = 10)
{
    if (max <= 0) return new string('░', segments);
    double ratio = Math.Clamp((double)current / max, 0, 1);
    int filled = (int)Math.Round(segments * ratio);
    return new string('█', filled) + new string('░', segments - filled);
}

Console.WriteLine("== 1. La tabla de íconos ==");
UiEmojis.Set([]);
Check(UiEmojis.AllNames.Count == 7 + 3 * 6 && UiEmojis.AllNames.Distinct().Count() == UiEmojis.AllNames.Count && UiEmojis.AllNames.All(n => n.Length is >= 2 and <= 32 && n.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')),
    $"hay {UiEmojis.AllNames.Count} nombres para subir (FN, LV, XP, oro, banco, polvo, racha y 6 piezas por 3 barras), sin repetidos y con la forma que Discord acepta (2-32, letras, números y «_»)");
Check(UiEmojis.AllNames.Contains("ui_fn") && UiEmojis.AllNames.Contains("ui_lv") && UiEmojis.AllNames.Contains("ui_xp") && UiEmojis.AllNames.Contains("bar_hp_l_f") && UiEmojis.AllNames.Contains("bar_xp_m_e") && UiEmojis.AllNames.Contains("bar_pesar_r_f"),
    "los nombres son ui_fn, ui_lv, ui_xp y bar_{hp|xp|pesar}_{l|m|r}_{f|e}");
Check(UiEmojis.FuegoNuevo == "🔥" && UiEmojis.Xp == "📊" && UiEmojis.Lv is null && UiEmojis.LevelTag(41) == "Nivel **41**" && UiEmojis.FuegoNuevoTag(3) == "🔥 FN **3**" && UiEmojis.Bar("hp") is null,
    "sin emojis subidos todo cae a lo de siempre: 🔥, 📊, «Nivel **41**», «🔥 FN **3**» y sin barra de emojis");
UiEmojis.Set([("ui_fn", Code("ui_fn", animated: true)), ("ui_lv", Code("ui_lv")), ("ui_xp", Code("ui_xp"))]);
Check(UiEmojis.FuegoNuevo.StartsWith("<a:ui_fn:") && UiEmojis.Lv!.StartsWith("<:ui_lv:") && UiEmojis.Xp.StartsWith("<:ui_xp:") && UiEmojis.LevelTag(41) == $"{UiEmojis.Lv} **41**" && UiEmojis.FuegoNuevoTag(3) == $"{UiEmojis.FuegoNuevo} **3**",
    "con los tres íconos subidos (el de FN animado, «<a:...>») las etiquetas los usan: «<LV> **41**» y «<FN> **3**» (el ícono ya dice FN, sin texto)");
UiEmojis.Set([("UI_LV", Code("ui_lv"))]);
Check(UiEmojis.Lv is not null, "el nombre se busca sin mirar mayúsculas");
UiEmojis.Set([("ui_lv", "no es un emoji"), ("ui_fn", "<:ui_fn:123>"), ("ui_xp", ":ui_xp:"), ("", Code("ui_lv")), ("ui_a", "<:ui_a:111111111111111111> y más texto")]);
Check(UiEmojis.Lv is null && UiEmojis.FuegoNuevo == "🔥" && UiEmojis.Xp == "📊", "un código que no tiene la forma «<:nombre:id>» (texto suelto, id corto, con texto de más) no entra: un dato raro no rompe un mensaje");
UiEmojis.Set(Full().Take(UiEmojis.AllNames.Count - 1));
Check(UiEmojis.Bar("hp") is not null && UiEmojis.Bar("xp") is not null && UiEmojis.Bar("pesar") is null, "una barra con una pieza que falta (la última del pesar) NO cuenta como subida: se usa la de texto");

Console.WriteLine("\n== 2. La barra de progreso ==");
UiEmojis.Set([]);
var samples = new (int c, int m, int s)[] { (0, 100, 10), (1, 100, 10), (50, 100, 10), (55, 100, 10), (99, 100, 10), (100, 100, 10), (150, 100, 10), (-5, 100, 10), (5, 0, 10), (5, -1, 10), (30, 100, 20), (7, 9, 4), (0, 0, 20) };
Check(samples.All(x => ProgressBar.Render(x.c, x.m, x.s) == OldRender(x.c, x.m, x.s) && ProgressBar.Render(x.c, x.m, x.s, "hp") == OldRender(x.c, x.m, x.s)),
    "sin emojis subidos la barra es idéntica a la de antes, también pidiendo un tipo (13 casos, incluidos vida cero, de más, negativa y máximo cero)");
UiEmojis.Set(Full());
var bp = UiEmojis.Bar("hp")!;
string Bar(int c, int m, int segs = 10, string kind = "hp") => ProgressBar.Render(c, m, segs, kind);
Check(Bar(50, 100) == bp.LeftFull + string.Concat(Enumerable.Repeat(bp.MidFull, 4)) + string.Concat(Enumerable.Repeat(bp.MidEmpty, 4)) + bp.RightEmpty, "50 % = extremo izquierdo lleno, 4 medios llenos, 4 medios vacíos y extremo derecho vacío");
Check(Bar(0, 100) == bp.LeftEmpty + string.Concat(Enumerable.Repeat(bp.MidEmpty, 8)) + bp.RightEmpty, "0 % = todo vacío, con sus dos extremos");
Check(Bar(100, 100) == bp.LeftFull + string.Concat(Enumerable.Repeat(bp.MidFull, 8)) + bp.RightFull && Bar(500, 100) == Bar(100, 100), "100 % = todo lleno y de más se queda en lleno");
Check(Bar(-5, 100) == Bar(0, 100) && Bar(5, 0) == Bar(0, 100) && Bar(5, -3) == Bar(0, 100), "vida negativa o máximo cero = todo vacío, nunca tira");
int Count(string s, string piece) => (s.Length - s.Replace(piece, string.Empty).Length) / piece.Length;
Check(Count(Bar(30, 100), bp.MidFull) == 2 && Count(Bar(30, 100), bp.LeftFull) == 1 && Count(Bar(30, 100), bp.RightFull) == 0, "30 % = 3 tramos llenos (el extremo izquierdo y 2 medios)");
Check(Bar(30, 100, 20) == Bar(30, 100, 10), "pedir 20 tramos con emojis se queda en el tope de 10 (un campo admite 1024 caracteres)");
Check(Bar(100, 100, 20).Length <= 1024 && Bar(100, 100, 20).Length == Bar(100, 100, 10).Length, $"la barra de emojis más larga mide {Bar(100, 100, 20).Length} caracteres: entra en un campo");
Check(ProgressBar.Render(50, 100, 1, "hp") == OldRender(50, 100, 1) && ProgressBar.Render(50, 100, 10) == OldRender(50, 100, 10), "con un solo tramo, o sin pedir tipo, sigue la barra de texto (el combate en columnas y las misiones no cambian)");
Check(ProgressBar.Render(50, 100, 10, "xp").Contains(UiEmojis.Bar("xp")!.MidFull) && UiEmojis.Bar("xp")!.MidFull != UiEmojis.Bar("hp")!.MidFull, "cada tipo de barra (vida, XP, pesar) usa SUS piezas");
Check(ProgressBar.Render(50, 100, 10, "inventado") == OldRender(50, 100, 10), "un tipo que no existe cae a la barra de texto");

Console.WriteLine("\n== 3. El refresco lee los emojis de la aplicación por nombre ==");
UiEmojis.Set([]);
int calls = 0;
var refresher = new UiEmojiRefresher(() => { calls++; return Task.FromResult<IEnumerable<(string, string)>>(new List<(string, string)> { ("ui_lv", Code("ui_lv")), ("espada_madera", Code("espada_madera")), ("roto", "basura") }); });
await refresher.RefreshAsync();
Check(calls == 1 && UiEmojis.Lv is not null && UiEmojis.Icon("espada_madera", "x").StartsWith("<:espada_madera:") && UiEmojis.Icon("roto", "x") == "x", "el refresco carga lo que devuelve la aplicación (también emojis que no son de la interfaz) y descarta lo roto");
await new UiEmojiRefresher(() => throw new InvalidOperationException("Discord no responde")).RefreshAsync();
await new UiEmojiRefresher(() => Task.FromException<IEnumerable<(string, string)>>(new HttpRequestException("caído"))).RefreshAsync();
Check(UiEmojis.Lv is not null, "si Discord no responde (tire sincrónico o asincrónico) el refresco no tira y queda la tabla que ya había");
await new UiEmojiRefresher(() => Task.FromResult<IEnumerable<(string, string)>>(Full())).RefreshAsync();
Check(UiEmojis.AllNames.All(n => UiEmojis.Icon(n, "").Length > 0) && UiEmojis.Bar("pesar") is not null, "con los 25 emojis subidos se arman las tres barras");

Console.WriteLine("\n== 4. Las pantallas ==");
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var itemsRepo = new ItemRepository(factory);
var zonesRepo = new ZoneRepository(factory);
var buffRepo = new BuffRepository(factory);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000094001UL, V = 999000000000094002UL;
foreach (var id in new[] { U, V })
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    await users.GetOrCreateUserAsync(id);
}
await db.ExecuteAsync("UPDATE users SET level = 41, xp = 120, fuego_nuevo = 3, class = 'Guerrero' WHERE discord_id = @id", new { id = (long)U });
await db.ExecuteAsync("UPDATE users SET level = 7, fuego_nuevo = 0 WHERE discord_id = @id", new { id = (long)V });

async Task<Embed> Profile(ulong id) => await GameModule.BuildProfileEmbedAsync(users, itemsRepo, zonesRepo, buffRepo, id, "T", "https://example.com/a.png");

UiEmojis.Set([]);
var plain = await Profile(U);
Check(plain.Title == "⚔️ T - Recién Llegado (Guerrero)" && plain.Description == "🔥 FN **3** · Nivel **41**" && plain.Fields[0].Name == "XP" && plain.Fields[0].Value.StartsWith("░░░░░░░░░░") && !plain.Fields[0].Value.Contains("<:") && plain.Fields[0].Value.Contains("120 / ") && plain.Fields[0].Value.EndsWith(" 📊"),
    $"perfil sin emojis subidos: el encabezado es «Nombre - Título (Clase)» (v0.23.0), la descripción dice «🔥 FN 3 · Nivel 41» (el FN va antes del nivel), el campo es «XP» con barra de texto y «120 / N 📊» sin repetir XP");
var plainV = await Profile(V);
Check(plainV.Description == "Nivel **7**" && !plainV.Description.Contains("FN"), "sin Fuego Nuevo la línea es solo el nivel (no aparece «FN 0»)");

UiEmojis.Set(Full());
bool StartsWithBar(string value, string kind) { var b = UiEmojis.Bar(kind)!; return value.StartsWith(b.LeftFull) || value.StartsWith(b.LeftEmpty); }
await db.ExecuteAsync("UPDATE users SET has_bank = true, bank_gold = 99, dust = 5, daily_streak = 2, gold = 1234 WHERE discord_id = @id", new { id = (long)U });
var rich = await Profile(U);
string lv = UiEmojis.Lv!, fn = UiEmojis.FuegoNuevo;
Check(rich.Description == $"{fn} **3** · {lv} **41**" && !rich.Title.Contains("<:") && rich.Fields[0].Name == "XP" && rich.Fields[0].Value.EndsWith($" {UiEmojis.Xp}") && StartsWithBar(rich.Fields[0].Value, "xp") && StartsWithBar(rich.Fields[1].Value, "hp"),
    "perfil con los emojis subidos: «<FN> 3 · <LV> 41» arriba, el campo XP con su ícono y las barras de XP y de vida con sus piezas (el título sin emojis propios)");
Check(rich.Length <= 6000 && rich.Fields.All(f => f.Value.Length <= 1024) && rich.Fields.All(f => !f.Inline), $"el perfil con barras de emojis entra en los límites de Discord ({rich.Length} de 6000) y sigue apilado");
string BlockLine(Embed e, string label) => e.Fields.Single(f => f.Name == "Estado").Value.Split('\n').First(l => l.StartsWith($"**{label}**"));
Check(BlockLine(rich, "Oro") == $"**Oro** 1.234 {UiEmojis.Gold}" && BlockLine(rich, "Banco") == $"**Banco** 99 {UiEmojis.Bank}" && BlockLine(rich, "Polvo") == $"**Polvo** 5 {UiEmojis.Dust}" && BlockLine(rich, "Racha") == $"**Racha** día 2 {UiEmojis.Streak}" && UiEmojis.Gold.StartsWith("<:ui_oro:") && UiEmojis.Streak.StartsWith("<:ui_racha:"),
    "con los íconos subidos, cada línea del bloque (Oro, Banco, Polvo, Racha) termina con el suyo");
Check(rich.Fields.Select(f => f.Name).SequenceEqual(["XP", "Vida", "Estado", "✨ Habilidad"]) && BlockLine(rich, "ATQ").StartsWith("**ATQ** ") && BlockLine(rich, "DEF").StartsWith("**DEF** "),
    "el perfil son cuatro campos: XP, Vida, el bloque «Estado» (ATQ, DEF, Oro, Banco...) y Habilidad");

// El ranking.
await db.ExecuteAsync("UPDATE users SET fuego_nuevo = 996 WHERE discord_id = @id", new { id = (long)U });
var board = await LeaderboardModule.BuildLeaderboardEmbedAsync(users);
Check(board.Description.Contains($"<@{U}> — {fn} **996** · {lv} **41** (Guerrero)"), "el ranking usa los íconos propios: «<FN> 996 · <LV> 41 (Guerrero)»");
UiEmojis.Set([]);
board = await LeaderboardModule.BuildLeaderboardEmbedAsync(users);
Check(board.Description.Contains($"<@{U}> — 🔥 FN **996** · Nivel **41** (Guerrero)"), "sin emojis subidos el ranking queda como en la v0.19.3");

// La subida de nivel.
var zones = (await zonesRepo.GetAllAsync()).ToList();
var cardPlain = LevelUpCard.Compose(U, 7, 1, zones, new Random(1));
Check(cardPlain.Description.Contains("⬆️ Nivel 6  ➜  **Nivel 7**"), "la subida de nivel sin emojis queda igual (Nivel 6 ➜ Nivel 7)");
UiEmojis.Set(Full());
var cardRich = LevelUpCard.Compose(U, 7, 1, zones, new Random(1));
Check(cardRich.Description.Contains($"⬆️ {UiEmojis.Lv} 6  ➜  {UiEmojis.Lv} **7**"), "con el «LV» subido la subida de nivel muestra «<LV> 6 ➜ <LV> 7»");

foreach (var id in new[] { U, V })
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
}
UiEmojis.Set([]);

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
