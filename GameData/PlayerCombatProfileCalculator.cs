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
        int weaponDamage = ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily);
        int damage = AttackBuff.Apply(CombatStats.TotalAttack(player.Level, weaponDamage), attackBuffPercent);
        int defense = CombatStats.TotalDefense(player.Level, amulet?.StatValue ?? 0);

        var passives = ClassPassives.For(player.Class);
        int combatMaxHp = (int)Math.Round(player.MaxHp * passives.MaxHpMultiplier);
        int combatCurrentHp = (int)Math.Round(player.CurrentHp * passives.MaxHpMultiplier);

        return new PlayerCombatProfile(damage, defense, combatMaxHp, combatCurrentHp, passives, attackBuffPercent);
    }
}
