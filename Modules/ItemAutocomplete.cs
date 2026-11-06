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
                string missingGold = playerGold is int gold && gold < item.BuyPrice ? " (" + Loc.T("te falta oro") + ")" : string.Empty;
                string what = item.Type == "Caja"
                    ? ShopModule.BoxDescription(item, unlockedZoneRank)
                    : item.Type == ShopCatalog.PetFoodType ? Loc.T("para tus mascotas")
                    : item.Type == ShopCatalog.DragonFoodType ? Loc.T("para tu dragón")
                    : buffs is not null && buffs.TryGetValue(item.ItemId, out var buff) ? Loc.F($"cura {item.StatValue} HP y +{buff.AttackPercent}% ATQ") : Loc.F($"cura {item.StatValue} HP");
                string price = Loc.F($"{GameHistory.Number(item.BuyPrice)} oro");
                return new AutocompleteResult(
                    Truncate(what.Length > 0 ? $"{item.Name} — {what} · {price}{missingGold}" : $"{item.Name} — {price}{missingGold}"), item.Name);
            })
            .ToList();
    }

    // El nombre es el VALOR de la opción: ver AutocompleteText.FitsAsValue.
    private static bool FitsAsValue(Item item) => AutocompleteText.FitsAsValue(item.Name);

    // Solo las cajas (y los huevos de mascota, que /open también abre) que el jugador TIENE, con cuántas, de la más rara a la más común (lo que quiere abrir primero).
    // hatchedEggNames (v0.51.1, el dueño: «una vez abro el primero que no aparezcan más en /open, me corta todo»): los huevos de las especies que el jugador YA tiene. Esos no se pueden abrir (nace una sola mascota
    // de cada especie, el huevo no se gasta) y son de repuesto para evolucionar, así que no se ofrecen acá; siguen en la mochila y se cuentan en /pet. null = no se filtra nada (los llamadores viejos).
    public static IReadOnlyList<AutocompleteResult> ForOwnedBoxes(IEnumerable<OwnedItem> owned, string typed, IReadOnlySet<string>? hatchedEggNames = null)
    {
        return owned
            .Where(o => o.Item.Type is "Caja" or "Huevo" && o.Quantity > 0 && FitsAsValue(o.Item) && Matches(o.Item.Name, typed)
                && !(o.Item.Type == "Huevo" && hatchedEggNames is not null && hatchedEggNames.Contains(o.Item.Name)))
            .OrderBy(o => Relevance(o.Item.Name, typed))
            .ThenByDescending(o => RarityCatalog.RankOf(o.Item.Rarity))
            .ThenBy(o => o.Item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(o => new AutocompleteResult(
                o.Item.Type == "Huevo"
                    ? Truncate(Loc.F($"🥚 {o.Item.Name} — tenés {o.Quantity} · nace tu mascota"))
                    : Truncate(Loc.F($"📦 {o.Item.Name} — tenés {o.Quantity}{(BoxCatalog.RangeText(o.Item.BoxMinItems, o.Item.BoxMaxItems) is { Length: > 0 } range ? " · " + range : string.Empty)}")),
                o.Item.Name))
            .ToList();
    }

    // Lo que el jugador TIENE y se puede desmantelar (materiales: madera, minerales y drops), con cuántos y cuánto Polvo da cada uno; lo que más Polvo da arriba.
    public static IReadOnlyList<AutocompleteResult> ForDismantle(IEnumerable<InventoryEntry> inventory, string typed)
    {
        return inventory
            .Where(e => e.Quantity > 0 && Dismantling.CanDismantle(e.Type) && Dismantling.DustPerUnit(e.Rarity, e.DustValue) > 0 && AutocompleteText.FitsAsValue(e.ItemName) && Matches(e.ItemName, typed))
            .OrderBy(e => Relevance(e.ItemName, typed))
            .ThenByDescending(e => Dismantling.DustPerUnit(e.Rarity, e.DustValue))
            .ThenBy(e => e.ItemName, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(e => new AutocompleteResult(
                Truncate(Loc.F($"{e.ItemName} — tenés {e.Quantity} · +{Dismantling.DustPerUnit(e.Rarity, e.DustValue)} Polvo c/u{(e.DustValue is null ? $" ({RarityCatalog.DisplayName(e.Rarity)})" : string.Empty)}")), e.ItemName))
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
                Truncate(i.Type == "Weapon" ? Loc.F($"⚠️ {i.Name} — tu arma equipada · {i.SellPrice} oro") : Loc.F($"⚠️ {i.Name} — tu amuleto equipado · {i.SellPrice} oro")), i.Name));

        var stock = inventory
            .Where(e => e.Quantity > 0 && e.SellPrice > 0 && AutocompleteText.FitsAsValue(e.ItemName) && Matches(e.ItemName, typed))
            .OrderBy(e => Relevance(e.ItemName, typed))
            .ThenByDescending(e => (long)e.SellPrice * e.Quantity)
            .ThenBy(e => e.ItemName, StringComparer.Ordinal)
            .Select(e => new AutocompleteResult(
                Truncate(Loc.F($"{e.ItemName} — tenés {e.Quantity} · {e.SellPrice} oro c/u{(e.Type == "Material" ? string.Empty : $" ({RarityCatalog.DisplayName(e.Rarity)})")}")), e.ItemName));

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
                string buffText = buffs is not null && buffs.TryGetValue(o.Item.ItemId, out var buff) ? Loc.F($" y +{buff.AttackPercent}% ATQ") : string.Empty;
                return new AutocompleteResult(Truncate(Loc.F($"{o.Item.Name} — cura {o.Item.StatValue} HP{buffText} · tenés {o.Quantity}")), o.Item.Name);
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

        var choices = ItemChoices.ForBuy(catalog, player?.Gold, typed, buffs, await services.GetRequiredService<IBoxContextService>().MaxUnlockedRankAsync(userId)).ToList();

        // El dragón (v0.30.0) también se compra con /shop buy: aparece en la lista mientras no tengas uno (y lo que se escriba lo nombre).
        if (DragonModule.MatchesDragon(typed) && await services.GetRequiredService<IDragonRepository>().GetAsync(userId) is null)
        {
            choices.Insert(0, new AutocompleteResult(Loc.F($"🐲 Dragón de cría · {GameHistory.Number(DragonRules.ShopPrice)} oro (una vez, tipo al azar)"), "dragon"));
        }

        return choices.Take(25).ToList();
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

// Lista de /open: las cajas y los huevos de mascota que el jugador TIENE, con cuántos.
public sealed class BoxAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var inventory = services.GetRequiredService<IInventoryRepository>();
        var owned = (await inventory.GetOwnedByTypeAsync(userId, "Caja")).Concat(await inventory.GetOwnedByTypeAsync(userId, "Huevo")).ToList();

        // Los huevos de las mascotas que ya nacieron no se ofrecen (son de repuesto). Si no se pueden leer las mascotas la lista sale igual, solo que sin ese filtro.
        IReadOnlySet<string>? hatchedEggs = null;
        try
        {
            hatchedEggs = (await services.GetRequiredService<IPetRepository>().GetOwnedAsync(userId)).Select(p => p.Species.EggName).ToHashSet();
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }

        return ItemChoices.ForOwnedBoxes(owned, typed, hatchedEggs);
    }
}
