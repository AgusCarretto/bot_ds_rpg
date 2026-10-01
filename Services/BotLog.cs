using System.Runtime.CompilerServices;

namespace BotDsRpg.Services;

// Log mínimo a la consola: en producción (Railway) stdout/stderr SON los logs, no hay otro lugar donde mirar. Antes
// casi todos los try/catch de los comandos le respondían "¡Upa! Algo falló" al jugador y se tragaban la excepción, así
// que cuando alguien reportaba un fallo no había forma de saber qué había pasado.
//
// Error/Warn reciben solo la excepción: el archivo y el método los pone el compilador ([CallerFilePath] /
// [CallerMemberName]), así que cada catch se arregla con UNA línea y el log dice dónde ocurrió. Se imprime el stack
// completo (ex.ToString()), no solo el mensaje. Error va a stderr (Railway lo marca como error); Warn y Info a stdout.
public static class BotLog
{
    public static void Error(Exception ex, [CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        Console.Error.WriteLine($"{Stamp()} [ERROR] {Where(file, member)}: {ex}");

    // Para fallas esperables que no hay que alarmar (un mensaje que ya no se puede editar porque venció el token, un
    // temporizador en segundo plano que no llegó a escribir): quedan registradas, pero como advertencia.
    public static void Warn(Exception ex, [CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        Console.WriteLine($"{Stamp()} [AVISO] {Where(file, member)}: {ex.GetType().Name}: {ex.Message}");

    public static void Info(string message) => Console.WriteLine($"{Stamp()} [INFO] {message}");

    // Errores que no pasan por un catch nuestro (excepciones sin manejar, tareas en segundo plano): el contexto lo
    // decide quien llama, porque CallerMemberName ahí diría "Main" o el nombre de un lambda.
    public static void ErrorIn(string context, Exception ex) =>
        Console.Error.WriteLine($"{Stamp()} [ERROR] {context}: {ex}");

    private static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'");

    private static string Where(string file, string member) => $"{Path.GetFileNameWithoutExtension(file)}.{member}";
}
