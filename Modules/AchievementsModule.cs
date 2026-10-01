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
    [SlashCommand("achievements", "Tus logros: cazador, herrero, coleccionista... con su progreso y premios.")]
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

    // El id del botón lleva a quién pertenece la pantalla: otro jugador no puede cobrar con la tuya.
    [ComponentInteraction("achievements_claim:*")]
    public async Task HandleClaimButtonAsync(string ownerRaw)
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
            var view = await BuildViewAsync(eventRepository, achievementRepository, Context.User.Id);

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

    public static async Task<AchievementsView> BuildViewAsync(
        IGameEventRepository eventRepository, IAchievementRepository achievementRepository, ulong discordId)
    {
        var rows = await LoadRowsAsync(eventRepository, achievementRepository, discordId);
        int claimable = rows.Sum(r => r.ClaimableTiers.Count);

        return new AchievementsView(BuildEmbed(rows), BuildComponents(discordId, claimable), claimable);
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

        if (totals.LevelsGained > 0)
        {
            embed.AddField("🆙 ¡Subiste de nivel!", $"Ahora sos nivel **{totals.NewLevel}**.", true);
        }

        return new AchievementClaimResult(null, embed.Build());
    }

    // Público y puro: se prueba sin Discord. Dos campos de cinco logros (un campo de Discord tiene tope de 1024 caracteres).
    public static Embed BuildEmbed(IReadOnlyList<AchievementRow> rows)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🏆 Tus logros")
            .WithColor(Color.Gold)
            .WithDescription("Cada logro tiene tres tramos. ✅ cobrado · 🎁 listo para reclamar · 🔒 todavía no. Cuentan desde que se activó el registro de eventos.");

        int half = (rows.Count + 1) / 2;
        embed.AddField("Logros", string.Join("\n\n", rows.Take(half).Select(Line)));
        if (rows.Count > half)
        {
            // Discord no deja un campo con el nombre vacío: se usa un espacio de ancho cero (U+200B) para que la segunda columna no
            // lleve título. Va como número y no escrito, porque ese carácter es invisible en el código.
            embed.AddField(((char)0x200B).ToString(), string.Join("\n\n", rows.Skip(half).Select(Line)));
        }

        int claimable = rows.Sum(r => r.ClaimableTiers.Count);
        embed.WithFooter(claimable > 0 ? $"🎁 Tenés {claimable} para reclamar: tocá el botón." : "Seguí jugando: se suman solos.");

        return embed.Build();
    }

    public static MessageComponent? BuildComponents(ulong ownerId, int claimable) =>
        claimable <= 0
            ? null
            : new ComponentBuilder().WithButton($"Reclamar ({claimable})", $"achievements_claim:{ownerId}", ButtonStyle.Success, new Emoji("🏆")).Build();

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
