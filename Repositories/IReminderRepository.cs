using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

// Un aviso que ya venció y se sacó de la cola para mandarlo.
public sealed record DueReminder(ulong DiscordId, string Kind, ulong ChannelId, DateTime DueAtUtc);

// Los recordatorios de cooldown (v0.16.0; GameData/Reminders.cs). Nada de esto toca el juego: si algo falla, el jugador simplemente no recibe el aviso.
public interface IReminderRepository
{
    // Deja la cola del jugador como la describe "candidates" (los cooldowns que todavía corren) en UNA sola instrucción: lo apagado en sus ajustes se descarta y se borra,
    // lo demás se guarda o se actualiza (un comando nuevo pisa el aviso anterior del mismo tipo). El canal es el del comando, salvo que el aviso ya existiera para el MISMO
    // momento: ahí se queda con el canal original (que un comando de otro canal no le robe el aviso a un cooldown que ya estaba corriendo).
    Task SyncAsync(ulong discordId, ulong channelId, IReadOnlyList<ReminderDue> candidates, CancellationToken cancellationToken = default);

    // Saca de la cola (los borra) los avisos que ya vencieron, de a todos los jugadores. Es atómico: un aviso se saca UNA sola vez aunque corran dos relojes.
    Task<IReadOnlyList<DueReminder>> ClaimDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    // Los tipos que el jugador apagó (vacío = recibe todos).
    Task<IReadOnlyList<string>> GetOffKindsAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Reemplaza la lista de tipos apagados y borra de la cola los avisos que ya estaban pendientes de esos tipos.
    Task SetOffKindsAsync(ulong discordId, IReadOnlyCollection<string> offKinds, CancellationToken cancellationToken = default);
}
