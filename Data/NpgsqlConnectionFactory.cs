using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BotDsRpg.Data;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        // Igual que con el token de Discord: la cadena de conexión nunca se hardcodea.
        // Desarrollo: dotnet user-secrets set "Postgres:ConnectionString" "<cadena>"
        // Producción: variable de entorno "Postgres__ConnectionString"
        _connectionString = configuration["Postgres:ConnectionString"]
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión a PostgreSQL. Configurala con " +
                "'dotnet user-secrets set \"Postgres:ConnectionString\" \"<cadena>\"' en desarrollo, " +
                "o con la variable de entorno 'Postgres__ConnectionString' en producción.");
    }

    public DbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}
