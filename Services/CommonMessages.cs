using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// Los mensajes que Program.cs le manda al jugador cuando el comando ni llega a ejecutarse (todavía no tiene cuenta, un error que se escapó, un comando que no existe). Viven acá, y no
// escritos en Program.cs, para poder probarlos en los dos idiomas (GameData/Loc.cs): Program es la clase de arranque y no se puede usar desde un arnés.
public static class CommonMessages
{
    public static string NotRegistered(ulong discordId) =>
        Loc.F($"<@{discordId}> ¡Bienvenido a Asado y Acero RPG! 🥩 Para empezar tu aventura, primero tenés que elegir una clase con el comando `/start`.");

    public static string UnexpectedError() => Loc.T("¡Upa! Ocurrió un error inesperado al procesar tu comando.");

    public static string UnknownCommand() => Loc.T("❌ No reconozco ese comando. Probá `/info` o `aa info` para ver la lista completa.");

    public static string CommandFailed() => Loc.T("¡Upa! Algo falló procesando ese comando, intentá de nuevo en un momento.");
}
