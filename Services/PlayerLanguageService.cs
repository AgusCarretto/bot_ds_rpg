using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Qué idioma habla cada jugador (GameData/Loc.cs). Se consulta en CADA comando, así que lleva un caché en memoria.
public interface IPlayerLanguageService
{
    // «es» o «en». NUNCA tira: si la tabla no existe o la base falla, devuelve español y el bot anda como siempre.
    // Con algo guardado, gana lo guardado. Sin nada guardado, lo deduce del idioma de Discord del jugador y lo guarda; sin ese dato (un comando de texto no lo trae) devuelve español sin guardar.
    Task<string> ResolveAsync(ulong discordId, string? discordLocale);

    // Guarda el idioma elegido (cualquier cosa que no sea inglés se guarda como español). A propósito NO atrapa errores: /language tiene que poder decirle al jugador que no se guardó.
    Task SetAsync(ulong discordId, string language);
}

public sealed class PlayerLanguageService(ILanguageRepository repository) : IPlayerLanguageService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LogEvery = TimeSpan.FromMinutes(1);

    // Después de un fallo (la tabla sin crear, la base caída) no se vuelve a consultar durante este rato: sin esto CADA comando repetiría la consulta que falla (y llenaría el registro de Postgres de errores).
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ulong, (string Language, DateTime ExpiresUtc)> _cache = new();
    private DateTime _lastLogUtc = DateTime.MinValue;
    private DateTime _failingUntilUtc = DateTime.MinValue;

    // El idioma de Discord del jugador («es-ES», «es-419», «en-US», «pt-BR»…): los que empiezan con «es» son español y cualquier otro, inglés. Sin dato, ninguno.
    public static string? LanguageForLocale(string? locale)
    {
        string text = (locale ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? Loc.Spanish : Loc.English;
    }

    public async Task<string> ResolveAsync(ulong discordId, string? discordLocale)
    {
        if (_cache.TryGetValue(discordId, out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
        {
            return hit.Language;
        }

        if (DateTime.UtcNow < _failingUntilUtc)
        {
            return Loc.Spanish;
        }

        try
        {
            string? saved = await repository.GetAsync(discordId);
            if (saved is not null)
            {
                return Remember(discordId, Loc.Normalize(saved));
            }

            string? detected = LanguageForLocale(discordLocale);
            if (detected is null)
            {
                return Loc.Spanish;
            }

            try
            {
                await repository.SetAsync(discordId, detected);
                return Remember(discordId, detected);
            }
            catch (Exception ex)
            {
                // No se pudo guardar: se usa lo detectado en este comando y se vuelve a intentar en el próximo.
                LogThrottled(ex);
                return detected;
            }
        }
        catch (Exception ex)
        {
            _failingUntilUtc = DateTime.UtcNow + RetryAfterFailure;
            LogThrottled(ex);
            return Loc.Spanish;
        }
    }

    public async Task SetAsync(ulong discordId, string language)
    {
        string normalized = Loc.Normalize(language);
        await repository.SetAsync(discordId, normalized);
        Remember(discordId, normalized);
    }

    private string Remember(ulong discordId, string language)
    {
        _cache[discordId] = (language, DateTime.UtcNow + CacheLifetime);
        return language;
    }

    // Una tabla sin crear fallaría en CADA comando: se avisa una vez por minuto como mucho, no una por comando.
    private void LogThrottled(Exception ex)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastLogUtc >= LogEvery)
        {
            _lastLogUtc = now;
            BotLog.Warn(ex);
        }
    }
}
