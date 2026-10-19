using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Dapper;
using Discord;
using Microsoft.Extensions.Configuration;
using Npgsql;

// v0.14.5: jugar el turno ESCRIBIENDO (aa attack / aa ability / aa flee) y 60 s por turno. El turno solitario es el MISMO método que usa el botón (AdventureModule.ResolveTurnCoreAsync),
// acá probado con una salida falsa contra la base real (usuario descartable); el raid: el índice jugador -> raid y el "ese raid ya no existe".
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var users = new UserRepository(factory);
var items = new ItemRepository(factory);
var adventure = new AdventureRepository(factory);
var inventory = new InventoryRepository(factory);
var buffs = new BuffRepository(factory);
var zoneBoxes = new ZoneBoxService(factory, new ZoneRepository(factory));
var events = new RecordingEvents();
await using var db = new NpgsqlConnection(conn);
await db.OpenAsync();
const ulong U = 999000000000016000UL;

async Task Reset(int hp = 100, int gold = 0)
{
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
    await users.GetOrCreateUserAsync(U);
    await db.ExecuteAsync("UPDATE users SET level = 5, xp = 100, max_hp = 100, current_hp = @hp, gold = @gold WHERE discord_id = @id", new { id = (long)U, hp, gold });
}

CombatState State(string cmd = "hunt", int monsterHp = 1000, int monsterDmg = 1, int playerHp = 100, int startHp = 100, int playerDmg = 10, string cls = "Guerrero") =>
    new(cmd, "Toro de prueba", "🐂", monsterHp, monsterHp, monsterDmg, [], 0, 0, null, 100, playerHp, startHp, playerDmg, 0, 5, cls, ClassPassives.For(cls));

Task Turn(ICombatSessionService combat, bool fled, bool ability, FakeOutput output) =>
    AdventureModule.ResolveTurnCoreAsync(combat, users, items, adventure, inventory, buffs, events, zoneBoxes, U, fled, ability, output);

Console.WriteLine("== 1. El turno por texto (solitario) ==");
{
    await Reset();
    var combat = new CombatSessionService(users);
    var o0 = new FakeOutput();
    await Turn(combat, false, false, o0);
    Check(o0.Notes.Count == 1 && o0.Notes[0].Contains("No tenés ningún combate activo") && o0.Shown.Count == 0, "sin combate: avisa y no muestra nada");

    var first = new FakeTarget();
    Check(combat.TryStart(U, State(), first), "arranca un combate de prueba");
    var o1 = new FakeOutput();
    await Turn(combat, false, false, o1);
    var s1 = combat.Peek(U);
    Check(s1 is not null && s1.State.TurnsElapsed == 1 && s1.State.MonsterCurrentHp < 1000, "atacar: el turno se juega y el combate sigue (turno 1, el monstruo perdió vida)");
    Check(o1.Shown.Count == 1 && o1.Shown[0].Components.Components.Count > 0 && ReferenceEquals(o1.Shown[0].Previous, first) && ReferenceEquals(o1.Shown[0].Next, o1.Made) && ReferenceEquals(s1!.ReplyTarget, o1.Made),
        "la pantalla sale con botones, con el destino de antes como «previous» y la sesión sigue en el destino NUEVO");

    var o2 = new FakeOutput();
    await Turn(combat, false, true, o2);
    Check(o2.Shown.Count == 1 && combat.Peek(U)!.State.TurnsElapsed == 2 && combat.Peek(U)!.State.Ability.CooldownRemaining > 0, "la habilidad se usa y queda en enfriamiento");
    var o3 = new FakeOutput();
    await Turn(combat, false, true, o3);
    Check(o3.Shown.Count == 0 && o3.Notes.Count == 1 && o3.Notes[0].Contains("enfriamiento") && combat.Peek(U)!.State.TurnsElapsed == 2, "la habilidad en enfriamiento se rechaza SIN gastar el turno");
    var left = combat.Peek(U);
    if (left is not null)
    {
        combat.TryAdvance(U, left, null, null);
    }
}

