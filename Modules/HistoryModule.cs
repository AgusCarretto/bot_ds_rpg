using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// /history: cuántas veces jugaste, ganaste y perdiste en CADA juego (cacería, viaje, jefe, raid, duelos, arena y los dos del casino con el oro que
// ganaste y perdiste). /duels: tu récord de duelos y los últimos rivales. Los dos salen de los eventos que el bot ya registra (game_events);
// no se guarda nada aparte. Con "jugador" se mira el de otra persona, como /profile.
public class HistoryModule(IUserRepository userRepository, IGameEventRepository eventRepository) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("history", "Tu historial de juegos: cuántas veces jugaste, ganaste y perdiste en cada uno.")]
    public async Task HandleHistoryAsync(
        [Summary("jugador", "Opcional: de qué jugador del server querés ver el historial.")] SocketGuildUser? targetUser = null)
    {
        await DeferAsync();

        try
        {
            IUser target = targetUser ?? Context.User;
            await FollowupAsync(embed: await BuildHistoryEmbedAsync(userRepository, eventRepository, target.Id, GameModule.GetDisplayName(target)));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar el historial, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("duels", "Tu historial de duelos: tu récord y contra quién fueron los últimos.")]
    public async Task HandleDuelsAsync(
        [Summary("jugador", "Opcional: de qué jugador del server querés ver los duelos.")] SocketGuildUser? targetUser = null)
    {
        await DeferAsync();

        try
        {
            IUser target = targetUser ?? Context.User;
            await FollowupAsync(embed: await BuildDuelsEmbedAsync(userRepository, eventRepository, target.Id, GameModule.GetDisplayName(target), DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar el historial de duelos, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa history" / "aa duels" (sin Context) ----

    public static async Task<Embed> BuildHistoryEmbedAsync(IUserRepository userRepository, IGameEventRepository eventRepository, ulong discordId, string name)
    {
        // GetByDiscordIdAsync (no GetOrCreate): mirar un historial no le crea cuenta a nadie.
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new EmbedBuilder().WithTitle("📜 Historial").WithColor(Color.DarkGrey).WithDescription(GameModule.BuildNotRegisteredMessage(name)).Build();
        }

        var report = GameHistory.Build(await eventRepository.GetBreakdownAsync(discordId, GameHistory.Kinds));
        var arena = GameHistory.DescribeArena(report);

        var embed = new EmbedBuilder()
            .WithTitle($"📜 Historial de {name}")
            .WithColor(Color.Orange);

        if (report.Games.Count == 0 && arena is null)
        {
            return embed.WithDescription("Todavía no hay nada para contar: salí a cazar (**/hunt**), viajá (**/travel**) o probá suerte en el casino (**/play**).").Build();
        }

        // Un campo por juego, con una fila en blanco entre uno y otro para que respire.
        bool first = true;
        foreach (var game in report.Games)
        {
            if (!first)
            {
                embed.AddField(Blank, Blank, false);
            }

            first = false;
            var (title, value) = GameHistory.Describe(game);
            embed.AddField(title, value, false);
        }

        if (arena is { } arenaField)
        {
            if (!first)
            {
                embed.AddField(Blank, Blank, false);
            }

            embed.AddField(arenaField.Title, arenaField.Value, false);
        }

        return embed.WithFooter("Se cuenta desde que el bot empezó a registrar cada juego · los duelos en detalle, con /duels").Build();
    }

    public static async Task<Embed> BuildDuelsEmbedAsync(
        IUserRepository userRepository, IGameEventRepository eventRepository, ulong discordId, string name, DateTime utcNow)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new EmbedBuilder().WithTitle("🥊 Duelos").WithColor(Color.DarkGrey).WithDescription(GameModule.BuildNotRegisteredMessage(name)).Build();
        }

        var totals = await eventRepository.GetBreakdownAsync(discordId, DuelHistory.Kinds);
        var recent = await eventRepository.GetRecentAsync(discordId, DuelHistory.Kinds, DuelHistory.RecentCount);
        var (title, description) = DuelHistory.Describe(name, totals, recent, utcNow);

        return new EmbedBuilder().WithTitle(title).WithColor(Color.Orange).WithDescription(description).WithFooter("Los duelos son amistosos: no se gana ni se pierde nada.").Build();
    }

    // Espacio de ancho cero (U+200B): Discord no deja campos con el texto vacío; así se arma una fila en blanco (igual que en /inventory).
    private static readonly string Blank = ((char)0x200B).ToString();
}
