using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): desmantelar y encantar.
public partial class TextCommandModule
{
    // "aa dismantle <item>", "aa dismantle <cantidad> <item...>" o "aa dismantle todo <item...>" (alias "aa desmantelar") — misma lógica que /dismantle. Sin tope por comando (v0.16.2).
    [Command("dismantle")]
    [Alias("desmantelar")]
    [Summary("Desmantelá un material para conseguir Polvo: \"aa dismantle <item>\", \"aa dismantle <cantidad> <item>\" o \"aa dismantle todo <item>\".")]
    public async Task DismantleAsync([Remainder] string args = "")
    {
        try
        {
            var (quantity, item) = DismantleRequest.Parse(args);
            var result = await DustModule.ExecuteDismantleAsync(userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, item, quantity);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude desmantelar, intentá de nuevo en un momento.");
        }
    }

    // "aa enchant" (mirar) o "aa enchant arma" / "aa enchant amuleto" (alias "aa encantar") — misma lógica que /enchant.
    [Command("enchant")]
    [Alias("encantar")]
    [Summary("Encantá tu arma o tu amuleto con oro y Polvo: \"aa enchant\" para mirar, \"aa enchant arma\" o \"aa enchant amuleto\" para probar (\"aa enchant arma avanzado\" con el oficio Encantador al 100).")]
    public async Task EnchantAsync(string pieza = "", string modo = "")
    {
        try
        {
            if (GatheringModule.ParseAdvanced(modo) is not bool advanced)
            {
                await ReplyAsync($"No entendí **{modo}**: usá `aa enchant {pieza}` o `aa enchant {pieza} avanzado`.");
                return;
            }

            var result = await DustModule.ExecuteEnchantAsync(
                userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, pieza, bonusService: bonusService, advanced: advanced);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude encantar, intentá de nuevo en un momento.");
        }
    }
}
