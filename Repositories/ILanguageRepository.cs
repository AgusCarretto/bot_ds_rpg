namespace BotDsRpg.Repositories;

// El idioma que eligió (o que se le detectó a) cada jugador, en la tabla player_language (Database/add_language.sql). Sin clave foránea a users: se puede elegir idioma antes de /start.
public interface ILanguageRepository
{
    // «es» o «en»; null si no hay nada guardado.
    Task<string?> GetAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Guarda (o pisa) el idioma del jugador.
    Task SetAsync(ulong discordId, string language, CancellationToken cancellationToken = default);
}
