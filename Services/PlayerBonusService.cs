using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Arma el PlayerBonuses de un jugador: sus mascotas (con la bendición Manada aplicada), sus Fuegos Nuevos, sus bendiciones y sus oficios. Lo consultan el arranque de cada pelea
// (AdventureCombatStarter, el raid), /chop, /mine, /enchant y el perfil: es el ÚNICO lugar que junta las fuentes.
public interface IPlayerBonusService
{
    // NUNCA tira: es un extra y no puede romper una pelea ni una recolección. Si una fuente falla, se loguea y esa parte queda en cero (el jugador pelea con lo que se pudo leer).
    // fuegoNuevo viene del User que el llamador ya tiene (users.fuego_nuevo), así no se vuelve a leer la fila.
    Task<PlayerBonuses> GetAsync(ulong discordId, int fuegoNuevo, CancellationToken cancellationToken = default);
}

public sealed class PlayerBonusService(IPetRepository petRepository, IBlessingRepository blessingRepository, IGameEventRepository eventRepository) : IPlayerBonusService
{
    public async Task<PlayerBonuses> GetAsync(ulong discordId, int fuegoNuevo, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, int> levels = new Dictionary<string, int>();
        try
        {
            levels = await blessingRepository.GetLevelsAsync(discordId, cancellationToken);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }

        // Los oficios (v0.13.0): su XP sale de los contadores de player_stats (cada oficio lee el de su comando), no de una tabla propia.
        Dictionary<string, long>? professionXp = null;
        try
        {
            var stats = await eventRepository.GetStatsAsync(discordId, cancellationToken);
            professionXp = ProfessionCatalog.All.ToDictionary(p => p.Key, p => ProfessionRules.XpFrom(p, stats), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }

        // Manada sube TODOS los bonus de las mascotas a la vez (+10 % por nivel): se aplica una sola vez, acá, y PlayerBonuses ya recibe las mascotas escaladas.
        int packLevel = levels.TryGetValue(BlessingCatalog.PetsKey, out int level) ? Math.Clamp(level, 0, BlessingCatalog.MaxLevel) : 0;
        var pets = (await petRepository.GetBonusesAsync(discordId, cancellationToken)).Scaled(1 + (BlessingCatalog.PetsPerLevel * packLevel));

        return new PlayerBonuses(pets, Math.Max(0, fuegoNuevo), levels.Count == 0 ? null : levels, professionXp);
    }
}
