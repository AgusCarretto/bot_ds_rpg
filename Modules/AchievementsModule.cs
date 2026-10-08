using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Un logro tal como se le muestra a un jugador: cuánto lleva del contador y qué tramos ya cobró.
public sealed record AchievementRow(AchievementDefinition Definition, long Value, IReadOnlySet<int> ClaimedTiers)
{
    public int Reached => AchievementCatalog.TiersReached(Definition, Value);

    // Los tramos desbloqueados que todavía no cobró (de menor a mayor).
    public IReadOnlyList<int> ClaimableTiers => Enumerable.Range(1, Reached).Where(t => !ClaimedTiers.Contains(t)).ToList();
}

public sealed record AchievementsView(Embed Embed, MessageComponent? Components, int ClaimableCount);

public sealed record AchievementClaimResult(string? PlainMessage, Embed? Embed);

// /achievements: los logros por tramos (Cazador I-III, Herrero, Coleccionista...) con el progreso hacia el próximo, y el botón para cobrar
// los que ya se desbloquearon. El logro no tiene estado propio: es "el contador de player_stats llegó a tal número"
// (GameData/AchievementCatalog.cs), y lo único que se guarda es qué tramos ya se cobraron (achievement_claims).
public class AchievementsModule(
    IUserRepository userRepository, IZoneRepository zoneRepository, IGameEventRepository eventRepository,
    IAchievementRepository achievementRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("achievements", "Tus logros: cazador, herrero, recolector... con su progreso y premios.")]
    public async Task HandleAchievementsAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildViewAsync(eventRepository, achievementRepository, Context.User.Id);
            await FollowupAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude mostrar tus logros, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Los ids de los controles llevan a quién pertenece la pantalla: otro jugador no puede cobrar ni cambiar de página con la tuya.
    // El desplegable de páginas ("ach_page:{dueño}") y el botón de reclamar ("ach_claim:{dueño}:{página}", así vuelve a la misma página);
    // "achievements_claim:{dueño}" es el botón de antes de las páginas, que sigue andando en los mensajes que ya estaban publicados.
    [ComponentInteraction("ach_page:*")]
    public async Task HandlePageAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos logros son de otra persona: usá **/achievements** para ver los tuyos.", ephemeral: true);
                return;
            }

            int page = int.TryParse(selected.FirstOrDefault(), out int parsed) ? parsed : 0;
            var view = await BuildViewAsync(eventRepository, achievementRepository, Context.User.Id, page);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = view.Embed;
                p.Components = view.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir esa página de logros, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("ach_claim:*:*")]
    public Task HandleClaimPageButtonAsync(string ownerRaw, string pageRaw) =>
        ClaimAndRefreshAsync(ownerRaw, int.TryParse(pageRaw, out int page) ? page : 0);

    [ComponentInteraction("achievements_claim:*")]
    public Task HandleClaimButtonAsync(string ownerRaw) => ClaimAndRefreshAsync(ownerRaw, 0);

    private async Task ClaimAndRefreshAsync(string ownerRaw, int page)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos logros son de otra persona: usá **/achievements** para ver los tuyos.", ephemeral: true);
                return;
            }

            var result = await ExecuteClaimAsync(userRepository, zoneRepository, eventRepository, achievementRepository, gameEvents, Context.User.Id);
            var view = await BuildViewAsync(eventRepository, achievementRepository, Context.User.Id, page);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = view.Embed;
                p.Components = view.Components ?? new ComponentBuilder().Build();
            });
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude cobrar los logros, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa achievements" (sin Context) ----

    // page: la página por tema que se muestra (0 = Combate...; AchievementCatalog.Categories). El botón de reclamar cobra los de TODAS las páginas.
    public static async Task<AchievementsView> BuildViewAsync(
        IGameEventRepository eventRepository, IAchievementRepository achievementRepository, ulong discordId, int page = 0)
    {
        var rows = await LoadRowsAsync(eventRepository, achievementRepository, discordId);
        int claimable = rows.Sum(r => r.ClaimableTiers.Count);
        page = Math.Clamp(page, 0, AchievementCatalog.Categories.Count - 1);

        return new AchievementsView(BuildEmbed(rows, page), BuildComponents(discordId, rows, page), claimable);
    }

    // Cobra todos los tramos desbloqueados. Cada uno es su propia transacción atómica (ver AchievementRepository).
    public static async Task<AchievementClaimResult> ExecuteClaimAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IGameEventRepository eventRepository,
        IAchievementRepository achievementRepository, IGameEvents gameEvents, ulong discordId)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        int zoneRank = await MissionsModule.ResolveZoneRankAsync(zoneRepository, player.CurrentZoneId);

        var rows = await LoadRowsAsync(eventRepository, achievementRepository, discordId);
        var totals = new RewardTotals();
        var lines = new List<string>();

        foreach (var row in rows)
        {
            foreach (int tier in row.ClaimableTiers)
            {
                var outcome = await achievementRepository.ClaimTierAsync(discordId, row.Definition, tier, zoneRank);
                if (outcome.Status != ClaimStatus.Claimed)
                {
                    continue; // otra pantalla lo cobró justo antes: no se paga dos veces
                }

                totals.Add(outcome.Receipt!);
                lines.Add($"🏆 {row.Definition.Emoji} {AchievementCatalog.TierName(row.Definition, tier)}");
                await gameEvents.RecordAsync(discordId, GameEventKinds.AchievementUnlocked, player.CurrentZoneId, detail: $"{row.Definition.Key}:{tier}");
            }
        }

        if (totals.IsEmpty)
        {
            return new AchievementClaimResult("No tenés logros listos para reclamar ahora. Mirá tu progreso con **/achievements**.", null);
        }

        if (totals.LevelsGained > 0)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.LevelUp, player.CurrentZoneId, totals.LevelsGained, totals.NewLevel.ToString());
        }

        var embed = new EmbedBuilder()
            .WithTitle("🏆 ¡Logros reclamados!")
            .WithColor(Color.Gold)
            .WithDescription($"{string.Join('\n', lines)}\n\n**Total:** {totals.Describe()}")
            .AddField("💰 Tu oro ahora", totals.GoldAfter.ToString(), true);

        // La subida de nivel sale como mensaje propio (GameData/LevelUpCard.cs), no como una línea más acá.
        return new AchievementClaimResult(null, embed.Build());
    }

    // Público y puro: se prueba sin Discord. Una página por tema (Combate, Oficios, Economía, Constancia): con los logros de v0.9.0 ya eran demasiados
    // para una sola pantalla (un campo de Discord tiene tope de 1024 caracteres y el mensaje tiene que respirar). Un logro por bloque, separados por una línea.
    public static Embed BuildEmbed(IReadOnlyList<AchievementRow> rows, int page = 0)
    {
        page = Math.Clamp(page, 0, AchievementCatalog.Categories.Count - 1);
        var category = AchievementCatalog.Categories[page];
        var shown = category.Keys.Select(k => rows.FirstOrDefault(r => r.Definition.Key == k)).OfType<AchievementRow>().ToList();

        var embed = new EmbedBuilder()
            .WithTitle($"🏆 Tus logros — {category.Emoji} {category.Name}")
            .WithColor(Color.Gold)
            .WithDescription("Cada logro tiene tres tramos. ✅ cobrado · 🎁 listo para reclamar · 🔒 todavía no. Cuentan desde que se activó el registro de eventos.")
            .AddField("Logros", string.Join("\n\n", shown.Select(Line)));

        int claimable = rows.Sum(r => r.ClaimableTiers.Count);
        string tail = claimable > 0 ? $"🎁 Tenés {claimable} para reclamar en total: tocá el botón." : "Seguí jugando: se suman solos.";
        embed.WithFooter($"Página {page + 1}/{AchievementCatalog.Categories.Count} · {tail}");

        return embed.Build();
    }

    // El desplegable para cambiar de página (siempre) y, debajo, el botón de reclamar si hay algo listo. Cada página dice cuántos tramos tiene para reclamar.
    public static MessageComponent BuildComponents(ulong ownerId, IReadOnlyList<AchievementRow> rows, int page)
    {
        page = Math.Clamp(page, 0, AchievementCatalog.Categories.Count - 1);

        var menu = new SelectMenuBuilder().WithCustomId($"ach_page:{ownerId}").WithPlaceholder("Elegí una página de logros");
        for (int i = 0; i < AchievementCatalog.Categories.Count; i++)
        {
            var category = AchievementCatalog.Categories[i];
            var inCategory = rows.Where(r => category.Keys.Contains(r.Definition.Key)).ToList();
            int ready = inCategory.Sum(r => r.ClaimableTiers.Count);
            int done = inCategory.Sum(r => r.ClaimedTiers.Count);
            int total = inCategory.Sum(r => r.Definition.Tiers.Count);

            // El emoji va en el texto de la opción y no como emote de la opción (regla del proyecto: un emoji que Discord rechaza tira abajo el mensaje entero).
            menu.AddOption($"{category.Emoji} {category.Name}", i.ToString(), ready > 0 ? $"🎁 {ready} para reclamar" : $"{done}/{total} tramos cobrados", isDefault: i == page);
        }

        var components = new ComponentBuilder().WithSelectMenu(menu, row: 0);

        int claimable = rows.Sum(r => r.ClaimableTiers.Count);
        if (claimable > 0)
        {
            components.WithButton($"Reclamar ({claimable})", $"ach_claim:{ownerId}:{page}", ButtonStyle.Success, new Emoji("🏆"), row: 1);
        }

        return components.Build();
    }

    // Dos renglones por logro: los tramos y la barra hacia el próximo.
    private static string Line(AchievementRow row)
    {
        var tiers = string.Join("  ", row.Definition.Tiers.Select((_, i) =>
        {
            int tier = i + 1;
            string mark = row.ClaimedTiers.Contains(tier) ? "✅" : tier <= row.Reached ? "🎁" : "🔒";
            return $"{AchievementCatalog.Roman(tier)} {mark}";
        }));

        string progress;
        if (row.Reached >= row.Definition.Tiers.Count)
        {
            progress = $"✨ ¡Completo! ({row.Value})";
        }
        else
        {
            int next = (int)row.Definition.Tiers[row.Reached].Threshold;
            int shown = (int)Math.Min(row.Value, next);
            progress = $"{ProgressBar.Render(shown, next)} {shown}/{next}";
        }

        return $"{row.Definition.Emoji} **{row.Definition.Name}** — {tiers}\n   {progress}";
    }

    private static async Task<IReadOnlyList<AchievementRow>> LoadRowsAsync(
        IGameEventRepository eventRepository, IAchievementRepository achievementRepository, ulong discordId)
    {
        var stats = await eventRepository.GetStatsAsync(discordId);
        var claimed = await achievementRepository.GetClaimedTiersAsync(discordId);

        return AchievementCatalog.All
            .Select(a => new AchievementRow(
                a, stats.GetValueOrDefault(a.StatKind), claimed.TryGetValue(a.Key, out var tiers) ? tiers : new HashSet<int>()))
            .ToList();
    }
}
