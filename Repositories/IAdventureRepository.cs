using BotDsRpg.Models;

namespace BotDsRpg.Repositories;

public interface IAdventureRepository
{
    // Aplica de forma atómica el resultado de un /hunt o /travel: valida y actualiza el
    // cooldown, suma oro, aplica la XP (con nivelado y curación de HP al subir de nivel),
    // resta el HP perdido en el combate, y si corresponde agrega un material al inventario.
    // Devuelve null si el cooldown seguía vigente (perdió la carrera contra otra ejecución
    // concurrente del mismo comando); en ese caso no se aplicó ningún cambio.
    // Precondición: el usuario ya debe existir en la tabla users (llamar antes a
    // IUserRepository.GetOrCreateUserAsync).
    Task<LevelUpOutcome?> ApplyRewardAsync(
        ulong discordId,
        string commandName,
        TimeSpan cooldownDuration,
        int goldReward,
        int xpReward,
        int hpLost,
        int? droppedItemId,
        int droppedItemQuantity,
        CancellationToken cancellationToken = default);
}
