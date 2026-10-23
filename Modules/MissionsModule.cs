using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Una misión tal como se le muestra a un jugador: la meta, cuánto lleva, si ya la cobró y qué premio le toca HOY (según su zona y nivel).
public sealed record MissionRow(MissionTemplate Mission, long Progress, bool Claimed, ResolvedReward Reward)
{
    public bool Completed => Progress >= Mission.Target;
    public bool Claimable => Completed && !Claimed;
}

// Las misiones de un día o de una semana, con el premio por completar todas.
public sealed record MissionPeriodState(
    MissionPeriod Period, DateTime StartUtc, DateTime EndUtc, IReadOnlyList<MissionRow> Rows, bool BonusClaimed, ResolvedReward BonusReward)
{
    public bool AllCompleted => Rows.Count > 0 && Rows.All(r => r.Completed);
    public bool BonusClaimable => AllCompleted && !BonusClaimed;
    public int ClaimableCount => Rows.Count(r => r.Claimable) + (BonusClaimable ? 1 : 0);
}

public sealed record MissionsView(Embed Embed, MessageComponent? Components, int ClaimableCount);

public sealed record MissionClaimResult(string? PlainMessage, Embed? Embed);

// /missions: las misiones del día (3) y de la semana (2), y el botón para cobrar lo que ya completaste. Qué misiones tocan es una
// función pura del día (GameData/MissionCatalog.cs, las mismas para todos); el progreso sale del registro de eventos y lo único que
// se guarda es qué ya se cobró (Repositories/MissionRepository.cs). Se reinician a medianoche hora de Uruguay.
public class MissionsModule(
    IUserRepository userRepository, IZoneRepository zoneRepository, IMissionRepository missionRepository, IGameEvents gameEvents, IZoneBoxService zoneBoxService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("missions", "Tus misiones del día y de la semana (se reinician a medianoche, hora de Uruguay).")]
    public async Task HandleMissionsAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildViewAsync(userRepository, zoneRepository, missionRepository, zoneBoxService, Context.User.Id, DateTime.UtcNow);
            await FollowupAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude mostrar tus misiones, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // El botón "Reclamar": el id lleva a quién pertenece la pantalla, así que otro jugador no puede cobrar con la tuya.
    [ComponentInteraction("missions_claim:*")]
    public async Task HandleClaimButtonAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esas misiones son de otra persona: usá **/missions** para ver las tuyas."), ephemeral: true);
                return;
            }

            var result = await ExecuteClaimAsync(userRepository, zoneRepository, missionRepository, gameEvents, Context.User.Id, DateTime.UtcNow);
            var view = await BuildViewAsync(userRepository, zoneRepository, missionRepository, zoneBoxService, Context.User.Id, DateTime.UtcNow);

            // La pantalla se actualiza en su lugar (sin el botón si ya no queda nada) y el recibo sale aparte.
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
            await FollowupAsync(Loc.T("¡Upa! No pude cobrar las misiones, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa missions" (sin Context) ----

    public static async Task<MissionsView> BuildViewAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IMissionRepository missionRepository, IZoneBoxService zoneBoxService, ulong discordId, DateTime utcNow)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        int zoneRank = await ResolveZoneRankAsync(zoneRepository, player.CurrentZoneId);
        var boxes = await zoneBoxService.GetAsync();

        var states = new List<MissionPeriodState>();
        foreach (var period in new[] { MissionPeriod.Daily, MissionPeriod.Weekly })
        {
            var (start, end) = PeriodBounds(period, utcNow);
            var missions = MissionCatalog.ForPeriod(period, start);
            var progress = await missionRepository.GetProgressAsync(discordId, start, missions.Select(m => m.Kind).Distinct().ToArray());
            var claimed = await missionRepository.GetClaimedAsync(discordId, period, start);

            var rows = missions
                .Select(m => new MissionRow(m, progress.GetValueOrDefault(m.Kind), claimed.Contains(m.Key), MissionRewards.Resolve(m.Reward, zoneRank, player.Level, boxes)))
                .ToList();

            states.Add(new MissionPeriodState(
                period, start, end, rows, claimed.Contains(MissionCatalog.BonusKey),
                MissionRewards.Resolve(MissionCatalog.BonusFor(period), zoneRank, player.Level, boxes)));
        }

        int claimable = states.Sum(s => s.ClaimableCount);
        return new MissionsView(BuildEmbed(states, utcNow), BuildComponents(discordId, claimable), claimable);
    }

    // Cobra TODO lo que esté listo: cada misión completada y, si quedaron todas cobradas, el premio por completarlas. Cada cobro es
    // su propia transacción atómica (ver MissionRepository), así que un error en uno no deshace los demás.
    public static async Task<MissionClaimResult> ExecuteClaimAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IMissionRepository missionRepository, IGameEvents gameEvents,
        ulong discordId, DateTime utcNow)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        int zoneRank = await ResolveZoneRankAsync(zoneRepository, player.CurrentZoneId);

        var totals = new RewardTotals();
        var lines = new List<string>();

        foreach (var period in new[] { MissionPeriod.Daily, MissionPeriod.Weekly })
        {
            var (start, end) = PeriodBounds(period, utcNow);
            var missions = MissionCatalog.ForPeriod(period, start);
            var progress = await missionRepository.GetProgressAsync(discordId, start, missions.Select(m => m.Kind).Distinct().ToArray());
            var claimed = (await missionRepository.GetClaimedAsync(discordId, period, start)).ToHashSet();

            foreach (var mission in missions)
            {
                if (claimed.Contains(mission.Key) || progress.GetValueOrDefault(mission.Kind) < mission.Target)
                {
                    continue;
                }

                var outcome = await missionRepository.ClaimMissionAsync(discordId, mission, start, end, zoneRank);
                if (outcome.Status != ClaimStatus.Claimed)
                {
                    continue; // otra pantalla la cobró justo antes (o venció): no se paga dos veces
                }

                claimed.Add(mission.Key);
                totals.Add(outcome.Receipt!);
                lines.Add($"✅ {Loc.Tr(mission.Title)}");
                await gameEvents.RecordAsync(discordId, GameEventKinds.MissionClaimed, player.CurrentZoneId, detail: mission.Key);
            }

            if (!claimed.Contains(MissionCatalog.BonusKey) && missions.Count > 0 && missions.All(m => claimed.Contains(m.Key)))
            {
                var bonus = await missionRepository.ClaimBonusAsync(discordId, period, missions, start, end, zoneRank);
                if (bonus.Status == ClaimStatus.Claimed)
                {
                    totals.Add(bonus.Receipt!);
                    lines.Add(period == MissionPeriod.Daily ? Loc.T("🎁 Completaste todas las diarias") : Loc.T("🎁 Completaste todas las semanales"));
                    await gameEvents.RecordAsync(
                        discordId, GameEventKinds.MissionClaimed, player.CurrentZoneId, detail: $"{MissionCatalog.BonusKey}:{MissionCatalog.PeriodKey(period)}");
                }
            }
        }

        if (totals.IsEmpty)
        {
            return new MissionClaimResult(Loc.T("No tenés misiones listas para reclamar ahora. Mirá tu progreso con **/missions**."), null);
        }

        if (totals.LevelsGained > 0)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.LevelUp, player.CurrentZoneId, totals.LevelsGained, totals.NewLevel.ToString());
        }

        return new MissionClaimResult(null, BuildReceiptEmbed(lines, totals));
    }

    // Público y puro: se prueba sin Discord.
    public static Embed BuildEmbed(IReadOnlyList<MissionPeriodState> states, DateTime utcNow)
    {
        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("📋 Tus misiones"))
            .WithColor(Color.Gold)
            .WithDescription(Loc.T("Las diarias se reinician a medianoche y las semanales el lunes, **hora de Uruguay**. Se cuentan solas mientras jugás; los premios dependen de tu zona."));

        foreach (var state in states)
        {
            string left = UruguayCalendar.FormatRemaining(state.EndUtc - utcNow);
            string title = state.Period == MissionPeriod.Daily ? Loc.F($"☀️ Diarias — se reinician en {left}") : Loc.F($"📅 Semanales — se reinician en {left}");
            embed.AddField(title, string.Join("\n\n", FieldLines(state)));
        }

        int claimable = states.Sum(s => s.ClaimableCount);
        embed.WithFooter(claimable > 0 ? Loc.F($"🎁 Tenés {claimable} para reclamar: tocá el botón.") : Loc.T("Cada misión se cuenta sola mientras jugás."));

        return embed.Build();
    }

    // El botón de reclamar, solo si hay algo para cobrar (si no, la pantalla queda sin botones).
    public static MessageComponent? BuildComponents(ulong ownerId, int claimable) =>
        claimable <= 0
            ? null
            : new ComponentBuilder().WithButton(Loc.F($"Reclamar ({claimable})"), $"missions_claim:{ownerId}", ButtonStyle.Success, new Emoji("🎁")).Build();

    private static List<string> FieldLines(MissionPeriodState state)
    {
        var lines = new List<string>();

        foreach (var row in state.Rows)
        {
            if (row.Claimed)
            {
                lines.Add(Loc.F($"✅ ~~{Loc.Tr(row.Mission.Title)}~~ — cobrada"));
            }
            else if (row.Completed)
            {
                lines.Add(Loc.F($"🎁 **{Loc.Tr(row.Mission.Title)}** — ¡lista para reclamar!\n   {MissionRewards.Describe(row.Reward)}"));
            }
            else
            {
                int shown = (int)Math.Min(row.Progress, row.Mission.Target);
                lines.Add($"▫️ **{Loc.Tr(row.Mission.Title)}**\n   {ProgressBar.Render(shown, (int)row.Mission.Target)} {shown}/{row.Mission.Target} · {MissionRewards.Describe(row.Reward)}");
            }
        }

        // Una frase entera por período (no se arma pegando «diarias» / «semanales»): el inglés ordena distinto.
        string bonusText = state.Period == MissionPeriod.Daily
            ? Loc.F($"Completá las {state.Rows.Count} diarias: {MissionRewards.Describe(state.BonusReward)}")
            : Loc.F($"Completá las {state.Rows.Count} semanales: {MissionRewards.Describe(state.BonusReward)}");
        if (state.BonusClaimed)
        {
            lines.Add(Loc.F($"✅ ~~{bonusText}~~ — cobrado"));
        }
        else if (state.AllCompleted)
        {
            lines.Add(Loc.F($"🎁 **{bonusText}** — ¡listo para reclamar!"));
        }
        else
        {
            lines.Add($"🔒 {bonusText} ({state.Rows.Count(r => r.Completed)}/{state.Rows.Count})");
        }

        return lines;
    }

    private static Embed BuildReceiptEmbed(IReadOnlyList<string> lines, RewardTotals totals)
    {
        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🎁 ¡Premios reclamados!"))
            .WithColor(Color.Green)
            .WithDescription($"{string.Join('\n', lines)}\n\n**Total:** {totals.Describe()}")
            .AddField(Loc.T("💰 Tu oro ahora"), totals.GoldAfter.ToString(), true);

        // La subida de nivel sale como mensaje propio (GameData/LevelUpCard.cs), no como una línea más acá.
        return embed.Build();
    }

    private static (DateTime Start, DateTime End) PeriodBounds(MissionPeriod period, DateTime utcNow) =>
        period == MissionPeriod.Daily
            ? (UruguayCalendar.DayStartUtc(utcNow), UruguayCalendar.NextDayStartUtc(utcNow))
            : (UruguayCalendar.WeekStartUtc(utcNow), UruguayCalendar.NextWeekStartUtc(utcNow));

    // La posición de la zona del jugador por dificultad (0 si no se conoce: los premios la toman como la primera).
    internal static async Task<int> ResolveZoneRankAsync(IZoneRepository zoneRepository, int zoneId) =>
        ZoneRanking.RankOf(ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync()), zoneId);
}
