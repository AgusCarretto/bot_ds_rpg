namespace BotDsRpg.Models;

public sealed class Item
{
    public int ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Rarity { get; init; } = string.Empty;
    public int StatValue { get; init; }
    public int SellPrice { get; init; } // lo que paga la tienda al comprarle un ítem al jugador
    public int BuyPrice { get; init; }  // lo que cobra la tienda al venderle un ítem al jugador
    public string? WeaponFamily { get; init; } // solo si Type == "Weapon"; ver GameData/ClassWeaponSynergy.cs
    public string? ClassRequirement { get; init; } // NULL = cualquier clase; ver Modules/EquipModule.cs y Modules/ForgeModule.cs
}
