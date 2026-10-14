using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el Tabernero del Inframundo (v0.17.0).
public partial class TextCommandModule
{
    // "aa deepshop" (alias "aa inframundo", "aa deep") — la misma carta que /deepshop. "aa deepshop corteza 2" o "aa deepshop meteorito" compra directo (de 1 a 10).
    [Command("deepshop")]
    [Alias("inframundo", "deep")]
    [Summary("El Tabernero del Inframundo: comprá con Polvo la Corteza y el Meteorito (se conoce al vencer al jefe de la Zona 5): \"aa deepshop\" o \"aa deepshop corteza 2\".")]
    public async Task DeepShopAsync(string item = "", int cantidad = 1)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                var scene = await DeepShopModule.BuildSceneAsync(userRepository, zoneRepository, dustRepository, itemRepository, Context.User.Id, null);
                if (scene.Embed is null)
                {
                    await ReplyAsync(scene.PlainMessage);
                    return;
                }

                await NpcImages.SendAsync(Context.Channel, scene.Embed, scene.Components);
                return;
            }

            var result = await DeepShopModule.ExecuteBuyAsync(userRepository, zoneRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, item, cantidad);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! El Tabernero del Inframundo no te escucha ahora, intentá de nuevo en un momento.");
        }
    }
}
