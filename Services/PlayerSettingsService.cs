using System.Collections.Concurrent;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// La configuración de cada jugador (v0.26.1, /config). Se lee al arrancar cada pelea, así que lleva un caché en memoria como el del idioma (Services/PlayerLanguageService.cs).
public interface IPlayerSettingsService
{
    // NUNCA tira: sin fila, o si la tabla no existe o la base falla, devuelve PlayerSettings.Default (el bot anda como siempre).
    Task<PlayerSettings> GetAsync(ulong discordId);

    // Guarda la opción y la deja al día en el caché (sin tocar las otras opciones del jugador). A propósito NO atrapa errores: /config tiene que poder decirle al jugador que no se guardó.
    Task SetShowFightSummaryAsync(ulong discordId, bool value);

    // Qué recetario ve en la herrería (v0.28.1): true = el completo, false = el clásico. Misma regla: no atrapa errores.
    Task SetFullRecipeBookAsync(ulong discordId, bool value);
}

public sealed class PlayerSettingsService(ISettingsRepository repository) : IPlayerSettingsService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LogEvery = TimeSpan.FromMinutes(1);

    // Después de un fallo (la tabla sin crear, la base caída) no se vuelve a consultar durante este rato: sin esto CADA pelea repetiría la consulta que falla.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ulong, (PlayerSettings Settings, DateTime ExpiresUtc)> _cache = new();
    private DateTime _lastLogUtc = DateTime.MinValue;
    private DateTime _failingUntilUtc = DateTime.MinValue;

    public async Task<PlayerSettings> GetAsync(ulong discordId)
    {
        if (_cache.TryGetValue(discordId, out var hit) && hit.ExpiresUtc > DateTime.UtcNow)
        {
            return hit.Settings;
        }

        if (DateTime.UtcNow < _failingUntilUtc)
        {
            return PlayerSettings.Default;
        }

        try
        {
            var settings = await repository.GetAsync(discordId) ?? PlayerSettings.Default;
            return Remember(discordId, settings);
        }
        catch (Exception ex)
        {
            _failingUntilUtc = DateTime.UtcNow + RetryAfterFailure;
            LogThrottled(ex);
            return PlayerSettings.Default;
        }
    }

    public async Task SetShowFightSummaryAsync(ulong discordId, bool value)
    {
        await repository.SetShowFightSummaryAsync(discordId, value);
        await RefreshAsync(discordId);
    }

    public async Task SetFullRecipeBookAsync(ulong discordId, bool value)
    {
        await repository.SetFullRecipeBookAsync(discordId, value);
        await RefreshAsync(discordId);
    }

    // Después de guardar una opción se vuelve a leer la fila ENTERA: con varias opciones, armar el caché solo con la que se acaba de cambiar pisaría las otras con su valor por defecto (cuando el caché estaba frío).
    // Si esa lectura falla, el guardado ya se hizo: se saca al jugador del caché y la próxima lectura trae lo que haya.
    private async Task RefreshAsync(ulong discordId)
    {
        try
        {
            Remember(discordId, await repository.GetAsync(discordId) ?? PlayerSettings.Default);
        }
        catch (Exception ex)
        {
            _cache.TryRemove(discordId, out _);
            LogThrottled(ex);
        }
    }

    private PlayerSettings Remember(ulong discordId, PlayerSettings settings)
    {
        _cache[discordId] = (settings, DateTime.UtcNow + CacheLifetime);
        return settings;
    }

    // Una tabla sin crear fallaría en cada pelea: se avisa una vez por minuto como mucho.
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
