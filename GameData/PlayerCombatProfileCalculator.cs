using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Stats de combate resueltos de un jugador puntual (nivel + equipo + pasivas de clase) — la MISMA
// cuenta que hacía Services/AdventureCombatStarter.cs para /hunt, /travel y /boss solitarios,
// extraída acá para que Modules/RaidModule.cs (jefes cooperativos) la reuse sin duplicarla.
public sealed record PlayerCombatProfile(int Damage, int Defense, int CombatMaxHp, int CombatCurrentHp, ClassPassiveProfile Passives, int AttackBuffPercent = 0);

public static class PlayerCombatProfileCalculator
{
    // attackBuffPercent: el +% de ataque de un banquete activo (GameData/AttackBuff.cs), sobre el ataque TOTAL (nivel + arma).
    public static PlayerCombatProfile Resolve(User player, Item? weapon, Item? amulet, int attackBuffPercent = 0)
    {
        int damage = AttackBuff.Apply(CombatStats.TotalAttack(player.Level, WeaponDamage(player, weapon)), attackBuffPercent);
        int defense = CombatStats.TotalDefense(player.Level, AmuletDefense(player, amulet));

        var passives = ClassPassives.For(player.Class);
        int combatMaxHp = (int)Math.Round(player.MaxHp * passives.MaxHpMultiplier);
        int combatCurrentHp = (int)Math.Round(player.CurrentHp * passives.MaxHpMultiplier);

        return new PlayerCombatProfile(damage, defense, combatMaxHp, combatCurrentHp, passives, attackBuffPercent);
    }

    // Lo que SUMA el arma puesta al ataque: su stat con la sinergia de la clase y, encima, el encantamiento (GameData/Enchantments.cs). El perfil, el combate y
    // /enchant usan estas dos cuentas para mostrar y pelear con lo mismo.
    public static int WeaponDamage(User player, Item? weapon) =>
        Enchantments.Apply(ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily), player.WeaponEnchant);

    // Lo que SUMA el amuleto puesto a la defensa, con su encantamiento.
    public static int AmuletDefense(User player, Item? amulet) => Enchantments.Apply(amulet?.StatValue ?? 0, player.AmuletEnchant);
}
