using BotDsRpg.GameData;
using Discord.Commands;
using BotDsRpg.Services;

// Tercera parte de TextCommandModule (ver el comentario en TextCommandModule.cs): recolección.
public partial class TextCommandModule
{
    // "aa chop" / "aa ch" — misma lógica que GatheringModule.HandleChopAsync.
    [Command("chop")]
    [Alias("ch")]
    [Summary("Talá madera cercana (cooldown de 5 minutos).")]
    public Task ChopAsync() => GatherAsync(CooldownCatalog.Chop, itemType: "Madera");

    // "aa mine" / "aa m" — misma lógica que GatheringModule.HandleMineAsync.
    [Command("mine")]
    [Alias("m")]
    [Summary("Miná materiales cercanos (cooldown de 5 minutos).")]
    public Task MineAsync() => GatherAsync(CooldownCatalog.Mine, itemType: "Mineral");

    private async Task GatherAsync(CooldownDefinition definition, string itemType)
    {
        try
        {
            var remaining = await cooldownRepository.GetRemainingAsync(Context.User.Id, definition.CommandName, definition.Duration);
            if (remaining is not null)
            {
                await ReplyAsync(embed: GatheringModule.BuildCooldownEmbed(definition, remaining.Value));
                return;
            }

            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            await userRepository.GetOrCreateUserAsync(Context.User.Id);

            string rarity = RarityCatalog.RollGatheringRarity();
            var item = await itemRepository.GetRandomByTypeAndRarityAsync(itemType, rarity);

            if (item is null)
            {
                await ReplyAsync($"Todavía no hay materiales de tipo **{itemType}** y rareza **{rarity}** cargados en el catálogo.");
                return;
            }

            // Cuántas unidades salen depende de la rareza: lo común a montones, lo mejor de a una (GatheringYield).
            int quantity = GatheringYield.Roll(item.Rarity);

            bool applied = await gatheringRepository.ApplyGatheringRewardAsync(
                Context.User.Id, definition.CommandName, definition.Duration, item.ItemId, quantity);

            if (!applied)
            {
                await ReplyAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.");
                return;
            }

            await ReplyAsync(embed: GatheringModule.BuildResultEmbed(definition, item, quantity));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló procesando la recolección, intentá de nuevo en un momento.");
        }
    }
}
