using System.Reflection;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Microsoft.Extensions.Configuration;

// v0.8.2: la cara de cada monstruo (emoji de la aplicación) como miniatura de los mensajes de combate, y las 4 maderas como emojis de la aplicación.
// Contra la base REAL (solo lectura).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

string conn = TestEnv.Connection();
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = conn }).Build();
var factory = new NpgsqlConnectionFactory(cfg);
var monsterRepo = new MonsterRepository(factory);
var itemRepo = new ItemRepository(factory);

Console.WriteLine("== 1. Las caras de los 20 monstruos están en la base ==");
var expectedFaces = new Dictionary<string, string>
{
    ["Jabalí Rabioso"] = "zona1_jabali:1556680082343460985", ["Ñandú Salvaje"] = "zona1_andu:1556680077649911869",
    ["Toro Bravo"] = "zona1_toro:1556680075817259108", ["Rey Jabalí"] = "zona1_reyjabali:1556680072222736585",
    ["Espíritu del Monte"] = "zona2_espiritu:1556681290277720084", ["Puma de las Cenizas"] = "zona2_puma:1556681288210055308",
    ["Ciervo Sagrado"] = "zona2_siervo:1556681286515560499", ["Lobisón Alfa"] = "zona2_reyLOBO:1556681284862877737",
    ["Excavador Profundo"] = "zona3_topo:1556682150114365540", ["Gólem del Yunque"] = "zona3_golem:1556682148512010350",
    ["Mole de Escoria"] = "zona3_mole:1556682147010584710", ["Capataz de Hierro"] = "zona3_reycapataz:1556682145416740964",
    ["Coloso de Magma"] = "zona4_coloso_de_magma:1556683367733264484", ["Salamandra Infernal"] = "zona4_salamandra_infernal:1556683354512949418",
    ["Dragón de Lava"] = "zona4_ragon_de_lava:1556683352675983380", ["Señor del Volcán"] = "zona4_senor_del_volcan:1556683350792601760",
    ["Devorador de Almas"] = "zona_5_devorador_de_almas:1556687063972061265", ["Titán de Escoria"] = "zona_5_titan_de_escoria:1556687062143336558",
    ["Quimera del Abismo"] = "zona_5_quimera_del_abismo:1556687060591444149", ["Soberano de la Escoria"] = "zona_5_soberano_de_la_escoria:1556687058809127062",
};
var all = await monsterRepo.GetAllAsync();
Check(all.Count == 20, $"hay {all.Count} monstruos (2 de cacería + viaje + jefe en cada una de las 5 zonas)");
var wrong = expectedFaces.Where(kv => all.Count(m => m.Monster.Name == kv.Key && m.Monster.Portrait == $"<:{kv.Value}>") != 1).Select(kv => kv.Key).ToList();
Check(wrong.Count == 0, $"cada monstruo tiene SU cara ({(wrong.Count == 0 ? "20 de 20" : "mal: " + string.Join(", ", wrong))})");
Check(all.All(m => ItemDisplay.ImageUrl(m.Monster.Portrait) is { } u && u.StartsWith("https://cdn.discordapp.com/emojis/")), "y todas dan una URL de imagen para la miniatura");
Check(all.Select(m => m.Monster.Portrait).Distinct().Count() == 20, "ninguna cara está repetida");
Check(all.All(m => !string.IsNullOrEmpty(m.Monster.Emoji)), "el emoji de texto de cada monstruo sigue ahí (los mensajes lo usan)");

Console.WriteLine("\n== 2. Las 4 Maderas son emojis de la aplicación ==");
foreach (var (name, code) in new[] { ("Madera de Pino", "madera_pino:1556678837482422392"), ("Madera de Roble", "madera_roble:1556678835339006023"), ("Madera de Nogal", "madera_nogal:1556678833569275914"), ("Madera de Ébano", "madera_ebano:1556678831841091715") })
{
    var item = await itemRepo.GetByNameAsync(name);
    Check(item?.Emoji == $"<:{code}>", $"{name}: {item?.Emoji}");
}

Console.WriteLine("\n== 3. La cara sale como miniatura en los mensajes de combate ==");
string face = "<:zona1_jabali:1556680082343460985>";
string faceUrl = ItemDisplay.ImageUrl(face)!;
CombatState State(string? portrait) => new("hunt", "Jabalí Rabioso", "🐗", 100, 60, 10, [], 0, 0, null, 100, 80, 100, 10, 2, 5, "Guerrero", ClassPassives.For("Guerrero"), MonsterPortrait: portrait);
var st = State(face);
var turn = new TurnResult(null, false, 12, 0, 0, 60, 100, new MonsterHitOutcome(false, 7), 93, default);
var player = new User { MaxHp = 100, CurrentHp = 80, Level = 5, Class = "Guerrero" };
var outcome = new LevelUpOutcome(player, 0);
var reward = new CombatReward(10, 10, false);
var dropped = new Item { ItemId = 1, Name = "Colmillo de Jabalí", Type = "Material", Rarity = "Raro", Emoji = "<:colmillo_jabali:1555578251605442581>" };
var food = new Item { ItemId = 2, Name = "Mate Amargo", Type = "Consumable", Rarity = "Común", Emoji = "<:mate_comun:1555578601892749433>" };
const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
Embed Call(Type t, string method, params object?[] args) => (Embed)t.GetMethod(method, Any)!.Invoke(null, args)!;
string? Thumb(Embed e) => e.Thumbnail?.Url;

