using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// /drops: qué suelta cada monstruo de cada zona, para saber dónde conseguir cada material. Cada monstruo suelta UN
// solo ítem (ver Database/finalize_monster_roster.sql). Solo lectura: no crea cuentas ni toca nada.
public class DropsModule(
    IUserRepository userRepository,
    IZoneRepository zoneRepository,
    IMonsterRepository monsterRepository,
    IItemRepository itemRepository,
    IZoneBoxService zoneBoxService) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("drops", "Mirá qué suelta cada monstruo de cada zona (cazar, viajar y jefe) y con qué chance.")]
    public async Task HandleDropsAsync()
    {
        await DeferAsync();

        try
        {
            var message = await BuildDropsMessageAsync(userRepository, zoneRepository, monsterRepository, itemRepository, Context.User.Id, zoneBoxService);
            await FollowupAsync(message.Text, embeds: message.Embeds);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("No pude consultar los drops ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Un texto de cabecera + UN embed por zona (con su color, su título y un field por tipo de pelea) en vez de un
    // solo embed con un bloque enorme por zona: cada zona se lee como una tarjeta aparte. Todos los embeds de un
    // mensaje cuentan juntos para el límite de 6000 caracteres (son ~2500 con las 5 zonas).
    public sealed record DropsMessage(string Text, Embed[] Embeds);

    private static readonly Color[] ZoneColors = [Color.Green, Color.Orange, Color.Blue, Color.Red, Color.Purple];

    // Estático (sin Context) y público por el mismo motivo que ZoneModule.BuildZoneListEmbedAsync: lo reusa "aa drops".
    // GetByDiscordIdAsync y no GetOrCreateUserAsync: mirar esta lista no tiene que crear una cuenta.
    public static async Task<DropsMessage> BuildDropsMessageAsync(
        IUserRepository userRepository,
        IZoneRepository zoneRepository,
        IMonsterRepository monsterRepository,
        IItemRepository itemRepository,
        ulong discordId,
        IZoneBoxService? zoneBoxService = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
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

        var embeds = new List<Embed>();
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            string here = player is not null && zone.ZoneId == player.CurrentZoneId ? "  📍 _acá estás_" : string.Empty;

            var embed = new EmbedBuilder()
                .WithTitle($"{zone.Emoji} Zona {zone.ZoneId} · {zone.Name}")
                .WithColor(ZoneColors[i % ZoneColors.Length]);

            if (here.Length > 0)
            {
                embed.WithDescription(here.Trim());
            }

            var blocks = DropsCatalog.BuildZoneBlocks(
                monsters.Where(m => m.ZoneId == zone.ZoneId), dropItems, boxTable?.ForZone(zone.ZoneId, ZoneBoxRole.Repeat));
            if (blocks.Count == 0)
            {
                embed.AddField("Sin monstruos", "_Todavía no hay monstruos cargados en esta zona._", false);
            }

            foreach (var block in blocks)
            {
                embed.AddField(block.Title, block.Text, false);
            }

            embeds.Add(embed.Build());
        }

        return new DropsMessage(
            "🎁 **Qué suelta cada monstruo** — uno solo por monstruo, y solo si ganás la pelea (la chance va en cada bloque).",
            embeds.ToArray());
    }
}
