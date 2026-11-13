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
    IChristmasService christmasService,
    ICooldownRepository cooldownRepository) : InteractionModuleBase<SocketInteractionContext>
{
    private const string ClaimPrefix = "adv_claim";
    private const string ShopPrefix = "xmas_shop";
    private const string BackPrefix = "xmas_back";
    private const string BuyPrefix = "xmas_buy";

    [SlashCommand("navidad", "Navidad en el Fogón: tus Regalos y el calendario de adviento.")]
    public async Task HandleChristmasAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildAsync(christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow, cooldownRepository);
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
            var view = await BuildAsync(christmasRepository, christmasService, ownerId, DateTime.UtcNow, cooldownRepository);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
            await FollowupAsync(embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir la caja de hoy, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{ShopPrefix}:*")]
    public async Task HandleShopAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa tienda es de otra persona: abrí la tuya con **/navidad**."), ephemeral: true);
                return;
            }

            var view = await BuildShopAsync(christmasRepository, christmasService, ownerId, DateTime.UtcNow);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir la tienda de Navidad, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{BackPrefix}:*")]
    public async Task HandleBackAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Ese calendario es de otra persona: abrí el tuyo con **/navidad**."), ephemeral: true);
                return;
            }

            var view = await BuildAsync(christmasRepository, christmasService, ownerId, DateTime.UtcNow, cooldownRepository);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir la Navidad, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{BuyPrefix}:*")]
    public async Task HandleBuyAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa tienda es de otra persona: abrí la tuya con **/navidad**."), ephemeral: true);
                return;
            }

            var result = await BuyAsync(userRepository, zoneRepository, christmasRepository, christmasService, ownerId, selected.FirstOrDefault(), 1, DateTime.UtcNow);
            if (result.Embed is null)
            {
                await FollowupAsync(result.Error, ephemeral: true);
                return;
            }

            // La tienda se refresca (el saldo bajó) y la compra sale aparte.
            var view = await BuildShopAsync(christmasRepository, christmasService, ownerId, DateTime.UtcNow);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
            await FollowupAsync(embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude hacer la compra, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // La tienda navideña: un artículo por línea y un desplegable para comprar de a uno (las Campanas de a muchas, con «aa navidad comprar campana 10»).
    public static async Task<ChristmasView> BuildShopAsync(IChristmasRepository repository, IChristmasService service, ulong discordId, DateTime utcNow)
    {
        var settings = service.Settings;
        var phase = ChristmasRules.PhaseAt(settings, utcNow);
        var embed = new EmbedBuilder().WithTitle(Loc.T("🛒 Tienda de Navidad")).WithColor(new Color(0x27AE60));
        if (phase == ChristmasPhase.Off)
        {
            embed.WithDescription(Loc.T("La tienda de Navidad abre del 1 de diciembre al 6 de enero."));
            return new ChristmasView(embed.Build());
        }

        var gifts = await repository.GetAsync(discordId, settings.Season);
        var lines = new List<string> { Loc.F($"🎁 **Tenés**: {GameHistory.Number(gifts.Balance)} Regalos"), string.Empty };
        var menu = new SelectMenuBuilder().WithCustomId($"{BuyPrefix}:{discordId}").WithPlaceholder(Loc.T("Elegí qué comprar"));
        foreach (var item in ChristmasShop.Items)
        {
            bool available = ChristmasShop.Available(item, phase);
            lines.Add(Loc.F($"{item.Emoji} **{ChristmasShopText.Label(item)}**: {GameHistory.Number(item.Price)} 🎁"));
            lines.Add(available ? Loc.Tr(item.Description) : Loc.T("Ya no se vende: los Regalos dejaron de caer."));
            if (available)
            {
                menu.AddOption(Loc.F($"{ChristmasShopText.Label(item)} — {GameHistory.Number(item.Price)} Regalos"), item.Key, emote: new Emoji(item.Emoji));
            }
        }

        embed.WithDescription(string.Join("\n", lines));
        embed.WithFooter(Loc.T("Campanas de a muchas: aa navidad comprar campana 10"));
        var components = new ComponentBuilder()
            .WithSelectMenu(menu)
            .WithButton(Loc.T("Volver"), $"{BackPrefix}:{discordId}", ButtonStyle.Secondary, new Emoji("↩️"))
            .Build();
        return new ChristmasView(embed.Build(), components);
    }

    public sealed record ChristmasBuyView(string? Error, Embed? Embed = null);

    // Compra en la tienda (desplegable o «aa navidad comprar X [n]»). Crea la cuenta si hace falta (paga con lo que juntó).
    public static async Task<ChristmasBuyView> BuyAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IChristmasRepository repository, IChristmasService service,
        ulong discordId, string? itemKey, int quantity, DateTime utcNow)
    {
        var settings = service.Settings;
        var phase = ChristmasRules.PhaseAt(settings, utcNow);
        if (phase == ChristmasPhase.Off)
        {
            return new ChristmasBuyView(Loc.T("La tienda de Navidad abre del 1 de diciembre al 6 de enero."));
        }

        var item = ChristmasShop.Find(itemKey);
        if (item is null)
        {
            return new ChristmasBuyView(Loc.F($"No conozco ese artículo. Probá con: {string.Join(", ", ChristmasShop.Items.Select(i => i.Key))}."));
        }

        if (!ChristmasShop.Available(item, phase))
        {
            return new ChristmasBuyView(Loc.T("La Vela ya no se vende: los Regalos dejaron de caer."));
        }

        long cost = (long)item.Price * (item.Kind == ChristmasShopKind.Bell ? Math.Clamp(quantity, 1, ChristmasShop.MaxBellsPerPurchase) : 1);
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        int zoneRank = await MissionsModule.ResolveZoneRankAsync(zoneRepository, player.CurrentZoneId);
        var outcome = await repository.BuyAsync(discordId, settings.Season, item, quantity, zoneRank);

        return outcome.Status switch
        {
            ChristmasBuyStatus.NotEnoughGifts => new ChristmasBuyView(Loc.F($"No te alcanzan los Regalos: cuesta **{GameHistory.Number(cost)}** y tenés menos.")),
            ChristmasBuyStatus.AlreadyOwned => new ChristmasBuyView(Loc.F($"Ya tenés **{ChristmasShopText.Label(item)}**.")),
            ChristmasBuyStatus.NoAccount => new ChristmasBuyView(Loc.T("No encontré tu cuenta, probá de nuevo.")),
            _ => new ChristmasBuyView(null, BuildBuyEmbed(outcome)),
        };
    }

    public static Embed BuildBuyEmbed(ChristmasBuyOutcome outcome)
    {
        var item = outcome.Item;
        var lines = new List<string>();
        switch (item.Kind)
        {
            case ChristmasShopKind.Bell:
                lines.Add(Loc.F($"🔔 **Campanas**: +{outcome.Quantity} (tenés {GameHistory.Number(outcome.BellsAfter)})"));
                lines.Add(Loc.T("Usalas con **aa campana** cuando /hunt esté esperando."));
                break;
            case ChristmasShopKind.Candle:
                lines.Add(Loc.F($"🕯️ **Vela prendida** hasta las {(outcome.CandleUntil!.Value + UruguayCalendar.Offset):HH:mm} (hora de Uruguay): +{ChristmasShop.CandleGiftPercent} % de Regalos."));
                break;
            case ChristmasShopKind.Sock:
                if (outcome.Receipt is { } receipt)
                {
                    lines.Add(Loc.F($"💰 **Oro**: +{GameHistory.Number(receipt.Reward.Gold)}"));
                    lines.Add(Loc.F($"📊 **EXP**: +{GameHistory.Number(receipt.Reward.Xp)}"));
                    if (receipt.Reward.BoxName is not null)
                    {
                        lines.Add(Loc.F($"{ItemEmojiRegistry.Label(receipt.Reward.BoxName, "📦")} — abrila con **/open**"));
                    }
                }

                lines.Add(Loc.F($"✨ **Polvo**: +{GameHistory.Number(outcome.Dust)}"));
                break;
            case ChristmasShopKind.Unlock when item.UnlockStat == RenoRules.StatKey:
                lines.Add(Loc.F($"🦌 **¡El Reno es tuyo!** +{RenoRules.XpGoldPercent} % de XP y de oro, y /travel cada {(int)RenoRules.TravelCooldown.TotalMinutes} minutos. Para siempre."));
                break;
            case ChristmasShopKind.Unlock:
                lines.Add(Loc.F($"{item.Emoji} Nuevo título: **{item.Name}**. Ponételo con **aa title**."));
                break;
        }

        lines.Add(string.Empty);
        lines.Add(Loc.F($"Te quedan **{GameHistory.Number(outcome.GiftsAfter)}** Regalos."));
        return new EmbedBuilder()
            .WithTitle(Loc.F($"{item.Emoji} Compraste {ChristmasShopText.Label(item)}"))
            .WithColor(new Color(0x27AE60))
            .WithDescription(string.Join("\n", lines))
            .Build();
    }

    // «aa campana»: reinicia /hunt con una Campana. Fuera del evento también sirven las que quedaron (hasta que termina la temporada).
    public static async Task<string> UseBellAsync(IChristmasRepository repository, IChristmasService service, ulong discordId)
    {
        var (status, left) = await repository.UseBellAsync(discordId, service.Settings.Season, CooldownCatalog.Hunt.Duration);
        return status switch
        {
            BellUseStatus.Ok => Loc.F($"🔔 ¡Ding! **/hunt** está listo otra vez. Te quedan **{GameHistory.Number(left)}** Campanas."),
            BellUseStatus.NotNeeded => Loc.F($"**/hunt** ya está listo: no gasté la Campana (tenés **{GameHistory.Number(left)}**)."),
            _ => Loc.T("No tenés Campanas. Compralas en la tienda de **/navidad** (3 Regalos cada una)."),
        };
    }

    // null si el Krampus se puede pelear ahora; si no, el aviso (antes del 1 o después del 24 de diciembre).
    public static string? KrampusClosedMessage(IChristmasService service, DateTime utcNow) =>
        ChristmasRules.PhaseAt(service.Settings, utcNow) == ChristmasPhase.Farm
            ? null
            : Loc.T("👹 El Krampus solo anda suelto del 1 al 24 de diciembre.");

    public sealed record ChristmasView(Embed Embed, MessageComponent? Components = null);

    public sealed record AdventClaimView(string? Error, Embed? Embed = null);

    // La pantalla de /navidad. Solo mira: nunca crea cuentas (sin cuenta, 0 Regalos y el calendario sin abrir).
    public static async Task<ChristmasView> BuildAsync(
        IChristmasRepository repository, IChristmasService service, ulong discordId, DateTime utcNow, ICooldownRepository? cooldowns = null)
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
        if (gifts.Bells > 0)
        {
            description.Add(Loc.F($"🔔 **Campanas**: {GameHistory.Number(gifts.Bells)} (usalas con aa campana)"));
        }

        if (gifts.CandleLit(utcNow))
        {
            description.Add(Loc.F($"🕯️ **Vela prendida** hasta las {(DateTime.SpecifyKind(gifts.CandleUntil!.Value, DateTimeKind.Utc) + UruguayCalendar.Offset):HH:mm}: +{ChristmasShop.CandleGiftPercent} % de Regalos"));
        }

        if (phase == ChristmasPhase.Farm)
        {
            description.Add(string.Empty);
            description.Add(Loc.T("Caen Regalos de cada **/hunt**, **/travel**, jefe y **/daily** hasta el 24 de diciembre."));
            if (cooldowns is not null)
            {
                var krampusWait = await cooldowns.GetRemainingAsync(discordId, KrampusRules.CommandName, KrampusRules.Cooldown.Duration);
                description.Add(krampusWait is null
                    ? Loc.F($"👹 **/krampus**: ¡listo! ({ChristmasRules.KrampusMinGifts}–{ChristmasRules.KrampusMaxGifts} Regalos)")
                    : Loc.F($"👹 **/krampus**: {TimeFormat.Remaining(krampusWait.Value)}"));
            }
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

        var buttons = new ComponentBuilder();
        if (phase == ChristmasPhase.Farm && today > 0 && !claimed.Contains(today))
        {
            buttons.WithButton(Loc.F($"Abrir el día {today}"), $"{ClaimPrefix}:{discordId}", ButtonStyle.Success, new Emoji("🎁"));
        }

        if (phase == ChristmasPhase.Farm)
        {
            buttons.WithButton(Loc.T("Muñeco"), $"{SnowmanModule.PanelPrefix}:{discordId}", ButtonStyle.Primary, new Emoji("☃️"));
        }

        buttons.WithButton(Loc.T("Tienda"), $"{ShopPrefix}:{discordId}", ButtonStyle.Primary, new Emoji("🛒"));
        return new ChristmasView(embed.Build(), buttons.Build());
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

        if (day.Bells > 0)
        {
            lines.Add(Loc.F($"🔔 **Campanas**: +{day.Bells} (usalas con aa campana)"));
        }

        if (outcome.Sock is { } sock)
        {
            lines.Add(Loc.F($"🧦 **Calcetín**: +{GameHistory.Number(sock.Reward.Gold)} de oro, +{GameHistory.Number(sock.Reward.Xp)} de EXP y {GameHistory.Number(ChristmasShop.SockDust)} de Polvo"));
            if (sock.Reward.BoxName is not null)
            {
                lines.Add(Loc.F($"{ItemEmojiRegistry.Label(sock.Reward.BoxName, "📦")} — abrila con **/open**"));
            }
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

        if (day.Bells > 0)
        {
            parts.Add(Loc.F($"{day.Bells} Campanas"));
        }

        if (day.Sock)
        {
            parts.Add(Loc.T("un Calcetín"));
        }

        return string.Join(" + ", parts);
    }
}
