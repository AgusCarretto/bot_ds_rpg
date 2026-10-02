using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// AttachmentPath: el archivo de la foto del tabernero que hay que adjuntar al mensaje (null si no hay foto o es una URL).
public sealed record TabernaScene(Embed Embed, MessageComponent? Components, string? AttachmentPath = null);

// /taberna (aa taberna): la taberna como una escena, igual que la herrería. Aparece el tabernero con su foto (si hay una, ver NpcImages), te
// saluda y muestra la carta (comida y cajas con su precio) y debajo van listas desplegables para: comer algo de tu mochila, comprar comida,
// comprar una caja y vender algo. Cada elección es de a UNA unidad (para más cantidad están los comandos directos de /shop), el tabernero
// contesta y la escena se vuelve a armar con tu oro y tus cosas al día. La respuesta del tabernero NO reemplaza el texto de la escena (se perdía
// entre la carta y las listas): sale como un mensaje APARTE, justo debajo, con lo que pasó con lo que elegiste. Por debajo es EXACTAMENTE la misma lógica de siempre
// (ShopModule.ExecuteBuyAsync / ExecuteSellAsync y UseModule.ExecuteUseAsync): mismas validaciones, mismo cobro atómico, el cooldown de
// las cajas, los eventos para misiones y logros... Nada se reimplementa acá.
public class TabernaModule(
    IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository, IShopRepository shopRepository,
    IBuffRepository buffRepository, ICombatSessionService combatSessions, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string Eat = "taberna_eat";
    private const string BuyFood = "taberna_buy";
    private const string BuyBox = "taberna_box";
    private const string Sell = "taberna_sell";
    private const string GearYes = "taberna_gearyes";
    private const string GearNo = "taberna_gearno";

    [SlashCommand("taberna", "Pasá por la taberna: comé, comprá comida y cajas, y vendé con el tabernero.")]
    public async Task HandleTabernaAsync()
    {
        await DeferAsync();

        try
        {
            var scene = await BuildSceneAsync(
                userRepository, itemRepository, inventoryRepository, buffRepository, Context.User.Id, null);

            if (scene.AttachmentPath is { } file)
            {
                await FollowupWithFileAsync(file, embed: scene.Embed, components: scene.Components);
            }
            else
            {
                await FollowupAsync(embed: scene.Embed, components: scene.Components);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! La taberna está cerrada ahora, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // El id de cada lista lleva a quién pertenece la escena: nadie puede pedirle cosas al tabernero con la tuya.
    [ComponentInteraction($"{Eat}:*")]
    public Task HandleEatAsync(string ownerRaw, string[] selected) => RunAsync(Eat, ownerRaw, selected);

    [ComponentInteraction($"{BuyFood}:*")]
    public Task HandleBuyFoodAsync(string ownerRaw, string[] selected) => RunAsync(BuyFood, ownerRaw, selected);

    [ComponentInteraction($"{BuyBox}:*")]
    public Task HandleBuyBoxAsync(string ownerRaw, string[] selected) => RunAsync(BuyBox, ownerRaw, selected);

    [ComponentInteraction($"{Sell}:*")]
    public Task HandleSellAsync(string ownerRaw, string[] selected) => RunAsync(Sell, ownerRaw, selected);

    // Vender el arma o el amuleto EQUIPADOS pide confirmación (un click de más te deja sin tu pieza, y forjarla vale más que lo que pagan).
    [ComponentInteraction($"{GearYes}:*:*")]
    public async Task HandleGearYesAsync(string ownerRaw, string itemIdRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: pasá por la taberna con **/taberna**.", ephemeral: true);
                return;
            }

            if (!int.TryParse(itemIdRaw, out int itemId) || await itemRepository.GetByIdAsync(itemId) is not { } item)
            {
                await FollowupAsync("No encuentro esa pieza: probá de nuevo desde la taberna.", ephemeral: true);
                return;
            }

            string talk = await ExecuteActionAsync(
                Sell, userRepository, itemRepository, inventoryRepository, shopRepository, buffRepository, combatSessions, gameEvents, Context.User.Id, item.Name);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = BuildAnswerEmbed(talk);
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude hacer la venta, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{GearNo}:*")]
    public async Task HandleGearNoAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: pasá por la taberna con **/taberna**.", ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = BuildAnswerEmbed(NpcDialogue.Shopkeeper(ShopkeeperLine.GearKept));
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! Algo falló, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    private async Task RunAsync(string action, string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: pasá por la taberna con **/taberna**.", ephemeral: true);
                return;
            }

            // Si lo que querés vender es lo que llevás puesto, antes de venderlo se te pregunta (solo lo ves vos).
            if (action == Sell && await BuildGearSellConfirmationAsync(userRepository, itemRepository, Context.User.Id, selected.FirstOrDefault() ?? string.Empty) is { } confirm)
            {
                await FollowupAsync(embed: confirm.Embed, components: confirm.Components, ephemeral: true);
                return;
            }

            string talk = await ExecuteActionAsync(
                action, userRepository, itemRepository, inventoryRepository, shopRepository, buffRepository, combatSessions, gameEvents,
                Context.User.Id, selected.FirstOrDefault() ?? string.Empty);

            // La escena se refresca (tu oro y tus listas al día) y la respuesta va en un mensaje aparte.
            var scene = await BuildSceneAsync(userRepository, itemRepository, inventoryRepository, buffRepository, Context.User.Id, null);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });

            await FollowupAsync(embed: BuildAnswerEmbed(talk));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude pasarle el pedido al tabernero, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Las piezas que el jugador lleva puestas (arma primero, después amuleto): no están en el inventario, así que la taberna las lista aparte.
    private static async Task<IReadOnlyList<Item>> EquippedGearAsync(IItemRepository itemRepository, User player)
    {
        var gear = new List<Item>();
        foreach (int? itemId in new[] { player.WeaponId, player.AmuletId })
        {
            if (itemId is int id && await itemRepository.GetByIdAsync(id) is { } item)
            {
                gear.Add(item);
            }
        }

        return gear;
    }

    // Si "itemName" es justo el arma o el amuleto que el jugador lleva puestos, la pregunta de confirmación (con sus botones); si no, null y se
    // vende directo como siempre. Pública y sin Context para probarla.
    public static async Task<(Embed Embed, MessageComponent Components)?> BuildGearSellConfirmationAsync(
        IUserRepository userRepository, IItemRepository itemRepository, ulong discordId, string itemName)
    {
        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null || item.Type is not ("Weapon" or "Amulet") || item.SellPrice <= 0)
        {
            return null;
        }

        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null || (item.Type == "Weapon" ? player.WeaponId : player.AmuletId) != item.ItemId)
        {
            return null;
        }

        bool weapon = item.Type == "Weapon";
        string slot = weapon ? "arma" : "amuleto";

        var embed = new EmbedBuilder()
            .WithTitle($"⚠️ ¿Vender tu {slot}?")
            .WithColor(Color.Orange)
            .WithDescription(
                $"{NpcDialogue.Shopkeeper(ShopkeeperLine.GearConfirm)}\n\n" +
                $"Vas a vender **{ItemDisplay.Format(item.Emoji, item.Name)}** ({ItemStatLabel.FormatFor(item, player.Class)}) por **{item.SellPrice}** de oro.\n" +
                $"Te quedás sin {slot} hasta que forjes otr{(weapon ? "a" : "o")}, y forjarl{(weapon ? "a" : "o")} cuesta bastante más de lo que te pagan.")
            .Build();

        var buttons = new ComponentBuilder()
            .WithButton("Sí, venderla", $"{GearYes}:{discordId}:{item.ItemId}", ButtonStyle.Danger, new Emoji("💰"))
            .WithButton("No, me la quedo", $"{GearNo}:{discordId}", ButtonStyle.Secondary, new Emoji("🛡️"))
            .Build();

        return (embed, buttons);
    }

    // La respuesta del tabernero como mensaje propio: su frase y lo que pasó (compraste, vendiste, te curaste, te faltó oro...).
    public static Embed BuildAnswerEmbed(string talk) =>
        new EmbedBuilder()
            .WithColor(Color.Gold)
            .WithDescription(string.IsNullOrWhiteSpace(talk) ? "🍺" : talk.Length <= 4000 ? talk : talk[..3999] + "…")
            .Build();

    // Lo que contesta el tabernero a lo que elegiste. Estático y sin Context para probarlo sin Discord.
    public static async Task<string> ExecuteActionAsync(
        string action, IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository,
        IShopRepository shopRepository, IBuffRepository buffRepository, ICombatSessionService combatSessions, IGameEvents gameEvents,
        ulong discordId, string itemName)
    {
        switch (action)
        {
            case BuyFood or BuyBox:
            {
                var bought = await ShopModule.ExecuteBuyAsync(userRepository, itemRepository, shopRepository, combatSessions, gameEvents, discordId, itemName, 1);
                return bought.PlainMessage ?? bought.Embed!.Description;
            }

            case Sell:
            {
                var sold = await ShopModule.ExecuteSellAsync(itemRepository, shopRepository, gameEvents, discordId, itemName, 1);
                return sold.PlainMessage ?? sold.Embed!.Description;
            }

            default: // Eat
            {
                if (combatSessions.Peek(discordId) is not null)
                {
                    return NpcDialogue.Innkeeper(InnkeeperLine.InCombat);
                }

                var used = await UseModule.ExecuteUseAsync(userRepository, itemRepository, inventoryRepository, combatSessions, buffRepository, discordId, itemName);
                if (used.Embed is null)
                {
                    return used.PlainMessage ?? string.Empty;
                }

                bool full = used.Embed.Title?.Contains("máximo") == true;
                return $"{NpcDialogue.Innkeeper(full ? InnkeeperLine.FullHp : InnkeeperLine.Healed)}\n\n{used.Embed.Description}";
            }
        }
    }

    // La escena: el saludo (o la respuesta del tabernero a lo último que hiciste), la carta, tu oro y las listas. Pública y sin Context para
    // que "aa taberna" muestre exactamente lo mismo.
    public static async Task<TabernaScene> BuildSceneAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository,
        IBuffRepository buffRepository, ulong discordId, string? talk)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new TabernaScene(
                new EmbedBuilder().WithTitle("🍺 La Taberna").WithColor(Color.Gold)
                    .WithDescription("Primero tenés que empezar tu aventura con **/start**.").Build(),
                null);
        }

        var shopItems = await ShopModule.LoadShopItemsAsync(itemRepository);
        var buffs = await buffRepository.GetItemBuffsAsync();
        var inventory = await inventoryRepository.GetByDiscordIdAsync(discordId);
        var foodItems = await inventoryRepository.GetOwnedByTypeAsync(discordId, "Consumable");

        var embed = NpcImages.Decorate(ShopModule.BuildViewEmbed(shopItems, buffs, talk, player.Gold), NpcImages.Innkeeper);

        var components = new ComponentBuilder();
        int row = 0;

        if (foodItems.Count > 0)
        {
            components.WithSelectMenu(Menu(
                Eat, discordId, "Comer algo de tu mochila",
                foodItems.OrderByDescending(o => o.Item.StatValue).Take(25).Select(o => (
                    $"{o.Item.Name} ×{o.Quantity}", $"Cura {o.Item.StatValue} HP{BuffText(o.Item, buffs)}", o.Item.Name))), row++);
        }

        var foodForSale = shopItems.Where(i => i.Type != "Caja").OrderBy(i => i.BuyPrice).Take(25).ToList();
        if (foodForSale.Count > 0)
        {
            components.WithSelectMenu(Menu(
                BuyFood, discordId, "Comprar comida",
                foodForSale.Select(i => (i.Name, $"Cura {i.StatValue} HP{BuffText(i, buffs)} · {i.BuyPrice} oro", i.Name))), row++);
        }

        var boxesForSale = shopItems.Where(i => i.Type == "Caja").OrderBy(i => i.BuyPrice).Take(25).ToList();
        if (boxesForSale.Count > 0)
        {
            components.WithSelectMenu(Menu(
                BuyBox, discordId, "Comprar una caja (una por hora)",
                boxesForSale.Select(i => (i.Name, $"{i.BuyPrice} oro", i.Name))), row++);
        }

        // Primero lo que llevás puesto (avisando que es lo equipado: vender eso te deja sin la pieza) y después lo de la mochila.
        var equippedGear = (await EquippedGearAsync(itemRepository, player)).Where(i => i.SellPrice > 0 && AutocompleteText.FitsAsValue(i.Name)).ToList();
        var sellable = inventory.Where(e => e.Quantity > 0 && e.SellPrice > 0 && AutocompleteText.FitsAsValue(e.ItemName))
            .OrderByDescending(e => (long)e.SellPrice * e.Quantity).ThenBy(e => e.ItemName, StringComparer.Ordinal).Take(25 - equippedGear.Count).ToList();
        if (equippedGear.Count + sellable.Count > 0)
        {
            components.WithSelectMenu(Menu(
                Sell, discordId, "Vender algo (de a una unidad)",
                equippedGear.Select(i => ($"{(i.Type == "Weapon" ? "🗡️" : "📿")} {i.Name} (equipad{(i.Type == "Weapon" ? "a" : "o")})", $"⚠️ Lo que llevás puesto · {i.SellPrice} oro", i.Name))
                    .Concat(sellable.Select(e => ($"{e.ItemName} ×{e.Quantity}", $"{e.SellPrice} oro c/u", e.ItemName)))), row);
        }

        return new TabernaScene(embed, components.Build(), NpcImages.AttachmentPathFor(embed));
    }

    // " · +15% ATQ 30 min" para los banquetes.
    private static string BuffText(Item item, IReadOnlyDictionary<int, ItemBuff>? buffs) =>
        buffs is not null && buffs.TryGetValue(item.ItemId, out var buff) ? $" · +{buff.AttackPercent}% ATQ {buff.Minutes} min" : string.Empty;

    // Sin emojis personalizados en las opciones (si el bot no puede usar uno, Discord rechaza el mensaje ENTERO): texto simple, y los límites
    // de Discord (100 caracteres por etiqueta, descripción y valor).
    private static SelectMenuBuilder Menu(
        string prefix, ulong ownerId, string placeholder, IEnumerable<(string Label, string Description, string Value)> options)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{prefix}:{ownerId}")
            .WithPlaceholder(placeholder)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var (label, description, value) in options)
        {
            menu.AddOption(Cut(label), Cut(value), Cut(description));
        }

        return menu;
    }

    private static string Cut(string text) => text.Length <= 100 ? text : text[..99] + "…";
}
