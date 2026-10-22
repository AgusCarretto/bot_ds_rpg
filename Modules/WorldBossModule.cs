using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /miniboss (aa miniboss): el jefe mundial del día (GameData/WorldBoss.cs). El dueño (2026-10-22): «aa miniboss y que todos peleen contra él, o worldboss; mientras más gente, mejor; que anotarse gaste el cooldown del
// jefe; que dé entre 30 y 50 cacerías de esa zona». Durante el día te anotás (sin pedir nivel: sirve para quien todavía no llega al nivel 5 de /boss); a las 00:00 de Uruguay lo pelean entre todos los anotados y cada
// uno cobra de 30 a 50 cacerías de oro de SU zona, más cuanta más vida le saquen entre todos. El bot lo anuncia en el canal donde se anotó el primero (o en WorldBoss__ChannelId, si está configurado).
public class WorldBossModule(IWorldBossService worldBoss) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("miniboss", "Anotate al jefe mundial de hoy: se pelea entre todos a las 00:00 y gasta tu cooldown del jefe.")]
    public async Task HandleMinibossAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: await ExecuteJoinAsync(worldBoss, Context.User, Context.Channel?.Id ?? 0, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude anotarte contra el jefe mundial, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa miniboss" (sin Context) ----

    public static async Task<Embed> ExecuteJoinAsync(IWorldBossService service, IUser user, ulong channelId, DateTime utcNow)
    {
        var result = await service.JoinAsync(user.Id, Short(GameModule.GetDisplayName(user), 24), channelId, utcNow);
        string day = ArenaRules.Format(result.Day);
        string until = UruguayCalendar.FormatRemaining(result.UntilPlay);
        var boss = result.Boss;
        string reward = $"cada anotado cobra de **{WorldBossRules.MinHunts} a {WorldBossRules.MaxHunts} cacerías de oro de su zona** ({WorldBossRules.MaxHunts} si el jefe cae)";

        return result.Status switch
        {
            WorldBossJoinStatus.Joined => new EmbedBuilder()
                .WithTitle($"{boss.Emoji} ¡Anotado contra {boss.Name}!")
                .WithColor(Color.DarkRed)
                .WithDescription(
                    $"{boss.Intro}\n\n" +
                    $"Quedaste anotado en el jefe del **{day}**. Se pelea a las **00:00** (hora de Uruguay), en **{until}**, **entre todos** los anotados.\n\n" +
                    $"Ya hay **{result.Count}** anotado(s). **Mientras más seamos, mejor**: {reward}. Con unos {WorldBossRules.FullCrowd:0} jugadores normales el jefe cae.\n\n" +
                    $"Anotarte gastó tu **cooldown del jefe** ({TimeFormat.Remaining(CooldownCatalog.Boss.Duration)}). No hace falta nivel.")
                .WithFooter("Volvé a escribir /miniboss para ver cómo viene el día.")
                .Build(),

            WorldBossJoinStatus.AlreadyJoined => Simple($"{boss.Emoji} Ya estabas anotado", Color.DarkGrey,
                $"Seguís anotado contra **{boss.Name}** (se pelea en **{until}**). Hay **{result.Count}** anotado(s): {reward}. Con unos {WorldBossRules.FullCrowd:0} jugadores normales el jefe cae, así que traé amigos."),

            WorldBossJoinStatus.OnCooldown => Simple("⏳ Todavía no podés anotarte", Color.DarkGrey,
                $"El jefe mundial usa tu **cooldown del jefe** y todavía te faltan **{TimeFormat.Remaining(result.CooldownLeft)}**. Cuando termine, anotate con **/miniboss** (se pelea en {until}; si te pasás de hora, mañana hay otro)."),

            WorldBossJoinStatus.NoAccount => Simple("Primero tu aventura", Color.DarkGrey, "Tenés que empezar con **/start** antes de anotarte contra el jefe mundial."),

            _ => Simple("⏳ El jefe de hoy ya se cerró", Color.DarkGrey, "Ese jefe ya no está abierto. Probá de nuevo en unos instantes."),
        };
    }

    // La transmisión del jefe mundial en el canal: quiénes se anotaron, cómo fue la batalla (los que más le pegaron y cuánta vida le sacaron entre todos) y el resultado con el premio. Pura: devuelve los mensajes, no los manda.
    public static IReadOnlyList<ArenaModule.ArenaMessage> BuildBroadcast(WorldBossResolution resolution)
    {
        var boss = resolution.Boss;
        string day = ArenaRules.Format(resolution.Day);

        if (resolution.Cancelled || resolution.Hits.Count == 0)
        {
            return [new ArenaModule.ArenaMessage(null, Simple($"{boss.Emoji} {boss.Name} se fue sin pelea", Color.DarkGrey, $"El jefe mundial del **{day}** no tuvo con quién pelear. Mañana aparece otro: anotate con **/miniboss**."))];
        }

        var messages = new List<ArenaModule.ArenaMessage>();
        var rng = new Random(resolution.Day.DayNumber);

        string roster = string.Join(", ", resolution.Hits.Select(h => Short(h.Name, 18)));
        if (roster.Length > 900)
        {
            roster = roster[..899] + "…";
        }

        messages.Add(new ArenaModule.ArenaMessage(null, new EmbedBuilder()
            .WithTitle($"{boss.Emoji} ¡{boss.Name} se despertó!")
            .WithColor(Color.DarkRed)
            .WithDescription(
                $"{boss.Intro}\n\n" +
                $"Se anotaron **{resolution.Participants}** héroe(s) y le pegan **todos juntos**: tienen que sacarle el 100 % de la vida entre todos.\n\n🗡️ {roster}")
            .Build()));

        var top = resolution.Hits.Take(6).ToList();
        var lines = top.Select(h => WorldBossRules.HitLine(h, boss, rng)).ToList();
        int others = resolution.Hits.Count - top.Count;
        if (others > 0)
        {
            double sum = resolution.Hits.Skip(top.Count).Sum(h => h.DamagePercent);
            lines.Add($"👥 Y otros **{others}** más suman **{sum.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} %** entre todos.");
        }

        int left = (int)Math.Round(Math.Clamp(100 - resolution.TotalPercent, 0, 100));
        messages.Add(new ArenaModule.ArenaMessage(null, new EmbedBuilder()
            .WithTitle("⚔️ La batalla")
            .WithColor(Color.DarkOrange)
            .WithDescription(
                string.Join("\n", lines) + "\n\n" +
                $"{boss.Emoji} Vida de {boss.Name}\n{ProgressBar.Render(left, 100, kind: UiEmojis.BarHp)}\nLe sacaron **{resolution.TotalPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} %** entre todos.")
            .Build()));

        var mvp = resolution.Hits[0];
        string rewardLine = $"Cada uno de los **{resolution.Participants}** anotados cobró **{resolution.Hunts} cacerías de oro de su zona**" +
            (resolution.Defeated ? " (el máximo)." : $" (de {WorldBossRules.MinHunts} a {WorldBossRules.MaxHunts}: **mientras más gente, mejor**; con unos {WorldBossRules.FullCrowd:0} jugadores normales el jefe cae).");

        messages.Add(new ArenaModule.ArenaMessage(null, new EmbedBuilder()
            .WithTitle(resolution.Defeated ? $"🏆 ¡Cayó {boss.Name}!" : $"{boss.Emoji} {boss.Name} sigue en pie")
            .WithColor(resolution.Defeated ? Color.Gold : Color.DarkGrey)
            .WithDescription(
                (resolution.Defeated
                    ? $"Lo lograron: entre todos le sacaron más del 100 % de la vida. ¡Qué equipo!\n\n"
                    : $"Le quedó **{left} %** de vida: faltaron manos. Mañana vuelve otro jefe, y cuanta más gente se anote, más fácil cae.\n\n") +
                $"{rewardLine}\n\n🥇 El que más le pegó: **{Short(mvp.Name, 24)}** (nivel {mvp.Level}) con **{mvp.DamagePercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} %**.")
            .WithFooter("Anotate mañana con /miniboss (aa miniboss).")
            .Build()));

        return messages;
    }

    private static Embed Simple(string title, Color color, string description) =>
        new EmbedBuilder().WithTitle(title).WithColor(color).WithDescription(description).Build();

    private static string Short(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