{
    await Reset(hp: 100);
    var combat = new CombatSessionService(users);
    combat.TryStart(U, State(playerHp: 70, startHp: 100, cls: "Ninja"), new FakeTarget());
    var of = new FakeOutput();
    await Turn(combat, true, false, of);
    int hpAfter = await db.ExecuteScalarAsync<int>("SELECT current_hp FROM users WHERE discord_id = @id", new { id = (long)U });
    Check(combat.Peek(U) is null && of.Shown.Count == 1 && of.Shown[0].Components.Components.Count == 0 && of.Shown[0].Next is null && hpAfter == 70, $"huir: sin combate, pantalla final sin botones y la vida perdida queda guardada (HP {hpAfter})");
}

{
    await Reset(gold: 0);
    var combat = new CombatSessionService(users);
    combat.TryStart(U, State(monsterHp: 1, playerDmg: 50), new FakeTarget());
    var ov = new FakeOutput();
    await Turn(combat, false, false, ov);
    long gold = await db.ExecuteScalarAsync<long>("SELECT gold FROM users WHERE discord_id = @id", new { id = (long)U });
    Check(combat.Peek(U) is null && ov.Shown.Count == 1 && ov.Shown[0].Components.Components.Count == 0 && gold > 0 && events.Kinds.Contains(GameEventKinds.HuntWin), $"victoria: cobra el premio ({gold} de oro), registra el evento y deja la pantalla final");
}

{
    await Reset();
    await db.ExecuteAsync("UPDATE users SET xp = 500 WHERE discord_id = @id", new { id = (long)U });
    var combat = new CombatSessionService(users);
    combat.TryStart(U, State(monsterHp: 5000, monsterDmg: 99999, playerHp: 100, startHp: 100), new FakeTarget());
    var od = new FakeOutput();
    await Turn(combat, false, false, od);
    long xp = await db.ExecuteScalarAsync<long>("SELECT xp FROM users WHERE discord_id = @id", new { id = (long)U });
    Check(combat.Peek(U) is null && od.Shown.Count == 1 && od.Shown[0].Components.Components.Count == 0 && xp == 0, $"derrota: pantalla final y la penalidad por morir se aplica igual que con el botón (EXP {xp})");
}

