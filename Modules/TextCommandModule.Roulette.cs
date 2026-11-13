using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa ruleta 5k" abre el paño de fichas (v0.55.1); "aa ruleta rojo 5k" / "aa ruleta 17 500" / "aa ruleta 5k par" apuesta y gira de una (v0.55.0, ver
    // RouletteModule). Con dos palabras, la PRIMERA que sea una apuesta (rojo, negro, par, impar o 0 a 36) es la apuesta y la otra el
    // monto: «17 500» es 500 al 17 y «500 17» también (500 no es una casilla).
    [Command("ruleta")]
    [Alias("roulette", "rl")]
    [Summary("La ruleta: \"aa ruleta 1k\" abre el paño para poner fichas en varias apuestas; \"aa ruleta rojo 5k\" o \"aa ruleta 17 500\" apuesta y gira de una.")]
    public async Task RouletteAsync(string first = "", string second = "")
    {
        try
        {
            var (bet, amountText) = TextShortcuts.SplitRouletteArgs(first, second);
            if (string.IsNullOrWhiteSpace(amountText))
            {
                await ReplyAsync(Loc.T("Decime cuánto apostás: **aa ruleta 1k** (abre el paño de fichas) o **aa ruleta rojo 5k** (gira de una)."));
                return;
            }

            if (bet is null)
            {
                var table = await RouletteModule.OpenBoardAsync(userRepository, rouletteBoards, Context.User.Id, amountText);
                await ReplyAsync(table.Error, embed: table.Embed, components: table.Components);
                return;
            }

            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);
            var parsed = AmountParser.Parse(amountText, player.Gold);
            if (!parsed.Ok)
            {
                await ReplyAsync(parsed.Error);
                return;
            }

            IUserMessage? message = null;
            await RouletteModule.PlayAndAnimateAsync(userRepository, casinoRepository, gameEvents, Context.User.Id, parsed.Amount, bet, Random.Shared,
                async (embed, components) =>
                {
                    if (message is null) message = await ReplyAsync(embed: embed, components: components);
                    else await message.ModifyAsync(p => { p.Embed = embed; p.Components = components; });
                },
                async error => await ReplyAsync(error));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! Algo falló en la ruleta, intentá de nuevo en un momento."));
        }
    }
}
