using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
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

// v0.37.1: «Huir» PREGUNTA antes de huir. El dueño (2026-10-29): «cada vez que se huya de una batalla necesito que se pregunte: ¿estás seguro?, cobarde o algo así… porque el guerrero al editar el mensaje
// en el celu me tira el huir donde está el atacar: si vas atacando rápido tocás sin querer». La pregunta es un mensaje PRIVADO aparte (el de la pelea no cambia, los botones no se mueven) y el «Sí, huir» lleva el
// id de ESTA pelea. Lo que se prueba: los botones de la pregunta (orden, estilo, ids, los dos idiomas), el id de cada pelea que arma el iniciador, cuándo vale el «Sí» (no hay pelea / es otra pelea), que el «Sí»
// huye de verdad editando el mensaje de la PELEA (no el privado) contra la base real, que «aa flee» sigue sin preguntar y el ruteo con el InteractionService de verdad (solo y en equipo).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

const ulong U = 999000000000099801UL, U2 = 999000000000099802UL;
const BindingFlags AnyS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

Loc.LoadEmbedded();
string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn, ["Discord:Token"] = "x" }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var adventure = new AdventureRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var zoneBoxes = new ZoneBoxService(factory, new ZoneRepository(factory));
var events = new RecordingEvents();

async Task Clean()
{
    foreach (var id in new[] { U, U2 })
    {
        await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)id });
        await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)id });
    }
}

await Clean();

CombatState State(Guid fightId, int hp = 80) =>
    new("hunt", "Toro de prueba", "🐂", 1000, 1000, 5, [], 0, 0, null, 100, hp, 100, 10, 0, 5, "Guerrero", ClassPassives.For("Guerrero"), FightId: fightId);

Console.WriteLine("== 1. Los botones de la pregunta ==");
var fightId = Guid.NewGuid();
var confirm = AdventureModule.BuildFleeConfirmButtons(fightId);
var buttons = confirm.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(buttons.Count == 2 && buttons[0].CustomId == $"btn_flee_no:{fightId:N}" && buttons[1].CustomId == $"btn_flee_yes:{fightId:N}", "dos botones: «No, a pelear» primero y «Sí, huir» segundo, con el id de la pelea");
Check(buttons[0].Style == ButtonStyle.Success && buttons[1].Style == ButtonStyle.Danger && buttons[0].Label == "No, a pelear" && buttons[1].Label == "Sí, huir", "«No» en verde y «Sí, huir» en rojo (para no confundirlos)");
using (Loc.Use("en"))
{
    var en = AdventureModule.BuildFleeConfirmButtons(fightId).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
    Check(en[0].Label == "No, keep fighting" && en[1].Label == "Yes, run" && en[0].CustomId == buttons[0].CustomId, "en inglés cambian las etiquetas y no los ids");
}

var raidId = Guid.NewGuid();
var raidButtons = RaidModule.BuildFleeConfirmButtons(raidId).Components.OfType<ActionRowComponent>().SelectMany(r => r.Components).OfType<ButtonComponent>().ToList();
Check(raidButtons.Count == 2 && raidButtons[0].CustomId == $"raid_flee_no:{raidId}" && raidButtons[1].CustomId == $"raid_flee_yes:{raidId}" && raidButtons[0].Style == ButtonStyle.Success && raidButtons[1].Style == ButtonStyle.Danger,
    "en el combate en equipo: los mismos dos botones con el id del raid");

Console.WriteLine("\n== 2. ¿Sigue valiendo el «Sí, huir»? ==");
var sessions = new CombatSessionService(users);
Check(AdventureModule.ResolveFleeConfirm(sessions, U, fightId.ToString("N")) == AdventureModule.FleeConfirmStatus.NoFight, "sin pelea en curso: «ya no tenés combate»");
var target = new FakeTarget();
sessions.TryStart(U, State(fightId), target);
Check(AdventureModule.ResolveFleeConfirm(sessions, U, fightId.ToString("N")) == AdventureModule.FleeConfirmStatus.Ok, "con LA pelea de la pregunta en curso: vale");
Check(AdventureModule.ResolveFleeConfirm(sessions, U, Guid.NewGuid().ToString("N")) == AdventureModule.FleeConfirmStatus.OtherFight && AdventureModule.ResolveFleeConfirm(sessions, U, "basura") == AdventureModule.FleeConfirmStatus.OtherFight,
    "con otra pelea en curso (la de la pregunta terminó) o un id roto: NO vale, no huye de la nueva");

