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
    public static IReadOnlyList<AutocompleteResult> ForBuy(IEnumerable<Item> catalog, int? playerGold, string typed)
    {
        return catalog
            .Where(item => item.Type == "Consumable" && FitsAsValue(item) && Matches(item.Name, typed))
            .OrderBy(item => Relevance(item.Name, typed))
            .ThenBy(item => item.BuyPrice)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(item =>
            {
                string missingGold = playerGold is int gold && gold < item.BuyPrice ? " (te falta oro)" : string.Empty;
                return new AutocompleteResult(
                    Truncate($"{item.Name} — cura {item.StatValue} HP · {item.BuyPrice} oro{missingGold}"), item.Name);
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
    private static int EffectiveStat(Item item, string playerClass) =>
        item.Type == "Weapon" ? ClassWeaponSynergy.ApplyBonus(item.StatValue, playerClass, item.WeaponFamily) : item.StatValue;
}

// Lista de /shop buy: los consumibles de la tienda, con cuánto curan y cuánto cuestan.
public sealed class BuyItemAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        string typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        var catalog = await services.GetRequiredService<IItemRepository>().GetAllByTypeAsync("Consumable");
        // GetByDiscordIdAsync (no GetOrCreate): abrir una lista no tiene que crearle cuenta a nadie.
        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(context.User.Id);

        return AutocompletionResult.FromSuccess(ItemChoices.ForBuy(catalog, player?.Gold, typed));
    }
}

// Lista de /equip: las armas y amuletos del inventario que el jugador puede usar.
public sealed class EquipItemAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        string typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(context.User.Id);
        if (player is null)
        {
            return AutocompletionResult.FromSuccess([]);
        }

        var inventory = services.GetRequiredService<IInventoryRepository>();
        var weapons = await inventory.GetOwnedByTypeAsync(context.User.Id, "Weapon");
        var amulets = await inventory.GetOwnedByTypeAsync(context.User.Id, "Amulet");

        return AutocompletionResult.FromSuccess(ItemChoices.ForEquip(weapons.Concat(amulets), player, typed));
    }
}
