using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

// Títulos (GameData/TitleCatalog.cs). Qué títulos tiene un jugador NO se guarda: sale de los contadores y eventos que el juego ya lleva. Lo único que se escribe es cuál tiene puesto (users.equipped_title).
public interface ITitleRepository
{
    // Todo lo que los títulos necesitan saber del jugador, en una sola conexión: los contadores, el nivel más alto que alcanzó, los jefes de la escalera que venció (por orden de zona, 1 = la más fácil),
    // los hallazgos únicos y el nivel de la bendición Brasa de Color. zonesByDifficulty son las zonas normales ya ordenadas por dificultad (ZoneRanking.OrderByDifficulty).
    Task<TitleContext> GetContextAsync(ulong discordId, IReadOnlyList<Zone> zonesByDifficulty, CancellationToken cancellationToken = default);

    // Pone el título (null = sacarlo). No comprueba que lo tenga desbloqueado: eso lo hace quien llama con GetContextAsync. False si el jugador no tiene cuenta.
    Task<bool> SetEquippedAsync(ulong discordId, string? titleKey, CancellationToken cancellationToken = default);
}
