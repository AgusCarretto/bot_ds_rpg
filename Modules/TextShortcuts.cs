using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;

// Los atajos de texto (v0.23.1, el dueño: «que se pueda hacer `aa sell all`, `aa sell arma`, `aa sell amuleto`, sin pasar por shop; lo mismo con deposit y withdraw sin poner bank antes; y en el play `aa cf h 100`»).
// Cada atajo llama a la MISMA lógica de siempre (ShopModule.Execute*, BankModule.Execute*, CasinoModule.ExecutePlayAsync): acá solo se lee lo que escribió el jugador. Todo es estático y sin Context para probarlo
// contra la base sin Discord (tests/v0231test).
public enum SellKind { Help, All, Weapon, Amulet, Item }

// Qué pidió vender: Item y Quantity solo importan con Kind == Item (Quantity es el texto: un número, «10k» o «all» = todas las unidades que tenga de ese ítem).
public sealed record SellRequest(SellKind Kind, string Item = "", string Quantity = "1");

public sealed record CoinflipRequest(string Amount, string Side);

public static class TextShortcuts
{
    public const string SellHelp =
        "Vendé sin pasar por la tienda: **aa sell all** (todo lo vendible de tu mochila), **aa sell arma**, **aa sell amuleto** (lo que llevás puesto), **aa sell <ítem>** o **aa sell <cantidad> <ítem>** (también **aa sell all <ítem>** para todas las unidades de ese ítem).";

    public const string CoinflipHelp = "Cara o cruz: **aa cf h 100** (h = heads, t = tails) y la apuesta puede ser un número, **10k**, **1m** o **all**.";

    private static readonly string[] WeaponWords = ["arma", "weapon"];
    private static readonly string[] AmuletWords = ["amuleto", "amulet"];

    // «all», «arma», «amuleto», «Hierro», «10 Hierro», «all Hierro»... Sin mirar mayúsculas. Una sola palabra «arma» o «amuleto» es la pieza equipada; si el nombre de un ítem fuera justo esa palabra, tiene que usar /shop sell.
    public static SellRequest ParseSell(string? args)
    {
        string text = (args ?? string.Empty).Trim();
        string lower = text.ToLowerInvariant();

        if (text.Length == 0)
        {
            return new SellRequest(SellKind.Help);
        }

        if (AmountParser.IsAllWord(lower) || lower is "inventario" or "mochila" or "inventory" or "bag")
        {
            return new SellRequest(SellKind.All);
        }

        if (WeaponWords.Contains(lower))
        {
            return new SellRequest(SellKind.Weapon);
        }

        if (AmuletWords.Contains(lower))
        {
            return new SellRequest(SellKind.Amulet);
        }

        // «<cantidad|all> <ítem...>»: la primera palabra cuenta como cantidad si es una palabra de «todo» o empieza con un dígito (los ítems no empiezan con un número).
        int space = text.IndexOf(' ');
        if (space > 0)
        {
            string first = text[..space];
            string rest = text[(space + 1)..].Trim();
            if (rest.Length > 0 && (AmountParser.IsAllWord(first) || char.IsAsciiDigit(first[0])))
            {
                return new SellRequest(SellKind.Item, rest, first);
            }
        }

        return new SellRequest(SellKind.Item, text);
    }

    // «h 100», «100 h», «tails 1k», «all t»: el lado y la apuesta en cualquier orden. Null si no hay un lado claro o falta la apuesta.
    public static CoinflipRequest? ParseCoinflip(string? first, string? second)
    {
        string a = (first ?? string.Empty).Trim(), b = (second ?? string.Empty).Trim();

        if (SideOf(a) is { } sideFirst && b.Length > 0)
        {
            return new CoinflipRequest(b, sideFirst);
        }

        if (SideOf(b) is { } sideSecond && a.Length > 0)
        {
            return new CoinflipRequest(a, sideSecond);
        }

        return null;
    }

    // h / head / heads / cara → heads; t / tail / tails / cruz → tails.
    public static string? SideOf(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "h" or "head" or "heads" or "cara" => "heads",
        "t" or "tail" or "tails" or "cruz" => "tails",
        _ => null,
    };

    // Vende lo que pidió (ver ParseSell): todo el inventario, la pieza equipada, o un ítem por nombre y cantidad. Devuelve el mismo resultado que /shop sell y /shop sellall.
    public static async Task<ShopModule.ShopActionResult> ExecuteSellAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository, IShopRepository shopRepository,
        IGameEvents gameEvents, ulong discordId, string? args)
    {
        var request = ParseSell(args);

        switch (request.Kind)
        {
            case SellKind.Help:
                return new ShopModule.ShopActionResult(SellHelp, null);

            case SellKind.All:
                return await ShopModule.ExecuteSellAllAsync(shopRepository, gameEvents, discordId);

            case SellKind.Weapon or SellKind.Amulet:
            {
                bool weapon = request.Kind == SellKind.Weapon;
                var player = await userRepository.GetByDiscordIdAsync(discordId);
                if (player is null)
                {
                    return new ShopModule.ShopActionResult("Todavía no tenés cuenta: empezá con **/start**.", null);
                }

                int? itemId = weapon ? player.WeaponId : player.AmuletId;
                var worn = itemId is int id ? await itemRepository.GetByIdAsync(id) : null;
                if (worn is null)
                {
                    return new ShopModule.ShopActionResult($"No tenés {(weapon ? "ningún arma equipada" : "ningún amuleto equipado")} para vender.", null);
                }

                return await ShopModule.ExecuteSellAsync(itemRepository, shopRepository, gameEvents, discordId, worn.Name, 1);
            }

            default:
            {
                int quantity;
                if (AmountParser.IsAllWord(request.Quantity))
                {
                    // «all <ítem>»: todas las unidades que tenga (si no tiene ninguna, se pide 1 para que conteste lo de siempre: que no lo tiene o que no existe).
                    var item = await itemRepository.GetByNameAsync(request.Item);
                    quantity = item is null ? 1 : Math.Max(1, await inventoryRepository.GetQuantityAsync(discordId, item.ItemId));
                }
                else
                {
                    var parsed = AmountParser.Parse(request.Quantity, 0);
                    if (!parsed.Ok)
                    {
                        return new ShopModule.ShopActionResult(parsed.Error, null);
                    }

                    quantity = parsed.Amount;
                }

                return await ShopModule.ExecuteSellAsync(itemRepository, shopRepository, gameEvents, discordId, request.Item, quantity);
            }
        }
    }
}
