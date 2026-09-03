using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class EquipModule(IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /equip
    [SlashCommand("equip", "Equipate un arma o amuleto que tengas en tu inventario.")]
    public async Task HandleEquipAsync([Summary("item", "Nombre del arma o amuleto a equipar.")] string itemName)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteEquipAsync(userRepository, itemRepository, inventoryRepository, Context.User.Id, itemName);

            if (result.PlainMessage is not null)
            {
                await FollowupAsync(result.PlainMessage, ephemeral: true);
            }
            else
            {
                await FollowupAsync(embed: result.Embed);
            }
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude equipar ese ítem, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa equip". Exactamente uno de los dos campos del resultado
    // viene con valor.
    public sealed record EquipResult(string? PlainMessage, Embed? Embed);

    public static async Task<EquipResult> ExecuteEquipAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository, ulong discordId, string itemName)
    {
        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null)
        {
            return new EquipResult($"No encontré ningún ítem llamado **{itemName}**.", null);
        }

        if (item.Type is not ("Weapon" or "Amulet"))
        {
            return new EquipResult($"**{ItemDisplay.Format(item.Emoji, item.Name)}** es de tipo `{item.Type}` y no se puede equipar (solo Armas o Amuletos).", null);
        }

        int owned = await inventoryRepository.GetQuantityAsync(discordId, item.ItemId);
        if (owned <= 0)
        {
            return new EquipResult($"No tenés **{ItemDisplay.Format(item.Emoji, item.Name)}** en tu inventario.", null);
        }

        // Solo pedimos al jugador si el ítem tiene una clase exclusiva — evita una lectura de más
        // para el caso común (equipo genérico, class_requirement NULL).
        if (item.ClassRequirement is not null)
        {
            var currentPlayer = await userRepository.GetOrCreateUserAsync(discordId);
            if (!string.Equals(item.ClassRequirement, currentPlayer.Class, StringComparison.OrdinalIgnoreCase))
            {
                return new EquipResult(
                    $"**{ItemDisplay.Format(item.Emoji, item.Name)}** es exclusivo de la clase **{item.ClassRequirement}** — vos sos **{currentPlayer.Class}**.", null);
            }
        }

        bool isWeapon = item.Type == "Weapon";
        var player = isWeapon
            ? await userRepository.EquipWeaponAsync(discordId, item.ItemId)
            : await userRepository.EquipAmuletAsync(discordId, item.ItemId);

        string slot = isWeapon ? "arma" : "amuleto";
        string emoji = isWeapon ? "🗡️" : "📿";

        return new EquipResult(null, new EmbedBuilder()
            .WithTitle($"{emoji} ¡Equipado!")
            .WithDescription($"Ahora tenés equipada/o **{ItemDisplay.Format(item.Emoji, item.Name)}** como {slot} ({item.Rarity}, +{item.StatValue}).")
            .WithColor(Color.Green)
            .Build());
    }
}
