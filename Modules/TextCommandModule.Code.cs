using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): los códigos canjeables (v0.37.0). La lógica está en CodeHandler; acá solo se lee lo que escribió el jugador.
public partial class TextCommandModule
{
    // "aa code CODIGO" — canjea un código. Escrito en un canal lo puede leer cualquiera: el bot borra el mensaje del jugador si tiene permiso y la RESPUESTA va por mensaje directo (en el canal solo queda un aviso con la
    // mención; ver CodeHandler.DeliverTypedAsync). /code es privado de por sí.
    [Command("code")]
    [Alias("codigo", "código", "redeem", "cupon", "cupón")]
    [Summary("Canjeá un código por premios: \"aa code CODIGO\" (te respondo por privado; con /code lo ves solo vos).")]
    public async Task CodeAsync([Remainder] string typed = "")
    {
        // "aa code list" no es un código: abre la libreta (y no se borra el mensaje, no hay nada que esconder).
        if (typed.Trim().ToLowerInvariant() is "list" or "lista")
        {
            await CodeListAsync();
            return;
        }

        try
        {
            var reply = await CodeHandler.RedeemAsync(redeemCodeRepository, gameEvents, Context.User.Id, typed);
            await CodeHandler.DeliverTypedAsync(
                reply, Context.IsPrivate, Context.User.Mention,
                (plain, embed) => Context.User.SendMessageAsync(plain, embed: embed),
                (plain, embed) => ReplyAsync(plain, embed: embed, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users)));
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

    // "aa codelist [página]" — misma pantalla que /codelist (CodeListModule): los códigos que canjeaste y los que te faltan como ???.
    [Command("codelist")]
    [Alias("codes", "codigos", "códigos")]
    [Summary("Tus códigos: los que canjeaste y los que te faltan (??? sin pista). \"aa codelist 2\" abre la página 2.")]
    public async Task CodeListAsync(int pagina = 1)
    {
        try
        {
            // Por privado (el dueño, 2026-11-16): en el canal la vería cualquiera. Si los mensajes directos están cerrados, en el canal solo queda el aviso de usar /codelist.
            var view = await CodeListModule.BuildAsync(redeemCodeRepository, Context.User.Id, pagina - 1);
            if (Context.IsPrivate)
            {
                await ReplyAsync(embed: view.Embed, components: view.Components);
                return;
            }

            try
            {
                await Context.User.SendMessageAsync(embed: view.Embed, components: view.Components);
            }
            catch (Exception dmError)
            {
                BotLog.Warn(dmError);
                await ReplyAsync(Loc.F($"{Context.User.Mention} No pude escribirte por privado (¿tenés los mensajes directos cerrados?): usá **/codelist**, que responde solo para vos."),
                    allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
                return;
            }

            await ReplyAsync(Loc.F($"{Context.User.Mention} 🎟️ Te respondí por mensaje privado."), allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir tus códigos, intentá de nuevo en un momento."));
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
                await ReplyAsync(Loc.T("¡Upa! Falló admincode; mirá el registro del servidor (¿corriste add_redeem_codes.sql?)."));
            }
        }
    }
}
