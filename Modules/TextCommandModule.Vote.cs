using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): votar en top.gg (v0.18.0).
public partial class TextCommandModule
{
    // "aa vote" (alias "aa votar", "aa voto") — lo mismo que /vote: cuenta qué da votar y, si ya votaste, cobra el voto.
    [Command("vote")]
    [Alias("votar", "voto")]
    [Summary("Votá en top.gg: te llevás un Baúl de Roble y el jefe se resetea. Escribilo de nuevo después de votar para cobrar.")]
    public async Task VoteAsync()
    {
        try
        {
            var result = await VoteModule.ExecuteVoteAsync(itemRepository, voteRepository, cooldownRepository, voteClient, gameEvents, Context.User.Id);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude revisar tu voto ahora mismo, intentá de nuevo en un momento.");
        }
    }
}
