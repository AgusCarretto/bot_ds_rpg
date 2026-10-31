namespace BotDsRpg.GameData;

// Invitar amigos (v0.43.0, `aa invitedby @amigo` y `/invitedby`). El dueño (2026-10-31): «después del tutorial: si te invitó un amigo, escribí aa invitedby y el nombre de tu amigo así le damos un premio…
// obviamente solo 1 validez por cuenta», con 20k para el que invita, 80k más cuando el invitado llega a nivel 5 y 10k para el invitado. Y después: «que cualquiera lo pueda hacer pero 1 SOLA vez… si ya pasó
// el nivel 5 o de FN, que le dé los 80k también instantáneo» (los jugadores de antes son pocos y no le molesta que se regalen un poco de oro).
// Cómo se cobra (UNA transacción, una vez por cuenta): Repositories/ReferralRepository.cs. El pago del nivel 5 de un invitado que todavía no llegó lo dispara el evento level_up (Services/GameEventService.cs).
public static class ReferralRules
{
    public const int NewPlayerGold = 10_000;          // para el invitado, al anotar quién lo invitó
    public const int InviterStartGold = 20_000;       // para el que invitó, en el mismo momento
    public const int InviterLevelGold = 80_000;       // para el que invitó, cuando el invitado llega a RequiredLevel (en el momento si ya había llegado)
    public const int RequiredLevel = 5;

    // El invitado ya "llegó": nivel 5 o más, o al menos un Fuego Nuevo (que vuelve el nivel a 1 pero ya pasó por todo).
    public static bool ReachedGoal(int level, int fuegoNuevo) => level >= RequiredLevel || fuegoNuevo > 0;
}
