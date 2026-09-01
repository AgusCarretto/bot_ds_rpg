using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class TavernModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    private const int HealGoldCost = 10;
    private const int HealHpRestored = 30;

    // Comando barra: /heal
    [SlashCommand("heal", "Pagá oro por algo de comer y recuperá HP (10 de oro = 30 HP).")]
    public async Task HandleHealAsync()
    {
        await DeferAsync();

        try
        {
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);

            if (player.CurrentHp >= player.MaxHp)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("❤️ Ya estás al máximo")
                    .WithDescription($"Tenés {player.CurrentHp}/{player.MaxHp} HP, no necesitás curarte todavía.")
                    .WithColor(Color.Green)
                    .Build(), ephemeral: true);
                return;
            }

            var healed = await userRepository.HealAsync(Context.User.Id, HealGoldCost, HealHpRestored);

            if (healed is null)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("💸 No te alcanza el oro")
                    .WithDescription($"Curarte cuesta **{HealGoldCost} de oro** y tenés **{player.Gold}**.")
                    .WithColor(Color.Red)
                    .Build(), ephemeral: true);
                return;
            }

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("🍖 ¡Buen provecho!")
                .WithDescription($"Comiste algo en la parrilla y recuperaste HP. Ahora tenés **{healed.CurrentHp}/{healed.MaxHp}** HP y **{healed.Gold}** de oro.")
                .WithColor(Color.Green)
                .Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude procesar la curación, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
