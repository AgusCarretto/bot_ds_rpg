using System.Globalization;
using System.Text.Json;

namespace BotDsRpg.GameData;

// /vote (v0.18.0): votar en top.gg (por el bot o por el servidor: lo decide el token que se cargue, el código es el mismo) da una caja y resetea el cooldown del jefe. Todo lo que se decide sin base de datos ni red vive acá; el pedido a top.gg está en
// Services/TopGgVoteClient.cs, el cobro (UNA transacción) en Repositories/VoteRepository.cs y la pantalla en Modules/VoteModule.cs.
//
// Cómo sabe el bot que votaste: le pregunta a top.gg (GET /projects/@me/votes/{id}?source=discord, con el token del proyecto) cuando escribís /vote. NO hay webhook ni puerto abierto:
// el bot sigue siendo un solo proceso sin red pública. Cada voto se cobra UNA vez: la fila de cooldown 'vote' guarda el momento del voto cobrado (created_at de top.gg), y un voto solo
// se puede cobrar si es MÁS NUEVO que el último cobrado, así que el mismo voto no paga dos veces aunque se pida en el mismo instante.
public static class VoteRules
{
    // La caja que paga cada voto. Si algún día se quiere otra (o una que siga la zona del jugador, como las de las misiones), este es el único lugar del nombre.
    public const string RewardBoxName = "Baúl de Roble";
    public const int RewardBoxes = 1;

    // Cada cuánto se puede votar: top.gg deja votar de nuevo 12 horas después. Es lo que muestra /cd y lo que usa el recordatorio; el cobro NO depende de esto (depende de que el voto sea más nuevo
    // que el último cobrado), así que si top.gg cambiara la espera solo se desfasa el texto.
    public static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    // Cuánto falta para poder votar de nuevo: lo que dice top.gg (expires_at) si lo sabemos, o 12 horas después del voto. null si ya se puede.
    public static TimeSpan? NextVoteIn(DateTime nowUtc, DateTime votedUtc, DateTime? expiresUtc)
    {
        var at = expiresUtc ?? votedUtc + Interval;
        var left = at - nowUtc;
        return left > TimeSpan.Zero ? left : null;
    }
}

public enum VoteCheckStatus
{
    NotConfigured, // no hay token de top.gg: votar todavía no está habilitado
    NoActiveVote,  // top.gg no tiene un voto vigente de esta persona (nunca votó, o el último ya venció)
    Active,        // hay un voto vigente
    Unavailable,   // no se pudo saber (top.gg caído, token malo, respuesta rara): no se cobra nada y se reintenta
}

// weight: cuánto cuenta el voto para top.gg (los fines de semana vale doble). Hoy no cambia el premio, pero se lee por si se quiere usar.
public sealed record VoteCheck(VoteCheckStatus Status, DateTime? CreatedUtc = null, DateTime? ExpiresUtc = null, int Weight = 1);

// Lee la respuesta de top.gg: 200 con { created_at, expires_at, weight }, 404 si nunca votó o el voto venció. Pura (código HTTP + texto) para probarla sin red.
public static class TopGgVoteParser
{
    public static VoteCheck Parse(int statusCode, string? body, DateTime nowUtc)
    {
        if (statusCode == 404)
        {
            return new VoteCheck(VoteCheckStatus.NoActiveVote);
        }

        if (statusCode != 200 || string.IsNullOrWhiteSpace(body))
        {
            return new VoteCheck(VoteCheckStatus.Unavailable);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ReadDate(root, "created_at") is not { } created)
            {
                return new VoteCheck(VoteCheckStatus.Unavailable);
            }

            var expires = ReadDate(root, "expires_at");
            if (expires is { } e && e <= nowUtc)
            {
                return new VoteCheck(VoteCheckStatus.NoActiveVote); // top.gg dice 404 al vencer, pero por si llega con el vencimiento ya pasado
            }

            int weight = root.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out int n) ? Math.Max(1, n) : 1;
            return new VoteCheck(VoteCheckStatus.Active, created, expires, weight);
        }
        catch (JsonException)
        {
            return new VoteCheck(VoteCheckStatus.Unavailable);
        }
    }

    private static DateTime? ReadDate(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
}
