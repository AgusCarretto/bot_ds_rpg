using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class DailyModule(IUserRepository userRepository, IProgressionRepository progressionRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /daily
    [SlashCommand("daily", "Reclamá tu recompensa diaria (la racha multiplica la recompensa hasta el día 10).")]
    public async Task HandleDailyAsync()
    {
        await DeferAsync();

        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            await userRepository.GetOrCreateUserAsync(Context.User.Id);

            var outcome = await progressionRepository.ClaimDailyAsync(Context.User.Id);
            var calculation = outcome.Calculation;

            if (calculation.Status == DailyClaimStatus.TooSoon)
            {
                await FollowupAsync(embed: BuildTooSoonEmbed(calculation), ephemeral: true);
                return;
            }

            await FollowupAsync(embed: BuildResultEmbed(calculation, outcome.Result!));
        }
        catch (Exception)
        {
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

    public static Embed BuildResultEmbed(DailyClaimCalculation calculation, LevelUpOutcome result)
    {
        var player = result.Player;

        var embed = new EmbedBuilder()
            .WithTitle("🎁 ¡Recompensa diaria reclamada!")
            .WithColor(calculation.Status == DailyClaimStatus.StreakReset ? Color.Orange : Color.Gold)
            .AddField("🔥 Racha", $"Día {calculation.NewStreak}", true)
            .AddField("💰 Oro ganado", calculation.GoldReward.ToString(), true)
            .AddField("📊 EXP ganada", calculation.XpReward.ToString(), true);

        if (calculation.Status == DailyClaimStatus.StreakReset)
        {
            embed.WithDescription("⚠️ ¡Perdiste tu racha! Volvemos al Día 1.");
        }

        if (result.LevelsGained > 0)
        {
            embed.AddField("🎉 ¡Subiste de nivel!", $"Ahora sos nivel **{player.Level}**.", false);
        }

        return embed.Build();
    }
}
