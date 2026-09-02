using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class LeaderboardModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    private const int TopPlayersCount = 10;
    private static readonly string[] RankEmojis = ["🥇", "🥈", "🥉"];

    // Comando barra: /leaderboard
    [SlashCommand("leaderboard", "Mostrá a los jugadores con más nivel/experiencia del server.")]
    public async Task HandleLeaderboardAsync()
    {
        await DeferAsync();

        try
        {
            var topPlayers = await userRepository.GetTopPlayersAsync(TopPlayersCount);

            var embed = new EmbedBuilder()
                .WithTitle("🏆 Líderes de Asado y Acero RPG")
                .WithColor(Color.Gold)
                .WithCurrentTimestamp();

            if (topPlayers.Count == 0)
            {
                embed.WithDescription("Todavía no hay ningún aventurero registrado.");
            }
            else
            {
                var lines = topPlayers.Select((player, index) =>
                {
                    string rank = index < RankEmojis.Length ? RankEmojis[index] : $"{index + 1}.";
                    return $"{rank} <@{player.DiscordId}> — Nivel **{player.Level}** ({player.Class})";
                });

                embed.WithDescription(string.Join('\n', lines))
                    .WithFooter("El ranking ordena por nivel; el XP de cada nivel solo desempata.");
            }

            await FollowupAsync(embed: embed.Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude cargar el ranking ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
