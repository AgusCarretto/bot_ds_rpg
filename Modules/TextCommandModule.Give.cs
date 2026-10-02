using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): darle monedas a otro jugador.
public partial class TextCommandModule
{
    // "aa give @jugador 100" (o "all" para todo tu oro) — misma lógica que GiveModule.HandleGiveAsync.
    [Command("give")]
    [Alias("dar")]
    [Summary("Dale monedas a otro jugador: \"aa give @jugador 100\" (o \"all\" para darle todo).")]
    public Task GiveAsync(IUser player, string amount) => RunGiveAsync(player, amount);

    // También se acepta la cantidad primero: "aa give 100 @jugador" / "aa give all @jugador".
    [Command("give")]
    [Alias("dar")]
    [Summary("Dale monedas a otro jugador: \"aa give 100 @jugador\" (o \"all\" para darle todo).")]
    public Task GiveAmountFirstAsync(string amount, IUser player) => RunGiveAsync(player, amount);

    private async Task RunGiveAsync(IUser player, string amount)
    {
        try
        {
            var result = await GiveModule.ExecuteGiveAsync(userRepository, transferRepository, gameEvents, Context.User.Id, player, amount);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude dar las monedas, intentá de nuevo en un momento.");
        }
    }
}
