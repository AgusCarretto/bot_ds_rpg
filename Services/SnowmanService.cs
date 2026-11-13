using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Microsoft.Extensions.Configuration;

namespace BotDsRpg.Services;

// Una tirada para el panel en vivo («Fulano tiró 3 Petardos −1.800»).
public sealed record SnowFeedLine(string Name, SnowProjectile Projectile, int Count, int Damage);

// El Muñeco de Nieve Gigante (v0.57.0): el estado y los tiros pasan por el repositorio (la verdad está en la base, compartida por todos los servidores); acá viven la vida
// inicial de la configuración y las últimas tiradas EN MEMORIA (si el bot se reinicia, el panel arranca sin historial; nada más).
public interface ISnowmanService
{
    int InitialHp { get; }

    Task<SnowmanState> GetStateAsync(ulong viewerId);

    // Tira (solo mientras caen Regalos: lo mira quien llama). Anota la tirada en el historial del panel si salió bien.
    Task<SnowThrowOutcome> ThrowAsync(ulong discordId, string displayName, SnowProjectile projectile, int count);

    // Las últimas tiradas, la más nueva primero.
    IReadOnlyList<SnowFeedLine> Feed();
}

public sealed class SnowmanService(ISnowmanRepository repository, IChristmasService christmas, int initialHp) : ISnowmanService
{
    private const int FeedSize = 5;
    private readonly object _lock = new();
    private readonly LinkedList<SnowFeedLine> _feed = new();

    public int InitialHp => initialHp;

    public Task<SnowmanState> GetStateAsync(ulong viewerId) => repository.GetStateAsync(viewerId, christmas.Settings.Season, initialHp);

    public async Task<SnowThrowOutcome> ThrowAsync(ulong discordId, string displayName, SnowProjectile projectile, int count)
    {
        // Vuelve con ×1,5 mientras caen Regalos; si cae justo al final, queda caído.
        bool canRespawn = christmas.PhaseNow() == ChristmasPhase.Farm;
        var outcome = await repository.ThrowAsync(discordId, christmas.Settings.Season, projectile, count, initialHp, canRespawn);
        if (outcome.Status == SnowThrowStatus.Ok)
        {
            lock (_lock)
            {
                _feed.AddFirst(new SnowFeedLine(displayName, projectile, outcome.Count, outcome.Damage));
                while (_feed.Count > FeedSize)
                {
                    _feed.RemoveLast();
                }
            }
        }

        return outcome;
    }

    public IReadOnlyList<SnowFeedLine> Feed()
    {
        lock (_lock)
        {
            return _feed.ToList();
        }
    }

    // Christmas__SnowmanHp: la vida de la primera vuelta (sin ella, 100.000). Solo cuenta al abrir la primera vuelta de la temporada.
    public static int LoadInitialHp(IConfiguration configuration) =>
        int.TryParse(configuration["Christmas:SnowmanHp"], out int hp) && hp > 0 ? hp : SnowmanRules.DefaultInitialHp;
}
