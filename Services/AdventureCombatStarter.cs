using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

public sealed class AdventureCombatStarter(
    ICooldownRepository cooldownRepository,
    IAdventureRepository adventureRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    ICombatSessionService combatSessions) : IAdventureCombatStarter
{
    public async Task<CombatStartOutcome> PrepareAsync(
        ulong discordId, CooldownDefinition definition, IReadOnlyList<MonsterTemplate> monsterPool, CancellationToken cancellationToken = default)
    {
        if (combatSessions.Peek(discordId) is not null)
        {
            return new CombatStartOutcome(CombatStartStatus.AlreadyInCombat, null, null);
        }

        var remaining = await cooldownRepository.GetRemainingAsync(discordId, definition.CommandName, definition.Duration, cancellationToken);
        if (remaining is not null)
        {
            return new CombatStartOutcome(CombatStartStatus.OnCooldown, remaining, null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId, cancellationToken: cancellationToken);

        if (player.CurrentHp <= 0)
        {
            return new CombatStartOutcome(CombatStartStatus.NoHp, null, null);
        }

        // El cooldown se cobra ACÁ, al iniciar el combate: si el jugador lo abandona o se le
        // acaba el tiempo de respuesta, igual "gastó" el intento (no puede reintentar gratis).
        bool claimed = await adventureRepository.TryClaimCooldownAsync(discordId, definition.CommandName, definition.Duration, cancellationToken);
        if (!claimed)
        {
            // Perdió la carrera contra otra ejecución concurrente del mismo comando (ej. doble click).
            return new CombatStartOutcome(CombatStartStatus.RaceLost, null, null);
        }

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId, cancellationToken) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId, cancellationToken) : null;
        int weaponDamage = ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily);
        // El nivel pesa en combate por sí solo (Ataque/Defensa Base = Nivel), no solo a través del
        // equipo — ver GameData/CombatStats.cs.
        int playerDamage = CombatStats.TotalAttack(player.Level, weaponDamage);
        int defense = CombatStats.TotalDefense(player.Level, amulet?.StatValue ?? 0);

        // Pasivas de clase (ver GameData/ClassPassives.cs), resueltas una sola vez acá. El HP
        // Máximo/Actual de COMBATE se escala por MaxHpMultiplier (Guerrero ×1.2) preservando el
        // % de vida real del jugador — CombatState.ToDbHpDelta se encarga de "destraducir" el
        // delta de vuelta a unidades reales al persistir (ver Modules/AdventureModule.cs).
        var passives = ClassPassives.For(player.Class);
        int combatMaxHp = (int)Math.Round(player.MaxHp * passives.MaxHpMultiplier);
        int combatCurrentHp = (int)Math.Round(player.CurrentHp * passives.MaxHpMultiplier);

        var monster = MonsterCatalog.RollFrom(monsterPool);
        int monsterMaxHp = Random.Shared.Next(monster.MinHp, monster.MaxHp + 1);
        int monsterDamage = Random.Shared.Next(monster.MinDamage, monster.MaxDamage + 1);

        var state = new CombatState(
            CommandName: definition.CommandName,
            MonsterName: monster.Name,
            MonsterEmoji: monster.Emoji,
            MonsterMaxHp: monsterMaxHp,
            MonsterCurrentHp: monsterMaxHp,
            MonsterDamage: monsterDamage,
            MonsterDropItemNames: monster.DropItemNames,
            PlayerMaxHp: combatMaxHp,
            PlayerCurrentHp: combatCurrentHp,
            PlayerStartingHp: combatCurrentHp,
            PlayerDamage: playerDamage,
            PlayerDefense: defense,
            PlayerLevel: player.Level,
            Passives: passives);

        return new CombatStartOutcome(CombatStartStatus.Started, null, state);
    }
}