Console.WriteLine("\n== 2. El destino que se reapunta y el tiempo por turno ==");
{
    var a = new FakeTarget(); var b = new FakeTarget();
    var sw = new SwitchableCombatReplyTarget(a);
    await sw.UpdateAsync(new EmbedBuilder().WithTitle("x").Build(), new ComponentBuilder().Build());
    await sw.ClearComponentsAsync();
    sw.Current = b;
    await sw.UpdateAsync(new EmbedBuilder().WithTitle("y").Build(), new ComponentBuilder().Build());
    Check(a.Updates == 1 && a.Clears == 1 && b.Updates == 1 && b.Clears == 0, "el destino reapuntable edita el de antes hasta que se lo cambia y después edita el nuevo");
    var timeout = (TimeSpan)typeof(CombatSessionService).GetField("TurnTimeout", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    Check(timeout == TimeSpan.FromSeconds(60), $"el tiempo por turno es de 60 segundos ({timeout.TotalSeconds:0})");
}

Console.WriteLine("\n== 3. Raid: el índice jugador -> raid ==");
{
    var raids = new RaidSessionService();
    var id = Guid.NewGuid();
    Check(raids.RaidIdOf(U) is null, "sin raid: null");
    raids.TryRegisterParticipant(U, id);
    Check(raids.RaidIdOf(U) == id && raids.IsInAnyRaid(U), "en un raid: devuelve su id");
    raids.UnregisterParticipant(U);
    Check(raids.RaidIdOf(U) is null, "al salir vuelve a null");
    var orf = new FakeRaidOutput();
    await RaidModule.ResolveAttackCoreAsync(raids, users, items, adventure, events, zoneBoxes, Guid.NewGuid(), U, false, orf);
    var orf2 = new FakeRaidOutput();
    await RaidModule.ResolveFleeCoreAsync(raids, users, Guid.NewGuid(), U, orf2);
    Check(orf.Notes.SequenceEqual(["Ese combate ya no existe."]) && orf2.Notes.SequenceEqual(["Ese combate ya no existe."]), "un raid que ya no existe avisa (atacar y huir) sin romper nada");
}

Console.WriteLine("\n== 4. Kit inicial de /start (v0.14.6: 5 Mate Amargo, una sola vez por cuenta) ==");
{
    const ulong K = 999000000000016001UL;
    async Task<int> Mates() => await db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0)::int FROM inventory inv JOIN items i USING (item_id) WHERE inv.discord_id = @id AND i.name = 'Mate Amargo'", new { id = (long)K });
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)K });

    var first = await users.CreateAccountAsync(K, "Ninja");
    Check(first.KitGranted && first.Player.Class == "Ninja" && first.Player.Gold == 50 && first.Player.Level == 1 && await Mates() == StarterKit.FoodQuantity, $"cuenta nueva: kit entregado ({await Mates()}× Mate Amargo), 50 de oro, nivel 1 y la clase elegida");

    var again = await users.CreateAccountAsync(K, "Guerrero");
    Check(!again.KitGranted && await Mates() == StarterKit.FoodQuantity && again.Player.Gold == 50, "la misma cuenta otra vez: NO se entrega de nuevo (sigue habiendo 5)");

    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)K });
    var race = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => users.CreateAccountAsync(K, "Arquero")));
    Check(race.Count(r => r.KitGranted) == 1 && await Mates() == StarterKit.FoodQuantity, $"12 clics a la vez sobre una cuenta nueva: el kit se entrega exactamente una vez ({race.Count(r => r.KitGranted)} entregas, {await Mates()}× Mate Amargo)");

    var food = await items.GetByNameAsync(StarterKit.FoodItemName);
    Check(food is { Type: "Consumable" } && food.BuyPrice <= 10, $"el ítem del kit existe y es la comida más barata ({food?.Name}, {food?.BuyPrice} de oro)");

    var confirm = typeof(OnboardingModule).GetMethod("BuildConfirmationEmbed", BindingFlags.NonPublic | BindingFlags.Static)!;
    var classDef = ClassCatalog.All.First(c => c.Name == "Ninja");
    var withKit = (Embed)confirm.Invoke(null, ["agus", first.Player, classDef, true])!;
    var withoutKit = (Embed)confirm.Invoke(null, ["agus", first.Player, classDef, false])!;
    Check(withKit.Fields.Any(f => f.Name == "🎁 Kit inicial" && f.Value.Contains("5× Mate Amargo") && f.Value.Contains("/heal")) && !withoutKit.Fields.Any(f => f.Name.Contains("Kit")), "la pantalla de bienvenida avisa del kit (y de /heal) solo cuando se entregó");
    await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)K });
}

await db.ExecuteAsync("DELETE FROM users WHERE discord_id = @id", new { id = (long)U });
Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;

sealed class FakeTarget : ICombatReplyTarget
{
    public int Updates, Clears;
    public Task UpdateAsync(Embed embed, MessageComponent components) { Updates++; return Task.CompletedTask; }
    public Task ClearComponentsAsync() { Clears++; return Task.CompletedTask; }
}

sealed class FakeOutput : ICombatTurnOutput
{
    public List<string> Notes { get; } = [];
    public List<(Embed Embed, MessageComponent Components, ICombatReplyTarget? Next, ICombatReplyTarget Previous)> Shown { get; } = [];
    public FakeTarget? Made { get; private set; }
    public Task NoteAsync(string text) { Notes.Add(text); return Task.CompletedTask; }
    public ICombatReplyTarget NextReplyTarget(ICombatReplyTarget previous) { Made = new FakeTarget(); return Made; }
    public Task ShowAsync(Embed embed, MessageComponent components, ICombatReplyTarget? next, ICombatReplyTarget previous) { Shown.Add((embed, components, next, previous)); return Task.CompletedTask; }
}

sealed class FakeRaidOutput : IRaidTurnOutput
{
    public List<string> Notes { get; } = [];
    public Task NoteAsync(string text) { Notes.Add(text); return Task.CompletedTask; }
    public Task ShowAsync(RaidSession session, Embed embed, MessageComponent components) => Task.CompletedTask;
    public Task PrepareFinalAsync(RaidSession session) => Task.CompletedTask;
}

sealed class RecordingEvents : IGameEvents
{
    public List<string> Kinds { get; } = [];
    public Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null) { Kinds.Add(kind); return Task.CompletedTask; }
    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId) => [];
}
