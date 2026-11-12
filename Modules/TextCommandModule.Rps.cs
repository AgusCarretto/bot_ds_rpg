using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa ppt @amigo 5k" — misma lógica que /ppt (RpsModule). Respondiendo al mensaje de alguien con "aa ppt 5k" también anda: el
    // primer parámetro es un jugador, así que la regla de «responder = mencionar» (v0.51.3) lo completa.
    [Command("ppt")]
    [Alias("rps", "piedrapapeltijera", "piedra")]
    [Summary("Piedra, papel o tijera apostando oro contra otro jugador: aa ppt @amigo 5k")]
    public async Task PptAsync(IUser jugador, [Remainder] string apuesta = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apuesta))
            {
                await ReplyAsync(Loc.T("Decime cuánto apuestan: **aa ppt @amigo 5k** (o 500, o all)."));
                return;
            }

            var result = await RpsModule.PrepareChallengeAsync(userRepository, rpsService, Context.User.Id, Context.User.GlobalName ?? Context.User.Username, jugador, apuesta.Trim());
            if (result.Game is null)
            {
                await ReplyAsync(result.Error);
                return;
            }

            var message = await ReplyAsync(jugador.Mention, embed: RpsModule.BuildEmbed(result.Game), components: RpsModule.BuildComponents(result.Game),
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
            result.Game.Message = message;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude armar la partida, intentá de nuevo en un momento."));
        }
    }
}
