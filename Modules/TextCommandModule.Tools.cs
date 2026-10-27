using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el Carpintero (hacha y pico).
public partial class TextCommandModule
{
    // "aa herramientas" / "aa equipamiento" / "aa tools" — misma escena que /tools (con la lista para armar).
    [Command("tools")]
    [Alias("tool", "herramientas", "herramienta", "equipamiento", "equipment", "carpintero")]
    [Summary("Hablá con el Carpintero: mirá tu hacha y tu pico y armá uno mejor para recolectar más: \"aa herramientas\".")]
    public async Task ToolsAsync()
    {
        try
        {
            var scene = await ToolsModule.BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, toolRepository, Context.User.Id);
            await NpcImages.SendAsync(Context.Channel, scene.Embed, scene.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! El carpintero no está en el taller ahora, intentá de nuevo en un momento.");
        }
    }
}
