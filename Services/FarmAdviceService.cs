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
    // cada /tips pide un consejo y no tiene sentido releer todo eso cada vez. Lo único que se lee siempre es lo del jugador.
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
            // Con la puerta abierta (venció al jefe de la última zona) también aconseja el equipo del Fogón (GameData/FogonRules.cs).
            bool gateOpen = FogonRules.IsGateOpen(ZoneRanking.OrderByDifficulty(statics.Zones), player.HighestZoneCleared);
            var view = RecipeCatalog.ViewFor(statics.Recipes, statics.Zones, player.Class, player.CurrentZoneId, gateOpen);
            if (view.Zone is null || view.Recipes.Count == 0)
            {
                return null;
            }

            var owned = (await inventoryRepository.GetByDiscordIdAsync(discordId, cancellationToken))
                .ToDictionary(e => e.ItemName, e => e.Quantity);

            // El arma y el amuleto no están en el inventario: se forjan directo a equipamiento. Lo que lleva puesto cuenta como "ya forjado" y solo
            // se aconseja lo que lo MEJORA (una receta del mismo casillero con igual o menos ataque / defensa no tiene sentido para alguien que ya la pasó).
            var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId, cancellationToken) : null;
            var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId, cancellationToken) : null;
            foreach (var worn in new[] { weapon, amulet })
            {
                if (worn is not null)
                {
                    owned[worn.Name] = 1;
                }
            }

            int WeaponPower(Item item) => ClassWeaponSynergy.ApplyBonus(item.StatValue, player.Class, item.WeaponFamily);
            var candidates = view.Recipes
                .Where(r => r.ResultItem.Type switch
                {
                    "Weapon" when weapon is not null => WeaponPower(r.ResultItem) > WeaponPower(weapon),
                    "Amulet" when amulet is not null => r.ResultItem.StatValue > amulet.StatValue,
                    _ => true,
                })
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

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

                // Primero los de la zona de la receta; si no, de cualquier zona (el equipo del Fogón pide drops de las 5).
                var dropper = zoneMonsters.FirstOrDefault(m => m.Monster.DropItemNames.Contains(itemName))
                    ?? statics.Monsters.FirstOrDefault(m => m.Monster.DropItemNames.Contains(itemName));
                return dropper?.Kind switch
                {
                    MonsterKind.Hunt => FarmSource.Hunt,
                    MonsterKind.Travel => FarmSource.Travel,
                    MonsterKind.Boss => FarmSource.Boss,
                    _ => FarmSource.Unknown,
                };
            }

            var advice = FarmAdvisor.Choose(candidates, owned, player.Gold, SourceOf);

            // Si ya puede forjarla pero ese casillero está ocupado, hay que decirle que primero venda lo equipado.
            if (advice is { CraftableNow: true } && candidates.FirstOrDefault(r => r.ResultItem.Name == advice.RecipeName) is { } ready)
            {
                string? sellFirst = ready.ResultItem.Type switch
                {
                    "Weapon" when weapon is not null => "tu arma equipada",
                    "Amulet" when amulet is not null => "tu amuleto equipado",
                    _ => null,
                };

                advice = advice with { SellFirst = sellFirst };
            }

            return advice;
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
