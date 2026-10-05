using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Lo que el sorteo de las cajas necesita saber de AFUERA (el sorteo en sí es puro, ver GameData/BoxLoot.cs): qué materiales de recolección y qué drops
// de monstruo existen, y hasta qué zona llegó el jugador. La compra de cajas usa MaxUnlockedRankAsync para cerrar las de zonas que todavía no abrió.
public interface IBoxContextService
{
    // El contexto de una apertura de este jugador (zona máxima desbloqueada + los materiales y drops que se pueden sortear).
    Task<BoxRollContext> BuildAsync(ulong discordId, CancellationToken cancellationToken = default);

    // La zona más alta que el jugador tiene desbloqueada, 1-indexada por dificultad (1 si no tiene cuenta todavía).
    Task<int> MaxUnlockedRankAsync(ulong discordId, CancellationToken cancellationToken = default);
}

public sealed class BoxContextService(
    IItemRepository itemRepository, IMonsterRepository monsterRepository, IZoneRepository zoneRepository, IUserRepository userRepository) : IBoxContextService
{
    // Los materiales, los monstruos y las zonas casi no cambian (solo cuando se corre un seed), así que se guardan unos minutos: lo único que se lee
    // siempre es el jugador.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private sealed record Statics(
        DateTime LoadedUtc, IReadOnlyList<Zone> OrderedZones, IReadOnlyList<GatherCandidate> Gather, IReadOnlyList<ZoneDropCandidate> ZoneDrops);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Statics? _statics;

    public async Task<BoxRollContext> BuildAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        var statics = await LoadStaticsAsync(cancellationToken);
        return new BoxRollContext(await MaxUnlockedRankAsync(discordId, cancellationToken), statics.Gather, statics.ZoneDrops);
    }

    public async Task<int> MaxUnlockedRankAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        // GetByDiscordIdAsync (no GetOrCreate): mirar la tienda no tiene que crearle una cuenta a nadie.
        var player = await userRepository.GetByDiscordIdAsync(discordId, cancellationToken);
        if (player is null)
        {
            return 1;
        }

        var statics = await LoadStaticsAsync(cancellationToken);
        return ZoneRanking.MaxUnlockedRank(statics.OrderedZones, player.Level, player.HighestZoneCleared);
    }

    private async Task<Statics> LoadStaticsAsync(CancellationToken cancellationToken)
    {
        var current = _statics;
        if (current is not null && DateTime.UtcNow - current.LoadedUtc < CacheLifetime)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_statics is not null && DateTime.UtcNow - _statics.LoadedUtc < CacheLifetime)
            {
                return _statics;
            }

            var ordered = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync(cancellationToken));

            var gather = new List<GatherCandidate>();
            foreach (string type in new[] { "Madera", "Mineral" })
            {
                gather.AddRange((await itemRepository.GetAllByTypeAsync(type, cancellationToken))
                    .Select(i => new GatherCandidate(i.ItemId, i.Name, i.Rarity, i.Emoji, type)));
            }

            var zoneDrops = new List<ZoneDropCandidate>();
            var materials = (await itemRepository.GetAllByTypeAsync("Material", cancellationToken)).ToDictionary(i => i.Name);
            foreach (var zoneMonster in await monsterRepository.GetAllAsync(cancellationToken))
            {
                // El jefe suelta un cofre, no un material: no es un "drop de zona" para las cajas.
                if (zoneMonster.Kind == MonsterKind.Boss)
                {
                    continue;
                }

                int rank = ZoneRanking.RankOf(ordered, zoneMonster.ZoneId);
                if (rank < 1)
                {
                    continue;
                }

                foreach (string name in zoneMonster.Monster.DropItemNames)
                {
                    if (materials.TryGetValue(name, out var item))
                    {
                        zoneDrops.Add(new ZoneDropCandidate(item.ItemId, item.Name, item.Rarity, item.Emoji, rank, zoneMonster.Kind == MonsterKind.Travel));
                    }
                }
            }

            _statics = new Statics(DateTime.UtcNow, ordered, gather, zoneDrops);
            return _statics;
        }
        finally
        {
            _gate.Release();
        }
    }
}
