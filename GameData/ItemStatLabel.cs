using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Cómo se muestra lo que suma un ítem equipable: "+15 ATQ" en un arma (daño), "+20 DEF" en un amuleto
// (defensa). Un solo lugar para que las recetas, las listas de forjar/equipar y lo que venga usen el
// mismo texto.
public static class ItemStatLabel
{
    // null si el ítem no se equipa (material, consumible).
    public static string? Format(Item item) => item.Type switch
    {
        "Weapon" => $"+{item.StatValue} ATQ",
        "Amulet" => $"+{item.StatValue} DEF",
        _ => null,
    };

    // Igual, pero si es un arma de la familia de la clase del jugador agrega lo que RINDE de verdad con la
    // sinergia (ver ClassWeaponSynergy): "+15 ATQ (+22 con tu clase ⭐)".
    public static string? FormatFor(Item item, string playerClass)
    {
        string? label = Format(item);
        if (label is null || item.Type != "Weapon" || !ClassWeaponSynergy.Applies(playerClass, item.WeaponFamily))
        {
            return label;
        }

        return $"{label} (+{ClassWeaponSynergy.ApplyBonus(item.StatValue, playerClass, item.WeaponFamily)} con tu clase ⭐)";
    }
}
