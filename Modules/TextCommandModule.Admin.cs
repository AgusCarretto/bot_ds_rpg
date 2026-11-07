using BotDsRpg.GameData;
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
                await ReplyAsync(Loc.T("¡Upa! Falló admingive; mirá el registro del servidor."));
            }
        }
    }

    // "aa admininfo" — cuántas cuentas hay, cuántos juegan y en cuántos servidores está el bot (ver AdminInfoHandler).
    [Command("admininfo")]
    [Alias("admin-info", "admininformacion")]
    public async Task AdminInfoAsync()
    {
        try
        {
            var guilds = Context.Client.Guilds;
            string? reply = await AdminInfoHandler.ExecuteAsync(adminAccess, adminRepository, Context.User.Id, guilds.Count, guilds.Sum(g => (long)g.MemberCount));
            if (reply is not null)
            {
                await ReplyAsync(reply, allowedMentions: AllowedMentions.None);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            if (adminAccess.IsAdmin(Context.User.Id))
            {
                await ReplyAsync("¡Upa! Falló admininfo; mirá el registro del servidor.");
            }
        }
    }

    // "aa adminset dragon tier 5 @amigo" / "aa adminset dragon level 40" — fijar el tier o el nivel de entrenamiento del dragón (v0.48.1, ver AdminSetHandler).
    [Command("adminset")]
    [Alias("admin-set", "adminponer")]
    public async Task AdminSetAsync([Remainder] string args = "")
    {
        try
        {
            string? reply = await AdminSetHandler.ExecuteAsync(adminAccess, userRepository, adminRepository, gameEvents, Context.User.Id, args);
            if (reply is not null)
            {
                // Solo se mencionan usuarios, como en admingive.
                await ReplyAsync(reply, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            if (adminAccess.IsAdmin(Context.User.Id))
            {
                await ReplyAsync(Loc.T("¡Upa! Falló adminset; mirá el registro del servidor."));
            }
        }
    }
}
