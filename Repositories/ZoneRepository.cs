using System.Data;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class ZoneRepository(IDbConnectionFactory connectionFactory) : IZoneRepository
{
    public async Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {ZoneSql.SelectColumns}
            FROM zones
            WHERE kind = 'normal'
            ORDER BY min_level;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<Zone>(command);
        return rows.AsList();
    }

    public async Task<Zone?> GetByIdAsync(int zoneId, CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {ZoneSql.SelectColumns}
            FROM zones
            WHERE zone_id = @ZoneId;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { ZoneId = zoneId }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Zone>(command);
    }

    public async Task<Zone?> GetGateAsync(CancellationToken cancellationToken = default)
    {
        string sql = $"""
            SELECT {ZoneSql.SelectColumns}
            FROM zones
            WHERE kind = 'gate'
            ORDER BY zone_id
            LIMIT 1;
            """;

        using IDbConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Zone>(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }
}
