using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): abrir cajas.
public partial class TextCommandModule
{
    // "aa open <caja>" o "aa open <cantidad> <caja...>" (alias "aa open") — misma lógica que /open.
    [Command("open")]
    [Alias("abrir")]
    [Summary("Abrí una caja o un huevo de mascota de tu inventario: \"aa open <caja>\" o \"aa open <cantidad> <caja>\".")]
    public Task OpenBoxAsync([Remainder] string caja) => OpenBoxAsync(1, caja);

    [Command("open")]
    [Alias("abrir")]
    public async Task OpenBoxAsync(int cantidad, [Remainder] string caja)
    {
        try
        {
            var result = await BoxModule.ExecuteOpenAsync(itemRepository, boxRepository, boxContextService, combatSessions, gameEvents, Context.User.Id, caja, cantidad, petRepository: petRepository);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude abrir la caja, intentá de nuevo en un momento.");
        }
    }
}
