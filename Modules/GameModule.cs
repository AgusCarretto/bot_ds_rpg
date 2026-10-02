using System.Text;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using BotDsRpg.Services;

public class GameModule(IUserRepository userRepository, IInventoryRepository inventoryRepository, IItemRepository itemRepository, IZoneRepository zoneRepository, IBuffRepository buffRepository)
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

            var embed = await BuildProfileEmbedAsync(
                userRepository, itemRepository, zoneRepository, buffRepository, target.Id, GetDisplayName(target),
                target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl());
            await FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
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
    public static async Task<Embed> BuildProfileEmbedAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IZoneRepository zoneRepository, IBuffRepository buffRepository,
        ulong discordId, string username, string avatarUrl)
    {
        // Si es la primera vez que este usuario ejecuta un comando, se crea acá
        // automáticamente con los valores por defecto (Nivel 1, 0 EXP, 50 de oro, 100/100 HP, Guerrero).
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var zone = await zoneRepository.GetByIdAsync(player.CurrentZoneId);

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;
        bool hasSynergy = weapon is not null && ClassWeaponSynergy.Applies(player.Class, weapon.WeaponFamily);

        // Ataque/Defensa = Nivel (Base) + equipo (arma con sinergia de clase si corresponde /
        // amuleto), misma fórmula que usa el combate real — ver GameData/CombatStats.cs.
        int weaponDamage = ClassWeaponSynergy.ApplyBonus(weapon?.StatValue ?? 0, player.Class, weapon?.WeaponFamily);
        int amuletDefense = amulet?.StatValue ?? 0;
        // El banquete activo (si lo hay) suma a lo que se ve igual que en el combate real.
        var buff = await buffRepository.GetActiveAttackAsync(discordId);
        int attack = AttackBuff.Apply(CombatStats.TotalAttack(player.Level, weaponDamage), buff?.AttackPercent ?? 0);
        int defense = CombatStats.TotalDefense(player.Level, amuletDefense);

        var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == player.Class);
        var ability = ClassAbilities.For(player.Class);
        int requiredXp = LevelingCalculator.RequiredXpForLevel(player.Level);

        // El ataque lleva abajo el ARMA y la defensa el AMULETO (cada número con la pieza que lo da, en la misma columna); el oro, la racha. Una
        // fila en blanco separa los bloques. El historial de PvP ya no va acá: está en /history y /duels.
        string attackText = $"**{attack}**" + (hasSynergy ? " ⚡" : string.Empty)
            + (buff is null ? string.Empty : $" 🍖 +{buff.AttackPercent}% ({Math.Max(1, (int)Math.Ceiling(buff.Remaining.TotalMinutes))} min)")
            + $"\n{(weapon is null ? "_Sin arma_" : $"{ItemDisplay.Format(weapon.Emoji, weapon.Name)} (+{weapon.StatValue})")}";
        string defenseText = $"**{defense}**\n{(amulet is null ? "_Sin amuleto_" : $"{ItemDisplay.Format(amulet.Emoji, amulet.Name)} (+{amulet.StatValue})")}";
        string goldText = $"**{player.Gold}**\n🎁 Racha: {(player.DailyStreak > 0 ? $"día {player.DailyStreak}" : "_ninguna_")}";

        return new EmbedBuilder()
            .WithAuthor(username, avatarUrl)
            .WithTitle($"{classDef?.Emoji ?? "🔥"} {player.Class} — Nivel {player.Level}")
            .WithColor(Color.Orange) // Color cálido acorde al asado
            .WithThumbnailUrl(avatarUrl)
            .AddField("📊 Experiencia", $"{ProgressBar.Render(player.Xp, requiredXp)}\n{player.Xp} / {requiredXp} XP", false)
            .AddField("❤️ Vida", $"{ProgressBar.Render(player.CurrentHp, player.MaxHp)}\n{player.CurrentHp} / {player.MaxHp} HP", false)
            .AddField(Blank, Blank, false)
            .AddField("⚔️ Ataque", attackText, true)
            .AddField("🛡️ Defensa", defenseText, true)
            .AddField("💰 Oro", goldText, true)
            .AddField(Blank, Blank, false)
            .AddField("🗺️ Zona actual", zone is null ? "_Desconocida_" : $"{zone.Emoji} Zona {zone.ZoneId}: {zone.Name}", false)
            .AddField(
                "✨ Habilidad",
                ability is null
                    ? "_Ninguna_"
                    : $"{ability.Emoji} **{ability.Name}** (enfriamiento: {ability.CooldownTurns} turnos)\n{ability.Description}",
                false)
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
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("No pude consultar ese inventario ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs arme el mismo embed en "aa inventory".
    //
    // TODA la pantalla va en UNA sola columna, de arriba hacia abajo: un bloque por tipo y una línea en blanco entre bloque y bloque.
    //     🪵 Madera
    //     ⛏️ Mineral
    //     🩸 Drops de monstruo
    //     🍖 Comida
    //     📦 Cajas
    // El type "Material" ES, por diseño de todo el juego, exactamente lo que sueltan los monstruos (nunca madera ni piedra, ver
    // Database/seed_class_gear_and_monster_drops.sql). Antes eran dos columnas lado a lado y los drops (~40 ítems) se partían en "(1/2)" y
    // "(2/2)" porque un campo de embed admite 1024 caracteres: ahora todo va en la descripción (4096), que entra aunque el jugador tenga el
    // catálogo entero. Si algún día el catálogo crece y no entra, lo que sobra sigue en campos SIN título (nunca "(1/2)"). Cada línea es el
    // ícono, el nombre y la cantidad; la rareza se ve con el emoji del ítem (o un círculo de color si todavía no tiene), sin escribirla en
    // cada renglón. Las armas y los amuletos no están acá: se forjan directo a equipamiento (ver /profile).
    public static async Task<Embed> BuildInventoryEmbedAsync(IInventoryRepository inventoryRepository, ulong discordId, string username)
    {
        var entries = await inventoryRepository.GetByDiscordIdAsync(discordId);

        var embed = new EmbedBuilder()
            .WithTitle($"🎒 Inventario de {username}")
            .WithColor(Color.Orange)
            .WithCurrentTimestamp();

        var units = new List<string>();
        foreach (var (heading, type) in new[]
                 {
                     ("🪵 **Madera**", "Madera"),
                     ("⛏️ **Mineral**", "Mineral"),
                     ("🩸 **Drops de monstruo**", "Material"),
                     ("🍖 **Comida**", "Consumable"),
                     ("📦 **Cajas**", "Caja"),
                 })
        {
            AddBlock(units, heading, Lines(entries.Where(e => e.Type == type)));
        }

        if (units.Count == 0)
        {
            embed.WithDescription("Todavía no tenés ningún material. ¡Probá /chop, /mine o /travel!");
            return embed.Build();
        }

        var pages = Paginate(units, DescriptionLimit, FieldLimit);
        embed.WithDescription(pages[0]);
        foreach (string page in pages.Skip(1))
        {
            embed.AddField(Blank, page, false);
        }

        if (entries.Any(e => e.Type == "Caja"))
        {
            embed.WithFooter("📦 Abrí tus cajas con /open");
        }

        return embed.Build();
    }

    // Espacio de ancho cero (U+200B): Discord no deja campos con el texto vacío y así se arma una columna o una fila en blanco.
    private static readonly string Blank = ((char)0x200B).ToString();

    // Límites de Discord con un poco de margen: la descripción de un embed admite 4096 caracteres y un campo 1024.
    private const int DescriptionLimit = 4000;
    private const int FieldLimit = 1000;

    // Suma un bloque (título + sus líneas) a la lista de "unidades" que después se reparten en páginas. El título viaja pegado a su primera
    // línea (así una página nunca termina en un título huérfano) y lleva adelante el salto que deja la línea en blanco entre un bloque y el anterior.
    private static void AddBlock(List<string> units, string heading, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        units.Add((units.Count > 0 ? "\n" : string.Empty) + heading + "\n" + lines[0]);
        units.AddRange(lines.Skip(1));
    }

    // Junta las unidades en páginas de a lo sumo `firstLimit` caracteres la primera y `nextLimit` las demás, cortando siempre entre unidades.
    // Una página nueva nunca arranca con una línea en blanco.
    private static List<string> Paginate(IReadOnlyList<string> units, int firstLimit, int nextLimit)
    {
        var pages = new List<string>();
        var current = new StringBuilder();
        int limit = firstLimit;

        foreach (string raw in units)
        {
            if (current.Length > 0 && current.Length + 1 + raw.Length > limit)
            {
                pages.Add(current.ToString());
                current.Clear();
                limit = nextLimit;
            }

            if (current.Length == 0)
            {
                current.Append(raw.TrimStart('\n'));
            }
            else
            {
                current.Append('\n').Append(raw);
            }
        }

        if (current.Length > 0)
        {
            pages.Add(current.ToString());
        }

        return pages;
    }

    // "Madera de Roble ×3": de lo más común a lo más raro y por nombre dentro de la misma rareza.
    private static List<string> Lines(IEnumerable<InventoryEntry> entries) =>
        entries
            .OrderBy(e => RarityCatalog.RankOf(e.Rarity))
            .ThenBy(e => e.ItemName, StringComparer.Ordinal)
            .Select(e => $"{(e.Emoji is null ? $"{RarityDot(e.Rarity)} {e.ItemName}" : ItemDisplay.Format(e.Emoji, e.ItemName))} ×{e.Quantity}")
            .ToList();

    // El color de la rareza como círculo: lo mismo que usa el juego en los embeds de cada rareza.
    private static string RarityDot(string rarity) => rarity switch
    {
        "Común" => "⚪",
        "Raro" => "🔵",
        "Épico" => "🟣",
        "Legendario" => "🟡",
        "Mítico" => "🔴",
        _ => "⚫",
    };
}
