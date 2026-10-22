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

        var crown = resolution.Matches.Where(m => m.Round == resolution.Rounds && m.P2Id is not null).Select(ArenaNarrator.Crowning).FirstOrDefault();
        string? levelUp = resolution.LevelsGained > 0 ? $"🎉 ¡Y subió al nivel **{resolution.NewLevel}**!" : null;
        string? extra = string.Join("\n", new[] { crown, levelUp }.Where(x => x is not null)) is { Length: > 0 } joined ? joined : null;
        var embed = BuildResultsEmbed(
            resolution.Day, resolution.WinnerName, resolution.Participants, resolution.Rounds, resolution.RewardText, resolution.Matches, extra);

        return ($"🎉 ¡{MentionUtils.MentionUser(resolution.WinnerId!.Value)} es el campeón de la Arena!", embed);
    }

    // Un mensaje de la transmisión: texto opcional (la mención del campeón) + el embed.
    public sealed record ArenaMessage(string? Content, Embed Embed);

    // La Arena "narrada": en vez de tirar el resultado de golpe, el bot cuenta el torneo en el canal ronda por ronda (el reloj deja unos segundos
    // entre mensaje y mensaje). Abre con quiénes pelean, sigue con cada ronda —cada pelea con su frase: paliza, por un pelo, batalla larga— y
    // cierra con el campeón, su premio y la llave completa. Un torneo cancelado es un solo mensaje. Pura: devuelve los mensajes, no los manda.
    public static IReadOnlyList<ArenaMessage> BuildBroadcast(ArenaResolution resolution)
    {
        var (content, closing) = BuildAnnouncement(resolution);
        if (resolution.Cancelled)
        {
            return [new ArenaMessage(content, closing)];
        }

        string day = ArenaRules.Format(resolution.Day);
        var messages = new List<ArenaMessage>();

        var names = resolution.Matches
            .Where(m => m.Round == 1)
            .SelectMany(m => m.P2Name is null ? new[] { m.P1Name } : new[] { m.P1Name, m.P2Name })
            .Select(n => Short(n, 18))
            .ToList();
        string roster = string.Join(", ", names);
        if (roster.Length > 900)
        {
            roster = roster[..899] + "…";
        }

        // Con relato (v0.23.2) el torneo avisa que tiene azar; sin él (un torneo viejo o armado a mano) es el mensaje de siempre.
        var stories = resolution.Stories is { Count: > 0 } list ? list.GroupBy(s => (s.P1Id, s.P2Id)).ToDictionary(g => g.Key, g => g.First()) : null;
        string luckNote = stories is null
            ? string.Empty
            : $"🍀 En la Arena el nivel pesa pero no decide: cada pelea tiene su **forma del día** y en {(int)Math.Round(ArenaTuning.MiracleChance * 100)} de cada 100 hay un **milagro** que iguala a los dos.\n\n";

        messages.Add(new ArenaMessage(null, new EmbedBuilder()
            .WithTitle($"🏟️ ¡Arranca la Arena del {day}!")
            .WithColor(Color.Orange)
            .WithDescription(
                $"Se anotaron **{resolution.Participants}** peleadores y se juega por eliminación directa: **{resolution.Rounds}** ronda(s).\n\n" +
                $"{luckNote}🥊 {roster}\n\n¡Que empiecen los combates!")
            .Build()));

        foreach (var round in resolution.Matches.GroupBy(m => m.Round).OrderBy(g => g.Key))
        {
            var fights = round.OrderBy(m => m.Slot).ToList();
            int real = fights.Count(m => m.P2Id is not null);
            bool isFinal = round.Key == resolution.Rounds;
            string roundName = ArenaBracket.RoundName(round.Key, resolution.Rounds);

            if (stories is not null && resolution.Rounds - round.Key <= 1)
            {
                // Semifinales y final: cada pelea se cuenta golpe por golpe, en su propio mensaje.
                foreach (var fight in fights.Where(m => m.P2Id is not null))
                {
                    messages.Add(new ArenaMessage(null, DetailedFight(fight, roundName, isFinal, stories)));
                }

                continue;
            }

            // Las demás rondas, una pelea por renglón (con su milagro o su forma del día si hubo) y partidas en varios mensajes si no entran en uno.
            var lines = fights.Select(m => BriefLine(m, stories)).ToList();
            foreach (var (chunk, index) in Chunk(lines, 3800).Select((c, i) => (c, i)))
            {
                messages.Add(new ArenaMessage(null, new EmbedBuilder()
                    .WithTitle($"{roundName} · {real} combate(s){(index > 0 ? " (sigue)" : string.Empty)}")
                    .WithColor(isFinal ? Color.Gold : Color.DarkOrange)
                    .WithDescription(chunk)
                    .Build()));
            }
        }

        messages.Add(new ArenaMessage(content, closing));
        return messages;
    }

    // Una pelea de las semifinales o la final: cómo llegaron (forma del día, milagro), los golpes que más se notaron y el resultado de siempre.
    private static Embed DetailedFight(ArenaMatchRow fight, string roundName, bool isFinal, IReadOnlyDictionary<(ulong, ulong), ArenaFightStory> stories)
    {
        var lines = new List<string>();
        if (stories.TryGetValue((fight.P1Id, fight.P2Id!.Value), out var story))
        {
            lines.AddRange(story.Opening);
            if (story.Opening.Count > 0 && story.Highlights.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.AddRange(story.Highlights);
            lines.Add(string.Empty);
        }

        lines.Add(ArenaNarrator.Describe(fight));
        string text = string.Join("\n", lines);

        return new EmbedBuilder()
            .WithTitle($"{roundName} · {Short(fight.P1Name, 18)} ⚔️ {Short(fight.P2Name ?? "?", 18)}")
            .WithColor(isFinal ? Color.Gold : Color.DarkOrange)
            .WithDescription(text.Length <= 4000 ? text : text[..3999] + "…")
            .Build();
    }

    // El renglón de una pelea de las rondas de antes: la primera línea de su apertura (el milagro, o quién llegó muy bien o muy mal) y el resultado.
    private static string BriefLine(ArenaMatchRow match, IReadOnlyDictionary<(ulong, ulong), ArenaFightStory>? stories)
    {
        string result = ArenaNarrator.Describe(match);
        if (stories is not null && match.P2Id is { } p2 && stories.TryGetValue((match.P1Id, p2), out var story) && story.Opening.Count > 0)
        {
            return $"{story.Opening[0]}\n{result}";
        }

        return result;
    }

    // Reparte los renglones en grupos que no pasen de maxChars (un embed admite 4096 caracteres de descripción): nunca devuelve una lista vacía.
    private static IReadOnlyList<string> Chunk(IReadOnlyList<string> lines, int maxChars)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        int length = 0;
        foreach (string line in lines)
        {
            if (current.Count > 0 && length + line.Length + 1 > maxChars)
            {
                chunks.Add(string.Join("\n", current));
                current.Clear();
                length = 0;
            }

            current.Add(line);
            length += line.Length + 1;
        }

        chunks.Add(string.Join("\n", current));
        return chunks;
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
