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
            await FollowupAsync(embed: BuildViewEmbed(
                await LoadShopItemsAsync(itemRepository), await buffRepository.GetItemBuffsAsync(),
                unlockedZoneRank: await boxContextService.MaxUnlockedRankAsync(Context.User.Id)));
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

    // Público para que Modules/TextCommandModule.cs arme el mismo embed en "aa shop view".
    //
    // Limpio a propósito: dos columnas (Comida | Cajas), cada ítem con SU emoji si lo tiene y el nombre, y debajo qué hace y cuánto cuesta, con
    // una línea en blanco entre ítems. Sin la rareza escrita ("[Común]"), sin emojis de adorno en cada dato y sin el precio de venta (para
    // saber a cuánto se vende algo está la lista de /shop sell, que lo dice por cada ítem que tenés).
    // talk: lo que dice el tabernero (por defecto, el saludo); gold: tu oro, si se quiere mostrar.
    public static Embed BuildViewEmbed(
        IReadOnlyList<Item> items, IReadOnlyDictionary<int, ItemBuff>? buffs = null, string? talk = null, int? gold = null, int? unlockedZoneRank = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🍺 La Taberna")
            .WithColor(Color.Gold);

        if (items.Count == 0)
        {
            embed.WithDescription("No hay nada en la tienda todavía.");
            return embed.Build();
        }

        string goldLine = gold is int g ? $"\n\nTu oro: **{g}**" : string.Empty;
        embed.WithDescription(
            $"{talk ?? NpcDialogue.Shopkeeper(ShopkeeperLine.Greeting)}{goldLine}\n\n" +
            "Las cajas se compran **de a una y una vez cada 2 horas**; abrilas con `/open`.");

        AddColumn(embed, "Comida", items.Where(i => i.Type != "Caja"), i => $"Cura {i.StatValue} HP{BuffText(i, buffs)} · {i.BuyPrice} oro");
        AddColumn(embed, "Cajas", items.Where(i => i.Type == "Caja"), i => BoxLine(i, unlockedZoneRank));

        return embed.Build();
    }

    // "entre 1 y 10 ítems · 1.000 oro", y si es de una zona que todavía no desbloqueaste, "🔒 Zona 2" adelante.
    public static string BoxLine(Item box, int? unlockedZoneRank)
    {
        string range = BoxCatalog.RangeText(box.BoxMinItems, box.BoxMaxItems);
        string detail = (range.Length > 0 ? range + " · " : string.Empty) + $"{GameHistory.Number(box.BuyPrice)} oro";
        int required = BoxCatalog.RequiredZoneRank(box.Rarity);
        return unlockedZoneRank is int unlocked && required > unlocked ? $"🔒 Zona {required} · {detail}" : detail;
    }

    // " · +15% ATQ 30 min" para los banquetes, nada para el resto de la comida.
    private static string BuffText(Item item, IReadOnlyDictionary<int, ItemBuff>? buffs) =>
        buffs is not null && buffs.TryGetValue(item.ItemId, out var buff) ? $" · +{buff.AttackPercent}% ATQ {buff.Minutes} min" : string.Empty;

    // De lo más barato a lo más caro (que es también de lo más común a lo más raro).
    private static void AddColumn(EmbedBuilder embed, string title, IEnumerable<Item> items, Func<Item, string> detail)
    {
        var lines = items
            .OrderBy(i => i.BuyPrice)
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .Select(i => $"**{ItemDisplay.Format(i.Emoji, i.Name)}**\n{detail(i)}")
            .ToList();

        if (lines.Count > 0)
        {
            string text = string.Join("\n\n", lines);
            embed.AddField(title, text.Length <= 1024 ? text : text[..1023] + "…", true);
        }
    }
}
