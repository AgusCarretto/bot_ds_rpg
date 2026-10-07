using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Las cajas gratis de cada zona (tabla zone_boxes, GameData/ZoneBoxes.cs) para los módulos: el jefe (qué caja da al repetirlo) y las pantallas de misiones. Casi no cambia (solo cuando
// se corre un seed o el dueño la edita), así que se guarda unos minutos. Los premios de misiones y logros NO pasan por acá: RewardPayer lee la tabla dentro de su propia transacción.
public interface IZoneBoxService
{
    Task<ZoneBoxTable> GetAsync(CancellationToken cancellationToken = default);

    // Lo que le falta a la tabla para tener TODAS las zonas con sus cajas ("Zona 6:daily", ...). Vacío = completa. Lo loguea el arranque: una zona nueva sin cajas rompería su premio de misiones.
    Task<IReadOnlyList<string>> MissingAsync(CancellationToken cancellationToken = default);
}

public sealed class ZoneBoxService(IDbConnectionFactory connectionFactory, IZoneRepository zoneRepository) : IZoneBoxService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTime LoadedUtc, ZoneBoxTable Table)? _cached;

    public async Task<ZoneBoxTable> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = _cached;
        if (current is { } fresh && DateTime.UtcNow - fresh.LoadedUtc < CacheLifetime)
        {
            return fresh.Table;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } again && DateTime.UtcNow - again.LoadedUtc < CacheLifetime)
            {
                return again.Table;
            }

            using DbConnection connection = connectionFactory.CreateConnection();
            var table = await ZoneBoxQueries.LoadAsync(connection, null, cancellationToken);
            _cached = (DateTime.UtcNow, table);
            return table;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> MissingAsync(CancellationToken cancellationToken = default)
    {
        var table = await GetAsync(cancellationToken);
        var normal = (await zoneRepository.GetAllAsync(cancellationToken)).Select(z => (z.ZoneId, z.Name)).ToList();
        var gate = await zoneRepository.GetGateAsync(cancellationToken);
        return table.Missing(normal, gate is null ? [] : [(gate.ZoneId, gate.Name)]);
    }
}
