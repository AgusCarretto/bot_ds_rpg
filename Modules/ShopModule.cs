using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// partial: el resto de los subcomandos de "shop" (view, de solo lectura) vive en EconomyModule.cs.
// Discord.Net no permite que dos módulos distintos declaren el mismo grupo de nivel superior
// (cada uno registraría su propio comando "/shop" y Discord rechaza el nombre duplicado), así
// que para poder separar archivos sin romper el grupo, ambos son la misma clase de C#.
// Las recetas de forja viven en /forge (ForgeModule.cs), separadas de la Tienda por diseño de juego.
[Group("shop", "Comprá objetos con tu oro.")]
public partial class ShopModule(IUserRepository userRepository, IItemRepository itemRepository, IShopRepository shopRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /shop buy
    [SlashCommand("buy", "Comprá un consumible de la tienda.")]
    public async Task HandleBuyAsync(
        [Summary("item", "Nombre del consumible que querés comprar.")] string itemName,
        [Summary("cantidad", "Cuántos querés comprar (por defecto 1).")] int quantity = 1)
    {
        await DeferAsync();

        try
        {
            if (quantity < 1)
            {
                await FollowupAsync("La cantidad tiene que ser al menos 1.", ephemeral: true);
                return;
            }

            var item = await itemRepository.GetByNameAsync(itemName);
            if (item is null || item.Type != "Consumable")
            {
                await FollowupAsync($"**{itemName}** no está disponible en la tienda (solo se venden consumibles).", ephemeral: true);
                return;
            }

            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            await userRepository.GetOrCreateUserAsync(Context.User.Id);

            int totalCost = item.BuyPrice * quantity;
            var buyer = await shopRepository.BuyItemAsync(Context.User.Id, item.ItemId, quantity, totalCost);

            if (buyer is null)
            {
                await FollowupAsync($"No te alcanza el oro: **{item.Name}** x{quantity} cuesta **{totalCost}**.", ephemeral: true);
                return;
            }

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("🛒 ¡Compra realizada!")
                .WithDescription($"Compraste **{item.Name}** x{quantity} por **{totalCost}** de oro.\nOro restante: **{buyer.Gold}**.")
                .WithColor(Color.Green)
                .Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude procesar la compra, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /shop sell
    [SlashCommand("sell", "Vendé ítems de tu inventario a cambio de oro.")]
    public async Task HandleSellAsync(
        [Summary("item", "Nombre del ítem que querés vender.")] string itemName,
        [Summary("cantidad", "Cuántos querés vender (por defecto 1).")] int quantity = 1)
    {
        await DeferAsync();

        try
        {
            if (quantity < 1)
            {
                await FollowupAsync("La cantidad tiene que ser al menos 1.", ephemeral: true);
                return;
            }

            var item = await itemRepository.GetByNameAsync(itemName);
            if (item is null)
            {
                await FollowupAsync($"No encontré ningún ítem llamado **{itemName}**.", ephemeral: true);
                return;
            }

            int totalRefund = item.SellPrice * quantity;
            var seller = await shopRepository.SellItemAsync(Context.User.Id, item.ItemId, quantity, totalRefund);

            if (seller is null)
            {
                await FollowupAsync($"No tenés {quantity}x **{item.Name}** para vender.", ephemeral: true);
                return;
            }

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("💰 ¡Venta realizada!")
                .WithDescription($"Vendiste **{item.Name}** x{quantity} por **{totalRefund}** de oro.\nOro total: **{seller.Gold}**.")
                .WithColor(Color.Green)
                .Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude procesar la venta, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /shop sellall
    [SlashCommand("sellall", "Vendé todo tu inventario de una sola vez a cambio de oro.")]
    public async Task HandleSellAllAsync()
    {
        await DeferAsync();

        try
        {
            var outcome = await shopRepository.SellAllAsync(Context.User.Id);

            if (outcome is null)
            {
                await FollowupAsync("No tenés nada en tu inventario para vender.", ephemeral: true);
                return;
            }

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("💰 ¡Inventario liquidado!")
                .WithDescription($"Vendiste {outcome.ItemsSoldCount} ítems por **{outcome.GoldEarned}** de oro.\nOro total: **{outcome.Player.Gold}**.")
                .WithColor(Color.Green)
                .Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude procesar la venta, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
