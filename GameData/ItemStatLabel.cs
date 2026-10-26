using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Cómo se muestra lo que suma un ítem equipable: "+15 ATQ" en un arma (daño), "+20 DEF" en un amuleto
// (defensa). Un solo lugar para que las recetas, las listas de forjar/vender y lo que venga usen el
// mismo texto. Desde la v0.27.0 no hay sinergia de clase: el número del arma es lo que rinde.
public static class ItemStatLabel
{
    // null si el ítem no se equipa (material, consumible).
    public static string? Format(Item item) => item.Type switch
    {
        "Weapon" => $"+{item.StatValue} ATQ",
        "Amulet" => $"+{item.StatValue} DEF",
        _ => null,
    };
}
