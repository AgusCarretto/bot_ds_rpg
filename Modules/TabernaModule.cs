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
// contesta y la escena se vuelve a armar con tu oro y tus cosas al día. Por debajo es EXACTAMENTE la misma lógica de siempre
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

            string talk = await ExecuteActionAsync(
                action, userRepository, itemRepository, inventoryRepository, shopRepository, buffRepository, combatSessions, gameEvents,
                Context.User.Id, selected.FirstOrDefault() ?? string.Empty);

            var scene = await BuildSceneAsync(userRepository, itemRepository, inventoryRepository, buffRepository, Context.User.Id, talk);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude pasarle el pedido al tabernero, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

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

        var sellable = inventory.Where(e => e.Quantity > 0 && e.SellPrice > 0 && AutocompleteText.FitsAsValue(e.ItemName))
            .OrderByDescending(e => (long)e.SellPrice * e.Quantity).ThenBy(e => e.ItemName, StringComparer.Ordinal).Take(25).ToList();
        if (sellable.Count > 0)
        {
            components.WithSelectMenu(Menu(
                Sell, discordId, "Vender algo (de a una unidad)",
                sellable.Select(e => ($"{e.ItemName} ×{e.Quantity}", $"{e.SellPrice} oro c/u", e.ItemName))), row);
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
