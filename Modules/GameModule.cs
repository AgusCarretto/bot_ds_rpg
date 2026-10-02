using System.Text;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using BotDsRpg.Services;

public class GameModule(IUserRepository userRepository, IInventoryRepository inventoryRepository, IItemRepository itemRepository, IZoneRepository zoneRepository, IBuffRepository buffRepository, IGameEventRepository gameEventRepository)
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
                target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl(), gameEventRepository);
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
        ulong discordId, string username, string avatarUrl, IGameEventRepository? gameEventRepository = null)
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

        var builder = new EmbedBuilder()
            .WithAuthor(username, avatarUrl)
            .WithTitle($"{classDef?.Emoji ?? "🔥"} {player.Class} — Nivel {player.Level}")
            .WithColor(Color.Orange) // Color cálido acorde al asado
            .WithThumbnailUrl(avatarUrl)
            .AddField("📊 Experiencia", $"{ProgressBar.Render(player.Xp, requiredXp)}\n{player.Xp} / {requiredXp} XP", false)
            .AddField("❤️ Vida", $"{ProgressBar.Render(player.CurrentHp, player.MaxHp)}\n{player.CurrentHp} / {player.MaxHp} HP", false)
            .AddField("⚔️ Ataque", $"{attack} " + (hasSynergy ? " ⚡" : string.Empty) + (buff is null ? string.Empty : $" 🍖 +{buff.AttackPercent}% ({Math.Max(1, (int)Math.Ceiling(buff.Remaining.TotalMinutes))} min)"), true)
            .AddField("🛡️ Defensa", $"{defense} ", true)
            .AddField("💰 Oro", player.Gold.ToString(), true)
            .AddField("🗡️ Arma", weapon is null ? "_Ninguna_" : $"{ItemDisplay.Format(weapon.Emoji, weapon.Name)} (+{weapon.StatValue})", true)
            .AddField("📿 Amuleto", amulet is null ? "_Ninguno_" : $"{ItemDisplay.Format(amulet.Emoji, amulet.Name)} (+{amulet.StatValue})", true)
            .AddField("🎁 Racha diaria", player.DailyStreak > 0 ? $"Día {player.DailyStreak}" : "_Sin racha_", true)
            .AddField("🗺️ Zona actual", zone is null ? "_Desconocida_" : $"{zone.Emoji} Zona {zone.ZoneId}: {zone.Name}", true)
            .AddField(
                "✨ Habilidad",
                ability is null
                    ? "_Ninguna_"
                    : $"{ability.Emoji} **{ability.Name}** (enfriamiento: {ability.CooldownTurns} turnos)\n{ability.Description}",
                false)
            .WithFooter("Asado y Acero RPG • Preparando las brasas...")
            .WithCurrentTimestamp();

        // El resumen de PvP solo aparece si alguna vez peleó o se anotó en la Arena (GameData/PvpStats.cs). Es un extra: si falla la lectura, el perfil sale igual.
        if (gameEventRepository is not null)
        {
            try
            {
                if (PvpStats.Describe(await gameEventRepository.GetStatsAsync(discordId)) is { } pvp)
                {
                    builder.AddField("⚔️ PvP", pvp, false);
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        }

        return builder.Build();
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
    // La pantalla va en COLUMNAS (campos en línea de Discord), de dos en dos y con una fila en blanco entre filas, para que se lea de
    // un vistazo y no sea una pared de texto:
    //     🪵 Recolección (Madera y después Mineral)      🩸 Drops de monstruo
    //     🗡️ Armas                                         📿 Amuletos
    //     🍖 Comida                                        📦 Cajas
    // "Recolección" es lo que dan /chop y /mine (items.type Madera y Mineral, agrupados POR TIPO: primero toda la madera y después la
    // piedra y los minerales) y "Drops de monstruo" es otra columna: el type "Material" ES, por diseño de todo el juego, exactamente lo
    // que sueltan los monstruos (nunca madera ni piedra, ver Database/seed_class_gear_and_monster_drops.sql). Cada línea es el nombre y
    // la cantidad; la rareza se ve con el emoji del ítem (o un círculo de color si todavía no tiene), sin escribirla en cada renglón.
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
            return embed.Build();
        }

        var rows = new[]
        {
            (Left: GatheredColumn(entries), Right: Column("🩸 Drops de monstruo", entries, e => e.Type == "Material")),
            (Left: Column("🗡️ Armas", entries, e => e.Type == "Weapon"), Right: Column("📿 Amuletos", entries, e => e.Type == "Amulet")),
            (Left: Column("🍖 Comida", entries, e => e.Type == "Consumable"), Right: Column("📦 Cajas", entries, e => e.Type == "Caja")),
        };

        bool first = true;
        foreach (var (left, right) in rows)
        {
            if (left.Count == 0 && right.Count == 0)
            {
                continue;
            }

            // Una fila en blanco entre fila y fila: es lo que "despega" los bloques.
            if (!first)
            {
                embed.AddField(Blank, Blank, false);
            }

            first = false;

            // Dos columnas lado a lado; si una está vacía, la otra ocupa todo el ancho (no queda un hueco a la derecha).
            bool inline = left.Count > 0 && right.Count > 0;
            for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
            {
                if (left.Count > 0)
                {
                    var (title, text) = i < left.Count ? left[i] : (Blank, Blank);
                    embed.AddField(title, text, inline);
                }

                if (right.Count > 0)
                {
                    var (title, text) = i < right.Count ? right[i] : (Blank, Blank);
                    embed.AddField(title, text, inline);
                }
            }
        }

        if (entries.Any(e => e.Type == "Caja"))
        {
            embed.WithFooter("📦 Abrí tus cajas con /open");
        }

        return embed.Build();
    }

    // Espacio de ancho cero (U+200B): Discord no deja campos con el texto vacío y así se arma una columna o una fila en blanco.
    private static readonly string Blank = ((char)0x200B).ToString();

    // La columna de recolección: primero toda la Madera y después los Minerales (piedra, hierro...). Dentro de cada tipo, de lo más
    // común a lo más raro (que es, de paso, el orden en que se van consiguiendo).
    private static List<(string Title, string Text)> GatheredColumn(IReadOnlyList<InventoryEntry> entries)
    {
        var sections = new List<string>();

        foreach (var (type, heading) in new[] { ("Madera", "🪵 **Madera**"), ("Mineral", "⛏️ **Mineral**") })
        {
            var lines = Lines(entries.Where(e => e.Type == type));
            if (lines.Count > 0)
            {
                sections.Add($"{heading}\n{string.Join('\n', lines)}");
            }
        }

        // Solo hay unas pocas decenas de ítems de recolección: entra siempre en un campo (1024 caracteres), no hace falta partirlo.
        return sections.Count == 0 ? [] : [("⛏️ Recolección", string.Join("\n\n", sections))];
    }

    // Una columna simple de un grupo de ítems. Los campos de embed de Discord admiten 1024 caracteres: si un grupo (por ejemplo los
    // drops, que son ~40 ítems distintos) se pasa, se parte en "(1/2)", "(2/2)" en vez de que /inventory reviente.
    private static List<(string Title, string Text)> Column(string title, IReadOnlyList<InventoryEntry> entries, Func<InventoryEntry, bool> matches)
    {
        const int maxFieldLength = 1024;

        var lines = Lines(entries.Where(matches));
        if (lines.Count == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (current.Length > 0 && current.Length + 1 + line.Length > maxFieldLength)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
        }

        chunks.Add(current.ToString());

        return chunks.Select((text, i) => (chunks.Count > 1 ? $"{title} ({i + 1}/{chunks.Count})" : title, text)).ToList();
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
