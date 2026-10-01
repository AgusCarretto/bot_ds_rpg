using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// Segunda mitad de ShopModule (ver el comentario en ShopModule.cs): acá van las consultas de
// solo lectura del grupo "shop", separadas de las acciones (buy/sell/sellall) por claridad,
// sin duplicar el atributo [Group] ni el constructor primario (ya declarados en ShopModule.cs;
// itemRepository queda accesible acá también porque es la misma clase de C#).
public partial class ShopModule
{
    // Comando barra: /shop view
    [SlashCommand("view", "Mostrá la tienda: comida y cajas en venta.")]
    public async Task HandleViewAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: BuildViewEmbed(await LoadShopItemsAsync(itemRepository)));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude cargar la tienda ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Todo lo que se vende (comida y cajas con precio de compra), listo para mostrar. Compartido con "aa shop view".
    public static async Task<IReadOnlyList<Item>> LoadShopItemsAsync(IItemRepository itemRepository)
    {
        var items = new List<Item>();
        foreach (string type in ShopCatalog.SoldTypes)
        {
            items.AddRange(await itemRepository.GetAllByTypeAsync(type));
        }

        return items.Where(ShopCatalog.IsForSale).ToList();
    }

    // Público para que Modules/TextCommandModule.cs arme el mismo embed en "aa shop view". Primero la comida y después las cajas,
    // cada grupo de menor a mayor rareza.
    public static Embed BuildViewEmbed(IReadOnlyList<Item> items)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🏪 Tienda")
            .WithColor(Color.Gold);

        if (items.Count == 0)
        {
            embed.WithDescription("No hay nada en la tienda todavía.");
        }
        else
        {
            embed.WithDescription("🍖 **Comida** para curarte y 📦 **cajas** con premios sorpresa (abrilas con `/abrir`).");

            foreach (var item in items.OrderBy(i => i.Type == "Caja" ? 1 : 0).ThenBy(i => RarityCatalog.RankOf(i.Rarity)).ThenBy(i => i.Name))
            {
                string detail = item.Type == "Caja"
                    ? $"🎲 Premios sorpresa | 💰 Compra: {item.BuyPrice} Oro | 💸 Venta: {item.SellPrice} Oro"
                    : $"❤️ Cura: {item.StatValue} HP | 💰 Compra: {item.BuyPrice} Oro | 💸 Venta: {item.SellPrice} Oro";

                embed.AddField($"[{item.Rarity}] {ItemDisplay.Format(item.Emoji, item.Name)}", detail);
            }
        }

        return embed.Build();
    }
}
