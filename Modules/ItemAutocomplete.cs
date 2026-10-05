using BotDsRpg.GameData;
using BotDsRpg.Services;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// Listas desplegables de Discord (autocompletado) para los comandos que piden el nombre de un ítem:
// al escribir "/shop buy" o "/shop sell" aparece arriba una lista para elegir, en vez de tener que
// recordar y tipear el nombre exacto. Solo aplica a los comandos de barra — los comandos de texto
// ("aa shop buy ...") no tienen esa función en Discord.
//
// El valor de cada opción es el nombre EXACTO del ítem, así que la lógica de siempre
// (ShopModule.ExecuteBuyAsync / ExecuteSellAsync, que buscan por nombre) no cambia.
// Tampoco obliga a elegir de la lista: escribir el nombre a mano sigue funcionando.
public static class ItemChoices
{
    // Catálogo de la tienda (solo consumibles). playerGold == null si el jugador no existe todavía:
    // en ese caso simplemente no se marca qué puede pagar.
    public static IReadOnlyList<AutocompleteResult> ForBuy(
        IEnumerable<Item> catalog, int? playerGold, string typed, IReadOnlyDictionary<int, ItemBuff>? buffs = null, int? unlockedZoneRank = null)
    {
        return catalog
            .Where(item => ShopCatalog.IsForSale(item) && FitsAsValue(item) && Matches(item.Name, typed))
            .OrderBy(item => Relevance(item.Name, typed))
            .ThenBy(item => item.BuyPrice)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(item =>
            {
                string missingGold = playerGold is int gold && gold < item.BuyPrice ? " (te falta oro)" : string.Empty;
                string what = item.Type == "Caja"
                    ? ShopModule.BoxLine(item, unlockedZoneRank).Replace(" · " + GameHistory.Number(item.BuyPrice) + " oro", string.Empty)
                    : buffs is not null && buffs.TryGetValue(item.ItemId, out var buff) ? $"cura {item.StatValue} HP y +{buff.AttackPercent}% ATQ" : $"cura {item.StatValue} HP";
                return new AutocompleteResult(
                    Truncate($"{item.Name} — {what} · {GameHistory.Number(item.BuyPrice)} oro{missingGold}"), item.Name);
            })
            .ToList();
    }

    // El nombre es el VALOR de la opción: ver AutocompleteText.FitsAsValue.
    private static bool FitsAsValue(Item item) => AutocompleteText.FitsAsValue(item.Name);

