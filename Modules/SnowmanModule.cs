using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// El Muñeco de Nieve Gigante (v0.57.0, docs/navidad.md): el panel en vivo (vida, tu daño, las últimas tiradas y un botón por proyectil). Se abre desde /navidad (botón ☃️),
// con /munieco o con «aa muñeco». El panel se refresca solo cada pocos segundos durante un rato después de abrirlo o de tirar, así se ve la vida bajar mientras otros tiran.
public class SnowmanModule(
    IChristmasRepository christmasRepository,
    IChristmasService christmasService,
    ISnowmanService snowmanService) : InteractionModuleBase<SocketInteractionContext>
{
    public const string PanelPrefix = "snow_panel";
    private const string ThrowPrefix = "snow_throw";
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshFor = TimeSpan.FromMinutes(2);

    [SlashCommand("munieco", "Navidad: el Muñeco de Nieve Gigante. Tirale proyectiles con tus Regalos.")]
    public async Task HandleSnowmanAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildPanelAsync(christmasRepository, christmasService, snowmanService, Context.User.Id, DateTime.UtcNow);
            await FollowupAsync(embed: view.Embed, components: view.Components);
            StartLiveRefresh(view, () => BuildPanelAsync(christmasRepository, christmasService, snowmanService, Context.User.Id, DateTime.UtcNow),
                v => ModifyOriginalResponseAsync(p => { p.Embed = v.Embed; p.Components = v.Components; }));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir el Muñeco, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{PanelPrefix}:*")]
    public async Task HandlePanelAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Ese panel es de otra persona: abrí el tuyo con **/munieco**."), ephemeral: true);
                return;
            }

            var view = await BuildPanelAsync(christmasRepository, christmasService, snowmanService, ownerId, DateTime.UtcNow);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
            StartLiveRefresh(view, () => BuildPanelAsync(christmasRepository, christmasService, snowmanService, ownerId, DateTime.UtcNow),
                v => ModifyOriginalResponseAsync(p => { p.Embed = v.Embed; p.Components = v.Components; }));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir el Muñeco, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{ThrowPrefix}:*:*")]
    public async Task HandleThrowAsync(string ownerRaw, string projectileKey)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Ese panel es de otra persona: abrí el tuyo con **/munieco**."), ephemeral: true);
                return;
            }

            var result = await ThrowAsync(christmasService, snowmanService, ownerId, DisplayName(Context.User), projectileKey, 1, DateTime.UtcNow);
            if (result.Error is not null)
            {
                await FollowupAsync(result.Error, ephemeral: true);
            }

            var view = await BuildPanelAsync(christmasRepository, christmasService, snowmanService, ownerId, DateTime.UtcNow);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });

            // Si cayó, el anuncio sale para todo el canal.
            if (result.Kill is not null)
            {
                await FollowupAsync(embed: result.Kill);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude tirar, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    public static string DisplayName(IUser user) => (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username;

    public sealed record SnowmanView(Embed Embed, MessageComponent? Components = null);

    // Error: algo para avisar (sin Regalos, fuera del evento). Line: el renglón de la tirada. Kill: el anuncio si cayó.
    public sealed record SnowThrowView(string? Error, string? Line = null, Embed? Kill = null, SnowThrowOutcome? Outcome = null);

    // Tira (botón o «aa muñeco tirar petardo 5»). Solo del 1 al 24 de diciembre.
    public static async Task<SnowThrowView> ThrowAsync(
        IChristmasService christmas, ISnowmanService snowman, ulong discordId, string displayName, string? projectileKey, int count, DateTime utcNow)
    {
        if (ChristmasRules.PhaseAt(christmas.Settings, utcNow) != ChristmasPhase.Farm)
        {
            return new SnowThrowView(Loc.T("☃️ El Muñeco de Nieve Gigante solo está del 1 al 24 de diciembre."));
        }

        var projectile = SnowmanRules.Find(projectileKey);
        if (projectile is null)
        {
            return new SnowThrowView(Loc.F($"No conozco ese proyectil. Probá con: {string.Join(", ", SnowmanRules.Projectiles.Select(p => p.Key))}."));
        }

        var outcome = await snowman.ThrowAsync(discordId, displayName, projectile, count);
        return outcome.Status switch
        {
            SnowThrowStatus.NotEnoughGifts => new SnowThrowView(
                Loc.F($"No te alcanzan los Regalos: {outcome.Count} × {projectile.Name} cuesta **{GameHistory.Number((long)projectile.Price * outcome.Count)}**.")),
            SnowThrowStatus.Dead => new SnowThrowView(Loc.T("☃️ El Muñeco ya cayó y no vuelve hasta la próxima Navidad.")),
            _ => new SnowThrowView(
                null,
                Loc.F($"{projectile.Emoji} Tiraste {outcome.Count} × **{projectile.Name}**: −{GameHistory.Number(outcome.Damage)} de vida. Te quedan **{GameHistory.Number(outcome.GiftsAfter)}** Regalos."),
                outcome.Kill is null ? null : BuildKillEmbed(outcome.Kill),
                outcome),
        };
    }

    public static async Task<SnowmanView> BuildPanelAsync(
        IChristmasRepository christmasRepository, IChristmasService christmas, ISnowmanService snowman, ulong discordId, DateTime utcNow)
    {
        var phase = ChristmasRules.PhaseAt(christmas.Settings, utcNow);
        var embed = new EmbedBuilder().WithColor(new Color(0x5DADE2));
        if (phase != ChristmasPhase.Farm)
        {
            embed.WithTitle(Loc.T("☃️ El Muñeco de Nieve Gigante"))
                .WithDescription(Loc.T("☃️ El Muñeco de Nieve Gigante solo está del 1 al 24 de diciembre."));
            return new SnowmanView(embed.Build());
        }

        var state = await snowman.GetStateAsync(discordId);
        var gifts = await christmasRepository.GetAsync(discordId, christmas.Settings.Season);
        int segments = UiEmojis.Bar(UiEmojis.BarHp) is null ? 10 : GameModule.ProfileBarSegments;

        embed.WithTitle(state.Round == 1 ? Loc.T("☃️ El Muñeco de Nieve Gigante") : Loc.F($"☃️ El Muñeco de Nieve Gigante — vuelta {state.Round}"));
        var lines = new List<string>
        {
            ProgressBar.Render(state.Hp, state.MaxHp, segments, UiEmojis.BarHp),
            Loc.F($"❤️ **Vida**: {GameHistory.Number(state.Hp)} / {GameHistory.Number(state.MaxHp)}"),
            Loc.F($"👥 **Le pegaron**: {GameHistory.Number(state.Participants)}"),
        };
        if (state.Kills > 0)
        {
            lines.Add(state.Kills == 1 ? Loc.T("🏆 **Cayó**: 1 vez") : Loc.F($"🏆 **Cayó**: {state.Kills} veces"));
        }

        lines.Add(string.Empty);
        lines.Add(state.MyDamage > 0
            ? Loc.F($"💥 **Tu daño**: {GameHistory.Number(state.MyDamage)} (puesto {state.MyRank})")
            : Loc.T("💥 **Tu daño**: todavía no le pegaste"));
        lines.Add(Loc.F($"🎁 **Tus Regalos**: {GameHistory.Number(gifts.Balance)}"));
        embed.WithDescription(string.Join("\n", lines));

        var feed = snowman.Feed();
        if (feed.Count > 0)
        {
            embed.AddField(Loc.T("📣 Últimas tiradas"), string.Join("\n", feed.Select(f =>
                Loc.F($"{f.Projectile.Emoji} **{f.Name}**: {f.Count} × {f.Projectile.Name} (−{GameHistory.Number(f.Damage)})"))), false);
        }

        embed.AddField(Loc.T("🎯 Proyectiles"), string.Join("\n", SnowmanRules.Projectiles.Select(p =>
            Loc.F($"{p.Emoji} **{p.Name}**: {GameHistory.Number(p.Price)} 🎁 → {GameHistory.Number(p.Damage)} de daño"))), false);
        embed.AddField(Loc.T("🏅 Si cae"), Loc.F($"Todos los que le pegaron: {SnowmanRules.AllGifts} Regalos, {SnowmanRules.AllBox} y Polvo.\nTop 10: +{SnowmanRules.Top10Gifts} Regalos. Top 3: además, {SnowmanRules.TopBox}."), false);
        embed.WithFooter(Loc.T("Se actualiza solo · de a muchos: aa muñeco tirar petardo 5"));

        var buttons = new ComponentBuilder();
        foreach (var p in SnowmanRules.Projectiles)
        {
            buttons.WithButton(GameHistory.Number(p.Price), $"{ThrowPrefix}:{discordId}:{p.Key}", ButtonStyle.Primary, new Emoji(p.Emoji), disabled: gifts.Balance < p.Price, row: 0);
        }

        buttons.WithButton(Loc.T("Actualizar"), $"{PanelPrefix}:{discordId}", ButtonStyle.Secondary, new Emoji("🔄"), row: 1);
        buttons.WithButton(Loc.T("Volver"), $"xmas_back:{discordId}", ButtonStyle.Secondary, new Emoji("↩️"), row: 1);
        return new SnowmanView(embed.Build(), buttons.Build());
    }

    // El anuncio de que cayó: quién dio el último golpe, el top 10 (con mención: en un embed no notifica) y si vuelve.
    public static Embed BuildKillEmbed(SnowmanKill kill)
    {
        var lines = new List<string>
        {
            Loc.F($"<@{kill.KillerId}> le dio el último golpe a la vuelta {kill.Round}."),
            Loc.F($"Cobran **{GameHistory.Number(kill.Participants)}** jugadores: {SnowmanRules.AllGifts} Regalos, {SnowmanRules.AllBox} y Polvo cada uno."),
            string.Empty,
        };
        foreach (var r in kill.Top)
        {
            string medal = r.Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => "🎖️" };
            lines.Add(Loc.F($"{medal} <@{r.DiscordId}>: {GameHistory.Number(r.Damage)} de daño"));
        }

        lines.Add(string.Empty);
        lines.Add(kill.NextMaxHp is int next
            ? Loc.F($"😠 ¡Volvió más enojado! Vida nueva: **{GameHistory.Number(next)}**.")
            : Loc.T("☃️ Quedó en el suelo hasta la próxima Navidad."));

        return new EmbedBuilder()
            .WithTitle(Loc.T("☃️ ¡El Muñeco de Nieve Gigante cayó!"))
            .WithColor(new Color(0xF1C40F))
            .WithDescription(string.Join("\n", lines))
            .Build();
    }

    // El panel en vivo: durante RefreshFor, cada RefreshEvery se vuelve a armar y, si cambió, se edita el mensaje. Nunca tira (si Discord no deja editar, se corta).
    public static void StartLiveRefresh(SnowmanView first, Func<Task<SnowmanView>> build, Func<SnowmanView, Task> apply)
    {
        _ = Task.Run(async () =>
        {
            string last = first.Embed.Description ?? string.Empty;
            var until = DateTime.UtcNow + RefreshFor;
            try
            {
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(RefreshEvery);
                    var next = await build();
                    string now = (next.Embed.Description ?? string.Empty) + string.Join("|", next.Embed.Fields.Select(f => f.Value));
                    if (now != last)
                    {
                        last = now;
                        await apply(next);
                    }
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        });
    }
}
