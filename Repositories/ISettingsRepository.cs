using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

// La configuración de cada jugador, en la tabla player_settings (Database/add_player_settings.sql). Sin clave foránea a users.
public interface ISettingsRepository
{
    // null si el jugador nunca guardó nada (se usa PlayerSettings.Default).
    Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Guarda (o pisa) si se muestra la tablita del resumen del combate.
    Task SetShowFightSummaryAsync(ulong discordId, bool value, CancellationToken cancellationToken = default);
}
