using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): la historia.
public partial class TextCommandModule
{
    // "aa historia" / "aa story" — la misma pantalla que /story (con la lista desplegable). Por texto no hay mensajes privados: sale en el canal.
    [Command("story")]
    [Alias("historia", "cronicas", "crónicas", "lore")]
    [Summary("Las Crónicas del Fogón: la historia que te cuenta el Tabernero: \"aa historia\".")]
    public async Task StoryAsync()
    {
        try
        {
            var view = await StoryModule.BuildViewAsync(userRepository, zoneRepository, Context.User.Id, null);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! El Tabernero no encuentra sus crónicas ahora, intentá de nuevo en un momento.");
        }
    }
}
