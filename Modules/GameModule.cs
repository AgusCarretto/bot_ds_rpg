using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

public class GameModule(IUserRepository userRepository, IInventoryRepository inventoryRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /profile
    [SlashCommand("profile", "Muestra tu estado actual, nivel y estadísticas en Asado y Acero RPG.")]
    public async Task HandleProfileAsync()
    {
        // Difuminamos la respuesta: la consulta a la base puede superar el límite de 3s
        // que impone Discord antes de que la interacción expire.
        await DeferAsync();

        var user = Context.User;

        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá
            // automáticamente con los valores por defecto (Nivel 1, 0 EXP, 50 de oro, 100/100 HP, Guerrero).
            var player = await userRepository.GetOrCreateUserAsync(user.Id);

            var embed = new EmbedBuilder()
                .WithTitle($"⚔️ Perfil de Aventurero: {user.Username}")
                .WithColor(Color.Orange) // Color cálido acorde al asado
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .AddField("🔥 Clase", player.Class, true)
                .AddField("⭐ Nivel", player.Level.ToString(), true)
                .AddField("📊 EXP", $"{player.Xp} / {LevelingCalculator.RequiredXpForLevel(player.Level)}", true)
                .AddField("❤️ Vida", $"{player.CurrentHp} / {player.MaxHp}", true)
                .AddField("💰 Oro", $"{player.Gold} monedas", true)
                .AddField("🗡️ Arma Equipada", player.CurrentWeaponId is null ? "Ninguna" : $"Ítem #{player.CurrentWeaponId}", false)
                .WithFooter("Asado y Acero RPG • Preparando las brasas...")
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed);
        }
        catch (Exception)
        {
            // Si la base falla o está saturada, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude acceder a tu perfil ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /inventory
    [SlashCommand("inventory", "Mostrá los materiales que tenés guardados.")]
    public async Task HandleInventoryAsync()
    {
        await DeferAsync();

        try
        {
            var entries = await inventoryRepository.GetByDiscordIdAsync(Context.User.Id);

            var embed = new EmbedBuilder()
                .WithTitle($"🎒 Inventario de {Context.User.Username}")
                .WithColor(Color.Orange)
                .WithCurrentTimestamp();

            if (entries.Count == 0)
            {
                embed.WithDescription("Todavía no tenés ningún material. ¡Probá /chop, /mine o /travel!");
            }
            else
            {
                var lines = entries
                    .OrderBy(e => RarityCatalog.RankOf(e.Rarity))
                    .ThenBy(e => e.ItemName)
                    .Select(e => $"**{e.ItemName}** ×{e.Quantity} _({e.Rarity})_");

                embed.WithDescription(string.Join('\n', lines));
            }

            await FollowupAsync(embed: embed.Build());
        }
        catch (Exception)
        {
            await FollowupAsync("No pude consultar tu inventario ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
