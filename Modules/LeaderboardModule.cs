using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

public class LeaderboardModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // El ranking muestra los 50 primeros en 5 páginas de 10 (el dueño, 2026-11-07: «que se pueda ver del 1 al 50 pero cada 10 personas, una página nueva»).
    public const int PageSize = 10;
    public const int MaxPages = 5;
    private static readonly string[] RankEmojis = ["🥇", "🥈", "🥉"];

    // Comando barra: /leaderboard [pagina]
    [SlashCommand("leaderboard", "El ranking del server: primero por Fuego Nuevo y después por nivel.")]
    public async Task HandleLeaderboardAsync([Summary("pagina", "Qué página ver (1 a 5, de a 10 jugadores)."), MinValue(1), MaxValue(MaxPages)] int pagina = 1)
    {
        await DeferAsync();

        try
        {
            var (embed, components) = await BuildLeaderboardAsync(userRepository, Context.User.Id, pagina);
            await FollowupAsync(embed: embed, components: components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync(Loc.T("No pude cargar el ranking ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Los botones ◀ ▶: «lb_page:{dueño}:{página}». Solo los usa quien abrió el ranking (como el resto de los menús); los demás abren el suyo.
    [ComponentInteraction("lb_page:*:*")]
    public async Task HandlePageAsync(string ownerId, string page)
    {
        if (!ulong.TryParse(ownerId, out ulong owner) || owner != Context.User.Id)
        {
            await RespondAsync(Loc.T("Ese ranking lo abrió otra persona: abrí el tuyo con **/leaderboard**."), ephemeral: true);
            return;
        }

        await DeferAsync();
        try
        {
            var (embed, components) = await BuildLeaderboardAsync(userRepository, owner, int.TryParse(page, out int number) ? number : 1);
            await ModifyOriginalResponseAsync(message =>
            {
                message.Embed = embed;
                message.Components = components;
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("No pude cargar el ranking ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs arme lo mismo en "aa leaderboard [página]".
    public static async Task<(Embed Embed, MessageComponent? Components)> BuildLeaderboardAsync(IUserRepository userRepository, ulong viewerId, int page)
    {
        int total = await userRepository.CountPlayersAsync();
        int pages = PageCount(total);
        page = Math.Clamp(page, 1, pages);
        var embed = await BuildLeaderboardEmbedAsync(userRepository, page, total);
        return (embed, pages > 1 ? BuildButtons(viewerId, page, pages) : null);
    }

    // Cuántas páginas hay con ese total: de a 10, como mucho 5 (los 50 primeros), y al menos 1.
    public static int PageCount(int totalPlayers) =>
        Math.Clamp((Math.Min(totalPlayers, PageSize * MaxPages) + PageSize - 1) / PageSize, 1, MaxPages);

    public static MessageComponent BuildButtons(ulong viewerId, int page, int pages) =>
        new ComponentBuilder()
            .WithButton("◀", $"lb_page:{viewerId}:{page - 1}", ButtonStyle.Secondary, disabled: page <= 1)
            .WithButton($"{page}/{pages}", $"lb_now:{viewerId}", ButtonStyle.Secondary, disabled: true)
            .WithButton("▶", $"lb_page:{viewerId}:{page + 1}", ButtonStyle.Secondary, disabled: page >= pages)
            .Build();

    // La página pedida del ranking (10 por página). Sin total, lo cuenta.
    public static async Task<Embed> BuildLeaderboardEmbedAsync(IUserRepository userRepository, int page = 1, int? total = null)
    {
        int players = total ?? await userRepository.CountPlayersAsync();
        int pages = PageCount(players);
        page = Math.Clamp(page, 1, pages);
        int offset = (page - 1) * PageSize;
        var topPlayers = await userRepository.GetTopPlayersAsync(PageSize, offset: offset);

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🏆 Líderes de Asado y Acero RPG"))
            .WithColor(Color.Gold)
            .WithCurrentTimestamp();

        if (topPlayers.Count == 0)
        {
            embed.WithDescription(Loc.T("Todavía no hay ningún aventurero registrado."));
        }
        else
        {
            var lines = topPlayers.Select((player, index) =>
            {
                int position = offset + index;
                string rank = position < RankEmojis.Length ? RankEmojis[position] : $"{position + 1}.";
                return $"{rank} <@{player.DiscordId}> — {UiEmojis.FuegoNuevoTag(player.FuegoNuevo)} · {UiEmojis.LevelTag(player.Level)} ({player.Class})";
            });

            // El pie es la letra chica: cuántos aventureros hay en total, la página y el criterio del orden.
            embed.WithDescription(string.Join('\n', lines))
                .WithFooter(Loc.F($"👥 {GameHistory.Number(players)} aventureros · Página {page}/{pages}") + " · " +
                            Loc.T("Ordena primero por Fuego Nuevo (FN) y, con el mismo FN, por nivel; el XP del nivel solo desempata."));
        }

        return embed.Build();
    }
}
