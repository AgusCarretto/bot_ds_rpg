using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el historial por juego y el de duelos.
public partial class TextCommandModule
{
    // "aa history" / "aa historial" — misma lógica que /history. "aa history @alguien" mira el de otro jugador.
    [Command("history")]
    [Alias("historial", "hist")]
    [Summary("Tu historial de juegos: cuántas veces jugaste, ganaste y perdiste en cada uno (o el de otro: \"aa history @alguien\").")]
    public async Task HistoryAsync([Remainder] IUser? targetUser = null)
    {
        IUser target = targetUser ?? Context.User;

        try
        {
            await ReplyAsync(embed: await HistoryModule.BuildHistoryEmbedAsync(userRepository, gameEventRepository, target.Id, GameModule.GetDisplayName(target)));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el historial, intentá de nuevo en un momento.");
        }
    }

    // "aa duels" / "aa duelos" — misma lógica que /duels.
    [Command("duels")]
    [Alias("duelos")]
    [Summary("Tu historial de duelos: tu récord y contra quién fueron los últimos (o el de otro: \"aa duels @alguien\").")]
    public async Task DuelsAsync([Remainder] IUser? targetUser = null)
    {
        IUser target = targetUser ?? Context.User;

        try
        {
            await ReplyAsync(embed: await HistoryModule.BuildDuelsEmbedAsync(
                userRepository, gameEventRepository, target.Id, GameModule.GetDisplayName(target), DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el historial de duelos, intentá de nuevo en un momento.");
        }
    }
}
