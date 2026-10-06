using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): los oficios (v0.13.0).
public partial class TextCommandModule
{
    // "aa professions" (alias "aa oficios", "aa profesiones", "aa prof") — misma lógica que /professions.
    [Command("professions")]
    [Alias("oficios", "oficio", "profesiones", "profesion", "prof")]
    [Summary("Tus oficios (Leñador, Minero, Encantador): nivel, XP, lo que dan y la versión avanzada del nivel 100.")]
    public async Task ProfessionsAsync()
    {
        try
        {
            var result = await ProfessionModule.ExecuteProfessionsAsync(userRepository, bonusService, Context.User.Id);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude abrir tus oficios, intentá de nuevo en un momento.");
        }
    }
}
