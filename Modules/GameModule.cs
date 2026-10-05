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
    // El inventario va en COLUMNAS arriba y los drops a ANCHO COMPLETO abajo (el dueño pidió columnas como las de otro bot, pero esos nombres son cortos):
    //     🪵 Madera           ⛏️ Mineral           🍖 Comida (con las 📦 Cajas debajo)       <- campos en línea, de a tres por fila
    //     🩸 Drops de monstruo: ícono Nombre: 3   ícono Otro nombre: 1   ícono ...             <- campo de ancho completo
    // POR QUÉ los drops no van en columnas: una columna angosta (~19 caracteres) parte los nombres largos en dos renglones ("Collar de Cuero / Viejo: 3"),
    // y los drops tienen nombres como "Cuero Curtido de Pradera". En ancho completo cada ítem es una FICHA que no se parte por dentro (espacios que no
    // se cortan, U+00A0) y la línea solo se corta ENTRE ítems: entran dos o tres por renglón y no hay nada alineado que se rompa.
    // Cada ítem es "ícono **Nombre**: cantidad" (nombre en negrita, cantidad con separador de miles). Un campo de embed admite 1024 caracteres y el
    // código de un emoji ocupa ~45, así que los drops (~32 ítems) se reparten en varios campos: el primero con título y los demás con título invisible
    // (nunca "(1/2)"). Solo aparece lo que tiene algo. El type "Material" ES, por diseño de todo el juego, exactamente lo que sueltan los monstruos (nunca
    // madera ni piedra, ver Database/seed_class_gear_and_monster_drops.sql). La rareza se ve con el emoji del ítem (o un círculo de color si todavía no
    // tiene). Las armas y los amuletos no están acá: se forjan directo a equipamiento (ver /profile). Los íconos no se pueden agrandar: dentro de un
    // texto Discord los dibuja siempre de 22 px.
    public static async Task<Embed> BuildInventoryEmbedAsync(IInventoryRepository inventoryRepository, ulong discordId, string username)
    {
        var entries = await inventoryRepository.GetByDiscordIdAsync(discordId);

        var embed = new EmbedBuilder()
            .WithTitle($"🎒 Inventario de {username}")
            .WithColor(Color.Orange)
            .WithCurrentTimestamp();

        var wood = Lines(entries.Where(e => e.Type == "Madera"));
        var minerals = Lines(entries.Where(e => e.Type == "Mineral"));
        var food = Lines(entries.Where(e => e.Type == "Consumable"));
        var dropChips = Chips(entries.Where(e => e.Type == "Material"));
        var boxes = Lines(entries.Where(e => e.Type == "Caja"));

        if (wood.Count + minerals.Count + food.Count + dropChips.Count + boxes.Count == 0)
        {
            embed.WithDescription("Todavía no tenés ningún material. ¡Probá /chop, /mine o /travel!");
            return embed.Build();
        }

        // El orden de los campos ES la distribución (Discord los acomoda de a tres por fila): primero lo corto (la primera fila), después los drops a ancho
        // completo. Comida y Cajas comparten la tercera columna de la primera fila si entran juntas en un campo (si no, las cajas van al final, aparte).
        AddColumn(embed, "🪵 Madera", wood);
        AddColumn(embed, "⛏️ Mineral", minerals);

        string stacked = food.Count > 0 && boxes.Count > 0 ? string.Join('\n', food) + "\n\n📦 **Cajas**\n" + string.Join('\n', boxes) : string.Empty;
        bool boxesUnderFood = stacked.Length > 0 && stacked.Length <= FieldLimit;
        if (boxesUnderFood)
        {
            embed.AddField("🍖 Comida", stacked, true);
        }
        else
        {
            AddColumn(embed, "🍖 Comida", food);
        }

        var dropFields = PackChips(dropChips);
        for (int i = 0; i < dropFields.Count; i++)
        {
            embed.AddField(i == 0 ? "🩸 Drops de monstruo" : Blank, dropFields[i], false);
        }

        if (!boxesUnderFood)
        {
            AddColumn(embed, "📦 Cajas", boxes);
        }

        if (boxes.Count > 0)
        {
            embed.WithFooter("📦 Abrí tus cajas con /open");
        }

        return embed.Build();
    }

    // Espacio de ancho cero (U+200B): Discord no deja campos con el texto vacío y así se arma una columna sin título.
    private static readonly string Blank = ((char)0x200B).ToString();

    // El límite de un campo de embed es 1024 caracteres; un poco de margen.
    private const int FieldLimit = 1000;

    // Una columna (campo en línea) con su título, o nada si no hay renglones. Si no entrara en un campo, sigue en columnas sin título.
    private static void AddColumn(EmbedBuilder embed, string title, IReadOnlyList<string> lines)
    {
        var columns = SplitIntoColumns(lines);
        for (int i = 0; i < columns.Count; i++)
        {
            embed.AddField(i == 0 ? title : Blank, columns[i], true);
        }
    }

    // Reparte los renglones en columnas PAREJAS (las mismas filas en cada una, la última puede quedar más corta): 1 columna hasta 8 renglones, 2 hasta 16
    // y 3 de ahí en adelante; y si alguna se pasa de FieldLimit caracteres (ítems con nombre largo y emoji de la aplicación) se agregan columnas hasta que entren.
    internal static List<string> SplitIntoColumns(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        for (int columns = lines.Count <= 8 ? 1 : lines.Count <= 16 ? 2 : 3; ; columns++)
        {
            int perColumn = (int)Math.Ceiling(lines.Count / (double)columns);
            var result = lines.Chunk(perColumn).Select(chunk => string.Join('\n', chunk)).ToList();
            if (result.All(text => text.Length <= FieldLimit) || perColumn == 1)
            {
                return result;
            }
        }
    }

    // "<emoji> **Madera de Roble**: 3": de lo más común a lo más raro y por nombre dentro de la misma rareza.
    private static List<string> Lines(IEnumerable<InventoryEntry> entries) =>
        Ordered(entries)
            .Select(e => $"{Icon(e)} **{e.ItemName}**: {GameHistory.Number(e.Quantity)}")
            .ToList();

    // Lo mismo, pero como FICHA que no se parte: todos los espacios son U+00A0 (no se cortan), así que si la línea se corta es entre una ficha y otra.
    private static List<string> Chips(IEnumerable<InventoryEntry> entries) =>
        Ordered(entries)
            .Select(e => $"{Icon(e)} **{e.ItemName}**: {GameHistory.Number(e.Quantity)}".Replace(' ', NoBreakSpace))
            .ToList();

    private static IEnumerable<InventoryEntry> Ordered(IEnumerable<InventoryEntry> entries) =>
        entries.OrderBy(e => RarityCatalog.RankOf(e.Rarity)).ThenBy(e => e.ItemName, StringComparer.Ordinal);

    private static string Icon(InventoryEntry e) => string.IsNullOrWhiteSpace(e.Emoji) ? RarityDot(e.Rarity) : e.Emoji;

    // Espacio que no se corta (U+00A0) y lo que separa una ficha de la siguiente: un espacio em (U+2003, deja un hueco bien visible) y uno común, que es donde la línea puede cortarse.
    private const char NoBreakSpace = (char)0xA0;
    private static readonly string ChipSeparator = ((char)0x2003).ToString() + " ";

    // Junta las fichas en campos de a lo sumo FieldLimit caracteres, cortando siempre entre ficha y ficha.
    internal static List<string> PackChips(IReadOnlyList<string> chips)
    {
        var fields = new List<string>();
        var current = new StringBuilder();

        foreach (string chip in chips)
        {
            if (current.Length > 0 && current.Length + ChipSeparator.Length + chip.Length > FieldLimit)
            {
                fields.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(ChipSeparator);
            }

            current.Append(chip);
        }

        if (current.Length > 0)
        {
            fields.Add(current.ToString());
        }

        return fields;
    }

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
