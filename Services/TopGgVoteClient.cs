using System.Collections.Concurrent;
using System.Net.Http.Headers;
using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// La configuración del voto, del .env o de las variables del servicio (mismo criterio que MiniEventSettings):
//   TopGg__Token    el token del PROYECTO en top.gg (el bot O el servidor: la API v1 llama «proyecto» a los dos; un secreto: nunca se imprime ni se guarda en el repo). Sin él, votar queda "dormido": /vote solo cuenta qué se lleva quien vote.
//   TopGg__VoteUrl  el link donde se vota (https://top.gg/...). Sin él, el mensaje dice que busquen Asado y Acero en top.gg.
//   TopGg__ApiBase  solo para probar contra un servidor falso; por defecto la API real.
public sealed record VoteSettings(string? Token, string? VoteUrl, string ApiBase)
{
    public const string DefaultApiBase = "https://top.gg/api/v1";

    public bool Enabled => !string.IsNullOrWhiteSpace(Token);

    public static VoteSettings FromEnvironment()
    {
        static string? Read(string key)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return new VoteSettings(Read("TopGg__Token"), Read("TopGg__VoteUrl"), (Read("TopGg__ApiBase") ?? DefaultApiBase).TrimEnd('/'));
    }
}

public interface ITopGgVoteClient
{
    // ¿Está habilitado el voto (hay token)? Sin token, CheckAsync devuelve NotConfigured sin hacer ningún pedido.
    bool Enabled { get; }

    // El link para votar (puede ser null si no se configuró).
    string? VoteUrl { get; }

    // El voto vigente de esta persona según top.gg. NUNCA tira excepción: una falla de red o una respuesta rara es Unavailable (y queda en el log), para que /vote le diga "probá de nuevo"
    // en lugar de romperse.
    Task<VoteCheck> CheckAsync(ulong discordId, CancellationToken cancellationToken = default);
}

// Habla con la API v1 de top.gg: GET {base}/projects/@me/votes/{discordId}?source=discord con "Authorization: Bearer <token>". Dos protecciones para no abusar de la API (no documenta límites):
// un pedido por persona cada 5 segundos como máximo (la respuesta anterior se reutiliza) y un tope de 6 segundos de espera por pedido.
public sealed class TopGgVoteClient(VoteSettings? settings = null, HttpClient? httpClient = null, Func<DateTime>? clock = null) : ITopGgVoteClient
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);
    private const int CacheLimit = 2000;

    private readonly VoteSettings _settings = settings ?? VoteSettings.FromEnvironment();
    private readonly HttpClient _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<ulong, (DateTime At, VoteCheck Check)> _recent = new();

    public bool Enabled => _settings.Enabled;

    public string? VoteUrl => _settings.VoteUrl;

    public async Task<VoteCheck> CheckAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return new VoteCheck(VoteCheckStatus.NotConfigured);
        }

        var now = _clock();
        if (_recent.TryGetValue(discordId, out var cached) && now - cached.At < CacheFor)
        {
            return cached.Check;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.ApiBase}/projects/@me/votes/{discordId}?source=discord");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var response = await _http.SendAsync(request, timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            var check = TopGgVoteParser.Parse((int)response.StatusCode, body, _clock());

            if (check.Status == VoteCheckStatus.Unavailable)
            {
                // Solo el código: el cuerpo podría traer cualquier cosa y el token nunca se imprime.
                BotLog.Info($"top.gg no dio un voto usable (HTTP {(int)response.StatusCode}); 401/403 suele ser un token mal cargado.");
                return check;
            }

            Remember(discordId, now, check);
            return check;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            BotLog.Warn(ex);
            return new VoteCheck(VoteCheckStatus.Unavailable);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return new VoteCheck(VoteCheckStatus.Unavailable);
        }
    }

    private void Remember(ulong discordId, DateTime now, VoteCheck check)
    {
        if (_recent.Count >= CacheLimit)
        {
            foreach (var key in _recent.Where(e => now - e.Value.At >= CacheFor).Select(e => e.Key).ToList())
            {
                _recent.TryRemove(key, out _);
            }
        }

        _recent[discordId] = (now, check);
    }
}
