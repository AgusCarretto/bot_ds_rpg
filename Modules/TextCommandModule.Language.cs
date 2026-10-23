using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el idioma. Misma lógica que /language (LanguageModule): acá solo se lee lo que escribió el jugador.
public partial class TextCommandModule
{
    // "aa language" dice cuál tenés; "aa language en" o "aa idioma español" lo cambia. Un comando de texto no trae el idioma de Discord: se usa el guardado (o español).
    [Command("language")]
    [Alias("idioma", "lang")]
    [Summary("El idioma del bot: \"aa language en\" para inglés, \"aa language es\" para español (sin nada te dice cuál tenés).")]
    public async Task LanguageAsync([Remainder] string idioma = "")
    {
        try
        {
            await ReplyAsync(await LanguageModule.ExecuteLanguageAsync(languageService, Context.User.Id, idioma, null));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude cambiar tu idioma, intentá de nuevo en un momento."));
        }
    }
}
