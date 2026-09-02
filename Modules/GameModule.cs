using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

public class GameModule(IUserRepository userRepository, IInventoryRepository inventoryRepository, IItemRepository itemRepository)
    : InteractionModuleBase<SocketInteractionContext>
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

            var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
            var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;
            bool hasSynergy = weapon is not null && ClassWeaponSynergy.Applies(player.Class, weapon.WeaponFamily);

            // Ataque = daño del arma equipada (con el bonus de sinergia si corresponde, igual que en combate).
            // Defensa = stat del amuleto equipado (reduce el HP que se pierde en /hunt y /travel).
            int attack = ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily);
            int defense = amulet?.StatValue ?? 0;

            var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == player.Class);
            int requiredXp = LevelingCalculator.RequiredXpForLevel(player.Level);

            var embed = new EmbedBuilder()
                .WithAuthor(user.Username, user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithTitle($"{classDef?.Emoji ?? "🔥"} {player.Class} — Nivel {player.Level}")
                .WithColor(Color.Orange) // Color cálido acorde al asado
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .AddField("📊 Experiencia", $"{ProgressBar.Render(player.Xp, requiredXp)}\n{player.Xp} / {requiredXp} XP", false)
                .AddField("❤️ Vida", $"{ProgressBar.Render(player.CurrentHp, player.MaxHp)}\n{player.CurrentHp} / {player.MaxHp} HP", false)
                .AddField("⚔️ Ataque", attack.ToString() + (hasSynergy ? " ⚡" : string.Empty), true)
                .AddField("🛡️ Defensa", defense.ToString(), true)
                .AddField("💰 Oro", player.Gold.ToString(), true)
                .AddField("🗡️ Arma", weapon is null ? "_Ninguna_" : $"{weapon.Name} (+{weapon.StatValue})", true)
                .AddField("📿 Amuleto", amulet is null ? "_Ninguno_" : $"{amulet.Name} (+{amulet.StatValue})", true)
                .AddField("🎁 Racha diaria", player.DailyStreak > 0 ? $"Día {player.DailyStreak}" : "_Sin racha_", true)
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
