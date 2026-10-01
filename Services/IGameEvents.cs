namespace BotDsRpg.Services;

// Un aviso para el jugador que salió de registrar un evento (una misión que se completó, un logro que se desbloqueó).
// Public = se muestra en el canal (los logros se festejan); si no, solo lo ve el jugador (slash) o igual en el canal (texto,
// que no tiene mensajes privados).
public sealed record GameNotice(string Text, bool Public);

// Punto único donde el bot cuenta lo que pasa en el juego. Los comandos registran sus eventos acá (RecordAsync) y no se
// ocupan de nada más: el servicio los guarda, actualiza los contadores y — desde que existen las misiones y los logros —
// avanza el progreso y deja los avisos en una cola que Program.cs entrega apenas termina el comando (TakeNotices).
//
// REGLA: registrar un evento NUNCA rompe el comando. RecordAsync no tira excepciones: si la base falla, queda en el log
// (BotLog.Warn) y el jugador ni se entera — medir no puede costarle una recompensa a nadie.
public interface IGameEvents
{
    // amount: 1 por defecto ("pasó una vez"); en eventos de oro o de unidades, la cantidad. zoneId y detail son opcionales.
    Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null);

    // Los avisos pendientes de ese jugador, en orden. Los saca de la cola: cada aviso se entrega una sola vez.
    IReadOnlyList<GameNotice> TakeNotices(ulong discordId);
}
