using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el consejo de qué farmear.
public partial class TextCommandModule
{
    // "aa tips" — misma lógica que TipsModule.HandleTipsAsync (por texto no hay mensajes privados: sale en el canal).
    [Command("tips")]
    [Alias("consejo", "tip")]
    [Summary("Un consejo: qué te falta para tu próxima forja y de dónde sacarlo: \"aa tips\".")]
    public async Task TipsAsync()
    {
        try
        {
            await ReplyAsync(embed: await TipsModule.BuildTipsEmbedAsync(farmAdvisor, Context.User.Id));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el consejo ahora mismo, intentá de nuevo en un momento.");
        }
    }
}
