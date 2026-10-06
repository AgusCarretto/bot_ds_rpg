using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

public class GatheringModule(
    ICooldownRepository cooldownRepository,
    IGatheringRepository gatheringRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IPlayerBonusService bonusService,
    IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("chop", "Talá madera cercana (cooldown de 5 minutos).")]
    public Task HandleChopAsync() =>
        RunGatheringAsync(CooldownCatalog.Chop, itemType: "Madera");

    [SlashCommand("mine", "Miná materiales cercanos (cooldown de 5 minutos).")]
    public Task HandleMineAsync() =>
        RunGatheringAsync(CooldownCatalog.Mine, itemType: "Mineral");

    private async Task RunGatheringAsync(CooldownDefinition definition, string itemType)
    {
        // La consulta de cooldown + la transacción pueden superar los 3s que da Discord
        // antes de que la interacción expire.
        await DeferAsync();

        try
        {
            var remaining = await cooldownRepository.GetRemainingAsync(Context.User.Id, definition.CommandName, definition.Duration);
            if (remaining is not null)
            {
                await FollowupAsync(embed: BuildCooldownEmbed(definition, remaining.Value), ephemeral: true);
                return;
            }

            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);

            string rarity = RarityCatalog.RollGatheringRarity();
            var item = await itemRepository.GetRandomByTypeAndRarityAsync(itemType, rarity);

            if (item is null)
            {
                // El catálogo todavía no tiene ítems cargados para esa combinación tipo+rareza.
                await FollowupAsync(
                    $"Todavía no hay materiales de tipo **{itemType}** y rareza **{rarity}** cargados en el catálogo.",
                    ephemeral: true);
                return;
            }

            // Cuántas unidades salen depende de la rareza: lo común a montones, lo mejor de a una (GatheringYield).
            int quantity = await RollQuantityAsync(bonusService, player, definition, item);

            bool applied = await gatheringRepository.ApplyGatheringRewardAsync(
                Context.User.Id, definition.CommandName, definition.Duration, item.ItemId, quantity);

            if (!applied)
            {
                // Perdió la carrera contra otra ejecución concurrente del mismo comando (ej. doble click).
                await FollowupAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.", ephemeral: true);
                return;
            }

            await GatheringEvents.RecordAsync(gameEvents, Context.User.Id, definition, item, quantity);

            await FollowupAsync(embed: BuildResultEmbed(definition, item, quantity));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló procesando la recolección, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Cuántas unidades da esta recolección: el sorteo por rareza (GatheringYield) por el multiplicador del jugador — Fuego Nuevo y la bendición de ESE oficio (Mano de Leñador en /chop,
    // Pico Fino en /mine), ver GameData/PlayerBonuses.cs. Pública para que "aa chop"/"aa mine" hagan exactamente lo mismo. Si no se pueden leer los bonus (el servicio nunca tira) da la cantidad base.
    public static async Task<int> RollQuantityAsync(IPlayerBonusService bonusService, User player, CooldownDefinition definition, Item item)
    {
        var bonuses = await bonusService.GetAsync((ulong)player.DiscordId, player.FuegoNuevo);
        double factor = definition.CommandName == CooldownCatalog.Chop.CommandName ? bonuses.ChopMultiplier : bonuses.MineMultiplier;
        return GatheringYield.RollScaled(item.Rarity, factor);
    }

    // Públicos para que Modules/TextCommandModule.cs arme los mismos embeds en "aa chop"/"aa mine".
    public static Embed BuildCooldownEmbed(CooldownDefinition definition, TimeSpan remaining)
    {
        return new EmbedBuilder()
            .WithTitle($"⏳ {definition.Emoji} {definition.DisplayName}: todavía no podés")
            .WithDescription($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a intentarlo.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildResultEmbed(CooldownDefinition definition, Item item, int quantity)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{definition.Emoji} ¡{definition.DisplayName} exitoso!")
            .WithColor(RarityColor(item.Rarity))
            .WithDescription($"Conseguiste **{quantity}× {ItemDisplay.Format(item.Emoji, item.Name)}**")
            .AddField("Rareza", item.Rarity, true)
            .WithItemThumbnail(item.Emoji);

        return embed.Build();
    }

    public static Color RarityColor(string rarity) => rarity switch
    {
        "Común" => Color.LightGrey,
        "Raro" => Color.Blue,
        "Épico" => Color.Purple,
        "Legendario" => Color.Gold,
        "Mítico" => Color.Red,
        _ => Color.Default,
    };
}
