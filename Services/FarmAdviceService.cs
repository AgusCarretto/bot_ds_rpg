using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// El consejo de "qué farmear" para un jugador (la lógica de elegir está en GameData/FarmAdvisor.cs, acá solo se juntan los datos).
public interface IFarmAdvisor
{
    // Null si no hay nada que aconsejar (no tiene cuenta, su zona no tiene recetas, ya tiene forjado todo lo que ve) o si algo falló:
    // un consejo es un extra, NUNCA puede romper el comando al que se le agrega.
    Task<FarmAdvice?> AdviceAsync(ulong discordId, CancellationToken cancellationToken = default);
}

public sealed class FarmAdviceService(
    IUserRepository userRepository, IInventoryRepository inventoryRepository, IRecipeRepository recipeRepository,
    IZoneRepository zoneRepository, IItemRepository itemRepository, IMonsterRepository monsterRepository) : IFarmAdvisor
{
    // Las recetas, las zonas, los monstruos y sus drops casi no cambian (solo cuando se corre un seed), así que se guardan unos minutos:
    // cada /chop, /mine y victoria pide un consejo y no tiene sentido releer todo eso cada vez. Lo único que se lee siempre es lo del jugador.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private sealed record Statics(
        DateTime LoadedUtc, IReadOnlyList<RecipeDetails> Recipes, IReadOnlyList<Zone> Zones, IReadOnlyList<ZoneMonster> Monsters,
        HashSet<string> Woods, HashSet<string> Minerals);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Statics? _statics;

    public async Task<FarmAdvice?> AdviceAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        try
        {
            var player = await userRepository.GetByDiscordIdAsync(discordId, cancellationToken);
            if (player is null)
            {
                return null;
            }

            var statics = await GetStaticsAsync(cancellationToken);
            var view = RecipeCatalog.ViewFor(statics.Recipes, statics.Zones, player.Class, player.CurrentZoneId);
            if (view.Zone is null || view.Recipes.Count == 0)
            {
                return null;
            }

            var owned = (await inventoryRepository.GetByDiscordIdAsync(discordId, cancellationToken))
                .ToDictionary(e => e.ItemName, e => e.Quantity);

            // De dónde sale cada ingrediente: madera y minerales de la recolección; el resto lo suelta un monstruo de la zona de la receta.
            var zoneMonsters = statics.Monsters.Where(m => m.ZoneId == view.Zone.ZoneId).ToList();
            FarmSource SourceOf(string itemName)
            {
                if (statics.Woods.Contains(itemName))
                {
                    return FarmSource.Chop;
                }

                if (statics.Minerals.Contains(itemName))
                {
                    return FarmSource.Mine;
                }

                var dropper = zoneMonsters.FirstOrDefault(m => m.Monster.DropItemNames.Contains(itemName));
                return dropper?.Kind switch
                {
                    MonsterKind.Hunt => FarmSource.Hunt,
                    MonsterKind.Travel => FarmSource.Travel,
                    MonsterKind.Boss => FarmSource.Boss,
                    _ => FarmSource.Unknown,
                };
            }

            return FarmAdvisor.Choose(view.Recipes, owned, player.Gold, SourceOf);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    private async Task<Statics> GetStaticsAsync(CancellationToken cancellationToken)
    {
        var current = _statics;
        if (current is not null && DateTime.UtcNow - current.LoadedUtc < CacheLifetime)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_statics is { } fresh && DateTime.UtcNow - fresh.LoadedUtc < CacheLifetime)
            {
                return fresh;
            }

            _statics = new Statics(
                DateTime.UtcNow,
                await recipeRepository.GetAllAsync(cancellationToken),
                await zoneRepository.GetAllAsync(cancellationToken),
                await monsterRepository.GetAllAsync(cancellationToken),
                (await itemRepository.GetAllByTypeAsync("Madera", cancellationToken)).Select(i => i.Name).ToHashSet(),
                (await itemRepository.GetAllByTypeAsync("Mineral", cancellationToken)).Select(i => i.Name).ToHashSet());

            return _statics;
        }
        finally
        {
            _gate.Release();
        }
    }
}
