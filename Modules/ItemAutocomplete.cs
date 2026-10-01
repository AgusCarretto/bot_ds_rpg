using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// Listas desplegables de Discord (autocompletado) para los comandos que piden el nombre de un ítem:
// al escribir "/shop buy" o "/equip" aparece arriba una lista para elegir, en vez de tener que
// recordar y tipear el nombre exacto. Solo aplica a los comandos de barra — los comandos de texto
// ("aa shop buy ...") no tienen esa función en Discord.
//
// El valor de cada opción es el nombre EXACTO del ítem, así que la lógica de siempre
// (ShopModule.ExecuteBuyAsync / EquipModule.ExecuteEquipAsync, que buscan por nombre) no cambia.
// Tampoco obliga a elegir de la lista: escribir el nombre a mano sigue funcionando.
public static class ItemChoices
{
    // Catálogo de la tienda (solo consumibles). playerGold == null si el jugador no existe todavía:
    // en ese caso simplemente no se marca qué puede pagar.
    public static IReadOnlyList<AutocompleteResult> ForBuy(
        IEnumerable<Item> catalog, int? playerGold, string typed, IReadOnlyDictionary<int, ItemBuff>? buffs = null)
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
                    ? $"caja {item.Rarity}"
                    : buffs is not null && buffs.TryGetValue(item.ItemId, out var buff) ? $"cura {item.StatValue} HP y +{buff.AttackPercent}% ATQ" : $"cura {item.StatValue} HP";
                return new AutocompleteResult(
                    Truncate($"{item.Name} — {what} · {item.BuyPrice} oro{missingGold}"), item.Name);
            })
            .ToList();
    }

    // Solo lo que el jugador TIENE en el inventario, es un arma o amuleto, y puede usar su clase (un
    // ítem exclusivo de otra clase no se puede equipar, así que ni se ofrece). Primero las armas y
    // dentro de cada grupo lo más fuerte arriba; el daño que se muestra ya incluye la sinergia de clase.
    public static IReadOnlyList<AutocompleteResult> ForEquip(IEnumerable<OwnedItem> owned, User player, string typed)
    {
        return owned
            .Select(o => o.Item)
            .Where(item => item.Type is "Weapon" or "Amulet"
                && FitsAsValue(item)
                && (item.ClassRequirement is null || string.Equals(item.ClassRequirement, player.Class, StringComparison.OrdinalIgnoreCase))
                && Matches(item.Name, typed))
            .OrderBy(item => Relevance(item.Name, typed))
            .ThenBy(item => item.Type == "Weapon" ? 0 : 1)
            .ThenByDescending(item => EffectiveStat(item, player.Class))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(item =>
            {
                bool equipped = item.ItemId == (item.Type == "Weapon" ? player.WeaponId : player.AmuletId);
                string equippedTag = equipped ? " · equipado" : string.Empty;
                string label = item.Type == "Weapon"
                    ? $"🗡️ {item.Name} — +{EffectiveStat(item, player.Class)} ATQ{(ClassWeaponSynergy.Applies(player.Class, item.WeaponFamily) ? " ⭐" : string.Empty)}{equippedTag}"
                    : $"📿 {item.Name} — +{item.StatValue} DEF{equippedTag}";
                return new AutocompleteResult(Truncate(label), item.Name);
            })
            .ToList();
    }

    // El nombre es el VALOR de la opción: ver AutocompleteText.FitsAsValue.
    private static bool FitsAsValue(Item item) => AutocompleteText.FitsAsValue(item.Name);

    // Daño real del arma para esta clase (con la sinergia de familia); el amuleto no tiene sinergia.
    // Solo las cajas que el jugador TIENE, con cuántas, de la más rara a la más común (lo que quiere abrir primero).
    public static IReadOnlyList<AutocompleteResult> ForOwnedBoxes(IEnumerable<OwnedItem> owned, string typed)
    {
        return owned
            .Where(o => o.Item.Type == "Caja" && o.Quantity > 0 && FitsAsValue(o.Item) && Matches(o.Item.Name, typed))
            .OrderBy(o => Relevance(o.Item.Name, typed))
            .ThenByDescending(o => RarityCatalog.RankOf(o.Item.Rarity))
            .ThenBy(o => o.Item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(o => new AutocompleteResult(Truncate($"📦 {o.Item.Name} — tenés {o.Quantity} · {o.Item.Rarity}"), o.Item.Name))
            .ToList();
    }

    // Lo que el jugador TIENE y se puede vender (los premios no: sell_price 0), lo que más plata deja arriba. "c/u" porque /shop sell
    // vende por unidad y el jugador elige cuántas.
    public static IReadOnlyList<AutocompleteResult> ForSell(IEnumerable<InventoryEntry> inventory, string typed)
    {
        return inventory
            .Where(e => e.Quantity > 0 && e.SellPrice > 0 && AutocompleteText.FitsAsValue(e.ItemName) && Matches(e.ItemName, typed))
            .OrderBy(e => Relevance(e.ItemName, typed))
            .ThenByDescending(e => (long)e.SellPrice * e.Quantity)
            .ThenBy(e => e.ItemName, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(e => new AutocompleteResult(
                Truncate($"{e.ItemName} — tenés {e.Quantity} · {e.SellPrice} oro c/u ({e.Rarity})"), e.ItemName))
            .ToList();
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

    private static int EffectiveStat(Item item, string playerClass) =>
        item.Type == "Weapon" ? ClassWeaponSynergy.ApplyBonus(item.StatValue, playerClass, item.WeaponFamily) : item.StatValue;
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

        return ItemChoices.ForBuy(catalog, player?.Gold, typed, buffs);
    }
}

// Lista de /shop sell: lo que el jugador TIENE y se puede vender, con cuántos y a cuánto se paga cada uno.
public sealed class SellItemAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var inventory = await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(userId);
        return ItemChoices.ForSell(inventory, typed);
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

// Lista de /equip: las armas y amuletos del inventario que el jugador puede usar.
public sealed class EquipItemAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(userId);
        if (player is null)
        {
            return [];
        }

        var inventory = services.GetRequiredService<IInventoryRepository>();
        var weapons = await inventory.GetOwnedByTypeAsync(userId, "Weapon");
        var amulets = await inventory.GetOwnedByTypeAsync(userId, "Amulet");

        return ItemChoices.ForEquip(weapons.Concat(amulets), player, typed);
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
