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
    public const string FightLost = "fight_lost";           // perdió una pelea (cacería, viaje, jefe o raid); detail = cuál ("hunt", "travel", "boss", "raid")

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

    // Casino: amount = el oro de la jugada (ganado neto en casino_win, perdido en casino_loss); detail = "slots" / "coinflip".
    public const string CasinoWin = "casino_win";
    public const string CasinoLoss = "casino_loss";

    // PvP.
    public const string DuelWin = "duel_win";               // ganó un duelo amistoso; detail = el nombre del rival
    public const string DuelLoss = "duel_loss";             // perdió un duelo amistoso; detail = el nombre del rival
    public const string ArenaJoin = "arena_join";           // se anotó en el torneo del día; detail = el día (yyyy-MM-dd)
    public const string ArenaWin = "arena_win";             // ganó el torneo del día; detail = el día

    // Progreso de misiones y logros (los registra el propio sistema).
    public const string MissionClaimed = "mission_claimed"; // detail = clave de la misión
    public const string AchievementUnlocked = "achievement_unlocked"; // detail = clave del logro

    // v0.9.0.
    public const string CommandUsed = "command_used";       // un comando (de barra o de texto) que se ejecutó bien: alimenta el logro Comandante
    public const string EnemyDefeated = "enemy_defeated";   // un enemigo vencido (cacería, viaje, jefe o raid): se registra junto con hunt_win / travel_win / boss_win / raid_win
    public const string BankOpened = "bank_opened";         // compró la cuenta del banco
    public const string BankDeposit = "bank_deposit";       // amount = oro depositado
    public const string Dismantle = "dismantle";            // amount = unidades desmanteladas; detail = el ítem
    public const string Enchant = "enchant";                // un intento de encantamiento; detail = "weapon:3" (de qué pieza y qué tier salió)

    // Las victorias de combate de las que sale un "enemigo vencido" (GameEventExtensions.RecordVictoryAsync suma el enemy_defeated con ellas).
    public static bool IsEnemyWin(string kind) => kind is HuntWin or TravelWin or BossWin or RaidWin;
}
