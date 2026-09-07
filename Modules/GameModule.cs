using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;

public class GameModule(IUserRepository userRepository, IInventoryRepository inventoryRepository, IItemRepository itemRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /profile [jugador]
    [SlashCommand("profile", "Mostrá tu estado actual, nivel y estadísticas (o los de otro jugador del server).")]
    public async Task HandleProfileAsync(
        [Summary("jugador", "Opcional: de qué jugador del server querés ver el perfil.")] SocketGuildUser? targetUser = null)
    {
        // Difuminamos la respuesta: la consulta a la base puede superar el límite de 3s
        // que impone Discord antes de que la interacción expire.
        await DeferAsync();

        IUser target = targetUser ?? Context.User;

        try
        {
            // Solo chequeamos existencia cuando se consulta a OTRO jugador: para uno mismo
            // conservamos el alta automática de siempre (ver BuildProfileEmbedAsync).
            if (target.Id != Context.User.Id && await userRepository.GetByDiscordIdAsync(target.Id) is null)
            {
                await FollowupAsync(BuildNotRegisteredMessage(GetDisplayName(target)));
                return;
            }

            var embed = await BuildProfileEmbedAsync(userRepository, itemRepository, target.Id, GetDisplayName(target), target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl());
            await FollowupAsync(embed: embed);
        }
        catch (Exception)
        {
            // Si la base falla o está saturada, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude acceder a ese perfil ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Nombre a mostrar: apodo del server si lo tiene, sino el username — usado tanto acá como en
    // Modules/TextCommandModule.cs para que "aa p @alguien" se vea igual que "/profile jugador:".
    public static string GetDisplayName(IUser user) => user is IGuildUser guildUser ? (guildUser.Nickname ?? guildUser.Username) : user.Username;

    // Mensaje cuando se consulta el perfil/inventario de alguien que nunca corrió /start.
    public static string BuildNotRegisteredMessage(string displayName) =>
        $"🚫 **{displayName}** todavía no arrancó su aventura en Asado y Acero. ¡Decile que pruebe `/start`!";

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs arme el mismo
    // embed en "aa profile" — acá vive tanto la lectura de datos como el embed.
    public static async Task<Embed> BuildProfileEmbedAsync(IUserRepository userRepository, IItemRepository itemRepository, ulong discordId, string username, string avatarUrl)
    {
        // Si es la primera vez que este usuario ejecuta un comando, se crea acá
        // automáticamente con los valores por defecto (Nivel 1, 0 EXP, 50 de oro, 100/100 HP, Guerrero).
        var player = await userRepository.GetOrCreateUserAsync(discordId);

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;
        bool hasSynergy = weapon is not null && ClassWeaponSynergy.Applies(player.Class, weapon.WeaponFamily);

        // Ataque/Defensa = Nivel (Base) + equipo (arma con sinergia de clase si corresponde /
        // amuleto), misma fórmula que usa el combate real — ver GameData/CombatStats.cs.
        int weaponDamage = ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily);
        int amuletDefense = amulet?.StatValue ?? 0;
        int attack = CombatStats.TotalAttack(player.Level, weaponDamage);
        int defense = CombatStats.TotalDefense(player.Level, amuletDefense);

        var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == player.Class);
        int requiredXp = LevelingCalculator.RequiredXpForLevel(player.Level);

        return new EmbedBuilder()
            .WithAuthor(username, avatarUrl)
            .WithTitle($"{classDef?.Emoji ?? "🔥"} {player.Class} — Nivel {player.Level}")
            .WithColor(Color.Orange) // Color cálido acorde al asado
            .WithThumbnailUrl(avatarUrl)
            .AddField("📊 Experiencia", $"{ProgressBar.Render(player.Xp, requiredXp)}\n{player.Xp} / {requiredXp} XP", false)
            .AddField("❤️ Vida", $"{ProgressBar.Render(player.CurrentHp, player.MaxHp)}\n{player.CurrentHp} / {player.MaxHp} HP", false)
            .AddField("⚔️ Ataque", $"{attack} " + (hasSynergy ? " ⚡" : string.Empty), true)
            .AddField("🛡️ Defensa", $"{defense} ", true)
            .AddField("💰 Oro", player.Gold.ToString(), true)
            .AddField("🗡️ Arma", weapon is null ? "_Ninguna_" : $"{ItemDisplay.Format(weapon.Emoji, weapon.Name)} (+{weapon.StatValue})", true)
            .AddField("📿 Amuleto", amulet is null ? "_Ninguno_" : $"{ItemDisplay.Format(amulet.Emoji, amulet.Name)} (+{amulet.StatValue})", true)
            .AddField("🎁 Racha diaria", player.DailyStreak > 0 ? $"Día {player.DailyStreak}" : "_Sin racha_", true)
            .WithFooter("Asado y Acero RPG • Preparando las brasas...")
            .WithCurrentTimestamp()
            .Build();
    }

    // Comando barra: /inventory [jugador]
    [SlashCommand("inventory", "Mostrá los materiales que tenés guardados (o los de otro jugador del server).")]
    public async Task HandleInventoryAsync(
        [Summary("jugador", "Opcional: de qué jugador del server querés ver el inventario.")] SocketGuildUser? targetUser = null)
    {
        await DeferAsync();

        IUser target = targetUser ?? Context.User;

        try
        {
            if (target.Id != Context.User.Id && await userRepository.GetByDiscordIdAsync(target.Id) is null)
            {
                await FollowupAsync(BuildNotRegisteredMessage(GetDisplayName(target)));
                return;
            }

            var embed = await BuildInventoryEmbedAsync(inventoryRepository, target.Id, GetDisplayName(target));
            await FollowupAsync(embed: embed);
        }
        catch (Exception)
        {
            await FollowupAsync("No pude consultar ese inventario ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs arme el mismo
    // embed en "aa inventory".
    public static async Task<Embed> BuildInventoryEmbedAsync(IInventoryRepository inventoryRepository, ulong discordId, string username)
    {
        var entries = await inventoryRepository.GetByDiscordIdAsync(discordId);

        var embed = new EmbedBuilder()
            .WithTitle($"🎒 Inventario de {username}")
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
                .Select(e => $"**{ItemDisplay.Format(e.Emoji, e.ItemName)}** ×{e.Quantity} _({e.Rarity})_");

            embed.WithDescription(string.Join('\n', lines));
        }

        return embed.Build();
    }
}
