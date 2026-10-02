using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /arena: el torneo PvP diario. Durante el día (hora de Uruguay) te anotás con /arena join; a las 00:00 se arma la llave de eliminación
// directa con todos los anotados, las peleas se juegan solas (con el nivel y el equipo que tengas en ese momento) y el campeón cobra oro, XP
// y una caja según su zona. Apenas termina arranca el torneo del día siguiente. El bot anuncia el resultado en el canal donde se anotó el
// primero (o en Arena__ChannelId si está configurado), y /arena results muestra la llave completa del último torneo.
[Group("arena", "El torneo PvP diario: anotate, mirá quiénes van y el resultado del último.")]
public class ArenaModule(IArenaService arena) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("join", "Anotate en el torneo de hoy (se juega a las 00:00, hora de Uruguay).")]
    public async Task HandleJoinAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: await ExecuteJoinAsync(arena, Context.User, Context.Channel?.Id ?? 0, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude anotarte en la Arena, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("listplayers", "Mirá quiénes están anotados en el torneo de hoy.")]
    public async Task HandleListAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: await ExecuteListAsync(arena, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar la lista de la Arena, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("results", "Mirá la llave y el campeón del último torneo que se jugó.")]
    public async Task HandleResultsAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: await ExecuteResultsAsync(arena));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude traer los resultados de la Arena, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa arena ..." (sin Context) ----

    public static async Task<Embed> ExecuteJoinAsync(IArenaService arena, IUser user, ulong channelId, DateTime utcNow)
    {
        var result = await arena.JoinAsync(user.Id, Short(GameModule.GetDisplayName(user), 24), channelId, utcNow);
        string day = ArenaRules.Format(result.Day);
        string until = UruguayCalendar.FormatRemaining(result.UntilPlay);

        return result.Status switch
        {
            ArenaJoinStatus.Joined => new EmbedBuilder()
                .WithTitle("⚔️ ¡Anotado en la Arena!")
                .WithColor(Color.Gold)
                .WithDescription(
                    $"Quedaste anotado en el torneo del **{day}**. Se juega a las **00:00** (hora de Uruguay), en **{until}**.\n\n" +
                    $"Ya hay **{result.Count}** anotado(s) (cupo {ArenaRules.MaxPlayers}, hacen falta al menos {ArenaRules.MinPlayers}).\n" +
                    "Vas a pelear con el **nivel y el equipo que tengas a esa hora**: todavía estás a tiempo de prepararte.\n" +
                    "El campeón se lleva oro, XP y una caja de su zona.")
                .WithFooter("Mirá quiénes van con /arena listplayers")
                .Build(),

            ArenaJoinStatus.AlreadyJoined => Simple("✅ Ya estabas anotado", Color.DarkGrey,
                $"Seguís anotado en el torneo del **{day}** (se juega en **{until}**). Hay **{result.Count}** anotado(s)."),

            ArenaJoinStatus.Full => Simple("🚫 La Arena está llena", Color.DarkGrey,
                $"El torneo de hoy ya tiene sus {ArenaRules.MaxPlayers} anotados. Mañana arranca otro: llegá temprano."),

            ArenaJoinStatus.NoAccount => Simple("Primero tu aventura", Color.DarkGrey, "Tenés que empezar con **/start** antes de anotarte en la Arena."),

            _ => Simple("⏳ La Arena de hoy ya cerró", Color.DarkGrey, "Ese torneo ya no está abierto. Probá de nuevo en unos instantes."),
        };
    }

    public static async Task<Embed> ExecuteListAsync(IArenaService arena, DateTime utcNow)
    {
        var listing = await arena.ListAsync(utcNow);
        string day = ArenaRules.Format(listing.Day);
        string until = UruguayCalendar.FormatRemaining(listing.UntilPlay);

        if (listing.Entries.Count == 0)
        {
            return Simple($"🏟️ Arena del {day}", Color.Gold,
                $"Todavía no se anotó nadie. Se juega en **{until}**: ¡sé el primero con **/arena join**!");
        }

        var lines = listing.Entries.Select((entry, index) =>
        {
            string emoji = ClassCatalog.All.FirstOrDefault(c => c.Name == entry.PlayerClass)?.Emoji ?? "🥊";
            return $"`{index + 1,2}.` **{Short(entry.DisplayName, 24)}** — {emoji} {entry.PlayerClass} · nivel {entry.Level}";
        });

        string needed = listing.Entries.Count < ArenaRules.MinPlayers
            ? $"\n\n⚠️ Hacen falta al menos **{ArenaRules.MinPlayers}** para que se juegue."
            : string.Empty;

        return new EmbedBuilder()
            .WithTitle($"🏟️ Arena del {day}")
            .WithColor(Color.Gold)
            .WithDescription(
                $"Se juega a las **00:00** (hora de Uruguay), en **{until}**.\n" +
                $"**{listing.Entries.Count}** anotado(s) de {ArenaRules.MaxPlayers}:\n\n{string.Join('\n', lines)}{needed}")
            .WithFooter("Anotate con /arena join · la llave se arma con el nivel y el equipo de las 00:00")
            .Build();
    }

    public static async Task<Embed> ExecuteResultsAsync(IArenaService arena)
    {
        var results = await arena.GetLatestResultsAsync();
        if (results is null)
        {
            return Simple("🏟️ Todavía no hubo torneos", Color.DarkGrey,
                "La Arena todavía no se jugó ni una vez. Anotate con **/arena join**: el torneo de hoy se juega a las 00:00.");
        }

        var info = results.Info;
        return BuildResultsEmbed(
            info.Day, info.WinnerName, info.Participants, info.Rounds, info.RewardText, results.Matches, extra: null);
    }

    // La llave de un torneo jugado: el campeón, lo que cobró y cada ronda con sus cruces. Se usa en /arena results y en el anuncio de las
    // 00:00 (con "extra" para sumar, por ejemplo, que el campeón subió de nivel).
    public static Embed BuildResultsEmbed(
        DateOnly day, string? winnerName, int participants, int rounds, string? rewardText, IReadOnlyList<ArenaMatchRow> matches, string? extra)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"🏆 Arena del {ArenaRules.Format(day)}")
            .WithColor(Color.Gold)
            .WithDescription(
                $"👑 Campeón: **{Short(winnerName ?? "—", 24)}**\n" +
                $"🎁 Premio: {rewardText ?? "—"}\n" +
                $"👥 {participants} participantes · {rounds} ronda(s){(extra is null ? string.Empty : $"\n{extra}")}");

        foreach (var group in matches.GroupBy(m => m.Round).OrderBy(g => g.Key))
        {
            var lines = group.OrderBy(m => m.Slot).Select(MatchLine).ToList();
            string title = ArenaBracket.RoundName(group.Key, rounds);

            // Un campo de Discord admite 1024 caracteres: si una ronda larga no entra, sigue en otro campo.
            var chunk = new List<string>();
            int length = 0;
            int part = 0;
            foreach (var line in lines)
            {
                if (length + line.Length + 1 > 1000 && chunk.Count > 0)
                {
                    embed.AddField(part++ == 0 ? title : $"{title} (cont.)", string.Join('\n', chunk), false);
                    chunk.Clear();
                    length = 0;
                }

                chunk.Add(line);
                length += line.Length + 1;
            }

            if (chunk.Count > 0)
            {
                embed.AddField(part == 0 ? title : $"{title} (cont.)", string.Join('\n', chunk), false);
            }
        }

        return embed.WithFooter("Un torneo por día · se juega a las 00:00 (hora de Uruguay) · /arena join para el próximo").Build();
    }

    // El anuncio de las 00:00: lo mismo que /arena results, o el aviso de que se canceló.
    public static (string? Content, Embed Embed) BuildAnnouncement(ArenaResolution resolution)
    {
        if (resolution.Cancelled)
        {
            return (null, Simple($"🏟️ Se canceló la Arena del {ArenaRules.Format(resolution.Day)}", Color.DarkGrey,
                $"No llegaron a {ArenaRules.MinPlayers} anotados, así que no hubo torneo. ¡Hoy arranca otro: anotate con **/arena join**!"));
        }

        string? extra = resolution.LevelsGained > 0 ? $"🎉 ¡Y subió al nivel **{resolution.NewLevel}**!" : null;
        var embed = BuildResultsEmbed(
            resolution.Day, resolution.WinnerName, resolution.Participants, resolution.Rounds, resolution.RewardText, resolution.Matches, extra);

        return ($"🎉 ¡{MentionUtils.MentionUser(resolution.WinnerId!.Value)} es el campeón de la Arena!", embed);
    }

    private static string MatchLine(ArenaMatchRow match)
    {
        if (match.P2Id is null)
        {
            return $"• {Short(match.P1Name, 18)} _(pasa directo)_";
        }

        string p1 = Short(match.P1Name, 18);
        string p2 = Short(match.P2Name ?? "?", 18);
        bool firstWon = match.WinnerId == match.P1Id;

        return $"• {(firstWon ? $"**{p1}**" : p1)} ⚔️ {(firstWon ? p2 : $"**{p2}**")}";
    }

    private static Embed Simple(string title, Color color, string description) =>
        new EmbedBuilder().WithTitle(title).WithColor(color).WithDescription(description).Build();

    private static string Short(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
