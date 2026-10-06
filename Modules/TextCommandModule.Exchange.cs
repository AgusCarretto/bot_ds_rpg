using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el trueque con el tabernero.
public partial class TextCommandModule
{
    // "aa exchange \"drop que das\" \"drop que querés\" [veces]" (alias "aa canje") — misma lógica que /exchange. Los nombres con espacios van entre comillas, como en "aa trade".
    [Command("exchange")]
    [Alias("canje", "canjear", "swap")]
    [Summary("El tabernero te cambia 3 drops de una zona por 1 de la misma: \"aa exchange \\\"Pluma de Ñandú\\\" \\\"Cuero Grueso\\\" [veces]\".")]
    public async Task ExchangeAsync(string dar, string recibir, int veces = 1)
    {
        try
        {
            var result = await ExchangeModule.ExecuteExchangeAsync(userRepository, itemRepository, dropExchangeRepository, gameEvents, Context.User.Id, dar, recibir, veces);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! El tabernero no pudo hacer el cambio ahora, intentá de nuevo en un momento.");
        }
    }
}
