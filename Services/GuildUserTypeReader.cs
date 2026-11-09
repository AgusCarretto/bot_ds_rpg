using BotDsRpg.GameData;
using Discord;
using Discord.Commands;

namespace BotDsRpg.Services;

// Reemplaza al UserTypeReader<T> por defecto de Discord.Net.Commands para los comandos de texto
// que aceptan "de qué jugador" (ej. "aa p @alguien", "aa i @alguien" — ver
// Modules/TextCommandModule.cs). El reader por defecto de la librería resuelve SIEMPRE con
// CacheMode.CacheOnly (hardcodeado en Discord.Net, sin forma de configurarlo), así que si el
// jugador mencionado no está en la caché de miembros del bot — algo común incluso con el intent
// GuildMembers habilitado, porque Discord.Net solo la completa de a poco salvo que se fuerce una
// descarga (ver AlwaysDownloadUsers en Program.cs, que igual no la garantiza al 100% en el momento
// exacto) — la búsqueda falla con ObjectNotFound aunque el usuario exista y sea válido.
//
// Esta versión cae a un pedido REST (CacheMode.AllowDownload) cuando la caché no lo tiene, así que
// siempre resuelve un mención/ID válido sin importar el estado de la caché.
//
// Se registra apuntando a IUser (no a IGuildUser ni SocketGuildUser): Discord.Net tiene un bug
// conocido donde overridear el reader por defecto de un tipo que IMPLEMENTA IUser (en vez de IUser
// directo) no siempre toma efecto — overridear la interfaz base sí funciona siempre. Como todo lo
// que consumimos de este resultado es .Id/.Username (y un cast opcional a IGuildUser para el
// apodo, ver GameModule.GetDisplayName), alcanza con IUser.
//
// Por NOMBRE solo vale el nombre EXACTO (v0.51.3, el dueño: «aa dragon breed po ya me activa el breed con Polar Frost»): hasta la v0.51.2 se tomaba el PRIMER jugador cuyo nombre arrancaba con
// lo escrito, y con comandos que mueven plata (aa give po 5k) o mandan invitaciones, un typo o una palabra suelta le caía a cualquiera. Ahora un nombre parcial no resuelve a nadie y el aviso dice
// cómo hacerlo bien (mención, nombre completo entre comillas, o responder su mensaje).
public sealed class GuildUserTypeReader : TypeReader
{
    public override async Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
    {
        if (context.Guild is null)
        {
            return TypeReaderResult.FromError(CommandError.ObjectNotFound, Loc.T("Este comando necesita ejecutarse en un canal de servidor."));
        }

        var user = await ResolveAsync(context.Guild, input);
        return user is not null
            ? TypeReaderResult.FromSuccess(user)
            : TypeReaderResult.FromError(CommandError.ObjectNotFound, NotFoundMessage());
    }

    // El aviso cuando no se encontró a nadie: dice cómo escribirlo bien (el dueño no quiere que una palabra suelta le caiga a un desconocido).
    public static string NotFoundMessage() =>
        Loc.T("No encontré a ningún jugador con ese nombre exacto. Mencionalo con @, escribí su nombre completo (entre comillas si tiene espacios) o respondé a su mensaje.");

    // Mención «<@id>», ID crudo o nombre EXACTO (usuario, apodo del servidor o nombre para mostrar; sin importar mayúsculas). null si no hay nadie. Sirve también a los comandos que
    // leen a quién mirar de un texto libre (aa cd @alguien).
    public static async Task<IGuildUser?> ResolveAsync(IGuild guild, string input)
    {
        input = (input ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            return null;
        }

        // Por mención "<@id>" / "<@!id>"
        if (MentionUtils.TryParseUser(input, out ulong id))
        {
            var mentioned = await guild.GetUserAsync(id, CacheMode.AllowDownload);
            if (mentioned is not null)
            {
                return mentioned;
            }
        }

        // Por ID crudo
        if (ulong.TryParse(input, out id))
        {
            var byId = await guild.GetUserAsync(id, CacheMode.AllowDownload);
            if (byId is not null)
            {
                return byId;
            }
        }

        // Por nombre: la búsqueda de Discord devuelve los que ARRANCAN con lo escrito (vía REST, funciona aunque no esté en caché), pero solo se acepta el que se llama EXACTAMENTE así.
        var matches = await guild.SearchUsersAsync(input, limit: 10, mode: CacheMode.AllowDownload);
        return matches.FirstOrDefault(u => IsExactName(u, input));
    }

    // Pura (se prueba sin Discord): el nombre de usuario, el apodo del servidor o el nombre para mostrar es EXACTAMENTE lo escrito, sin importar mayúsculas.
    public static bool IsExactName(IGuildUser user, string input) =>
        string.Equals(user.Username, input, StringComparison.OrdinalIgnoreCase)
        || string.Equals(user.Nickname, input, StringComparison.OrdinalIgnoreCase)
        || string.Equals(user.GlobalName, input, StringComparison.OrdinalIgnoreCase);
}
