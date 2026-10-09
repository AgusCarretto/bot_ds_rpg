using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Arma la cola de recordatorios (GameData/Reminders.cs) a partir del estado REAL del jugador cada vez que termina un comando. No guarda nada propio en la base
// más que la cola (IReminderRepository): los cooldowns, la comida de las mascotas y el diario se vuelven a leer de donde ya viven, así que no hay un segundo reloj que
// se pueda desfasar del juego.
public interface IReminderService
{
    // Después de un comando (de barra, de botón o de texto) del jugador en ese canal. commandHint: el nombre del comando o el id del botón; sirve solo para decidir si hay que
    // volver a leer las mascotas o el diario (los cooldowns de la tabla se leen siempre). NUNCA tira: un recordatorio es un extra y no puede romper un comando.
    Task SyncAsync(ulong discordId, ulong channelId, string commandHint, CancellationToken cancellationToken = default);

    // Lo mismo para varios jugadores (los que participaron de una raid, que no tienen una interacción propia al terminar).
    Task SyncManyAsync(IEnumerable<ulong> discordIds, ulong channelId, string commandHint, CancellationToken cancellationToken = default);

    // La última vez que el bot vio al jugador usar algo (UTC); null si no lo vio desde que arrancó. El reloj lo usa para no avisarle una espera corta a quien ya está jugando.
    DateTime? LastActivityUtc(ulong discordId);
}

public sealed class ReminderService(
    IReminderRepository reminders,
    ICooldownRepository cooldowns,
    IUserRepository users,
    IPetRepository pets,
    IPlayerBonusService bonuses) : IReminderService
{
    private readonly ConcurrentDictionary<ulong, DateTime> _lastActivity = new();

    public DateTime? LastActivityUtc(ulong discordId) => _lastActivity.TryGetValue(discordId, out var at) ? at : null;

    public async Task SyncManyAsync(IEnumerable<ulong> discordIds, ulong channelId, string commandHint, CancellationToken cancellationToken = default)
    {
        foreach (ulong id in discordIds.Distinct())
        {
            await SyncAsync(id, channelId, commandHint, cancellationToken);
        }
    }

    public async Task SyncAsync(ulong discordId, ulong channelId, string commandHint, CancellationToken cancellationToken = default)
    {
        try
        {
            _lastActivity[discordId] = DateTime.UtcNow;
            if (!ReminderPolicy.IsEligible(discordId) || channelId == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var rows = await cooldowns.GetAllAsync(discordId, cancellationToken);
            var candidates = new List<ReminderDue>(ReminderCatalog.FromCooldowns(rows.Select(r => (r.Command, r.LastExecutedUtc)), now));

            bool pet = ReminderCatalog.MayChangePet(commandHint);
            bool daily = ReminderCatalog.MayChangeDaily(commandHint);
            if (pet || daily)
            {
                var user = await users.GetByDiscordIdAsync(discordId, cancellationToken);
                if (user is not null)
                {
                    if (pet)
                    {
                        var playerBonuses = await bonuses.GetAsync(discordId, user.FuegoNuevo, cancellationToken);
                        var owned = await pets.GetOwnedAsync(discordId, cancellationToken);
                        if (ReminderCatalog.PetDue(owned, playerBonuses.PetFeedCooldown, now) is { } petDue)
                        {
                            candidates.Add(petDue);
                        }
                    }

                    if (daily && ReminderCatalog.DailyDue(user.LastDailyClaim, now) is { } dailyDue)
                    {
                        candidates.Add(dailyDue);
                    }
                }
            }

            if (candidates.Count > 0)
            {
                await reminders.SyncAsync(discordId, channelId, candidates, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex); // sin recordatorio, el comando salió igual
        }
    }
}
