using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// /drops: qué suelta cada monstruo de cada zona, para saber dónde conseguir cada material. Cada monstruo suelta UN
// solo ítem (ver Database/finalize_monster_roster.sql). Solo lectura: no crea cuentas ni toca nada.
public class DropsModule(
    IUserRepository userRepository,
    IZoneRepository zoneRepository,
    IMonsterRepository monsterRepository,
    IItemRepository itemRepository) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("drops", "Mirá qué suelta cada monstruo de cada zona (cazar, viajar y jefe) y con qué chance.")]
    public async Task HandleDropsAsync()
    {
        await DeferAsync();

        try
        {
            var embed = await BuildDropsEmbedAsync(userRepository, zoneRepository, monsterRepository, itemRepository, Context.User.Id);
            await FollowupAsync(embed: embed);
        }
        catch (Exception)
        {
            await FollowupAsync("No pude consultar los drops ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin Context) y público por el mismo motivo que ZoneModule.BuildZoneListEmbedAsync: lo reusa "aa drops".
    // GetByDiscordIdAsync y no GetOrCreateUserAsync: mirar esta lista no tiene que crear una cuenta.
    public static async Task<Embed> BuildDropsEmbedAsync(
        IUserRepository userRepository,
        IZoneRepository zoneRepository,
        IMonsterRepository monsterRepository,
        IItemRepository itemRepository,
        ulong discordId)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        var monsters = await monsterRepository.GetAllAsync();
        var materials = (await itemRepository.GetAllByTypeAsync("Material"))
            .ToDictionary(item => item.Name, item => new DropItemInfo(item.Name, item.Rarity, item.Emoji));

        var embed = new EmbedBuilder()
            .WithTitle("🎁 Qué suelta cada monstruo")
            .WithColor(Color.Gold)
            .WithDescription(
                "Cada monstruo suelta **un solo** material, y solo si ganás la pelea (con la chance de abajo). " +
                "`/hunt` elige al azar entre los de **Cazar**; **Viajar** y **Jefe** son siempre el mismo.");

        foreach (var zone in zones)
        {
            string here = player is not null && zone.ZoneId == player.CurrentZoneId ? " 📍" : string.Empty;
            string text = DropsCatalog.BuildZoneText(monsters.Where(m => m.ZoneId == zone.ZoneId), materials);
            embed.AddField($"{zone.Emoji} Zona {zone.ZoneId}: {zone.Name}{here}", text, false);
        }

        return embed.Build();
    }
}