Check(Thumb(AdventureModule.BuildEncounterEmbed(st)) == faceUrl, "/hunt, /travel y /boss: el encuentro trae la cara del monstruo");
Check(Thumb(Call(typeof(AdventureModule), "BuildOngoingEmbed", st, turn)) == faceUrl, "cada turno de la pelea la mantiene (si no, desaparecería a mitad de combate)");
Check(Thumb(Call(typeof(AdventureModule), "BuildDefeatEmbed", st, turn, null)) == faceUrl, "la derrota también");
Check(Thumb(Call(typeof(AdventureModule), "BuildFleeEmbed", st)) == faceUrl, "y huir");
Check(Thumb(Call(typeof(AdventureModule), "BuildVictoryEmbed", st, turn, reward, null, outcome, true)) == faceUrl, "la victoria SIN drop muestra la cara del monstruo");
Check(Thumb(Call(typeof(AdventureModule), "BuildVictoryEmbed", st, turn, reward, dropped, outcome, true)) == faceUrl, "la victoria CON drop TAMBIÉN muestra la cara del monstruo (v0.8.3: el dueño la quiere siempre; el ítem va en su campo)");
Check(Thumb(Call(typeof(UseModule), "BuildCombatOngoingEmbed", st, food, 5, false)) == faceUrl && Thumb(Call(typeof(UseModule), "BuildCombatDefeatEmbed", st, food, 5, null)) == faceUrl, "/use en plena pelea (curarse) no pierde la cara");
Check(Thumb(Call(typeof(AutoHuntModule), "BuildVictoryEmbed", st, outcome, reward, null)) == faceUrl && Thumb(Call(typeof(AutoHuntModule), "BuildDefeatEmbed", st, null)) == faceUrl, "/autohunt: victoria sin drop y derrota");
var noFace = State(null);
Check(Thumb(AdventureModule.BuildEncounterEmbed(noFace)) is null && Thumb(Call(typeof(AdventureModule), "BuildFleeEmbed", noFace)) is null, "un monstruo sin cara cargada no rompe nada (mensaje sin miniatura)");

var session = new RaidSession
{
    RaidId = Guid.NewGuid(), BossName = "Rey Jabalí", BossEmoji = "👑🐗", BossPortrait = "<:zona1_reyjabali:1556680072222736585>", BossBaseHp = 100, ZoneRank = 1, BossMaxHp = 200, BossDamage = 20,
    BossDropItemNames = [], BossGoldBonus = 0, BossXpBonus = 0, ZoneId = 1, ZoneName = "Praderas del Mate", StarterId = 1,
};
session.Participants.Add(new RaidParticipant { DiscordId = 1, DisplayName = "Tester", Damage = 20, Defense = 5, Level = 5, PlayerClass = "Guerrero", Passives = ClassPassives.For("Guerrero"), MaxHp = 100, StartingHp = 100, CurrentHp = 100 });
string bossUrl = ItemDisplay.ImageUrl(session.BossPortrait)!;
Check(Thumb(RaidModule.BuildLobbyEmbed(session)) == bossUrl && Thumb(RaidModule.BuildCombatEmbed(session, "log")) == bossUrl, "raid: la sala de espera y la pelea llevan la cara del jefe");
Check(Thumb(Call(typeof(RaidModule), "BuildWipeEmbed", session, "log")) == bossUrl, "raid: si el grupo cae también");
var results = new List<(RaidParticipant, CombatReward, Item?, LevelUpOutcome)>();
Check(Thumb(Call(typeof(RaidModule), "BuildVictoryEmbed", session, "log", results)) == bossUrl, "raid: al derrotarlo, el jefe vencido");

Console.WriteLine("\n== 4. El banner de nivel dice cuánto subís de ATQ y DEF ==");
var one = LevelUpCard.Compose(1UL, 7, 1, []);
string Field(LevelUpCardContent c, string startsWith) => c.Fields.Single(f => f.Name.Contains(startsWith)).Value;
Check(Field(one, "Ataque") == $"**+{CombatStats.BaseAttack(7) - CombatStats.BaseAttack(6)}**" && Field(one, "Defensa") == $"**+{CombatStats.BaseDefense(7) - CombatStats.BaseDefense(6)}**", $"un nivel: ATQ {Field(one, "Ataque")} y DEF {Field(one, "Defensa")} (los de la fórmula de /profile)");
Check(Field(one, "Vida máxima") == "**+15**" && Field(one, "vida").Contains("máximo"), "y la vida máxima y 'curada al máximo' siguen");
var three = LevelUpCard.Compose(1UL, 12, 3, []);
Check(Field(three, "Ataque") == $"**+{CombatStats.BaseAttack(12) - CombatStats.BaseAttack(9)}**" && Field(three, "Defensa") == $"**+{CombatStats.BaseDefense(12) - CombatStats.BaseDefense(9)}**" && Field(three, "Vida máxima") == "**+45**", $"tres niveles de una: suma los tres (ATQ {Field(three, "Ataque")}, DEF {Field(three, "Defensa")}, vida {Field(three, "Vida máxima")})");
Check(one.Fields.Where(f => f.Inline).Count() == 3 && one.Fields.Count(f => !f.Inline) == 1, "tres columnas (vida máxima, ataque, defensa) y 'tu vida' a todo el ancho abajo");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
