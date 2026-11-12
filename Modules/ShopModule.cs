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
public partial class ShopModule(IUserRepository userRepository, IItemRepository itemRepository, IShopRepository shopRepository, ICombatSessionService combatSessions, IGameEvents gameEvents, IBuffRepository buffRepository, IBoxContextService boxContextService, IDragonRepository dragonRepository)
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
            var result = await ExecuteBuyAsync(userRepository, itemRepository, shopRepository, combatSessions, gameEvents, Context.User.Id, itemName, quantity, boxContextService, dragonRepository);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /shop buy] {ex}");
            await FollowupAsync(Loc.T("¡Upa! No pude procesar la compra, intentá de nuevo en un momento."), ephemeral: true);
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
            await FollowupAsync(Loc.T("¡Upa! No pude procesar la venta, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Comando barra: /shop sellall
    [SlashCommand("sellall", "Vendé tu mochila y el equipo que llevás puesto a cambio de oro (te pregunta antes).")]
    public async Task HandleSellAllAsync()
    {
        // v0.53.0: «vender todo» pregunta antes (el dueño: «preguntar por las dudas si estás seguro en vender todo») y ahora también vende el arma y el amuleto puestos. La pregunta es PRIVADA y se contesta con
        // sus dos botones (ShopButtonsModule), que dejan el resultado en ese mismo mensaje.
        await DeferAsync(ephemeral: true);

        try
        {
            var result = await BuildSellAllConfirmAsync(shopRepository, Context.User.Id);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, components: result.Components, ephemeral: true);
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /shop sellall] {ex}");
            await FollowupAsync(Loc.T("¡Upa! No pude procesar la venta, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Todo lo que sigue es estático (sin dependencia de Context) para que
    // Modules/TextCommandModule.cs comparta exactamente la misma lógica en "aa shop buy/sell/sellall".
    // Exactamente uno de los dos campos del resultado viene con valor.
    // Components (v0.53.0): los botones de la pregunta de «vender todo»; el resto de los resultados no lleva.
    public sealed record ShopActionResult(string? PlainMessage, Embed? Embed, MessageComponent? Components = null);

    public static async Task<ShopActionResult> ExecuteBuyAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IShopRepository shopRepository, ICombatSessionService combatSessions,
        IGameEvents gameEvents, ulong discordId, string itemName, int quantity, IBoxContextService? boxContext = null, IDragonRepository? dragonRepository = null)
    {
        // No se puede ir de compras en pleno combate — mismo espíritu que el bloqueo de /heal
        // (Modules/TavernModule.cs): comprar no debería ser una salida gratuita en medio de una pelea.
        if (combatSessions.Peek(discordId) is not null)
        {
            return new ShopActionResult(Loc.T("No podés ir de compras en medio de un combate. Terminalo (atacando o huyendo) antes de pasar por la tienda."), null);
        }

        // El dragón (v0.30.0) también se compra acá: «/shop buy dragon» o «aa shop buy dragon» (el dueño: «que sea shop buy dragon»), con el mismo cobro y las mismas reglas que desde la taberna. Es uno por cuenta: la cantidad no cuenta.
        if (dragonRepository is not null && DragonModule.IsDragonName(itemName))
        {
            return new ShopActionResult(await DragonModule.ExecuteBuyAsync(dragonRepository, discordId), null);
        }

        if (quantity < 1)
        {
            return new ShopActionResult(Loc.T("La cantidad tiene que ser al menos 1."), null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null || !ShopCatalog.IsForSale(item))
        {
            return new ShopActionResult(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NotForSale)}\n**{itemName}** no está disponible en la tienda (se vende comida y cajas: mirá **/shop view**)."), null);
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
                    Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NotForSale)}\n🔒 **{ItemDisplay.Format(item.Emoji, item.Name)}** es una caja de la **Zona {required}** y todavía no la desbloqueaste ") +
                    Loc.F($"(llegaste hasta la Zona {unlocked}): avanzá de zona para poder comprarla."), null);
            }
        }

        // Las cajas se compran de a una y una vez cada 2 horas (CooldownCatalog.BoxBuy): un freno a cuántas entran al juego. Se avisa ANTES de
        // tocar nada, así pedir 5 no gasta el cooldown. La comida no tiene límite.
        if (item.Type == "Caja" && quantity > ShopCatalog.BoxesPerPurchase)
        {
            return new ShopActionResult(
                Loc.F($"Las cajas se compran **de a {ShopCatalog.BoxesPerPurchase}** y una compra cada 2 horas: pedí **{ItemDisplay.Format(item.Emoji, item.Name)}** con cantidad {ShopCatalog.BoxesPerPurchase}."), null);
        }

        // El total se calcula en long: en int, una cantidad enorme daba la vuelta (8 × 536.870.912 = 0, y 8 × 375.000.000 un número NEGATIVO) y la compra salía gratis o SUMABA oro
        // (auditoría 2026-11-12, tests/audittest). Lo que no entra en un int se rechaza antes de tocar nada.
        if (TotalOrNull(item.BuyPrice, quantity) is not int totalCost)
        {
            return new ShopActionResult(TooManyMessage(), null);
        }

        User? buyer;
        if (item.Type == "Caja")
        {
            var cooldownDefinition = CooldownCatalog.BoxBuy;
            var outcome = await shopRepository.BuyItemWithCooldownAsync(
                discordId, item.ItemId, quantity, totalCost, cooldownDefinition.CommandName, cooldownDefinition.Duration);

            if (outcome.CooldownRemaining is { } wait)
            {
                return new ShopActionResult(null, new EmbedBuilder()
                    .WithTitle(Loc.F($"{cooldownDefinition.Emoji} Ya compraste una caja hace poco"))
                    .WithDescription(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.BoxWait)}\n\nLas cajas se compran de a una y **una vez cada 2 horas**. Te falta **{TimeFormat.Remaining(wait)}** para comprar otra."))
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
            return new ShopActionResult(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NoGold)}\nNo te alcanza el oro: **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} cuesta **{totalCost}**."), null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldSpent, amount: totalCost, detail: item.Name);

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle(Loc.T("🛒 ¡Compra realizada!"))
            .WithDescription(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.BuySuccess)}\n\nCompraste **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} por **{GameHistory.Number(totalCost)}** de oro.\nOro restante: **{GameHistory.Number(buyer.Gold)}**."))
            .WithColor(Color.Green)
            .Build());
    }

    // precio × cantidad sin desbordar: null si el total no entra en un int (o si algo vino negativo). Público para probarlo.
    public static int? TotalOrNull(int unitPrice, int quantity)
    {
        long total = (long)unitPrice * quantity;
        return unitPrice < 0 || quantity < 1 || total > int.MaxValue ? null : (int)total;
    }

    private static string TooManyMessage() => Loc.T("Esa cantidad es demasiado grande: probá con una más chica.");

    public static async Task<ShopActionResult> ExecuteSellAsync(
        IItemRepository itemRepository, IShopRepository shopRepository, IGameEvents gameEvents, ulong discordId, string itemName, int quantity)
    {
        if (quantity < 1)
        {
            return new ShopActionResult(Loc.T("La cantidad tiene que ser al menos 1."), null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null)
        {
            return new ShopActionResult(Loc.F($"No encontré ningún ítem llamado **{itemName}**."), null);
        }

        if (item.SellPrice <= 0)
        {
            return new ShopActionResult(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.Unsellable)}\n**{ItemDisplay.Format(item.Emoji, item.Name)}** no se puede vender: es un premio."), null);
        }

        // Un arma o un amuleto no vive en el inventario: se forja directo a equipamiento, así que lo que se vende es lo EQUIPADO (y es siempre de a
        // uno). Si no es lo que tiene puesto, sigue el camino de siempre (una copia suelta en el inventario, de antes del cambio).
        if (item.Type is "Weapon" or "Amulet" && quantity == 1)
        {
            var soldGear = await shopRepository.SellEquippedAsync(discordId, item.ItemId, item.SellPrice);
            if (soldGear is not null)
            {
                await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: item.SellPrice, detail: item.Name);

                // Una frase entera por pieza (no se arma con "equipad{a/o}"): el género del español no existe en inglés.
                bool weapon = item.Type == "Weapon";
                string soldLine = weapon
                    ? Loc.F($"\n\nVendiste tu arma equipada **{ItemDisplay.Format(item.Emoji, item.Name)}** por **{GameHistory.Number(item.SellPrice)}** de oro.\n")
                    : Loc.F($"\n\nVendiste tu amuleto equipado **{ItemDisplay.Format(item.Emoji, item.Name)}** por **{GameHistory.Number(item.SellPrice)}** de oro.\n");
                string goldLine = weapon
                    ? Loc.F($"Oro total: **{GameHistory.Number(soldGear.Gold)}**.\n\n_Te quedaste sin arma: forjá otra en **/forge**._")
                    : Loc.F($"Oro total: **{GameHistory.Number(soldGear.Gold)}**.\n\n_Te quedaste sin amuleto: forjá otro en **/forge**._");
                return new ShopActionResult(null, new EmbedBuilder()
                    .WithTitle(Loc.T("💰 ¡Venta realizada!"))
                    .WithDescription(NpcDialogue.Shopkeeper(ShopkeeperLine.SellSuccess) + soldLine + goldLine)
                    .WithColor(Color.Green)
                    .Build());
            }
        }

        // Igual que al comprar: el total en long, y si no entra en un int no se vende nada.
        if (TotalOrNull(item.SellPrice, quantity) is not int totalRefund)
        {
            return new ShopActionResult(TooManyMessage(), null);
        }

        var seller = await shopRepository.SellItemAsync(discordId, item.ItemId, quantity, totalRefund);

        if (seller is null)
        {
            return new ShopActionResult(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.NotOwned)}\nNo tenés {quantity}x **{ItemDisplay.Format(item.Emoji, item.Name)}** para vender."), null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: totalRefund, detail: item.Name);

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle(Loc.T("💰 ¡Venta realizada!"))
            .WithDescription(Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.SellSuccess)}\n\nVendiste **{ItemDisplay.Format(item.Emoji, item.Name)}** x{quantity} por **{GameHistory.Number(totalRefund)}** de oro.\nOro total: **{GameHistory.Number(seller.Gold)}**."))
            .WithColor(Color.Green)
            .Build());
    }

    // La pregunta de «vender todo» (v0.53.0): qué se vende (la mochila y el arma y el amuleto puestos, con lo que paga cada cosa) y el total, con «No» (verde, primero) y «Sí» (rojo). El total viaja en el id del «Sí» para que
    // lo que se venda sea EXACTAMENTE lo que se mostró: si cambió algo en el medio, no se vende nada (ver ResolveSellAllConfirmAsync). Todas las puertas (/shop sellall, aa shop sellall, aa sell all) pasan por acá.
    public const string SellAllYesId = "shop_sellall_yes";
    public const string SellAllNoId = "shop_sellall_no";

    private static string NothingToSellMessage() =>
        NpcDialogue.Shopkeeper(ShopkeeperLine.NothingToSell) + Loc.T("\nNo tenés nada en tu inventario para vender (las cajas y los premios no se venden, ni lo que se queda en el Fuego Nuevo: huevos y comida de mascotas y de dragón).");

    public static async Task<ShopActionResult> BuildSellAllConfirmAsync(IShopRepository shopRepository, ulong discordId)
    {
        var preview = await shopRepository.PreviewSellAllAsync(discordId);
        if (preview.IsEmpty)
        {
            return new ShopActionResult(NothingToSellMessage(), null);
        }

        var lines = new List<string>();
        if (preview.ItemsCount > 0)
        {
            lines.Add(Loc.F($"🎒 Vas a vender **{preview.ItemsCount}** ítems de tu mochila por **{preview.BagGold}** de oro."));
        }

        // Una frase entera por pieza (no se arma con «puest{a/o}»): el género del español no existe en inglés.
        foreach (var piece in preview.Gear)
        {
            string name = ItemDisplay.Format(piece.Emoji, piece.Name);
            lines.Add(piece.IsWeapon
                ? Loc.F($"⚔️ Tu arma puesta: **{name}** por **{piece.SellPrice}** de oro (pierde su encantamiento).")
                : Loc.F($"🛡️ Tu amuleto puesto: **{name}** por **{piece.SellPrice}** de oro (pierde su encantamiento)."));
        }

        string footer = Loc.T("Forjar el equipo de nuevo cuesta mucho más de lo que paga la venta. No se venden las cajas ni los premios.") +
            (preview.KeptCount > 0 ? Loc.F($" Se quedan {preview.KeptCount} unidades que se conservan en el Fuego Nuevo (huevos y comida de mascotas y de dragón).") : string.Empty);

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("⚠️ ¿Vender todo?"))
            .WithDescription(string.Join("\n", lines) + "\n\n" + Loc.F($"Total: **{preview.TotalGold}** de oro."))
            .WithColor(Color.Orange)
            .WithFooter(footer)
            .Build();

        // «No» primero y en verde, «Sí» en rojo: lo mismo que la pregunta de huir (un toque sin querer no vende).
        var buttons = new ComponentBuilder()
            .WithButton(Loc.T("No, cancelar"), $"{SellAllNoId}:{discordId}", ButtonStyle.Success)
            .WithButton(Loc.T("Sí, vender todo"), $"{SellAllYesId}:{discordId}:{preview.TotalGold}", ButtonStyle.Danger)
            .Build();
        return new ShopActionResult(null, embed, buttons);
    }

    // Lo que pasa al apretar «Sí»: solo lo puede apretar quien pidió la pregunta, y se vende con el total que vio (si ya no es ese, no se vende nada).
    public static async Task<ShopActionResult> ResolveSellAllConfirmAsync(IShopRepository shopRepository, IGameEvents gameEvents, ulong ownerId, ulong clickerId, int expectedGold)
    {
        if (clickerId != ownerId)
        {
            return new ShopActionResult(Loc.T("Esa pregunta es de otra persona: pedí la tuya con **/shop sellall**."), null);
        }

        return await ExecuteSellAllAsync(shopRepository, gameEvents, ownerId, includeEquipped: true, expectedGold);
    }

    // La venta en sí. A secas (includeEquipped false) vende solo la mochila, como siempre; las puertas de los jugadores ya no la llaman directo: preguntan primero (BuildSellAllConfirmAsync) y el «Sí» llama con
    // includeEquipped y el total mostrado.
    public static async Task<ShopActionResult> ExecuteSellAllAsync(IShopRepository shopRepository, IGameEvents gameEvents, ulong discordId, bool includeEquipped = false, int? expectedGold = null)
    {
        var outcome = await shopRepository.SellAllAsync(discordId, includeEquipped, expectedGold);

        if (outcome is null)
        {
            return new ShopActionResult(NothingToSellMessage(), null);
        }

        if (outcome.Changed)
        {
            return new ShopActionResult(Loc.T("Algo cambió en tu mochila o en tu equipo desde que miraste (el total ya no es el mismo), así que no vendí nada. Volvé a pedirlo con **/shop sellall**."), null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.ShopGoldEarned, amount: outcome.GoldEarned, detail: "sellall");

        var gear = outcome.GearSold ?? [];
        string description;
        if (gear.Count == 0)
        {
            description = Loc.F($"{NpcDialogue.Shopkeeper(ShopkeeperLine.SellAll)}\n\nVendiste {outcome.ItemsSoldCount} ítems por **{outcome.GoldEarned}** de oro.\nOro total: **{outcome.Player.Gold}**.");
        }
        else
        {
            // Con equipo vendido (v0.53.0): la mochila, cada pieza puesta con su precio y el oro total; una frase entera por caso (el género no existe en inglés).
            var lines = new List<string>();
            if (outcome.ItemsSoldCount > 0)
            {
                lines.Add(Loc.F($"Vendiste {outcome.ItemsSoldCount} ítems de la mochila por **{outcome.GoldEarned - gear.Sum(g => g.SellPrice)}** de oro."));
            }

            foreach (var piece in gear)
            {
                string name = ItemDisplay.Format(piece.Emoji, piece.Name);
                lines.Add(piece.IsWeapon
                    ? Loc.F($"⚔️ Vendiste tu arma puesta **{name}** por **{piece.SellPrice}** de oro.")
                    : Loc.F($"🛡️ Vendiste tu amuleto puesto **{name}** por **{piece.SellPrice}** de oro."));
            }

            lines.Add(Loc.F($"Oro total: **{outcome.Player.Gold}**."));
            bool weapon = gear.Any(g => g.IsWeapon), amulet = gear.Any(g => !g.IsWeapon);
            lines.Add(weapon && amulet ? Loc.T("_Te quedaste sin arma ni amuleto: forjá otros en **/forge**._") : weapon ? Loc.T("_Te quedaste sin arma: forjá otra en **/forge**._") : Loc.T("_Te quedaste sin amuleto: forjá otro en **/forge**._"));
            description = NpcDialogue.Shopkeeper(ShopkeeperLine.SellAll) + "\n\n" + string.Join("\n", lines);
        }

        return new ShopActionResult(null, new EmbedBuilder()
            .WithTitle(Loc.T("💰 ¡Inventario liquidado!"))
            .WithDescription(description)
            .WithColor(Color.Green)
            // v0.38.0: lo que se conserva en el Fuego Nuevo no se vende con «todo»; si el jugador tiene de eso, se le avisa dónde quedó.
            .WithFooter(outcome.KeptCount > 0 ? Loc.F($"🐾 No se vendieron {outcome.KeptCount} unidades que se conservan en el Fuego Nuevo (huevos y comida de mascotas y de dragón). Para venderlas, una por una: /shop sell.") : null)
            .Build());
    }
}
