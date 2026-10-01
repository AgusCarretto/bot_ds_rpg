using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Las reglas del tradeo entre jugadores (/trade). Son pocas y estrictas a propósito: se cambia UNA unidad de un material por UNA
// unidad de otro material, los dos de la MISMA rareza, y solo de lo que dropean /chop y /mine (Madera y Mineral). Así el tradeo sirve
// para completar lo que le falta a cada uno (yo tengo Roble de más y vos Hierro) sin que nadie pueda "comprar" rareza con cantidad ni
// sacarle valor al sistema de rarezas. Pura: se prueba sin Discord ni base.
public static class TradeRules
{
    // Lo que se puede cambiar: los materiales de recolección.
    public static bool IsTradeable(Item item) => item.Type is "Madera" or "Mineral";

    // null si el cambio es válido; si no, el motivo para mostrarle al jugador.
    public static string? Validate(Item give, Item get)
    {
        if (!IsTradeable(give) || !IsTradeable(get))
        {
            return "Solo se cambian los materiales de `/chop` y `/mine` (maderas y minerales).";
        }

        if (give.ItemId == get.ItemId)
        {
            return "Tiene que ser un material distinto: cambiar algo por lo mismo no tiene sentido.";
        }

        if (!string.Equals(give.Rarity, get.Rarity, StringComparison.Ordinal))
        {
            return $"Solo se cambia por algo de la **misma rareza**: **{give.Name}** es {give.Rarity} y **{get.Name}** es {get.Rarity}.";
        }

        return null;
    }
}
