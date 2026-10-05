using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): la taberna.
public partial class TextCommandModule
{
    // "aa taberna" — misma escena que /taberna (la carta, la foto del tabernero y las listas para comer, comprar y vender).
    [Command("taberna")]
    [Alias("tavern", "tienda")]
    [Summary("Pasá por la taberna: comé, comprá comida y cajas y vendé con el tabernero: \"aa taberna\".")]
    public async Task TabernaAsync()
    {
        try
        {
            var scene = await TabernaModule.BuildSceneAsync(userRepository, itemRepository, inventoryRepository, buffRepository, Context.User.Id, null, boxContextService);
            await NpcImages.SendAsync(Context.Channel, scene.Embed, scene.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! La taberna está cerrada ahora, intentá de nuevo en un momento.");
        }
    }
}
