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
    [Summary("Encantá tu arma o tu amuleto con oro y Polvo: \"aa enchant\" para mirar, \"aa enchant arma\" o \"aa enchant amuleto\" para probar, con el efecto que quieras (\"aa enchant arma crítico\") y \"avanzado\" con el oficio Encantador al 100.")]
    public async Task EnchantAsync(string pieza = "", string segundo = "", string tercero = "")
    {
        try
        {
            // Después de la pieza pueden venir, en cualquier orden, el efecto («crítico») y el modo («avanzado»): aa enchant arma crítico avanzado.
            bool advanced = false;
            string? effect = null;
            foreach (string word in new[] { segundo, tercero }.Where(w => !string.IsNullOrWhiteSpace(w)))
            {
                if (GatheringModule.ParseAdvanced(word) == true)
                {
                    advanced = true;
                }
                else if (effect is null)
                {
                    effect = word;
                }
                else
                {
                    await ReplyAsync($"No entendí **{word}**: usá `aa enchant {pieza} <efecto>` o `aa enchant {pieza} <efecto> avanzado`.");
                    return;
                }
            }

            var result = await DustModule.ExecuteEnchantAsync(
                userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, pieza, bonusService: bonusService, advanced: advanced, effect: effect);
            await ReplyAsync(result.PlainMessage, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude encantar, intentá de nuevo en un momento.");
        }
    }
}
