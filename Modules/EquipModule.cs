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
            var item = await itemRepository.GetByNameAsync(itemName);
            if (item is null)
            {
                await FollowupAsync($"No encontré ningún ítem llamado **{itemName}**.", ephemeral: true);
                return;
            }

            if (item.Type is not ("Weapon" or "Amulet"))
            {
                await FollowupAsync($"**{item.Name}** es de tipo `{item.Type}` y no se puede equipar (solo Armas o Amuletos).", ephemeral: true);
                return;
            }

            int owned = await inventoryRepository.GetQuantityAsync(Context.User.Id, item.ItemId);
            if (owned <= 0)
            {
                await FollowupAsync($"No tenés **{item.Name}** en tu inventario.", ephemeral: true);
                return;
            }

            bool isWeapon = item.Type == "Weapon";
            var player = isWeapon
                ? await userRepository.EquipWeaponAsync(Context.User.Id, item.ItemId)
                : await userRepository.EquipAmuletAsync(Context.User.Id, item.ItemId);

            string slot = isWeapon ? "arma" : "amuleto";
            string emoji = isWeapon ? "🗡️" : "📿";

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle($"{emoji} ¡Equipado!")
                .WithDescription($"Ahora tenés equipada/o **{item.Name}** como {slot} ({item.Rarity}, +{item.StatValue}).")
                .WithColor(Color.Green)
                .Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude equipar ese ítem, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
