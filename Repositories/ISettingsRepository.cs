using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

// La configuración de cada jugador, en la tabla player_settings (Database/add_player_settings.sql). Sin clave foránea a users.
public interface ISettingsRepository
{
    // null si el jugador nunca guardó nada (se usa PlayerSettings.Default).
    Task<PlayerSettings?> GetAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Guarda (o pisa) si se muestra la tablita del resumen del combate. No toca las demás opciones del jugador.
    Task SetShowFightSummaryAsync(ulong discordId, bool value, CancellationToken cancellationToken = default);

    // Guarda (o pisa) qué recetario ve en la herrería: true = el completo, false = el clásico (v0.28.1). No toca las demás opciones del jugador.
    Task SetFullRecipeBookAsync(ulong discordId, bool value, CancellationToken cancellationToken = default);

    // Guarda (o pisa) el banner del perfil: la clave de una bendición, o null para no usar ninguno (v0.50.0). No toca las demás opciones del jugador.
    Task SetProfileBannerAsync(ulong discordId, string? blessingKey, CancellationToken cancellationToken = default);

    // Guarda (o pisa) qué líneas de /cd ve y en qué orden (v0.51.0, GameData/CooldownLayout.cs); null = todas, como siempre. No toca las demás opciones del jugador.
    Task SetCdLayoutAsync(ulong discordId, string? layout, CancellationToken cancellationToken = default);

    // Guarda (o pisa) cómo ve /drops: true = todas las zonas juntas (la vista compacta), false = una zona por vez con un menú (v0.65.0). No toca las demás opciones del jugador.
    Task SetDropsAllZonesAsync(ulong discordId, bool value, CancellationToken cancellationToken = default);
}