    // Solo las cajas que el jugador TIENE, con cuántas, de la más rara a la más común (lo que quiere abrir primero).
    public static IReadOnlyList<AutocompleteResult> ForOwnedBoxes(IEnumerable<OwnedItem> owned, string typed)
    {
        return owned
            .Where(o => o.Item.Type == "Caja" && o.Quantity > 0 && FitsAsValue(o.Item) && Matches(o.Item.Name, typed))
            .OrderBy(o => Relevance(o.Item.Name, typed))
            .ThenByDescending(o => RarityCatalog.RankOf(o.Item.Rarity))
            .ThenBy(o => o.Item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(o => new AutocompleteResult(
                Truncate($"📦 {o.Item.Name} — tenés {o.Quantity}{(BoxCatalog.RangeText(o.Item.BoxMinItems, o.Item.BoxMaxItems) is { Length: > 0 } range ? " · " + range : string.Empty)}"), o.Item.Name))
            .ToList();
    }

    // Lo que el jugador TIENE y se puede vender (los premios no: sell_price 0), lo que más plata deja arriba. "c/u" porque /shop sell
    // vende por unidad y el jugador elige cuántas. equippedGear: su arma y su amuleto EQUIPADOS (no están en el inventario) — se ofrecen
    // arriba de todo y avisan que son los puestos, porque venderlos te deja sin ellos.
    public static IReadOnlyList<AutocompleteResult> ForSell(IEnumerable<InventoryEntry> inventory, string typed, IEnumerable<Item>? equippedGear = null)
    {
        var gear = (equippedGear ?? [])
            .Where(i => i.SellPrice > 0 && AutocompleteText.FitsAsValue(i.Name) && Matches(i.Name, typed))
            .OrderBy(i => i.Type == "Weapon" ? 0 : 1)
            .Select(i => new AutocompleteResult(
                Truncate($"⚠️ {i.Name} — tu {(i.Type == "Weapon" ? "arma" : "amuleto")} equipad{(i.Type == "Weapon" ? "a" : "o")} · {i.SellPrice} oro"), i.Name));

        var stock = inventory
            .Where(e => e.Quantity > 0 && e.SellPrice > 0 && AutocompleteText.FitsAsValue(e.ItemName) && Matches(e.ItemName, typed))
            .OrderBy(e => Relevance(e.ItemName, typed))
            .ThenByDescending(e => (long)e.SellPrice * e.Quantity)
            .ThenBy(e => e.ItemName, StringComparer.Ordinal)
            .Select(e => new AutocompleteResult(
                Truncate($"{e.ItemName} — tenés {e.Quantity} · {e.SellPrice} oro c/u ({e.Rarity})"), e.ItemName));

        return gear.Concat(stock).Take(MaxChoices).ToList();
    }

    // La comida que el jugador TIENE, la que más cura arriba (igual que el desplegable del combate). Los banquetes avisan su buff.
    public static IReadOnlyList<AutocompleteResult> ForUse(
        IEnumerable<OwnedItem> owned, IReadOnlyDictionary<int, ItemBuff>? buffs, string typed)
    {
        return owned
            .Where(o => o.Item.Type == "Consumable" && o.Quantity > 0 && FitsAsValue(o.Item) && Matches(o.Item.Name, typed))
            .OrderBy(o => Relevance(o.Item.Name, typed))
            .ThenByDescending(o => o.Item.StatValue)
            .ThenBy(o => o.Item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(o =>
            {
                string buffText = buffs is not null && buffs.TryGetValue(o.Item.ItemId, out var buff) ? $" y +{buff.AttackPercent}% ATQ" : string.Empty;
                return new AutocompleteResult(Truncate($"{o.Item.Name} — cura {o.Item.StatValue} HP{buffText} · tenés {o.Quantity}"), o.Item.Name);
            })
            .ToList();
    }

}

// Lista de /shop buy: lo que se vende (comida y cajas), con cuánto cura / qué caja es y cuánto cuesta.
public sealed class BuyItemAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var catalog = await ShopModule.LoadShopItemsAsync(services.GetRequiredService<IItemRepository>());
        // GetByDiscordIdAsync (no GetOrCreate): abrir una lista no tiene que crearle cuenta a nadie.
        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(userId);
        var buffs = await services.GetRequiredService<IBuffRepository>().GetItemBuffsAsync();

        return ItemChoices.ForBuy(catalog, player?.Gold, typed, buffs, await services.GetRequiredService<IBoxContextService>().MaxUnlockedRankAsync(userId));
    }
}

// Lista de /shop sell: lo que el jugador TIENE y se puede vender, con cuántos y a cuánto se paga cada uno.
public sealed class SellItemAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var inventory = await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(userId);

        // El arma y el amuleto equipados no están en el inventario: se ofrecen aparte. GetByDiscordIdAsync (no GetOrCreate): abrir la lista no crea cuentas.
        var equipped = new List<Item>();
        if (await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(userId) is { } player)
        {
            var items = services.GetRequiredService<IItemRepository>();
            foreach (int? itemId in new[] { player.WeaponId, player.AmuletId })
            {
                if (itemId is int id && await items.GetByIdAsync(id) is { } gear)
                {
                    equipped.Add(gear);
                }
            }
        }

        return ItemChoices.ForSell(inventory, typed, equipped);
    }
}

// Lista de /use: la comida que el jugador TIENE, con cuánto cura (y el buff de los banquetes) y cuántas le quedan.
public sealed class UseItemAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var owned = await services.GetRequiredService<IInventoryRepository>().GetOwnedByTypeAsync(userId, "Consumable");
        var buffs = await services.GetRequiredService<IBuffRepository>().GetItemBuffsAsync();

        return ItemChoices.ForUse(owned, buffs, typed);
    }
}

// Lista de /heal: la comida que el jugador TIENE y sirve para curarse del todo (sin banquetes: esos se comen con /use).
public sealed class HealFoodAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var owned = await services.GetRequiredService<IInventoryRepository>().GetOwnedByTypeAsync(userId, "Consumable");
        var buffs = await services.GetRequiredService<IBuffRepository>().GetItemBuffsAsync();

        return ItemChoices.ForUse(owned.Where(o => !buffs.ContainsKey(o.Item.ItemId)), null, typed);
    }
}

// Lista de /open: las cajas que el jugador TIENE, con cuántas.
public sealed class BoxAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var owned = await services.GetRequiredService<IInventoryRepository>().GetOwnedByTypeAsync(userId, "Caja");
        return ItemChoices.ForOwnedBoxes(owned, typed);
    }
}
