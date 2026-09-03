using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

public class CasinoModule(IUserRepository userRepository, ICasinoRepository casinoRepository, ICasinoService casinoService)
    : InteractionModuleBase<SocketInteractionContext>
{
    public const int MinBet = 10;

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
            var result = await ExecutePlayAsync(userRepository, casinoRepository, casinoService, Context.User.Id, game, bet, lado);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló en el casino, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa play". Exactamente uno de los dos campos del resultado
    // viene con valor. "game" y "lado" no distinguen mayúsculas (los slash commands sí lo
    // garantizan por el [Choice], acá lo normalizamos a mano).
    public sealed record PlayResult(string? PlainMessage, Embed? Embed);

    public static async Task<PlayResult> ExecutePlayAsync(
        IUserRepository userRepository, ICasinoRepository casinoRepository, ICasinoService casinoService,
        ulong discordId, string game, int bet, string? lado)
    {
        game = game.Trim().ToLowerInvariant();
        lado = lado?.Trim().ToLowerInvariant();

        if (game is not ("coinflip" or "slots"))
        {
            return new PlayResult("El juego tiene que ser **coinflip** o **slots**.", null);
        }

        if (bet < MinBet)
        {
            return new PlayResult($"La apuesta mínima es **{MinBet}** de oro.", null);
        }

        if (game == "coinflip" && lado is not ("heads" or "tails"))
        {
            return new PlayResult("Para jugar al Coinflip tenés que elegir un lado: `heads` o `tails`.", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        if (player.Gold < bet)
        {
            return new PlayResult($"No tenés suficiente oro: la apuesta es **{bet}** y tenés **{player.Gold}**.", null);
        }

        var result = game switch
        {
            "coinflip" => casinoService.PlayCoinflip(bet, lado),
            "slots" => casinoService.PlaySlots(bet),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Juego desconocido"),
        };

        // El chequeo de arriba es solo para un mensaje más claro; la validación real y
        // atómica contra condiciones de carrera ocurre acá adentro (guarda en la transacción).
        var updatedPlayer = await casinoRepository.PlaceBetAsync(discordId, bet, result.Payout);

        if (updatedPlayer is null)
        {
            return new PlayResult($"No te alcanza el oro: la apuesta es **{bet}** y ya no tenés suficiente.", null);
        }

        return new PlayResult(null, BuildResultEmbed(game, bet, lado, result, updatedPlayer.Gold));
    }

    private static Embed BuildResultEmbed(string game, int bet, string? predictedSide, CasinoResult result, int goldBalance)
    {
        string title = game == "coinflip" ? "🪙 Coinflip" : "🎰 Slots";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(result.Won ? Color.Green : Color.Red);

        if (game == "coinflip")
        {
            string prediction = predictedSide == "heads" ? "Heads" : "Tails";
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
