namespace BotDsRpg.Services;

// Quién es administrador del bot (v0.34.0): los IDs de Discord de la variable de entorno Admin__UserIds, separados por coma (o por espacio o punto y coma). NADA se escribe en el código: sin la variable no hay ningún administrador
// y los comandos de admin están apagados para todos. Es lo único que decide quién puede usar «aa admingive»: el ID sale del mensaje que llega por el gateway de Discord, no de lo que escriba la persona, así que no se puede falsear.
public interface IAdminAccess
{
    // ¿Este usuario de Discord es administrador?
    bool IsAdmin(ulong discordId);

    // ¿Hay al menos un administrador configurado? (Sin ninguno, los comandos de admin explican que falta configurarlos.)
    bool HasAnyAdmin { get; }
}

public sealed class AdminAccess(string? userIds) : IAdminAccess
{
    private readonly HashSet<ulong> _ids = Parse(userIds);

    public bool HasAnyAdmin => _ids.Count > 0;

    public bool IsAdmin(ulong discordId) => _ids.Contains(discordId);

    // "123, 456;789" → {123, 456, 789}. Lo que no es un ID (letras, vacío, 0) se ignora: una variable mal escrita nunca le da admin a nadie por accidente.
    public static HashSet<ulong> Parse(string? text) =>
        (text ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => ulong.TryParse(token, out ulong id) && id > 0 ? id : 0UL)
            .Where(id => id > 0)
            .ToHashSet();
}
