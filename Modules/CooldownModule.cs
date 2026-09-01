using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class CooldownModule(ICooldownRepository cooldownRepository) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("cd", "Mostrá el estado de tus cooldowns (cazar, viajar, talar y minar).")]
    public async Task HandleCooldownsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var lines = new List<string>();

            foreach (var definition in CooldownCatalog.All)
            {
                var remaining = await cooldownRepository.GetRemainingAsync(Context.User.Id, definition.CommandName, definition.Duration);
                string status = remaining is null
                    ? "**¡Listo!** ✅"
                    : $"{TimeFormat.Remaining(remaining.Value)} restantes";

                lines.Add($"{definition.Emoji} **{definition.DisplayName}**: {status}");
            }

            var embed = new EmbedBuilder()
                .WithTitle("⏱️ Tus cooldowns")
                .WithColor(Color.Teal)
                .WithDescription(string.Join('\n', lines))
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude consultar tus cooldowns ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
