namespace BotDsRpg.GameData;

// Bonus de daño cuando la clase del jugador coincide con la familia de su arma equipada
// (ClassCatalog.WeaponType vs items.weapon_family). No restringe qué se puede equipar —
// cualquier clase puede usar cualquier arma — solo el bonus de daño depende del match.
public static class ClassWeaponSynergy
{
    public const double Multiplier = 1.5;

    public static bool Applies(string playerClass, string? weaponFamily)
    {
        if (weaponFamily is null)
        {
            return false;
        }

        var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == playerClass);
        return classDef is not null && classDef.WeaponType == weaponFamily;
    }

    public static int ApplyBonus(int baseWeaponDamage, string playerClass, string? weaponFamily) =>
        Applies(playerClass, weaponFamily) ? (int)Math.Round(baseWeaponDamage * Multiplier) : baseWeaponDamage;
}
