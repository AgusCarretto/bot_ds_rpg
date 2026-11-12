using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// /drops: qué suelta cada monstruo de cada zona, para saber dónde conseguir cada material. Cada monstruo suelta UN
// solo ítem (ver Database/finalize_monster_roster.sql). Solo lectura: no crea cuentas ni toca nada.
//
// v0.53.2 (el dueño, viendo las 10 zonas juntas: «es mucho emoji junto… de última por zonas y que puedas elegir la zona»):
// UNA zona por mensaje (abre en la tuya) con un desplegable para pasar a otra, y un renglón por monstruo con solo el emoji
// del ítem. Con 10 zonas el mensaje viejo (un embed por zona) además rozaba el límite de 6000 caracteres.
public class DropsModule(
    IUserRepository userRepository,
    IZoneRepository zoneRepository,
    IMonsterRepository monsterRepository,
    IItemRepository itemRepository,
    IZoneBoxService zoneBoxService) : InteractionModuleBase<SocketInteractionContext>
{
    private const string ZonePrefix = "drops_zone";

    [SlashCommand("drops", "Mirá qué suelta cada monstruo de cada zona (cazar, viajar y jefe) y con qué chance.")]
    public async Task HandleDropsAsync()
    {
        await DeferAsync();

        try
        {
            var message = await BuildDropsMessageAsync(userRepository, zoneRepository, monsterRepository, itemRepository, Context.User.Id, zoneBoxService);
            await FollowupAsync(NullIfEmpty(message.Text), embeds: message.Embeds, components: message.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("No pude consultar los drops ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // El desplegable de zonas: solo quien abrió la lista lo puede mover (el id lleva su id), y cambia el mensaje en el lugar.
    [ComponentInteraction($"{ZonePrefix}:*")]
    public async Task HandleZoneAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa lista es de otra persona: abrí la tuya con **/drops**."), ephemeral: true);
                return;
            }

            int? zoneId = int.TryParse(selected.FirstOrDefault(), out int parsed) ? parsed : null;
            var message = await BuildDropsMessageAsync(userRepository, zoneRepository, monsterRepository, itemRepository, Context.User.Id, zoneBoxService, zoneId);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = NullIfEmpty(message.Text);
                p.Embeds = message.Embeds;
                p.Components = message.Components;
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("No pude consultar los drops ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Text va vacío desde la v0.53.2 (la explicación pasó al footer); Components es el desplegable de zonas (nulo con una sola zona).
    public sealed record DropsMessage(string Text, Embed[] Embeds, MessageComponent? Components = null);

    private static readonly Color[] ZoneColors = [Color.Green, Color.Orange, Color.Blue, Color.Red, Color.Purple];

    public static string? NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;

    // Estático (sin Context) y público por el mismo motivo que ZoneModule.BuildZoneListEmbedAsync: lo reusa "aa drops".
    // GetByDiscordIdAsync y no GetOrCreateUserAsync: mirar esta lista no tiene que crear una cuenta.
    // zoneId: la zona pedida; si no está en la escalera del jugador (o no se pide), la zona donde está parado, y si no, la primera.
    public static async Task<DropsMessage> BuildDropsMessageAsync(
        IUserRepository userRepository,
        IZoneRepository zoneRepository,
        IMonsterRepository monsterRepository,
        IItemRepository itemRepository,
        ulong discordId,
        IZoneBoxService? zoneBoxService = null,
        int? zoneId = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        // Solo las zonas que su Fuego Nuevo ya abrió (v0.47.0): las de más arriba son una sorpresa.
        var zones = ZoneRanking.ForPlayer(await zoneRepository.GetAllAsync(), player?.FuegoNuevo ?? 0);
        if (zones.Count == 0)
        {
            var empty = new EmbedBuilder().WithTitle(Loc.T("🎁 Drops")).WithDescription(Loc.T("_Todavía no hay zonas cargadas._")).Build();
            return new DropsMessage(string.Empty, [empty]);
        }

        int index = IndexOf(zones, zoneId);
        if (index < 0) index = IndexOf(zones, player?.CurrentZoneId);
        if (index < 0) index = 0;
        var zone = zones[index];

        var monsters = await monsterRepository.GetAllAsync();
        // Materiales y cofres: el jefe suelta un cofre, y tiene que salir con su emoji igual que un material.
        var dropItems = (await itemRepository.GetAllByTypeAsync("Material")).Concat(await itemRepository.GetAllByTypeAsync("Caja"))
            .ToDictionary(item => item.Name, item => new DropItemInfo(item.Name, item.Rarity, item.Emoji));

        // La caja de las repeticiones del jefe sale de zone_boxes; si no se puede leer, el bloque del jefe solo dice qué pasa la primera vez.
        ZoneBoxTable? boxTable = null;
        if (zoneBoxService is not null)
        {
            try
            {
                boxTable = await zoneBoxService.GetAsync();
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        }

        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"{zone.Emoji} Zona {zone.ZoneId} · {zone.Name}"))
            .WithColor(ZoneColors[index % ZoneColors.Length])
            .WithFooter(zones.Count > 1
                ? Loc.T("Un solo ítem por monstruo, y solo si ganás · elegí otra zona en el menú")
                : Loc.T("Un solo ítem por monstruo, y solo si ganás"));

        if (player is not null && zone.ZoneId == player.CurrentZoneId)
        {
            embed.WithDescription(Loc.T("📍 _acá estás_"));
        }

        var blocks = DropsCatalog.BuildZoneBlocks(
            monsters.Where(m => m.ZoneId == zone.ZoneId), dropItems, boxTable?.ForZone(zone.ZoneId, ZoneBoxRole.Repeat), boxTable?.ForZone(zone.ZoneId, ZoneBoxRole.Travel));
        if (blocks.Count == 0)
        {
            embed.AddField(Loc.T("Sin monstruos"), Loc.T("_Todavía no hay monstruos cargados en esta zona._"), false);
        }

        foreach (var block in blocks)
        {
            embed.AddField(block.Title, block.Text, false);
        }

        return new DropsMessage(string.Empty, [embed.Build()], BuildZoneMenu(zones, zone.ZoneId, discordId));
    }

    // Una opción por zona de su escalera (hasta 10, el desplegable admite 25), sin emoji en la opción: el texto alcanza y un
    // emoji que Discord no acepte rechaza el mensaje entero. Con una sola zona no hay a dónde ir.
    private static MessageComponent? BuildZoneMenu(IReadOnlyList<Zone> zones, int shownZoneId, ulong ownerId)
    {
        if (zones.Count <= 1)
        {
            return null;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{ZonePrefix}:{ownerId}")
            .WithPlaceholder(Loc.T("Ver otra zona"))
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var zone in zones.Take(25))
        {
            string label = Loc.F($"Zona {zone.ZoneId} · {zone.Name}");
            menu.AddOption(label.Length <= 100 ? label : label[..99] + "…", zone.ZoneId.ToString(), isDefault: zone.ZoneId == shownZoneId);
        }

        return new ComponentBuilder().WithSelectMenu(menu).Build();
    }

    private static int IndexOf(IReadOnlyList<Zone> zones, int? zoneId)
    {
        if (zoneId is null) return -1;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].ZoneId == zoneId) return i;
        }

        return -1;
    }
}
