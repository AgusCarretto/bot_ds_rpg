using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

public class DailyModule(IUserRepository userRepository, IProgressionRepository progressionRepository, IZoneRepository zoneRepository, IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /daily
    [SlashCommand("daily", "Reclamá tu recompensa diaria: más oro y EXP con la racha, y cajas desde el día 6.")]
    public async Task HandleDailyAsync()
    {
        await DeferAsync();

        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);

            // El premio escala con la zona (y el nivel): mismo criterio que las misiones y los logros.
            int zoneRank = await MissionsModule.ResolveZoneRankAsync(zoneRepository, player.CurrentZoneId);
            var outcome = await progressionRepository.ClaimDailyAsync(Context.User.Id, zoneRank);
            var calculation = outcome.Calculation;

            if (calculation.Status == DailyClaimStatus.TooSoon)
            {
                await FollowupAsync(embed: BuildTooSoonEmbed(calculation), ephemeral: true);
                return;
            }

            await gameEvents.RecordVictoryAsync(Context.User.Id, GameEventKinds.DailyClaim, outcome.Result!);

            await FollowupAsync(embed: BuildResultEmbed(calculation, outcome.Result!, outcome.Receipt!));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude procesar tu recompensa diaria, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Público para que Modules/TextCommandModule.cs arme el mismo embed en "aa daily".
    public static Embed BuildTooSoonEmbed(DailyClaimCalculation calculation)
    {
        return new EmbedBuilder()
            .WithTitle("⏳ Todavía no podés reclamar")
            .WithDescription($"Te falta **{TimeFormat.Remaining(calculation.RemainingCooldown!.Value)}** para tu próximo /daily.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    // result ya no se usa para armar el mensaje (la subida de nivel sale aparte, ver GameData/LevelUpCard.cs); se conserva en la firma porque
    // la llaman el slash y el texto con el mismo resultado. receipt (v0.18.2): lo que se pagó de verdad, con la zona y el nivel del jugador ya aplicados.
    public static Embed BuildResultEmbed(DailyClaimCalculation calculation, LevelUpOutcome result, RewardReceipt receipt)
    {
        var reward = receipt.Reward;
        int day = calculation.NewStreak;

        var embed = new EmbedBuilder()
            .WithTitle("🎁 ¡Recompensa diaria reclamada!")
            .WithColor(calculation.Status == DailyClaimStatus.StreakReset ? Color.Orange : Color.Gold)
            .AddField("🔥 Racha", $"Día {day}", true)
            .AddField("💰 Oro ganado", GameHistory.Number(reward.Gold), true)
            .AddField("📊 EXP ganada", GameHistory.Number(reward.Xp), true);

        if (reward.BoxName is not null)
        {
            embed.AddField("📦 Caja", $"{(reward.BoxQuantity > 1 ? $"{reward.BoxQuantity}× " : string.Empty)}**{reward.BoxName}** — abrila con **/open**", false);
        }

        // Una línea de festejo si es un hito de la racha (o el aviso de que se perdió), y qué viene mañana si trae algo especial.
        var lines = new List<string>();
        if (calculation.Status == DailyClaimStatus.StreakReset)
        {
            lines.Add("⚠️ ¡Perdiste tu racha! Volvemos al Día 1.");
        }
        else if (DailyRewardCalculator.IsWeekMilestone(day))
        {
            int weeks = day / DailyRewardCalculator.WeekLength;
            lines.Add(weeks == 1 ? "🏆 **¡Una semana seguida!** Caja grande y monedas extra." : $"🏆 **¡{weeks} semanas seguidas!** Caja grande y monedas extra.");
        }
        else if (DailyRewardCalculator.IsThreeDayBonus(day))
        {
            lines.Add($"🎉 **¡{day} días seguidos!** Monedas extra de regalo.");
        }

        if (DailyRewardCalculator.NextDayHint(day) is { } hint)
        {
            lines.Add(hint);
        }

        if (lines.Count > 0)
        {
            embed.WithDescription(string.Join("\n\n", lines));
        }

        return embed.Build();
    }
}
