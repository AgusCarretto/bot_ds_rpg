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
            await FollowupAsync(Loc.T("No pude cargar la tienda ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
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
    // Un campo de ANCHO COMPLETO por categoría (Comida, Cajas, Mascotas) y UN renglón por ítem, `icono **Nombre**: precio · detalle corto`
    // (auditoría de UI, M6, aprobado por el dueño el 2026-11-14; hasta entonces eran tres columnas en línea con dos renglones por ítem, que en el
    // celular se apilaban en una tira larga antes de los menús). Sin la rareza escrita ("[Común]"), sin emojis de adorno en cada dato y sin el
    // precio de venta (para saber a cuánto se vende algo está la lista de /shop sell, que lo dice por cada ítem que tenés).
    // talk: lo que dice el tabernero (por defecto, el saludo); gold: tu oro, si se quiere mostrar.
    public static Embed BuildViewEmbed(
        IReadOnlyList<Item> items, IReadOnlyDictionary<int, ItemBuff>? buffs = null, string? talk = null, int? gold = null, int? unlockedZoneRank = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🍺 La Taberna"))
            .WithColor(Color.Gold);

        if (items.Count == 0)
        {
            embed.WithDescription(Loc.T("No hay nada en la tienda todavía."));
            return embed.Build();
        }

        string goldLine = gold is int g ? Loc.F($"\n\nTu oro: **{GameHistory.Number(g)}**") : string.Empty;
        embed.WithDescription(
            $"{talk ?? NpcDialogue.Shopkeeper(ShopkeeperLine.Greeting)}{goldLine}\n\n" +
            Loc.T("Las cajas se compran **de a una y una vez cada 2 horas**; abrilas con `/open`."));

        AddCategory(embed, Loc.T("Comida"), items.Where(i => i.Type is not ("Caja" or ShopCatalog.PetFoodType or ShopCatalog.DragonFoodType)), i => FoodDetail(i, buffs));
        AddCategory(embed, Loc.T("Cajas"), items.Where(i => i.Type == "Caja"), i => BoxDetail(i, unlockedZoneRank));
        // La comida de las mascotas y la del dragón van en su propio campo: no curan, se gastan con /pet feed y en cada cría del dragón.
        AddCategory(embed, Loc.T("Mascotas"), items.Where(i => i.Type is ShopCatalog.PetFoodType or ShopCatalog.DragonFoodType), i => i.Type == ShopCatalog.DragonFoodType ? Loc.T("para tu dragón") : Loc.T("para tus mascotas"));

        return embed.Build();
    }

    // El detalle corto de una comida en la pantalla de la tienda: «cura 150 HP», y los banquetes «cura 150 HP, +15% ATQ 30 min» (una oración entera por variante).
    private static string FoodDetail(Item food, IReadOnlyDictionary<int, ItemBuff>? buffs) =>
        buffs is not null && buffs.TryGetValue(food.ItemId, out var buff)
            ? Loc.F($"cura {food.StatValue} HP, +{buff.AttackPercent}% ATQ {buff.Minutes} min")
            : Loc.F($"cura {food.StatValue} HP");

    // El detalle corto de una caja en la pantalla de la tienda: «entre 1 y 10 ítems», y si es de una zona que todavía no desbloqueaste, «🔒 Zona 2» al final.
    private static string BoxDetail(Item box, int? unlockedZoneRank)
    {
        string range = BoxCatalog.RangeText(box.BoxMinItems, box.BoxMaxItems);
        int required = BoxCatalog.RequiredZoneRank(box.Rarity);
        string locked = unlockedZoneRank is int unlocked && required > unlocked ? Loc.F($"🔒 Zona {required}") : string.Empty;
        return string.Join(" · ", new[] { range, locked }.Where(part => part.Length > 0));
    }

    // Las tres que siguen son el texto de las OPCIONES de los menús de la taberna (Modules/TabernaModule.cs), no de la pantalla de arriba.
    // "Para tus mascotas · 100 oro": la comida de las mascotas no cura, así que no lleva el "Cura N HP" de la comida común.
    public static string PetFoodLine(Item food) => Loc.F($"Para tus mascotas · {GameHistory.Number(food.BuyPrice)} oro");

    // "Para tu dragón · 5.000 oro": la Comida de Dragón (v0.32.0) tampoco cura; se gasta sola en cada cría del dragón.
    public static string DragonFoodLine(Item food) => Loc.F($"Para tu dragón · {GameHistory.Number(food.BuyPrice)} oro");

    // "entre 1 y 10 ítems · 1.000 oro", y si es de una zona que todavía no desbloqueaste, "🔒 Zona 2" adelante.
    public static string BoxLine(Item box, int? unlockedZoneRank)
    {
        string range = BoxCatalog.RangeText(box.BoxMinItems, box.BoxMaxItems);
        string detail = (range.Length > 0 ? range + " · " : string.Empty) + Loc.F($"{GameHistory.Number(box.BuyPrice)} oro");
        int required = BoxCatalog.RequiredZoneRank(box.Rarity);
        return unlockedZoneRank is int unlocked && required > unlocked ? Loc.F($"🔒 Zona {required} · {detail}") : detail;
    }

    // Lo mismo sin el precio ("🔒 Zona 2 · entre 1 y 10 ítems"), para el autocompletado de /shop buy, que pone el precio a su manera. Antes le sacaba el precio
    // a BoxLine con un Replace en español, que en inglés no encontraba nada y el precio salía dos veces.
    public static string BoxDescription(Item box, int? unlockedZoneRank)
    {
        string range = BoxCatalog.RangeText(box.BoxMinItems, box.BoxMaxItems);
        int required = BoxCatalog.RequiredZoneRank(box.Rarity);
        string locked = unlockedZoneRank is int unlocked && required > unlocked ? Loc.F($"🔒 Zona {required}") : string.Empty;
        return string.Join(" · ", new[] { locked, range }.Where(part => part.Length > 0));
    }

    // Un campo de ancho completo con un renglón por ítem, de lo más barato a lo más caro (que es también de lo más común a lo más raro):
    // `icono **Nombre**: 30 oro · detalle`. Sin emoji cargado el renglón arranca directo en el nombre.
    private static void AddCategory(EmbedBuilder embed, string title, IEnumerable<Item> items, Func<Item, string> detail)
    {
        var lines = items
            .OrderBy(i => i.BuyPrice)
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .Select(i =>
            {
                string extra = detail(i);
                string price = Loc.F($"{GameHistory.Number(i.BuyPrice)} oro");
                return $"{ItemDisplay.Format(i.Emoji, $"**{i.Name}**")}: {price}{(extra.Length > 0 ? " · " + extra : string.Empty)}";
            })
            .ToList();

        if (lines.Count > 0)
        {
            embed.AddField(title, FieldText.FitLines(string.Join("\n", lines)), false);
        }
    }
}
