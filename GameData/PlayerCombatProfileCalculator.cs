using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Stats de combate resueltos de un jugador puntual (nivel + equipo + pasivas de clase) — la MISMA
// cuenta que hacía Services/AdventureCombatStarter.cs para /hunt, /travel y /boss solitarios,
// extraída acá para que Modules/RaidModule.cs (jefes cooperativos) la reuse sin duplicarla.
public sealed record PlayerCombatProfile(
    int Damage, int Defense, int CombatMaxHp, int CombatCurrentHp, ClassPassiveProfile Passives, int AttackBuffPercent = 0, PlayerBonuses? Bonuses = null);

public static class PlayerCombatProfileCalculator
{
    // attackBuffPercent: el +% de ataque de un banquete activo (GameData/AttackBuff.cs), sobre el ataque TOTAL (nivel + arma).
    // bonuses (v0.10.0 mascotas, v0.12.0 bendiciones, GameData/PlayerBonuses.cs): la defensa de la mascota del Gólem y las bendiciones de poder (ataque, defensa y vida, siempre CHICAS
    // y con tope) suben el ataque y la defensa TOTALES (nivel + equipo) y la vida de combate. Solo las pasan las peleas contra monstruos (solo y raid): los duelos y la Arena no
    // (null), así que lo permanente no pelea entre jugadores. El perfil (/profile) lo calcula con esta misma cuenta.
    // La vida extra de las bendiciones se suma al MaxHpMultiplier de la clase (Guerrero ×1,2): así ToDbHpDelta, que desescala con ese mismo multiplicador, sigue devolviendo vida real.
    public static PlayerCombatProfile Resolve(User player, Item? weapon, Item? amulet, int attackBuffPercent = 0, PlayerBonuses? bonuses = null)
    {
        int damage = PlayerBonuses.Scale(AttackBuff.Apply(CombatStats.TotalAttack(player.Level, WeaponDamage(player, weapon)), attackBuffPercent), bonuses?.AttackMultiplier ?? 1.0);
        int defense = PlayerBonuses.Scale(CombatStats.TotalDefense(player.Level, AmuletDefense(player, amulet)), bonuses?.DefenseMultiplier ?? 1.0);

        var passives = ClassPassives.For(player.Class);
        if (bonuses is not null && bonuses.MaxHpMultiplier > 1.0)
        {
            passives = passives with { MaxHpMultiplier = passives.MaxHpMultiplier * bonuses.MaxHpMultiplier };
        }

        // El ajuste de vida por clase SOLO contra monstruos (GameData/PveTuning.cs): como los bonuses solo los pasan esas peleas, el PvP queda como estaba medido.
        if (bonuses is not null && PveTuning.HpFactor(player.Class) is var pveFactor && pveFactor != 1.0)
        {
            passives = passives with { MaxHpMultiplier = passives.MaxHpMultiplier * pveFactor };
        }

        int combatMaxHp = (int)Math.Round(player.MaxHp * passives.MaxHpMultiplier);
        int combatCurrentHp = (int)Math.Round(player.CurrentHp * passives.MaxHpMultiplier);

        return new PlayerCombatProfile(damage, defense, combatMaxHp, combatCurrentHp, passives, attackBuffPercent, bonuses);
    }

    // Lo que SUMA el arma puesta al ataque: su stat con la sinergia de la clase y, encima, el encantamiento (GameData/Enchantments.cs). El perfil, el combate y
    // /enchant usan estas dos cuentas para mostrar y pelear con lo mismo.
    public static int WeaponDamage(User player, Item? weapon) =>
        Enchantments.Apply(ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily), player.WeaponEnchant);

    // Lo que SUMA el amuleto puesto a la defensa, con su encantamiento.
    public static int AmuletDefense(User player, Item? amulet) => Enchantments.Apply(amulet?.StatValue ?? 0, player.AmuletEnchant);
}
