using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Stats de combate resueltos de un jugador puntual (nivel + equipo + pasivas de clase) — la MISMA
// cuenta que hacía Services/AdventureCombatStarter.cs para /hunt, /travel y /boss solitarios,
// extraída acá para que Modules/RaidModule.cs (jefes cooperativos) la reuse sin duplicarla.
public sealed record PlayerCombatProfile(
    int Damage, int Defense, int CombatMaxHp, int CombatCurrentHp, ClassPassiveProfile Passives, int AttackBuffPercent = 0, PetBonuses? Pets = null);

public static class PlayerCombatProfileCalculator
{
    // attackBuffPercent: el +% de ataque de un banquete activo (GameData/AttackBuff.cs), sobre el ataque TOTAL (nivel + arma).
    // pets (v0.10.0): el +% de defensa de la mascota del Gólem sube la defensa TOTAL (nivel + amuleto). Solo lo pasan las peleas contra monstruos (solo y raid): los duelos
    // y la Arena no lo pasan (null), así que las mascotas no pelean entre jugadores. El perfil (/profile) lo calcula con esta misma cuenta.
    public static PlayerCombatProfile Resolve(User player, Item? weapon, Item? amulet, int attackBuffPercent = 0, PetBonuses? pets = null)
    {
        int damage = AttackBuff.Apply(CombatStats.TotalAttack(player.Level, WeaponDamage(player, weapon)), attackBuffPercent);
        int defense = PetRules.Boost(CombatStats.TotalDefense(player.Level, AmuletDefense(player, amulet)), pets?.DefensePercent ?? 0);

        var passives = ClassPassives.For(player.Class);
        int combatMaxHp = (int)Math.Round(player.MaxHp * passives.MaxHpMultiplier);
        int combatCurrentHp = (int)Math.Round(player.CurrentHp * passives.MaxHpMultiplier);

        return new PlayerCombatProfile(damage, defense, combatMaxHp, combatCurrentHp, passives, attackBuffPercent, pets);
    }

    // Lo que SUMA el arma puesta al ataque: su stat con la sinergia de la clase y, encima, el encantamiento (GameData/Enchantments.cs). El perfil, el combate y
    // /enchant usan estas dos cuentas para mostrar y pelear con lo mismo.
    public static int WeaponDamage(User player, Item? weapon) =>
        Enchantments.Apply(ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily), player.WeaponEnchant);

    // Lo que SUMA el amuleto puesto a la defensa, con su encantamiento.
    public static int AmuletDefense(User player, Item? amulet) => Enchantments.Apply(amulet?.StatValue ?? 0, player.AmuletEnchant);
}
