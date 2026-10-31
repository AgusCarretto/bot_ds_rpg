using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): invitar amigos (v0.43.0).
public partial class TextCommandModule
{
    // "aa invitedby @amigo" (alias "aa invitado", "aa meinvito", "aa referido") — lo mismo que /invitedby. Sin nadie, muestra quién te invitó y a cuántos invitaste.
    [Command("invitedby")]
    [Alias("invited", "invitado", "meinvito", "referido")]
    [Summary("¿Te invitó un amigo? \"aa invitedby @amigo\": los dos se llevan oro (una sola vez por cuenta). Sin nadie, mirás quién te invitó y a cuántos invitaste.")]
    public async Task InvitedByAsync([Remainder] IUser? amigo = null)
    {
        try
        {
            var reply = await ReferralModule.ExecuteAsync(userRepository, referralRepository, gameEvents, Context.User.Id, amigo?.Id, amigo?.IsBot ?? false);
            await ReplyAsync(reply.Mention, embed: reply.Embed, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude anotar quién te invitó ahora mismo, intentá de nuevo en un momento."));
        }
    }
}
