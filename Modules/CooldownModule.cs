using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class CooldownModule(ICooldownRepository cooldownRepository, IUserRepository userRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("cd", "Mostrá el estado de tus cooldowns (cazar, viajar, talar, minar y diario).")]
    public async Task HandleCooldownsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var embed = await BuildStatusEmbedAsync(cooldownRepository, userRepository, Context.User.Id);
            await FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude consultar tus cooldowns ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs use exactamente
    // la misma lógica en "aa cd" — acá vive tanto el cálculo como el embed, no hay nada más que compartir.
    public static async Task<Embed> BuildStatusEmbedAsync(ICooldownRepository cooldownRepository, IUserRepository userRepository, ulong discordId)
    {
        var lines = new List<string>();

        foreach (var definition in CooldownCatalog.All)
        {
            var remaining = await cooldownRepository.GetRemainingAsync(discordId, definition.CommandName, definition.Duration);
            string status = remaining is null
                ? "**¡Listo!** ✅"
                : $"{TimeFormat.Remaining(remaining.Value)} restantes";

            lines.Add($"{definition.Emoji} **{definition.DisplayName}**: {status}");
        }

        // /daily no usa la tabla cooldowns (tiene su propia columna last_daily_claim con
        // ventana de 24h/48h), así que lo evaluamos aparte con la misma lógica pura de /daily.
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var dailyCalculation = DailyRewardCalculator.Evaluate(player.LastDailyClaim, player.DailyStreak, DateTime.UtcNow);
        string dailyStatus = dailyCalculation.Status == DailyClaimStatus.TooSoon
            ? $"{TimeFormat.Remaining(dailyCalculation.RemainingCooldown!.Value)} restantes"
            : "**¡Listo!** ✅";
        lines.Add($"🎁 **Diario**: {dailyStatus}");

        return new EmbedBuilder()
            .WithTitle("⏱️ Tus cooldowns")
            .WithColor(Color.Teal)
            .WithDescription(string.Join('\n', lines))
            .Build();
    }
}
