using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el comando del ADMINISTRADOR. A propósito NO tiene [Summary] ni versión de barra: no figura en ninguna lista de comandos y no se registra en Discord, así que
// nadie que no sea administrador lo ve. Toda la lógica (y la seguridad) está en AdminGiveHandler.
public partial class TextCommandModule
{
    // "aa admingive gold 1m" / "aa admingive item Cofre de Oro 3 @alguien" — dar sin tener (ver AdminGiveHandler).
    [Command("admingive")]
    [Alias("admin-give", "admindar")]
    public async Task AdminGiveAsync([Remainder] string args = "")
    {
        try
        {
            string? reply = await AdminGiveHandler.ExecuteAsync(
                adminAccess, userRepository, itemRepository, progressionRepository, adminRepository, gameEvents, Context.User.Id, args);
            if (reply is not null)
            {
                // Solo se mencionan usuarios (el ganador de un sorteo se entera); nunca @everyone ni roles, ni aunque el administrador los escriba en un nombre mal puesto.
                await ReplyAsync(reply, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            if (adminAccess.IsAdmin(Context.User.Id))
            {
                await ReplyAsync("¡Upa! Falló admingive; mirá el registro del servidor.");
            }
        }
    }
}
