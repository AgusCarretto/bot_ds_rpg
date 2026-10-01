using BotDsRpg.GameData;

namespace BotDsRpg.Repositories;

public interface IBuffRepository
{
    // Qué ítems dan un buff (item_buffs), por id de ítem. Pocos (los banquetes): sirve para listarlos en la tienda.
    Task<IReadOnlyDictionary<int, ItemBuff>> GetItemBuffsAsync(CancellationToken cancellationToken = default);

    // El buff de un ítem puntual, o null si ese ítem solo cura.
    Task<ItemBuff?> GetItemBuffAsync(int itemId, CancellationToken cancellationToken = default);

    // Activa el buff de ataque del jugador por "minutes" minutos desde ahora. REEMPLAZA al que hubiera (no se acumulan).
    Task<ActiveBuff> ActivateAttackAsync(ulong discordId, int percent, int minutes, string? source, CancellationToken cancellationToken = default);

    // El buff de ataque vigente del jugador, o null si no tiene o ya venció.
    Task<ActiveBuff?> GetActiveAttackAsync(ulong discordId, CancellationToken cancellationToken = default);
}
