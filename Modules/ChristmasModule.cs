using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Navidad en el Fogón (v0.57.0, docs/navidad.md): /navidad (aa navidad) muestra tus Regalos y el calendario de adviento, con el botón para abrir
// la caja de hoy. La tienda, el Reno, el Krampus y el Muñeco de Nieve Gigante se suman a esta misma pantalla en las próximas versiones.
public class ChristmasModule(
    IUserRepository userRepository,
    IZoneRepository zoneRepository,
    IChristmasRepository christmasRepository,
    IChristmasService christmasService) : InteractionModuleBase<SocketInteractionContext>
{
    private const string ClaimPrefix = "adv_claim";

    [SlashCommand("navidad", "Navidad en el Fogón: tus Regalos y el calendario de adviento.")]
    public async Task HandleChristmasAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildAsync(christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow);
            await FollowupAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir la Navidad, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{ClaimPrefix}:*")]
    public async Task HandleClaimAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Ese calendario es de otra persona: abrí el tuyo con **/navidad**."), ephemeral: true);
                return;
            }

            var result = await ClaimAsync(userRepository, zoneRepository, christmasRepository, christmasService, ownerId, DateTime.UtcNow);
            if (result.Embed is null)
            {
                await FollowupAsync(result.Error, ephemeral: true);
                return;
            }

            // El calendario se actualiza en el mismo mensaje (el día queda tachado) y el premio sale aparte.
            var view = await BuildAsync(christmasRepository, christmasService, ownerId, DateTime.UtcNow);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
            await FollowupAsync(embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir la caja de hoy, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    public sealed record ChristmasView(Embed Embed, MessageComponent? Components = null);

    public sealed record AdventClaimView(string? Error, Embed? Embed = null);

    // La pantalla de /navidad. Solo mira: nunca crea cuentas (sin cuenta, 0 Regalos y el calendario sin abrir).
    public static async Task<ChristmasView> BuildAsync(IChristmasRepository repository, IChristmasService service, ulong discordId, DateTime utcNow)
    {
        var settings = service.Settings;
        var phase = ChristmasRules.PhaseAt(settings, utcNow);
        var embed = new EmbedBuilder().WithTitle(Loc.T("❄️ Navidad en el Fogón")).WithColor(new Color(0xC0392B));

        if (phase == ChristmasPhase.Off)
        {
            string start = (settings.StartUtc + UruguayCalendar.Offset).ToString("dd/MM");
            embed.WithDescription(utcNow < settings.StartUtc
                ? Loc.F($"La Navidad llega al Fogón el **{start}**.\n\nDel 1 al 24 de diciembre van a caer 🎁 **Regalos** de cada pelea, y habrá un calendario de adviento con una caja por día.")
                : Loc.T("La Navidad ya terminó. ¡Gracias por festejarla en el Fogón!"));
            return new ChristmasView(embed.Build());
        }

        var gifts = await repository.GetAsync(discordId, settings.Season);
        var claimed = await repository.GetClaimedDaysAsync(discordId, settings.Season);
        int today = ChristmasRules.AdventDayAt(settings, utcNow);

        var description = new List<string>
        {
            Loc.F($"🎁 **Regalos**: {GameHistory.Number(gifts.Balance)}"),
            Loc.F($"📦 **Juntados en total**: {GameHistory.Number(gifts.Earned)}"),
        };

        if (phase == ChristmasPhase.Farm)
        {
            description.Add(string.Empty);
            description.Add(Loc.T("Caen Regalos de cada **/hunt**, **/travel**, jefe y **/daily** hasta el 24 de diciembre."));
        }
        else
        {
            string end = (settings.EndUtc.AddDays(-1) + UruguayCalendar.Offset).ToString("dd/MM");
            description.Add(string.Empty);
            description.Add(Loc.F($"Ya no caen Regalos. Gastalos antes del **{end}**: después desaparecen."));
        }

        embed.WithDescription(string.Join("\n", description));

        if (phase == ChristmasPhase.Farm)
        {
            embed.AddField(Loc.T("📅 Calendario de adviento"), AdventGrid(claimed, today), false);

            var next = ChristmasRules.Advent(today);
            string todayLine = claimed.Contains(today)
                ? Loc.F($"Ya abriste el día **{today}**. Volvé mañana.")
                : Loc.F($"Hoy es el día **{today}**: {Describe(next)}");
            embed.AddField(Loc.T("🎄 Hoy"), todayLine, false);
        }

        MessageComponent? components = null;
        if (phase == ChristmasPhase.Farm && today > 0 && !claimed.Contains(today))
        {
            components = new ComponentBuilder()
                .WithButton(Loc.F($"Abrir el día {today}"), $"{ClaimPrefix}:{discordId}", ButtonStyle.Success, new Emoji("🎁"))
                .Build();
        }

        return new ChristmasView(embed.Build(), components);
    }

    // Reclama el día de hoy. Crea la cuenta si hace falta (es un premio, como el /daily).
    public static async Task<AdventClaimView> ClaimAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IChristmasRepository repository, IChristmasService service, ulong discordId, DateTime utcNow)
    {
        var settings = service.Settings;
        int today = ChristmasRules.AdventDayAt(settings, utcNow);
        if (today == 0)
        {
            return new AdventClaimView(Loc.T("El calendario de adviento está abierto del 1 al 24 de diciembre."));
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);
        int zoneRank = await MissionsModule.ResolveZoneRankAsync(zoneRepository, player.CurrentZoneId);
        var outcome = await repository.ClaimAdventAsync(discordId, settings.Season, ChristmasRules.Advent(today), zoneRank);

        return outcome.Status switch
        {
            AdventClaimStatus.AlreadyClaimed => new AdventClaimView(Loc.F($"Ya abriste el día **{today}**. Volvé mañana.")),
            AdventClaimStatus.NoAccount => new AdventClaimView(Loc.T("No encontré tu cuenta, probá de nuevo.")),
            _ => new AdventClaimView(null, BuildClaimEmbed(outcome)),
        };
    }

    public static Embed BuildClaimEmbed(AdventClaimOutcome outcome)
    {
        var day = outcome.Day!;
        var lines = new List<string> { Loc.F($"🎁 **Regalos**: +{day.Gifts}") };

        if (outcome.Receipt is { } receipt)
        {
            if (receipt.Reward.Gold > 0)
            {
                lines.Add(Loc.F($"💰 **Oro**: +{GameHistory.Number(receipt.Reward.Gold)}"));
            }

            if (receipt.Reward.BoxName is not null)
            {
                lines.Add(Loc.F($"{ItemEmojiRegistry.Label(receipt.Reward.BoxName, "📦")} — abrila con **/open**"));
            }
        }

        if (day.Dust > 0)
        {
            lines.Add(Loc.F($"✨ **Polvo**: +{GameHistory.Number(day.Dust)}"));
        }

        lines.Add(string.Empty);
        lines.Add(Loc.F($"Ahora tenés **{GameHistory.Number(outcome.GiftsAfter)}** Regalos."));

        return new EmbedBuilder()
            .WithTitle(day.Day == ChristmasRules.AdventDays ? Loc.T("🎄 ¡Nochebuena!") : Loc.F($"🎁 Día {day.Day} del calendario"))
            .WithColor(new Color(0x27AE60))
            .WithDescription(string.Join("\n", lines))
            .Build();
    }

    // Los 24 días en 4 filas de 6: ✅ abierto, ❌ perdido, 🎁 hoy sin abrir, ⬜ todavía no llegó. Es una barra de ancho completo, no una lista de ítems.
    public static string AdventGrid(IReadOnlySet<int> claimed, int today)
    {
        var rows = new List<string>();
        for (int start = 1; start <= ChristmasRules.AdventDays; start += 6)
        {
            var cells = Enumerable.Range(start, 6).Select(d =>
                claimed.Contains(d) ? "✅" : d < today ? "❌" : d == today ? "🎁" : "⬜");
            rows.Add(string.Concat(cells));
        }

        return string.Join("\n", rows);
    }

    // Lo que trae un día, en una línea.
    public static string Describe(AdventDay day)
    {
        var parts = new List<string> { Loc.F($"**{day.Gifts}** Regalos") };
        if (day.Reward is { GoldUnits: > 0 })
        {
            parts.Add(Loc.T("una bolsa de oro"));
        }

        if (day.Reward is { Box: BoxGrant.Weekly })
        {
            parts.Add(Loc.T("una caja grande"));
        }
        else if (day.Reward is { Box: BoxGrant.Daily })
        {
            parts.Add(Loc.T("una caja"));
        }

        if (day.Dust > 0)
        {
            parts.Add(Loc.F($"{day.Dust} de Polvo"));
        }

        return string.Join(" + ", parts);
    }
}
