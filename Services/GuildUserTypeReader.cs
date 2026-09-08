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
public sealed class GuildUserTypeReader : TypeReader
{
    public override async Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
    {
        if (context.Guild is null)
        {
            return TypeReaderResult.FromError(CommandError.ObjectNotFound, "Este comando necesita ejecutarse en un canal de servidor.");
        }

        // Por mención "<@id>" / "<@!id>"
        if (MentionUtils.TryParseUser(input, out ulong id))
        {
            var mentioned = await context.Guild.GetUserAsync(id, CacheMode.AllowDownload);
            if (mentioned is not null)
            {
                return TypeReaderResult.FromSuccess(mentioned);
            }
        }

        // Por ID crudo
        if (ulong.TryParse(input, out id))
        {
            var byId = await context.Guild.GetUserAsync(id, CacheMode.AllowDownload);
            if (byId is not null)
            {
                return TypeReaderResult.FromSuccess(byId);
            }
        }

        // Por nombre de usuario o apodo (prefijo, vía REST — funciona aunque no esté en caché).
        var matches = await context.Guild.SearchUsersAsync(input, limit: 5, mode: CacheMode.AllowDownload);
        var exact = matches.FirstOrDefault(u =>
            string.Equals(u.Username, input, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Nickname, input, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return TypeReaderResult.FromSuccess(exact);
        }

        if (matches.Count > 0)
        {
            return TypeReaderResult.FromSuccess(matches.First());
        }

        return TypeReaderResult.FromError(CommandError.ObjectNotFound, "No encontré a ningún jugador con esa mención/nombre en este server.");
    }
}
