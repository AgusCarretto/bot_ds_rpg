namespace BotDsRpg.Services;

// ¿Está este jugador en medio de una pelea? (GameData/FightLock.cs decide qué comandos valen mientras tanto.) Junta las tres cosas que son «pelear»: un combate solitario (/hunt, /travel, /boss),
// un jefe en equipo que YA está peleando (no la sala de espera de las invitaciones) y un duelo en curso. Sin efectos: solo lee.
public static class FightGuard
{
    public static bool IsInFight(ICombatSessionService combatSessions, IRaidSessionService? raidSessions, IDuelService? duelService, ulong discordId) =>
        combatSessions.Peek(discordId) is not null
        || raidSessions?.IsInActiveFight(discordId) == true
        || duelService?.IsInDuel(discordId) == true;
}
