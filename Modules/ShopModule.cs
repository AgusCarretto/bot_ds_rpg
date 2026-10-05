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
public partial class ShopModule(IUserRepository userRepository, IItemRepository itemRepository, IShopRepository shopRepository, ICombatSessionService combatSessions, IGameEvents gameEvents, IBuffRepository buffRepository, IBoxContextService boxContextService)
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
            var result = await ExecuteBuyAsync(userRepository, itemRepository, shopRepository, combatSessions, gameEvents, Context.User.Id, itemName, quantity, boxContextService);
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
        IGameEvents gameEvents, ulong discordId, string itemName, int quantity, IBoxContextService? boxContext = null)
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
            return new ShopActionResult($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NotForSale)}\n**{itemName}** no está disponible en la tienda (se vende comida y cajas: mirá **/shop view**).", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        await userRepository.GetOrCreateUserAsync(discordId);

        // Una caja es de la zona de su rareza y solo se compra si ya desbloqueaste esa zona (el botín llega hasta ahí). Se avisa ANTES de tocar nada,
        // así no se cobra ni se gasta el cooldown de compra.
        if (item.Type == "Caja" && boxContext is not null)
        {
            int required = BoxCatalog.RequiredZoneRank(item.Rarity);
            int unlocked = await boxContext.MaxUnlockedRankAsync(discordId);
            if (required > unlocked)
            {
                return new ShopActionResult(
                    $"{NpcDialogue.Shopkeeper(ShopkeeperLine.NotForSale)}\n🔒 **{ItemDisplay.Format(item.Emoji, item.Name)}** es una caja de la **Zona {required}** y todavía no la desbloqueaste " +
                    $"(llegaste hasta la Zona {unlocked}): avanzá de zona para poder comprarla.", null);
            }
        }

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
                    .WithDescription($"{NpcDialogue.Shopkeeper(ShopkeeperLine.BoxWait)}\n\nLas cajas se compran de a una y **una vez por hora**. Te falta **{TimeFormat.Remaining(wait)}** para comprar otra.")
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
            return new ShopActionResult($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NoGold)}\nNo te alcanza el oro: **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} cuesta **{totalCost}**.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldSpent, amount: totalCost, detail: item.Name);

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle("🛒 ¡Compra realizada!")
            .WithDescription($"{NpcDialogue.Shopkeeper(ShopkeeperLine.BuySuccess)}\n\nCompraste **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} por **{totalCost}** de oro.\nOro restante: **{buyer.Gold}**.")
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
            return new ShopActionResult($"{NpcDialogue.Shopkeeper(ShopkeeperLine.Unsellable)}\n**{ItemDisplay.Format(item.Emoji, item.Name)}** no se puede vender: es un premio.", null);
        }

        // Un arma o un amuleto no vive en el inventario: se forja directo a equipamiento, así que lo que se vende es lo EQUIPADO (y es siempre de a
        // uno). Si no es lo que tiene puesto, sigue el camino de siempre (una copia suelta en el inventario, de antes del cambio).
        if (item.Type is "Weapon" or "Amulet" && quantity == 1)
        {
            var soldGear = await shopRepository.SellEquippedAsync(discordId, item.ItemId, item.SellPrice);
            if (soldGear is not null)
            {
                await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: item.SellPrice, detail: item.Name);

                string slotName = item.Type == "Weapon" ? "arma" : "amuleto";
                return new ShopActionResult(null, new EmbedBuilder()
                    .WithTitle("💰 ¡Venta realizada!")
                    .WithDescription(
                        $"{NpcDialogue.Shopkeeper(ShopkeeperLine.SellSuccess)}\n\nVendiste tu {slotName} equipad{(item.Type == "Weapon" ? "a" : "o")} **{ItemDisplay.Format(item.Emoji, item.Name)}** por **{item.SellPrice}** de oro.\n" +
                        $"Oro total: **{soldGear.Gold}**.\n\n_Te quedaste sin {slotName}: forjá otr{(item.Type == "Weapon" ? "a" : "o")} en **/forge**._")
                    .WithColor(Color.Green)
                    .Build());
            }
        }

        int totalRefund = item.SellPrice * quantity;
        var seller = await shopRepository.SellItemAsync(discordId, item.ItemId, quantity, totalRefund);

        if (seller is null)
        {
            return new ShopActionResult($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NotOwned)}\nNo tenés {quantity}x **{ItemDisplay.Format(item.Emoji, item.Name)}** para vender.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: totalRefund, detail: item.Name);

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle("💰 ¡Venta realizada!")
            .WithDescription($"{NpcDialogue.Shopkeeper(ShopkeeperLine.SellSuccess)}\n\nVendiste **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} por **{totalRefund}** de oro.\nOro total: **{seller.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }

    public static async Task<ShopActionResult> ExecuteSellAllAsync(IShopRepository shopRepository, IGameEvents gameEvents, ulong discordId)
    {
        var outcome = await shopRepository.SellAllAsync(discordId);

        if (outcome is null)
        {
            return new ShopActionResult(NpcDialogue.Shopkeeper(ShopkeeperLine.NothingToSell) + "\nNo tenés nada en tu inventario para vender (las cajas y los premios no se venden).", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: outcome.GoldEarned, detail: "sellall");

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle("💰 ¡Inventario liquidado!")
            .WithDescription($"{NpcDialogue.Shopkeeper(ShopkeeperLine.SellAll)}\n\nVendiste {outcome.ItemsSoldCount} ítems por **{outcome.GoldEarned}** de oro.\nOro total: **{outcome.Player.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }
}
