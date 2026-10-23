using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// v0.26.1: la tablita del resumen del combate se puede apagar desde /config (aa config | conf | set): las palabras que se escriben, la tabla player_settings, el servicio con su caché (que nunca tira y no repite la
// consulta que falla), la pantalla en los dos idiomas con su botón, los comandos de texto, el ruteo del botón con el InteractionService de verdad y que la pelea arranque respetando la opción.
// Contra la base LOCAL (usuarios descartables).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong S1 = 999000000000096101UL, S2 = 999000000000096102UL, S3 = 999000000000096103UL, S4 = 999000000000096104UL, S5 = 999000000000096105UL, S9 = 999000000000096109UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Loc.LoadEmbedded();   // el catálogo en inglés viaja incrustado en el ensamblado: sin esto Loc.Use("en") no traduce nada
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
async Task Clean()
{
    foreach (var id in new[] { S1, S2, S3, S4, S5, S9 })
    {
        await db.ExecuteAsync("DELETE FROM player_settings WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

await Clean();

Console.WriteLine("== 1. Las palabras que se escriben ==");
Check(new[] { "resumen", "Resumen", "summary", "FIGHT SUMMARY", "fight summary", "resumen del combate", "tablita" }.All(w => SettingWords.KeyOf(w) == SettingKey.FightSummary) && SettingWords.KeyOf("xyzzy") is null && SettingWords.KeyOf("") is null && SettingWords.KeyOf(null) is null,
    "la opción se nombra «resumen», «summary», «fight summary»…, sin importar mayúsculas");
Check(new[] { "on", "ON", "si", "sí", "yes", "true", "1", "activar", "mostrar", "show" }.All(w => SettingWords.ParseSwitch(w) == (true, false)) && new[] { "off", "no", "false", "0", "desactivar", "ocultar", "hide" }.All(w => SettingWords.ParseSwitch(w) == (false, false))
    && new[] { "toggle", "cambiar", "alternar" }.All(w => SettingWords.ParseSwitch(w) == (null, true)) && SettingWords.ParseSwitch("quizás") == (null, false) && SettingWords.ParseSwitch(null) == (null, false),
    "on/off, si/no, mostrar/ocultar y show/hide (español e inglés); toggle invierte; lo demás no se entiende");

Console.WriteLine("\n== 2. La tabla player_settings ==");
var repo = new SettingsRepository(factory);
Check(await repo.GetAsync(S1) is null, "un jugador sin fila no tiene nada guardado (se usa lo de siempre)");
await repo.SetShowFightSummaryAsync(S1, false);
Check(await repo.GetAsync(S1) is { ShowFightSummary: false }, "apagar el resumen se guarda");
await repo.SetShowFightSummaryAsync(S1, true);
Check(await repo.GetAsync(S1) is { ShowFightSummary: true } && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_settings WHERE discord_id = @id", new { id = (long)S1 }) == 1, "volver a prenderlo pisa la misma fila (una sola por jugador)");
var race = Enumerable.Range(0, 20).Select(i => repo.SetShowFightSummaryAsync(S2, i % 2 == 0)).ToArray();
await Task.WhenAll(race);
Check(await db.ExecuteScalarAsync<int>("SELECT count(*) FROM player_settings WHERE discord_id = @id", new { id = (long)S2 }) == 1, "20 cambios a la vez dejan una sola fila y ninguno tira");
Check(PlayerSettings.Default.ShowFightSummary, "lo de siempre (el resumen prendido) es el valor por defecto");

Console.WriteLine("\n== 3. El servicio: caché y fallas ==");
var service = new PlayerSettingsService(repo);
Check((await service.GetAsync(S3)).ShowFightSummary, "sin nada guardado el servicio dice que el resumen va");
await service.SetShowFightSummaryAsync(S3, false);
Check(!(await service.GetAsync(S3)).ShowFightSummary && !(await new PlayerSettingsService(repo).GetAsync(S3)).ShowFightSummary, "lo apagado queda en el caché y también lo lee un servicio nuevo (viene de la base)");
var flakyRepo = new CountingThrowingRepo(); var flaky = new PlayerSettingsService(flakyRepo);
for (ulong i = 0; i < 30; i++) { await flaky.GetAsync(990000000000 + i); }
Check(flakyRepo.Calls == 1 && (await flaky.GetAsync(990000000099)).ShowFightSummary, $"con la base fallando, 30 lecturas hacen UNA sola consulta y todas dicen «se muestra» ({flakyRepo.Calls})");
bool threw = false; try { await new PlayerSettingsService(new CountingThrowingRepo()).SetShowFightSummaryAsync(S4, false); } catch (InvalidOperationException) { threw = true; }
Check(threw && !await ConfigModule.TrySetSummaryAsync(new PlayerSettingsService(new CountingThrowingRepo()), S4, false), "guardar SÍ tira en el servicio (para poder avisar) y la pantalla lo atrapa: TrySetSummaryAsync devuelve false");

Console.WriteLine("\n== 4. La pantalla /config ==");
var screenOn = await ConfigModule.BuildScreenAsync(service, S5);
await service.SetShowFightSummaryAsync(S5, false);
var screenOff = await ConfigModule.BuildScreenAsync(service, S5);
ConfigScreen screenEn; using (Loc.Use("en")) { screenEn = await ConfigModule.BuildScreenAsync(service, S5); }
ConfigScreen screenOnEn; await service.SetShowFightSummaryAsync(S5, true); using (Loc.Use("en")) { screenOnEn = await ConfigModule.BuildScreenAsync(service, S5); }
(string Label, string Id, ButtonStyle Style)? ButtonOf(ConfigScreen s) => s.Components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().Select(b => (b.Label, b.CustomId, b.Style)).FirstOrDefault();
Check(screenOn.Embed.Title == "⚙️ Configuración" && screenOn.Embed.Fields[0].Name == "📋 Resumen del combate" && screenOn.Embed.Fields[0].Value.Contains("**Activado**") && ButtonOf(screenOn) is { Label: "Ocultar el resumen", Style: ButtonStyle.Secondary } on && on.Id == $"config_summary:{S5}:off",
    "con el resumen prendido: dice «Activado» y el botón lo oculta (el id lleva al dueño y el valor al que pasa)");
Check(screenOff.Embed.Fields[0].Value.Contains("**Desactivado**") && ButtonOf(screenOff) is { Label: "Mostrar el resumen", Style: ButtonStyle.Success } off && off.Id == $"config_summary:{S5}:on", "con el resumen apagado: dice «Desactivado» y el botón lo muestra");
Check(screenEn.Embed.Title == "⚙️ Settings" && screenEn.Embed.Fields[0].Name == "📋 Fight summary" && screenEn.Embed.Fields[0].Value.Contains("**Off**") && ButtonOf(screenEn)!.Value.Label == "Show the summary" && ButtonOf(screenOnEn)!.Value.Label == "Hide the summary" && screenEn.Embed.Description!.Contains("`/language`"),
    $"y en inglés ({screenEn.Embed.Title} | {screenEn.Embed.Fields[0].Value})");
string screenEnText = LeakCheck.AllText(screenEn.Embed) + " " + ButtonOf(screenEn)!.Value.Label + " " + LeakCheck.AllText(screenOnEn.Embed) + " " + ButtonOf(screenOnEn)!.Value.Label;
Check(LeakCheck.NoSpanish(screenEnText, []).Ok && LeakCheck.FitsDiscord(screenEn.Embed), $"la pantalla en inglés no deja español colado ({LeakCheck.NoSpanish(screenEnText, []).Found})");

Console.WriteLine("\n== 5. aa config (lo que se escribe) ==");
var (noteOpen, _) = await ConfigModule.ExecuteTypedAsync(service, S5, "", "");
Check(noteOpen is null, "«aa config» sin nada solo abre la pantalla");
var (noteOff, screenAfterOff) = await ConfigModule.ExecuteTypedAsync(service, S5, "resumen", "off");
Check(!(await service.GetAsync(S5)).ShowFightSummary && noteOff!.Contains("**desactivado**") && screenAfterOff.Embed.Fields[0].Value.Contains("**Desactivado**"), $"«aa config resumen off» lo apaga y la pantalla de vuelta ya está al día ({noteOff})");
var (noteOn, _) = await ConfigModule.ExecuteTypedAsync(service, S5, "summary", "on");
Check((await service.GetAsync(S5)).ShowFightSummary && noteOn!.Contains("**activado**"), "«aa config summary on» lo prende");
var (noteToggle, _) = await ConfigModule.ExecuteTypedAsync(service, S5, "resumen", "toggle");
Check(!(await service.GetAsync(S5)).ShowFightSummary && noteToggle!.Contains("**desactivado**"), "«toggle» lo invierte");
await ConfigModule.ExecuteTypedAsync(service, S5, "fight summary", "show");
Check((await service.GetAsync(S5)).ShowFightSummary, "«show» / «mostrar» también lo prenden");
var (noteUnknownValue, _) = await ConfigModule.ExecuteTypedAsync(service, S5, "resumen", "quizás");
var (noteUnknownOption, _) = await ConfigModule.ExecuteTypedAsync(service, S5, "color", "off");
Check((await service.GetAsync(S5)).ShowFightSummary && noteUnknownValue!.Contains("on") && noteUnknownValue.Contains("off") && noteUnknownOption!.Contains("No conozco esa opción"), "un valor o una opción que no se entiende no cambia nada y dice cómo se pide");
string noteEn; using (Loc.Use("en")) { (noteEn, _) = await ConfigModule.ExecuteTypedAsync(service, S5, "summary", "off"); }
Check(noteEn.Contains("Done") && noteEn.Contains("**off**") && LeakCheck.NoSpanish(noteEn, []).Ok, $"el aviso en inglés ({noteEn})");
await service.SetShowFightSummaryAsync(S5, true);
var (noteFail, _) = await ConfigModule.ExecuteTypedAsync(new FailingSetService(), S5, "resumen", "off");
Check(noteFail!.Contains("No pude guardar"), "si no se puede guardar, lo dice (no tira)");

Console.WriteLine("\n== 6. Comandos y botón registrados con Discord.Net de verdad ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp = (IServiceProvider)typeof(ConfigModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(ConfigModule).Assembly, sp);
var slash = interactions.SlashCommands.Single(c => c.Name == "config");
Check(slash.Description.Length is > 0 and <= 100 && slash.Parameters.Count == 0 && interactions.SlashCommands.Select(c => c.Module.SlashGroupName ?? c.Name).Distinct().Count() <= 100, $"/config existe, sin parámetros, con una descripción de {slash.Description.Length} caracteres (≤ 100) y siguen entrando los comandos de nivel superior");
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
var routed = interactions.SearchComponentCommand(Click($"config_summary:{S5}:off"));
Check(routed.IsSuccess && routed.Command.MethodName == "HandleSummaryButtonAsync" && routed.Command.Module.Name == "ConfigModule", "el botón config_summary:{dueño}:{valor} llega a su método");
var textCommands = new CommandService();
await textCommands.AddModuleAsync<TextCommandModule>(sp);
string? CommandFor(string typed) => textCommands.Search(typed) is { IsSuccess: true } found ? found.Commands.First().Command.Name : null;
var forms = new[] { "config", "conf", "set", "settings", "ajustes", "configuracion", "config resumen off", "set summary on", "conf fight summary" };
Check(forms.All(f => CommandFor(f) == "config"), $"aa config, conf, set, settings y ajustes (con o sin opción) son el mismo comando ({string.Join(", ", forms.Where(f => CommandFor(f) != "config").Select(f => f + "→" + CommandFor(f)))})");
Check(!FightLock.IsAllowed("config") && HelpModule.BuildInfoEmbed().Fields.Any(f => f.Value.Contains("`/config`")), "en plena pelea /config queda bloqueado (como todo lo que no es de la pelea) y /info lo nombra");

Console.WriteLine("\n== 7. La pelea arranca respetando la opción ==");
var userRepo = new UserRepository(factory);
await userRepo.GetOrCreateUserAsync(S9);
await db.ExecuteAsync("UPDATE users SET class = 'Guerrero', level = 3, current_hp = max_hp WHERE discord_id = @id", new { id = (long)S9 });
var starter = sp.GetRequiredService<IAdventureCombatStarter>();
var settingsService = sp.GetRequiredService<IPlayerSettingsService>();
await settingsService.SetShowFightSummaryAsync(S9, false);
var startedOff = await starter.PrepareHuntAsync(S9);
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)S9 });
await settingsService.SetShowFightSummaryAsync(S9, true);
var startedOn = await starter.PrepareHuntAsync(S9);
Check(startedOff.Status == CombatStartStatus.Started && startedOff.State!.ShowFightSummary == false && startedOn.Status == CombatStartStatus.Started && startedOn.State!.ShowFightSummary,
    $"con el resumen apagado la pelea nace con ShowFightSummary = false; prendido, true ({startedOff.Status}/{startedOn.Status})");
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)S9 });
var travelOff = await ((IAdventureCombatStarter)starter).PrepareTravelAsync(S9);
Check(travelOff.Status != CombatStartStatus.Started || travelOff.State!.ShowFightSummary, "y la que arranca sin haber cambiado nada sigue con el resumen prendido (/travel)");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

// Lo mismo que el repositorio de verdad, pero falla siempre y cuenta cuántas veces lo consultaron.
sealed class CountingThrowingRepo : ISettingsRepository
{
    public int Calls;
    public Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken ct = default) { Interlocked.Increment(ref Calls); throw new InvalidOperationException("simulado: la tabla no existe"); }
    public Task SetShowFightSummaryAsync(ulong discordId, bool value, CancellationToken ct = default) { Interlocked.Increment(ref Calls); throw new InvalidOperationException("simulado"); }
}

// Un servicio que lee bien pero NO puede guardar.
sealed class FailingSetService : IPlayerSettingsService
{
    public Task<PlayerSettings> GetAsync(ulong discordId) => Task.FromResult(PlayerSettings.Default);
    public Task SetShowFightSummaryAsync(ulong discordId, bool value) => throw new InvalidOperationException("simulado");
}

public class FakeProxy : DispatchProxy
{
    public Dictionary<string, object?> Values = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var v)) return v;
        return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
    }
    public static T Make<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)proxy).Values = values;
        return proxy;
    }
}
