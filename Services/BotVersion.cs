using System.Reflection;

namespace BotDsRpg.Services;

// La versión del bot ("0.9.7"), la de <Version> en el csproj: se loguea al arrancar y se muestra en /info, así se sabe
// qué versión está corriendo (local o en Railway) sin ir a mirar el deploy.
public static class BotVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        string? informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // El SDK le agrega "+<hash del commit>" cuando compila con git a mano ("0.9.7+6d62f36..."): se muestra solo la versión.
        string version = informational?.Split('+')[0] ?? "desconocida";
        return string.IsNullOrWhiteSpace(version) ? "desconocida" : version;
    }
}
