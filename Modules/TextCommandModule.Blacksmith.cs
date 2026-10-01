using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): hablar con el herrero.
public partial class TextCommandModule
{
    // "aa herrero" / "aa blacksmith" — misma escena que /blacksmith (con el desplegable de recetas).
    [Command("blacksmith")]
    [Alias("herrero")]
    [Summary("Hablá con el herrero y elegí de la lista qué querés que te forje: \"aa herrero\".")]
    public async Task BlacksmithAsync()
    {
        try
        {
            var scene = await BlacksmithModule.BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id, null);
            await ReplyAsync(embed: scene.Embed, components: scene.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! El herrero no está en la fragua ahora, intentá de nuevo en un momento.");
        }
    }
}
