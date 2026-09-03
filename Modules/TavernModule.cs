using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

public class TavernModule(IUserRepository userRepository, ICombatSessionService combatSessions) : InteractionModuleBase<SocketInteractionContext>
{
    private const int HealGoldCost = 10;
    private const int HealHpRestored = 30;

    // Comando barra: /heal
    [SlashCommand("heal", "Pagá oro por algo de comer y recuperá HP (10 de oro = 30 HP). No funciona en combate.")]
    public async Task HandleHealAsync()
    {
        await DeferAsync();

        try
        {
            var embed = await ExecuteHealAsync(userRepository, combatSessions, Context.User.Id);
            await FollowupAsync(embed: embed);
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude procesar la curación, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa heal".
    public static async Task<Embed> ExecuteHealAsync(IUserRepository userRepository, ICombatSessionService combatSessions, ulong discordId)
    {
        // /heal gasta oro, y no se puede gastar oro en pleno combate (ver Modules/UseModule.cs
        // para la alternativa gratuita con consumibles del inventario, que sí funciona en combate).
        if (combatSessions.Peek(discordId) is not null)
        {
            return new EmbedBuilder()
                .WithTitle("⚔️ No podés comprar comida en pleno combate")
                .WithDescription("Mientras estás peleando no se gasta oro. Usá **/use <ítem>** para curarte con un consumible de tu inventario.")
                .WithColor(Color.DarkGrey)
                .Build();
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (player.CurrentHp >= player.MaxHp)
        {
            return new EmbedBuilder()
                .WithTitle("❤️ Ya estás al máximo")
                .WithDescription($"Tenés {player.CurrentHp}/{player.MaxHp} HP, no necesitás curarte todavía.")
                .WithColor(Color.Green)
                .Build();
        }

        var healed = await userRepository.HealAsync(discordId, HealGoldCost, HealHpRestored);

        if (healed is null)
        {
            return new EmbedBuilder()
                .WithTitle("💸 No te alcanza el oro")
                .WithDescription($"Curarte cuesta **{HealGoldCost} de oro** y tenés **{player.Gold}**.")
                .WithColor(Color.Red)
                .Build();
        }

        return new EmbedBuilder()
            .WithTitle("🍖 ¡Buen provecho!")
            .WithDescription($"Comiste algo en la parrilla y recuperaste HP. Ahora tenés **{healed.CurrentHp}/{healed.MaxHp}** HP y **{healed.Gold}** de oro.")
            .WithColor(Color.Green)
            .Build();
    }
}
