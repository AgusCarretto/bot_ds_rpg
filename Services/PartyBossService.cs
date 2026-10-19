using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// El jefe de zona en equipo ("/boss @amigo", "aa boss @amigo"; v0.19.0, reemplaza al viejo /raid) visto desde los comandos: armar el grupo y publicarlo. La lógica vive en Modules/RaidModule.cs
// (estática, como el resto del bot); esto solo junta las dependencias que ella pide, para que /boss (Modules/AdventureModule.cs) y "aa boss" (Modules/TextCommandModule.Combat.cs) no
// tengan que inyectar una docena de repositorios cada uno.
public interface IPartyBossService
{
    // Valida a quien arma el grupo y a cada compañero (cuenta, misma zona máxima, cooldown, vida, nivel...) y arma la sesión. Session != null = lista para publicar; si no, Rejection dice por qué.
    Task<RaidModule.PartyPreparation> PrepareAsync(ulong starterId, string starterName, IReadOnlyList<RaidModule.PartyMember> companions);

    // Publica el grupo (session.ReplyTarget ya tiene que estar puesto): lo suma al servicio de raids, anota a quien lo armó y arma el vencimiento de las invitaciones. false = se le adelantó otra acción.
    bool TryOpen(RaidSession session, ulong channelId);
}

public sealed class PartyBossService(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IMonsterRepository monsterRepository,
    IZoneRepository zoneRepository,
    ICombatSessionService combatSessions,
    IRaidSessionService raidSessions,
    ICooldownRepository cooldownRepository,
    IBuffRepository buffRepository,
    IPlayerBonusService bonusService,
    IAdventureRepository adventureRepository,
    IGameEvents gameEvents) : IPartyBossService
{
    public Task<RaidModule.PartyPreparation> PrepareAsync(ulong starterId, string starterName, IReadOnlyList<RaidModule.PartyMember> companions) =>
        RaidModule.PreparePartyAsync(
            userRepository, itemRepository, monsterRepository, zoneRepository, combatSessions, raidSessions, cooldownRepository, buffRepository, bonusService,
            starterId, starterName, companions);

    public bool TryOpen(RaidSession session, ulong channelId) =>
        RaidModule.TryOpenParty(session, raidSessions, adventureRepository, new RaidTurnContext(userRepository, gameEvents, raidSessions), channelId);
}
