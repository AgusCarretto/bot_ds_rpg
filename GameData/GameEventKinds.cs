namespace BotDsRpg.GameData;

// El vocabulario de los eventos de juego: game_events.kind y player_stats.stat_key usan estos mismos nombres. Cada vez que
// pasa algo de esta lista el bot lo registra (Services/GameEventService.cs), y de ahí salen las misiones, los logros y las
// mediciones. Para sumar un evento nuevo: una constante acá, registrarlo donde ocurre y (si corresponde) usarlo en
// MissionCatalog / AchievementCatalog. No hace falta tocar la base: los valores son texto.
public static class GameEventKinds
{
    // Cuenta y progreso.
    public const string Start = "start";                    // se registró (/start)
    public const string LevelUp = "level_up";               // subió de nivel; amount = niveles que subió, detail = nivel nuevo

    // Combate (cada uno suma 1 por pelea ganada).
    public const string HuntWin = "hunt_win";
    public const string TravelWin = "travel_win";
    public const string BossWin = "boss_win";
    public const string RaidWin = "raid_win";
    public const string FightLost = "fight_lost";           // perdió una pelea (solitaria)

    // Recolección y forja.
    public const string Chop = "chop";                      // un /chop exitoso
    public const string Mine = "mine";                      // un /mine exitoso
    public const string GatheredUnits = "gathered_units";   // unidades que dio la recolección; amount = cantidad
    public const string Craft = "craft";                    // forjó un ítem; detail = nombre del ítem

    // Economía.
    public const string DailyClaim = "daily_claim";
    public const string ShopGoldSpent = "shop_gold_spent";  // amount = oro gastado en la tienda; detail = ítem
    public const string ShopGoldEarned = "shop_gold_earned";// amount = oro ganado vendiendo; detail = ítem
    public const string GoldGiven = "gold_given";           // amount = oro que dio a otro jugador
    public const string GoldReceived = "gold_received";     // amount = oro que recibió de otro jugador
    public const string BoxOpened = "box_opened";           // detail = nombre de la caja
    // Un trofeo (material que ningún monstruo suelta) que el jugador consigue por PRIMERA vez en una caja: amount = cuántos
    // nuevos; detail = sus nombres. Es el contador del logro Coleccionista (distintos, no repetidos: ver player_collection).
    public const string TrophyFound = "trophy_found";
    public const string MiniEvent = "mini_event";           // se sumó a un minievento y cobró; detail = el tipo (Stones / Wood / Silver)
    public const string Trade = "trade";                    // un cambio de materiales con otro jugador; detail = "lo que dio->lo que recibió"

    // Progreso de misiones y logros (los registra el propio sistema).
    public const string MissionClaimed = "mission_claimed"; // detail = clave de la misión
    public const string AchievementUnlocked = "achievement_unlocked"; // detail = clave del logro
}
