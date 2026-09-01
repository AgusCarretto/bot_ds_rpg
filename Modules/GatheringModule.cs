using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class GatheringModule(
    ICooldownRepository cooldownRepository,
    IGatheringRepository gatheringRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository) : InteractionModuleBase<SocketInteractionContext>
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
            await userRepository.GetOrCreateUserAsync(Context.User.Id);

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

            bool applied = await gatheringRepository.ApplyGatheringRewardAsync(
                Context.User.Id, definition.CommandName, definition.Duration, item.ItemId, quantity: 1);

            if (!applied)
            {
                // Perdió la carrera contra otra ejecución concurrente del mismo comando (ej. doble click).
                await FollowupAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.", ephemeral: true);
                return;
            }

            await FollowupAsync(embed: BuildResultEmbed(definition, item));
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló procesando la recolección, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    private static Embed BuildCooldownEmbed(CooldownDefinition definition, TimeSpan remaining)
    {
        return new EmbedBuilder()
            .WithTitle($"⏳ {definition.Emoji} {definition.DisplayName}: todavía no podés")
            .WithDescription($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a intentarlo.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    private static Embed BuildResultEmbed(CooldownDefinition definition, Item item)
    {
        return new EmbedBuilder()
            .WithTitle($"{definition.Emoji} ¡{definition.DisplayName} exitoso!")
            .WithColor(RarityColor(item.Rarity))
            .WithDescription($"Conseguiste **{item.Name}**")
            .AddField("Rareza", item.Rarity, true)
            .Build();
    }

    private static Color RarityColor(string rarity) => rarity switch
    {
        "Común" => Color.LightGrey,
        "Raro" => Color.Blue,
        "Épico" => Color.Purple,
        "Legendario" => Color.Gold,
        "Mítico" => Color.Red,
        _ => Color.Default,
    };
}
