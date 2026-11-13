using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// El bestiario (v0.56.0; /bestiary, aa bestiario [zona]). El dueño (2026-11-12): «un bestiario para poder ver todos los monstruos por
// zonas, los desbloqueados y los que no con ???»; el 2026-11-13 eligió: se desbloquea VENCIENDO al monstruo, todo desde cero (no
// retroactivo), cada uno muestra cara, vida, daño, drop y cuántas veces lo venciste, y un logro «Cazador de Bestias»; y «que la vista
// sea por zonas, la Zona 1 en principio, con un menú como /drops para ver más zonas». Solo mira: nunca crea cuentas.
public class BestiaryModule(
    IUserRepository userRepository,
    IZoneRepository zoneRepository,
    IMonsterRepository monsterRepository,
    IBestiaryRepository bestiaryRepository) : InteractionModuleBase<SocketInteractionContext>
{
    private const string ZonePrefix = "bes_zone";
    private const string CardPrefix = "bes_card";

    [SlashCommand("bestiary", "Tu bestiario: los monstruos de cada zona que ya venciste (y los que te faltan).")]
    public async Task HandleBestiaryAsync([Summary("zona", "Qué zona mirar (sin nada, la Zona 1)")] int? zona = null)
    {
        await DeferAsync();

        try
        {
            var view = await BuildAsync(userRepository, zoneRepository, monsterRepository, bestiaryRepository, Context.User.Id, zona);
            await FollowupAsync(view.Error, embed: view.Embed, components: view.Components, ephemeral: view.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir el bestiario, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{ZonePrefix}:*")]
    public async Task HandleZoneAsync(string ownerRaw, string[] selected) =>
        await ReplaceAsync(ownerRaw, int.TryParse(selected.FirstOrDefault(), out int zone) ? zone : null, null);

    [ComponentInteraction($"{CardPrefix}:*:*")]
    public async Task HandleCardAsync(string ownerRaw, string zoneRaw, string[] selected) =>
        await ReplaceAsync(ownerRaw, int.TryParse(zoneRaw, out int zone) ? zone : null, selected.FirstOrDefault());

    private async Task ReplaceAsync(string ownerRaw, int? zoneId, string? monsterName)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Ese bestiario es de otra persona: abrí el tuyo con **/bestiary**."), ephemeral: true);
                return;
            }

            var view = await BuildAsync(userRepository, zoneRepository, monsterRepository, bestiaryRepository, ownerId, zoneId, monsterName);
            if (view.Embed is null)
            {
                await FollowupAsync(view.Error, ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir el bestiario, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    public sealed record BestiaryView(string? Error, Embed? Embed = null, MessageComponent? Components = null);

    // Una página del bestiario: la de una zona (sin monsterName) o la ficha de UN monstruo de esa zona (con monsterName, solo si ya lo venció).
    // Las zonas son las de su escalera (ZoneRanking.ForPlayer: las del Fuego Nuevo que todavía no tiene no aparecen) más El Fogón (zona 0).
    // Una zona pedida que no está abre la primera.
    public static async Task<BestiaryView> BuildAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IMonsterRepository monsterRepository, IBestiaryRepository bestiaryRepository,
        ulong discordId, int? zoneId = null, string? monsterName = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new BestiaryView(Loc.T("Todavía no tenés cuenta: empezá con **/start**."));
        }

        var pages = await PagesAsync(zoneRepository, monsterRepository, player.FuegoNuevo);
        if (pages.Count == 0)
        {
            return new BestiaryView(Loc.T("Todavía no hay monstruos cargados."));
        }

        var kills = await bestiaryRepository.GetKillsAsync(discordId);
        var page = pages.FirstOrDefault(p => p.ZoneId == zoneId) ?? pages[0];
        var card = monsterName is null ? null : page.Monsters.FirstOrDefault(m => m.Monster.Name == monsterName && kills.ContainsKey(m.Monster.Name));

        var embed = card is null ? BuildZoneEmbed(page, pages, kills) : BuildCardEmbed(page, card, kills[card.Monster.Name], player.FuegoNuevo);
        return new BestiaryView(null, embed, BuildComponents(discordId, page, pages, kills, card?.Monster.Name));
    }

    // Una zona del bestiario: su número, nombre y emoji, y sus monstruos en el orden cacería, viaje, jefe.
    public sealed record BestiaryPage(int ZoneId, string Title, IReadOnlyList<ZoneMonster> Monsters);

    public static async Task<IReadOnlyList<BestiaryPage>> PagesAsync(IZoneRepository zoneRepository, IMonsterRepository monsterRepository, int fuegoNuevo)
    {
        var ladder = ZoneRanking.ForPlayer(await zoneRepository.GetAllAsync(), fuegoNuevo);
        var monsters = await monsterRepository.GetAllAsync();
        var pages = new List<BestiaryPage>();
        foreach (var zone in ladder)
        {
            var list = monsters.Where(m => m.ZoneId == zone.ZoneId).OrderBy(m => KindOrder(m.Kind)).ThenBy(m => m.Monster.Name, StringComparer.Ordinal).ToList();
            if (list.Count > 0)
            {
                pages.Add(new BestiaryPage(zone.ZoneId, Loc.F($"{zone.Emoji} Zona {zone.ZoneId} · {zone.Name}"), list));
            }
        }

        // El Asador, en su propia página al final (El Fogón no es una zona de la escalera: GetAllAsync no lo trae).
        var gate = await zoneRepository.GetGateAsync();
        var asador = await monsterRepository.GetBossByZoneAsync(0);
        if (gate is not null && asador is not null)
        {
            pages.Add(new BestiaryPage(0, Loc.F($"{gate.Emoji} {gate.Name}"), [new ZoneMonster(0, MonsterKind.Boss, asador)]));
        }

        return pages;
    }

    private static int KindOrder(MonsterKind kind) => kind switch { MonsterKind.Hunt => 0, MonsterKind.Travel => 1, _ => 2 };

    private static string KindName(MonsterKind kind) => kind switch
    {
        MonsterKind.Hunt => Loc.T("cacería"),
        MonsterKind.Travel => Loc.T("viaje"),
        _ => Loc.T("jefe"),
    };

    // Lo que suelta, con el emoji de cada ítem; el monstruo de viaje no suelta material (da la caja de la zona a veces).
    private static string DropText(MonsterTemplate monster) => monster.DropItemNames.Count == 0
        ? Loc.T("sin drop")
        : string.Join(" / ", monster.DropItemNames.Select(name => ItemEmojiRegistry.Label(name, "📦")));

    private static int Unlocked(BestiaryPage page, IReadOnlyDictionary<string, long> kills) => page.Monsters.Count(m => kills.ContainsKey(m.Monster.Name));

    public static Embed BuildZoneEmbed(BestiaryPage page, IReadOnlyList<BestiaryPage> pages, IReadOnlyDictionary<string, long> kills)
    {
        var blocks = page.Monsters.Select(m =>
        {
            var monster = m.Monster;
            if (!kills.TryGetValue(monster.Name, out long count))
            {
                return Loc.F($"❓ **???** · {KindName(m.Kind)}");
            }

            return Loc.F($"{monster.Emoji} **{monster.Name}** · {KindName(m.Kind)} · vencido **{GameHistory.Number(count)}**") + "\n" +
                Loc.F($"❤️ {GameHistory.Number(monster.MinHp)}-{GameHistory.Number(monster.MaxHp)} · ⚔️ {GameHistory.Number(monster.MinDamage)}-{GameHistory.Number(monster.MaxDamage)} · 🎁 {DropText(monster)}");
        });

        int total = pages.Sum(p => p.Monsters.Count);
        int have = pages.Sum(p => Unlocked(p, kills));

        return new EmbedBuilder()
            .WithTitle(Loc.F($"📖 Bestiario · {page.Title}"))
            .WithColor(new Color(0x8D6E63))
            .WithDescription(string.Join("\n\n", blocks))
            .WithFooter(Loc.F($"Esta zona: {Unlocked(page, kills)} de {page.Monsters.Count} · en total: {have} de {total} · los ??? se descubren venciéndolos"))
            .Build();
    }

    public static Embed BuildCardEmbed(BestiaryPage page, ZoneMonster entry, long count, int fuegoNuevo)
    {
        var monster = entry.Monster;
        bool isBoss = entry.Kind == MonsterKind.Boss;
        string says = NpcDialogue.Monster(monster.Name, monster.Emoji, NpcDialogue.BossLine.Intro, isBoss, fuegoNuevo: fuegoNuevo);

        return new EmbedBuilder()
            .WithTitle(Loc.F($"📖 {monster.Name}"))
            .WithColor(new Color(0x8D6E63))
            .WithMonsterPortrait(monster.Portrait)
            .WithDescription(
                Loc.F($"{monster.Emoji} {KindName(entry.Kind)} · {page.Title}") + "\n\n" +
                Loc.F($"❤️ **Vida:** {GameHistory.Number(monster.MinHp)}-{GameHistory.Number(monster.MaxHp)}") + "\n" +
                Loc.F($"⚔️ **Daño:** {GameHistory.Number(monster.MinDamage)}-{GameHistory.Number(monster.MaxDamage)}") + "\n" +
                Loc.F($"🎁 **Suelta:** {DropText(monster)}") + "\n" +
                Loc.F($"🏆 **Lo venciste:** {GameHistory.Number(count)} veces") + "\n\n" +
                says)
            .WithFooter(Loc.T("Elegí otro monstruo o volvé a la zona con los menús"))
            .Build();
    }

    // Fila 1: las zonas (la que se ve, marcada). Fila 2: la ficha de un monstruo ya vencido de esta zona (si hay alguno).
    public static MessageComponent BuildComponents(ulong ownerId, BestiaryPage page, IReadOnlyList<BestiaryPage> pages, IReadOnlyDictionary<string, long> kills, string? cardName)
    {
        var builder = new ComponentBuilder();
        if (pages.Count > 1)
        {
            var zones = new SelectMenuBuilder().WithCustomId($"{ZonePrefix}:{ownerId}").WithPlaceholder(Loc.T("Ver otra zona")).WithMinValues(1).WithMaxValues(1);
            foreach (var p in pages.Take(25))
            {
                string label = p.Title.Length <= 100 ? p.Title : p.Title[..99] + "…";
                zones.AddOption(label, p.ZoneId.ToString(), Loc.F($"{Unlocked(p, kills)} de {p.Monsters.Count}"), isDefault: p.ZoneId == page.ZoneId && cardName is null);
            }

            builder.WithSelectMenu(zones, row: 0);
        }

        var known = page.Monsters.Where(m => kills.ContainsKey(m.Monster.Name)).ToList();
        if (known.Count > 0)
        {
            var cards = new SelectMenuBuilder().WithCustomId($"{CardPrefix}:{ownerId}:{page.ZoneId}").WithPlaceholder(Loc.T("Ver la ficha de…")).WithMinValues(1).WithMaxValues(1);
            foreach (var m in known)
            {
                cards.AddOption(m.Monster.Name, m.Monster.Name, KindName(m.Kind), isDefault: m.Monster.Name == cardName);
            }

            builder.WithSelectMenu(cards, row: 1);
        }

        return builder.Build();
    }
}
