using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Lo que contesta /invitedby: Mention = a quién hay que avisar con un ping (va en el contenido del mensaje: un embed no avisa a nadie), o null.
public sealed record ReferralReply(string? Mention, Embed Embed);

// /invitedby (aa invitedby @amigo): decir quién te invitó al juego. Una sola vez por cuenta; el invitado cobra 10k, el que invitó 20k y 80k más cuando el invitado llega al nivel 5 (en el momento si ya
// había llegado). Reglas en GameData/Referrals.cs, el cobro en Repositories/ReferralRepository.cs. La lógica vive en un método estático para que el comando de texto use exactamente la misma.
public class ReferralModule(IUserRepository userRepository, IReferralRepository referralRepository, IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("invitedby", "¿Te invitó un amigo? Decí quién y los dos se llevan oro (una sola vez por cuenta).")]
    public async Task HandleInvitedByAsync([Summary("amigo", "El jugador que te invitó al juego (vacío: ver quién te invitó y a cuántos invitaste).")] IUser? amigo = null)
    {
        await DeferAsync();

        try
        {
            var reply = await ExecuteAsync(userRepository, referralRepository, gameEvents, Context.User.Id, amigo?.Id, amigo?.IsBot ?? false);
            await FollowupAsync(reply.Mention, embed: reply.Embed, allowedMentions: new AllowedMentions(AllowedMentionTypes.Users));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude anotar quién te invitó ahora mismo, intentá de nuevo en un momento."));
        }
    }

    public static async Task<ReferralReply> ExecuteAsync(
        IUserRepository userRepository, IReferralRepository referralRepository, IGameEvents gameEvents, ulong discordId, ulong? friendId, bool friendIsBot = false)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return Plain(Loc.T("Todavía no tenés personaje: empezá con `/start` y después decí quién te invitó."));
        }

        if (friendId is not ulong friend)
        {
            return new ReferralReply(null, BuildSummaryEmbed(await referralRepository.GetSummaryAsync(discordId)));
        }

        if (friendIsBot)
        {
            return Plain(Loc.T("Un bot no te puede invitar: nombrá al jugador que te trajo, por ejemplo `aa invitedby @amigo`."));
        }

        if (friend == discordId)
        {
            return Plain(Loc.T("No podés invitarte a vos mismo. 😄"));
        }

        var outcome = await referralRepository.RegisterAsync(discordId, friend);
        switch (outcome.Status)
        {
            case ReferralStatus.NoAccount:
                return Plain(Loc.T("Todavía no tenés personaje: empezá con `/start` y después decí quién te invitó."));

            case ReferralStatus.InviterNoAccount:
                return Plain(Loc.F($"<@{friend}> todavía no juega: tiene que hacer `/start` primero para poder cobrar."));

            case ReferralStatus.AlreadyRegistered:
                return Plain(Loc.F($"Ya anotaste que te invitó <@{outcome.InviterId}>: se puede hacer una sola vez por cuenta."));

            default:
                await gameEvents.RecordAsync(discordId, GameEventKinds.Referral, amount: outcome.InviterGold, detail: friend.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return new ReferralReply($"<@{friend}>", BuildRegisteredEmbed(discordId, friend, outcome));
        }
    }

    public static Embed BuildRegisteredEmbed(ulong inviteeId, ulong inviterId, ReferralOutcome outcome)
    {
        string mine = GameHistory.Number(outcome.NewPlayerGold);
        string theirs = GameHistory.Number(outcome.InviterGold);
        string later = GameHistory.Number(ReferralRules.InviterLevelGold);

        var embed = new EmbedBuilder()
            .WithColor(OnboardingModule.BrandColor)
            .WithTitle(Loc.T("🤝 ¡Gracias por venir con un amigo!"))
            .WithDescription(Loc.F($"<@{inviteeId}> anotó que lo invitó <@{inviterId}>."))
            .AddField(Loc.T("Para vos"), Loc.F($"**{mine}** {UiEmojis.Gold} de oro"), true)
            .AddField(Loc.T("Para quien te invitó"), Loc.F($"**{theirs}** {UiEmojis.Gold} de oro"), true);

        embed.AddField(
            Loc.T("Nivel 5"),
            outcome.LevelBonusPaid
                ? Loc.F($"Ya pasaste el nivel {ReferralRules.RequiredLevel}, así que el premio de **{later}** de oro ya va incluido.")
                : Loc.F($"Cuando llegues al nivel {ReferralRules.RequiredLevel}, <@{inviterId}> cobra **{later}** {UiEmojis.Gold} de oro más."),
            false);

        return embed.WithFooter(Loc.T("Se puede anotar una sola vez por cuenta.")).Build();
    }

    public static Embed BuildSummaryEmbed(ReferralSummary summary)
    {
        string invitedBy = summary.InvitedBy is ulong id
            ? Loc.F($"Te invitó <@{id}>.")
            : Loc.F($"Nadie todavía. Si te invitó un amigo, escribí `aa invitedby @amigo` (o `/invitedby`): vos cobrás **{GameHistory.Number(ReferralRules.NewPlayerGold)}** de oro y tu amigo **{GameHistory.Number(ReferralRules.InviterStartGold)}**.");

        return new EmbedBuilder()
            .WithColor(OnboardingModule.BrandColor)
            .WithTitle(Loc.T("🤝 Invitar amigos"))
            .AddField(Loc.T("¿Quién te invitó?"), invitedBy, false)
            .AddField(
                Loc.T("A quiénes invitaste"),
                Loc.F($"**{summary.Invited}** jugadores (de ellos, **{summary.ReachedLevel}** ya llegaron al nivel {ReferralRules.RequiredLevel})."),
                false)
            .AddField(
                Loc.T("Cuánto se cobra"),
                Loc.F($"Cuando alguien anota que lo invitaste, cobrás **{GameHistory.Number(ReferralRules.InviterStartGold)}** de oro, y **{GameHistory.Number(ReferralRules.InviterLevelGold)}** más cuando llega al nivel {ReferralRules.RequiredLevel} (en el momento si ya había llegado). Cada cuenta puede anotar a quien la invitó una sola vez."),
                false)
            .Build();
    }

    // Línea para el final del tutorial y de /start.
    public static string Hint() =>
        Loc.F($"¿Te invitó un amigo? Escribí `aa invitedby @amigo` (o `/invitedby`): vos cobrás **{GameHistory.Number(ReferralRules.NewPlayerGold)}** de oro y tu amigo también cobra.");

    private static ReferralReply Plain(string text) =>
        new(null, new EmbedBuilder().WithColor(OnboardingModule.BrandColor).WithDescription(text).Build());
}
