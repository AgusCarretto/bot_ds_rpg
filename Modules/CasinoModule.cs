using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

public class CasinoModule(IUserRepository userRepository, ICasinoRepository casinoRepository, ICasinoService casinoService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int MinBet = 10;

    // Comando barra: /play
    [SlashCommand("play", "Apostá tu oro en el casino (Coinflip o Slots).")]
    public async Task HandlePlayAsync(
        [Summary("game", "Elegí el juego.")]
        [Choice("Coinflip", "coinflip")]
        [Choice("Slots", "slots")]
        string game,
        [Summary("apuesta", "Cuánto oro querés apostar (mínimo 10).")]
        [MinValue(MinBet)]
        int bet,
        [Summary("lado", "Solo para Coinflip: elegí Heads o Tails.")]
        [Choice("Heads", "heads")]
        [Choice("Tails", "tails")]
        string? lado = null)
    {
        await DeferAsync();

        try
        {
            if (bet < MinBet)
            {
                await FollowupAsync($"La apuesta mínima es **{MinBet}** de oro.", ephemeral: true);
                return;
            }

            if (game == "coinflip" && lado is null)
            {
                await FollowupAsync("Para jugar al Coinflip tenés que elegir un lado: `lado: Heads` o `lado: Tails`.", ephemeral: true);
                return;
            }

            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);
            if (player.Gold < bet)
            {
                await FollowupAsync($"No tenés suficiente oro: la apuesta es **{bet}** y tenés **{player.Gold}**.", ephemeral: true);
                return;
            }

            var result = game switch
            {
                "coinflip" => casinoService.PlayCoinflip(bet, lado),
                "slots" => casinoService.PlaySlots(bet),
                _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Juego desconocido"),
            };

            // El chequeo de arriba es solo para un mensaje más claro; la validación real y
            // atómica contra condiciones de carrera ocurre acá adentro (guarda en la transacción).
            var updatedPlayer = await casinoRepository.PlaceBetAsync(Context.User.Id, bet, result.Payout);

            if (updatedPlayer is null)
            {
                await FollowupAsync($"No te alcanza el oro: la apuesta es **{bet}** y ya no tenés suficiente.", ephemeral: true);
                return;
            }

            await FollowupAsync(embed: BuildResultEmbed(game, bet, lado, result, updatedPlayer.Gold));
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló en el casino, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    private static Embed BuildResultEmbed(string game, int bet, string? predictedSide, CasinoResult result, int goldBalance)
    {
        string title = game == "coinflip" ? "🪙 Coinflip" : "🎰 Slots";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(result.Won ? Color.Green : Color.Red);

        if (game == "coinflip")
        {
            string prediction = string.Equals(predictedSide, "heads", StringComparison.OrdinalIgnoreCase) ? "Heads" : "Tails";
            embed.WithDescription($"Elegiste **{prediction}** — salió **{result.Reveal[0]}** 🪙");
        }
        else
        {
            embed.WithDescription($"**[ {string.Join(" | ", result.Reveal)} ]**");
        }

        embed.AddField(result.Won ? "🎉 ¡Ganaste!" : "😢 Perdiste", result.Won ? "¡Buena jugada!" : "La casa gana esta vez.", false)
            .AddField("💵 Apuesta", bet.ToString(), true)
            .AddField("🏆 Premio", result.Payout.ToString(), true)
            .AddField("💰 Oro actual", goldBalance.ToString(), true);

        return embed.Build();
    }
}
