using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): abrir cajas.
public partial class TextCommandModule
{
    // "aa abrir <caja>" o "aa abrir <cantidad> <caja...>" (alias "aa open") — misma lógica que /abrir.
    [Command("abrir")]
    [Alias("open")]
    [Summary("Abrí una caja de tu inventario: \"aa abrir <caja>\" o \"aa abrir <cantidad> <caja>\".")]
    public Task OpenBoxAsync([Remainder] string caja) => OpenBoxAsync(1, caja);

    [Command("abrir")]
    [Alias("open")]
    public async Task OpenBoxAsync(int cantidad, [Remainder] string caja)
    {
        try
        {
            var result = await BoxModule.ExecuteOpenAsync(itemRepository, boxRepository, combatSessions, gameEvents, Context.User.Id, caja, cantidad);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude abrir la caja, intentá de nuevo en un momento.");
        }
    }
}