Console.WriteLine("\n== 3. El «Sí, huir» huye de verdad ==");
await users.GetOrCreateUserAsync(U);
await db.ExecuteAsync("UPDATE users SET level = 5, max_hp = 100, current_hp = 100 WHERE discord_id = @id", new { id = (long)U });
var interaction = FakeProxy.Make<IDiscordInteraction>(new());
var output = new SessionTargetCombatTurnOutput(interaction);
await AdventureModule.ResolveTurnCoreAsync(sessions, users, items, adventure, inventory, buffs, events, zoneBoxes, U, fled: true, useAbility: false, output);
Check(sessions.Peek(U) is null && output.Completed, "la pelea termina y la pantalla del final se mostró");
Check(target.Updates == 1 && target.LastEmbed!.Title!.StartsWith("🏃 Huiste del combate contra Toro de prueba") && target.LastComponents!.Components.Count == 0,
    "se editó el mensaje de LA PELEA (el destino de la sesión), con el final y sin botones: no el mensaje privado de la pregunta");
Check(AdventureModule.ResolveFleeConfirm(sessions, U, fightId.ToString("N")) == AdventureModule.FleeConfirmStatus.NoFight, "tocar «Sí, huir» otra vez (o desde un mensaje viejo) ya no encuentra pelea");
var fledState = State(fightId);
Check(await db.ExecuteScalarAsync<int>("SELECT current_hp FROM users WHERE discord_id = @id", new { id = (long)U }) == 100 + fledState.ToDbHpDelta(80 - 100), "la vida con la que se huyó queda guardada (80 de combate, en unidades reales), como siempre");

Console.WriteLine("\n== 4. Cada pelea nueva trae su id ==");
var client = new DiscordSocketClient();
var interactions = new InteractionService(client);
var sp2 = (IServiceProvider)typeof(AdventureModule).Assembly.GetType("ServiceProviderBuilder")!.GetMethod("BuildServiceProvider", AnyS)!.Invoke(null, [client, interactions, cfg])!;
await users.GetOrCreateUserAsync(U2);
await db.ExecuteAsync("UPDATE users SET level = 5, current_zone_id = 1, max_hp = 100, current_hp = 100 WHERE discord_id = @id", new { id = (long)U2 });
var starter = sp2.GetRequiredService<IAdventureCombatStarter>();
var first = await starter.PrepareHuntAsync(U2);
await db.ExecuteAsync("DELETE FROM cooldowns WHERE discord_id = @id", new { id = (long)U2 });
var second = await starter.PrepareHuntAsync(U2);
Check(first.State is not null && second.State is not null && first.State.FightId != Guid.Empty && second.State.FightId != Guid.Empty && first.State.FightId != second.State.FightId,
    "el iniciador le pone un id distinto a cada pelea (así un «Sí» viejo no sirve para la nueva)");
Check((first.State! with { PlayerCurrentHp = 1 }).FightId == first.State.FightId, "el id sobrevive a los turnos (el estado se copia con «with»)");

Console.WriteLine("\n== 5. Ruteo con Discord.Net de verdad ==");
await interactions.AddModulesAsync(typeof(AdventureModule).Assembly, sp2);
IComponentInteraction Click(string id) => FakeProxy.Make<IComponentInteraction>(new() { ["Data"] = FakeProxy.Make<IComponentInteractionData>(new() { ["CustomId"] = id }) });
(string? Method, string? Module) Route(string id) { var r = interactions.SearchComponentCommand(Click(id)); return r.IsSuccess ? (r.Command.MethodName, r.Command.Module.Name) : (null, null); }
Check(Route("btn_flee") is ("HandleFleeAsync", "AdventureModule") && Route($"btn_flee_yes:{fightId:N}") is ("HandleFleeYesAsync", "AdventureModule") && Route($"btn_flee_no:{fightId:N}") is ("HandleFleeNoAsync", "AdventureModule"),
    "btn_flee ahora PREGUNTA (HandleFleeAsync) y los ids con el id de la pelea llegan a «sí» y «no»; «btn_flee» solo no cae en el «sí»");
Check(Route($"raid_flee:{raidId}") is ("HandleFleeAsync", "RaidModule") && Route($"raid_flee_yes:{raidId}") is ("HandleFleeYesAsync", "RaidModule") && Route($"raid_flee_no:{raidId}") is ("HandleFleeNoAsync", "RaidModule"),
    "lo mismo en el combate en equipo");
Check(Route("btn_attack") is ("HandleAttackAsync", _) && Route("btn_ability") is ("HandleAbilityAsync", _), "atacar y la habilidad siguen yendo directo (no preguntan)");

var textCommands = new CommandService();
await textCommands.AddModuleAsync<TextCommandModule>(sp2);
Check(textCommands.Search("flee") is { IsSuccess: true } f && f.Commands[0].Command.Name == "flee" && textCommands.Search("huir") is { IsSuccess: true }, "«aa flee» / «aa huir» siguen existiendo y huyen directo: escribirlo ya es a propósito");

await Clean();
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeTarget : ICombatReplyTarget
{
    public int Updates { get; private set; }
    public Embed? LastEmbed { get; private set; }
    public MessageComponent? LastComponents { get; private set; }
    public Task UpdateAsync(Embed embed, MessageComponent components) { Updates++; LastEmbed = embed; LastComponents = components; return Task.CompletedTask; }
}

sealed class RecordingEvents : IGameEvents
{
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) => Task.CompletedTask;
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
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
