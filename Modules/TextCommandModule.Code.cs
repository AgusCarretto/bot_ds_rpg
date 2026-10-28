using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): los códigos canjeables (v0.37.0). La lógica está en CodeHandler; acá solo se lee lo que escribió el jugador.
public partial class TextCommandModule
{
    // "aa code CODIGO" — canjea un código. Escrito en un canal lo puede leer cualquiera: el bot borra el mensaje del jugador si tiene permiso (y recomienda /code, que es privado).
    [Command("code")]
    [Alias("codigo", "código", "redeem", "cupon", "cupón")]
    [Summary("Canjeá un código por premios: \"aa code CODIGO\" (con /code lo ves solo vos).")]
    public async Task CodeAsync([Remainder] string typed = "")
    {
        try
        {
            var reply = await CodeHandler.RedeemAsync(redeemCodeRepository, redeemAttemptLimiter, gameEvents, Context.User.Id, typed);
            await ReplyAsync(reply.PlainMessage, embed: reply.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("No pude canjear el código ahora mismo, intentá de nuevo en un momento."));
        }
        finally
        {
            await TryDeleteTypedCodeAsync();
        }
    }

    // El código que se escribió en un canal queda a la vista: se intenta borrar el mensaje (necesita el permiso «Gestionar mensajes»; si falta, no pasa nada).
    private async Task TryDeleteTypedCodeAsync()
    {
        try
        {
            await Context.Message.DeleteAsync();
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    // "aa admincode create CODIGO 100 7d | gold 5k | item Cofre de Oro 1" — el panel del ADMINISTRADOR (ver CodeHandler.AdminAsync). A propósito sin [Summary] ni versión de barra: no figura en ninguna lista.
    [Command("admincode")]
    [Alias("admin-code", "admincodigo")]
    public async Task AdminCodeAsync([Remainder] string args = "")
    {
        try
        {
            string? reply = await CodeHandler.AdminAsync(adminAccess, redeemCodeRepository, itemRepository, adminRepository, gameEvents, Context.User.Id, args);
            if (reply is not null)
            {
                await ReplyAsync(reply, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            if (adminAccess.IsAdmin(Context.User.Id))
            {
                await ReplyAsync("¡Upa! Falló admincode; mirá el registro del servidor (¿corriste add_redeem_codes.sql?).");
            }
        }
    }
}
