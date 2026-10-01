using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// partial: el resto de los subcomandos de "shop" (view, de solo lectura) vive en ShopModule.View.cs.
// Discord.Net no permite que dos módulos distintos declaren el mismo grupo de nivel superior
// (cada uno registraría su propio comando "/shop" y Discord rechaza el nombre duplicado), así
// que para poder separar archivos sin romper el grupo, ambos son la misma clase de C#.
// Las recetas de forja viven en /forge (ForgeModule.cs), separadas de la Tienda por diseño de juego.
[Group("shop", "Comprá objetos con tu oro.")]
public partial class ShopModule(IUserRepository userRepository, IItemRepository itemRepository, IShopRepository shopRepository, ICombatSessionService combatSessions, IGameEvents gameEvents, IBuffRepository buffRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /shop buy
    [SlashCommand("buy", "Comprá un consumible de la tienda.")]
    public async Task HandleBuyAsync(
        [Summary("item", "Elegí de la lista el consumible que querés comprar.")]
        [Autocomplete(typeof(BuyItemAutocompleteHandler))] string itemName,
        [Summary("cantidad", "Cuántos querés comprar (por defecto 1).")] int quantity = 1)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteBuyAsync(userRepository, itemRepository, shopRepository, combatSessions, gameEvents, Context.User.Id, itemName, quantity);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /shop buy] {ex}");
            await FollowupAsync("¡Upa! No pude procesar la compra, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /shop sell
    [SlashCommand("sell", "Vendé ítems de tu inventario a cambio de oro.")]
    public async Task HandleSellAsync(
        [Summary("item", "Elegí de la lista lo que querés vender.")] [Autocomplete(typeof(SellItemAutocompleteHandler))] string itemName,
        [Summary("cantidad", "Cuántos querés vender (por defecto 1).")] int quantity = 1)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteSellAsync(itemRepository, shopRepository, gameEvents, Context.User.Id, itemName, quantity);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /shop sell] {ex}");
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
            var result = await ExecuteSellAllAsync(shopRepository, gameEvents, Context.User.Id);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /shop sellall] {ex}");
            await FollowupAsync("¡Upa! No pude procesar la venta, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Todo lo que sigue es estático (sin dependencia de Context) para que
    // Modules/TextCommandModule.cs comparta exactamente la misma lógica en "aa shop buy/sell/sellall".
    // Exactamente uno de los dos campos del resultado viene con valor.
    public sealed record ShopActionResult(string? PlainMessage, Embed? Embed);

    public static async Task<ShopActionResult> ExecuteBuyAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IShopRepository shopRepository, ICombatSessionService combatSessions,
        IGameEvents gameEvents, ulong discordId, string itemName, int quantity)
    {
        // No se puede ir de compras en pleno combate — mismo espíritu que el bloqueo de /heal
        // (Modules/TavernModule.cs): comprar no debería ser una salida gratuita en medio de una pelea.
        if (combatSessions.Peek(discordId) is not null)
        {
            return new ShopActionResult("No podés ir de compras en medio de un combate. Terminalo (atacando o huyendo) antes de pasar por la tienda.", null);
        }

        if (quantity < 1)
        {
            return new ShopActionResult("La cantidad tiene que ser al menos 1.", null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null || !ShopCatalog.IsForSale(item))
        {
            return new ShopActionResult($"**{itemName}** no está disponible en la tienda (se vende comida y cajas: mirá **/shop view**).", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        await userRepository.GetOrCreateUserAsync(discordId);

        // Las cajas se compran de a una y una vez por hora (CooldownCatalog.BoxBuy): un freno a cuántas entran al juego. Se avisa ANTES de
        // tocar nada, así pedir 5 no gasta el cooldown. La comida no tiene límite.
        if (item.Type == "Caja" && quantity > ShopCatalog.BoxesPerPurchase)
        {
            return new ShopActionResult(
                $"Las cajas se compran **de a {ShopCatalog.BoxesPerPurchase}** y una compra por hora: pedí **{ItemDisplay.Format(item.Emoji, item.Name)}** con cantidad {ShopCatalog.BoxesPerPurchase}.", null);
        }

        int totalCost = item.BuyPrice * quantity;
        User? buyer;
        if (item.Type == "Caja")
        {
            var cooldownDefinition = CooldownCatalog.BoxBuy;
            var outcome = await shopRepository.BuyItemWithCooldownAsync(
                discordId, item.ItemId, quantity, totalCost, cooldownDefinition.CommandName, cooldownDefinition.Duration);

            if (outcome.CooldownRemaining is { } wait)
            {
                return new ShopActionResult(null, new EmbedBuilder()
                    .WithTitle($"{cooldownDefinition.Emoji} Ya compraste una caja hace poco")
                    .WithDescription($"Las cajas se compran de a una y **una vez por hora**. Te falta **{TimeFormat.Remaining(wait)}** para comprar otra.")
                    .WithColor(Color.DarkGrey)
                    .Build());
            }

            buyer = outcome.Buyer;
        }
        else
        {
            buyer = await shopRepository.BuyItemAsync(discordId, item.ItemId, quantity, totalCost);
        }

        if (buyer is null)
        {
            return new ShopActionResult($"No te alcanza el oro: **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} cuesta **{totalCost}**.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldSpent, amount: totalCost, detail: item.Name);

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle("🛒 ¡Compra realizada!")
            .WithDescription($"Compraste **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} por **{totalCost}** de oro.\nOro restante: **{buyer.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }

    public static async Task<ShopActionResult> ExecuteSellAsync(
        IItemRepository itemRepository, IShopRepository shopRepository, IGameEvents gameEvents, ulong discordId, string itemName, int quantity)
    {
        if (quantity < 1)
        {
            return new ShopActionResult("La cantidad tiene que ser al menos 1.", null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null)
        {
            return new ShopActionResult($"No encontré ningún ítem llamado **{itemName}**.", null);
        }

        if (item.SellPrice <= 0)
        {
            return new ShopActionResult($"**{ItemDisplay.Format(item.Emoji, item.Name)}** no se puede vender: es un premio.", null);
        }

        int totalRefund = item.SellPrice * quantity;
        var seller = await shopRepository.SellItemAsync(discordId, item.ItemId, quantity, totalRefund);

        if (seller is null)
        {
            return new ShopActionResult($"No tenés {quantity}x **{ItemDisplay.Format(item.Emoji, item.Name)}** para vender.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: totalRefund, detail: item.Name);

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle("💰 ¡Venta realizada!")
            .WithDescription($"Vendiste **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} por **{totalRefund}** de oro.\nOro total: **{seller.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }

    public static async Task<ShopActionResult> ExecuteSellAllAsync(IShopRepository shopRepository, IGameEvents gameEvents, ulong discordId)
    {
        var outcome = await shopRepository.SellAllAsync(discordId);

        if (outcome is null)
        {
            return new ShopActionResult("No tenés nada en tu inventario para vender.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: outcome.GoldEarned, detail: "sellall");

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle("💰 ¡Inventario liquidado!")
            .WithDescription($"Vendiste {outcome.ItemsSoldCount} ítems por **{outcome.GoldEarned}** de oro.\nOro total: **{outcome.Player.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }
}
