using System.Data.Common;

namespace BotDsRpg.Data;

// Abstrae la creación de conexiones a la base de datos: los repositorios dependen
// de esta interfaz (no de Npgsql directamente) para quedar desacoplados y testeables.
// Devuelve DbConnection (no IDbConnection) porque los repositorios que necesitan
// transacciones explícitas (ej. AdventureRepository) requieren OpenAsync/BeginTransactionAsync.
public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}
