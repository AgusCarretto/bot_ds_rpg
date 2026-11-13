using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa elementos @amigo 5k" — el atajo de "aa play elementos @amigo 5k" (Elementos, v0.54.0: fuego, hielo y agua apostando oro; ver
    // RpsModule). Respondiendo al mensaje de alguien con "aa elementos 5k" también anda: el primer parámetro es un jugador, así que la
    // regla de «responder = mencionar» (v0.51.3) lo completa.
    [Command("elementos")]
    [Alias("elemento", "elem")]
    [Summary("Elementos: fuego, hielo y agua apostando oro contra otro jugador: aa elementos @amigo 5k")]
    public async Task ElementsAsync(IUser jugador, [Remainder] string apuesta = "")
    {
        await StartElementsAsync(jugador, apuesta);
    }

    // La parte común de "aa elementos" y "aa play elementos": arma el desafío y lo publica.
    private async Task StartElementsAsync(IUser? rival, string apuesta)
    {
        try
        {
            if (rival is null || string.IsNullOrWhiteSpace(apuesta))
            {
                await ReplyAsync(Loc.T("Decime a quién desafiás y cuánto apuestan: **aa play elementos @amigo 5k** (o 500, o all)."));
                return;
            }

            var result = await RpsModule.PrepareChallengeAsync(userRepository, rpsService, Context.User.Id, Context.User.GlobalName ?? Context.User.Username, rival, apuesta.Trim());
            if (result.Game is null)
            {
                await ReplyAsync(result.Error);
                return;
            }

            result.Game.Message = await ReplyAsync(rival.Mention, embed: RpsModule.BuildEmbed(result.Game), components: RpsModule.BuildComponents(result.Game),
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude armar la partida, intentá de nuevo en un momento."));
        }
    }

    // "aa play elementos @amigo 5k" llega a PlayAsync con la mención y el monto en cualquiera de los dos lugares: el rival es el
    // mencionado (o el autor del mensaje al que se responde) y el monto, la otra palabra.
    private async Task PlayElementsAsync(string first, string? second)
    {
        var words = new[] { first, second }.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w!.Trim()).ToList();
        string apuesta = words.FirstOrDefault(w => !w.StartsWith("<@", StringComparison.Ordinal)) ?? string.Empty;

        IUser? rival = Context.Message.MentionedUsers.FirstOrDefault(u => u.Id != Context.Client.CurrentUser.Id);
        if (rival is null && Context.Message.ReferencedMessage?.Author is { IsBot: false } replied && replied.Id != Context.User.Id)
        {
            rival = replied;
        }

        await StartElementsAsync(rival, apuesta);
    }
}
