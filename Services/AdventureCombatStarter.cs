using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

public sealed class AdventureCombatStarter(
    ICooldownRepository cooldownRepository,
    IAdventureRepository adventureRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IMonsterRepository monsterRepository,
    IZoneRepository zoneRepository,
    ICombatSessionService combatSessions) : IAdventureCombatStarter
{
    public Task<CombatStartOutcome> PrepareAsync(
        ulong discordId, CooldownDefinition definition, IReadOnlyList<MonsterTemplate> monsterPool, CancellationToken cancellationToken = default) =>
        PrepareInternalAsync(discordId, definition, _ => Task.FromResult(monsterPool), CombatStartStatus.NoMonstersInZone, isBossFight: false, extraGate: null, cancellationToken);

    public Task<CombatStartOutcome> PrepareHuntAsync(ulong discordId, CancellationToken cancellationToken = default) =>
        PrepareInternalAsync(
            discordId,
            CooldownCatalog.Hunt,
            player => monsterRepository.GetMonstersByZoneAsync(player.CurrentZoneId, cancellationToken),
            CombatStartStatus.NoMonstersInZone,
            isBossFight: false,
            extraGate: null,
            cancellationToken);

    public Task<CombatStartOutcome> PrepareBossAsync(ulong discordId, CancellationToken cancellationToken = default) =>
        PrepareInternalAsync(
            discordId,
            CooldownCatalog.Boss,
            async player =>
            {
                var boss = await monsterRepository.GetBossByZoneAsync(player.CurrentZoneId, cancellationToken);
                return boss is null ? [] : new[] { boss };
            },
            CombatStartStatus.NoBossInZone,
            isBossFight: true,
            extraGate: async player =>
            {
                // El jefe solo se puede desafiar una vez que el jugador ya está al nivel mínimo
                // de la PRÓXIMA zona (a lo que ganarle te deja avanzar) — no antes. Se compara por
                // POSICIÓN en la lista ordenada por min_level, no por zone_id crudo (mismo criterio
                // que el gate de /zona, ver Modules/ZoneModule.ExecuteTravelAsync).
                var orderedZones = (await zoneRepository.GetAllAsync(cancellationToken)).OrderBy(z => z.MinLevel).ToList();
                int currentRank = orderedZones.FindIndex(z => z.ZoneId == player.CurrentZoneId);

                if (currentRank < 0 || currentRank + 1 >= orderedZones.Count)
                {
                    // Última zona conocida (o zona no encontrada, no debería pasar): no hay
                    // "próxima zona" cuyo nivel exigirle, así que no hay gate extra que aplicar.
                    return null;
                }

                int requiredLevel = orderedZones[currentRank + 1].MinLevel;
                return player.Level < requiredLevel
                    ? new CombatStartOutcome(CombatStartStatus.NotLeveledForBoss, null, null, requiredLevel)
                    : null;
            },
            cancellationToken);

    private async Task<CombatStartOutcome> PrepareInternalAsync(
        ulong discordId,
        CooldownDefinition definition,
        Func<User, Task<IReadOnlyList<MonsterTemplate>>> resolvePool,
        CombatStartStatus emptyPoolStatus,
        bool isBossFight,
        Func<User, Task<CombatStartOutcome?>>? extraGate,
        CancellationToken cancellationToken)
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

        if (extraGate is not null)
        {
            var blocked = await extraGate(player);
            if (blocked is not null)
            {
                return blocked;
            }
        }

        // Antes de cobrar el cooldown: si la zona actual todavía no tiene monstruos (o jefe)
        // cargado, es un problema de contenido, no del jugador — no le quememos el intento por eso.
        var monsterPool = await resolvePool(player);
        if (monsterPool.Count == 0)
        {
            return new CombatStartOutcome(emptyPoolStatus, null, null);
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
            MonsterGoldBonus: monster.GoldBonus,
            MonsterXpBonus: monster.XpBonus,
            BossZoneId: isBossFight ? player.CurrentZoneId : null,
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
