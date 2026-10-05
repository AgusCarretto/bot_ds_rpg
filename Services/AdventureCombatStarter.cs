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
    IBuffRepository buffRepository,
    ICombatSessionService combatSessions,
    IRaidSessionService raidSessions) : IAdventureCombatStarter
{
    public Task<CombatStartOutcome> PrepareTravelAsync(ulong discordId, CancellationToken cancellationToken = default) =>
        PrepareInternalAsync(
            discordId,
            CooldownCatalog.Travel,
            async player =>
            {
                var travelMonster = await monsterRepository.GetTravelMonsterByZoneAsync(player.CurrentZoneId, cancellationToken);
                return travelMonster is null ? [] : new[] { travelMonster };
            },
            CombatStartStatus.NoMonstersInZone,
            isBossFight: false,
            extraGate: null,
            cancellationToken);

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
                // de la PRÓXIMA zona (a lo que ganarle te deja avanzar) — no antes. Ver
                // GameData/ZoneRanking.cs (también la usa Modules/RaidModule.cs para el mismo gate
                // en los jefes cooperativos, y Modules/ZoneModule.cs para el gate de /zona).
                var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync(cancellationToken));
                int? requiredLevel = ZoneRanking.RequiredLevelForBoss(orderedZones, player.CurrentZoneId);

                return requiredLevel is not null && player.Level < requiredLevel
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
        // También cuenta un jefe cooperativo activo (Modules/RaidModule.cs) — nadie puede estar en
        // dos combates a la vez, sea solitario o de raid.
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
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
        bool claimed = await adventureRepository.TryClaimCooldownAsync(discordId, definition.CommandName, definition.Duration, definition.RetryAfterFailure, cancellationToken);
        if (!claimed)
        {
            // Perdió la carrera contra otra ejecución concurrente del mismo comando (ej. doble click).
            return new CombatStartOutcome(CombatStartStatus.RaceLost, null, null);
        }

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId, cancellationToken) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId, cancellationToken) : null;
        // Nivel + equipo + pasivas de clase, misma cuenta que usa Modules/RaidModule.cs para los
        // participantes de un jefe cooperativo — ver GameData/PlayerCombatProfileCalculator.cs.
        // CombatState.ToDbHpDelta se encarga de "destraducir" el HP de combate (escalado si
        // corresponde) de vuelta a unidades reales al persistir (ver Modules/AdventureModule.cs).
        // Si comió un banquete antes, su +% de ataque entra desde el primer golpe (vence solo: GetActiveAttackAsync no lo devuelve vencido).
        var buff = await buffRepository.GetActiveAttackAsync(discordId, cancellationToken);
        var profile = PlayerCombatProfileCalculator.Resolve(player, weapon, amulet, buff?.AttackPercent ?? 0);

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
            PlayerMaxHp: profile.CombatMaxHp,
            PlayerCurrentHp: profile.CombatCurrentHp,
            PlayerStartingHp: profile.CombatCurrentHp,
            PlayerDamage: profile.Damage,
            PlayerDefense: profile.Defense,
            PlayerLevel: player.Level,
            PlayerClass: player.Class,
            Passives: profile.Passives,
            AttackBuffPercent: profile.AttackBuffPercent);

        return new CombatStartOutcome(CombatStartStatus.Started, null, state);
    }
}
