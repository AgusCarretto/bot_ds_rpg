using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Arma el "luchador" de un jugador para el PvP (el duelo amistoso y la arena): nivel + equipo + pasivas de clase + el banquete que tenga
// activo, con la MISMA cuenta que usa /hunt (GameData/PlayerCombatProfileCalculator.cs), y la vida completa de combate ajustada por clase
// para PvP (GameData/PvpTuning.cs: sin ese ajuste el Guerrero le ganaba a todo el mundo).
public interface IDuelFighterFactory
{
    // Null si el jugador no tiene cuenta.
    Task<DuelFighter?> BuildAsync(ulong discordId, string displayName, CancellationToken cancellationToken = default);
}

public sealed class DuelFighterFactory(IUserRepository userRepository, IItemRepository itemRepository, IBuffRepository buffRepository) : IDuelFighterFactory
{
    public async Task<DuelFighter?> BuildAsync(ulong discordId, string displayName, CancellationToken cancellationToken = default)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId, cancellationToken);
        if (player is null)
        {
            return null;
        }

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId, cancellationToken) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId, cancellationToken) : null;
        var buff = await buffRepository.GetActiveAttackAsync(discordId, cancellationToken);

        // El amuleto cuenta con su DEF de PvP (la escalera vieja, ver PvpTuning.Amulet): el balance de duelos y Arena se midió con esa.
        var profile = PlayerCombatProfileCalculator.Resolve(player, weapon, PvpTuning.Amulet(amulet), buff?.AttackPercent ?? 0);

        return new DuelFighter(
            discordId, displayName, player.Level, player.Class,
            new CombatantProfile(profile.Damage, profile.Defense, player.Class, profile.Passives),
            PvpTuning.MaxHp(profile.CombatMaxHp, player.Class));
    }
}
