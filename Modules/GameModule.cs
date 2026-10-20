using System.Text;
using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using BotDsRpg.Services;

public class GameModule(
    IUserRepository userRepository, IInventoryRepository inventoryRepository, IItemRepository itemRepository, IZoneRepository zoneRepository, IBuffRepository buffRepository,
    IPetRepository petRepository, IPlayerBonusService bonusService)
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
                target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl(), petRepository, bonusService);
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
        ulong discordId, string username, string avatarUrl, IPetRepository? petRepository = null, IPlayerBonusService? bonusService = null)
    {
        // Si es la primera vez que este usuario ejecuta un comando, se crea acá
        // automáticamente con los valores por defecto (Nivel 1, 0 EXP, 50 de oro, 100/100 HP, Guerrero).
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var allZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        var zone = allZones.FirstOrDefault(z => z.ZoneId == player.CurrentZoneId);
        // La zona más alta que tiene desbloqueada (la misma regla que /zona), para el "(máx. Zona N)".
        var maxZone = allZones.Count == 0 ? null : allZones[ZoneRanking.MaxUnlockedRank(allZones, player.Level, player.HighestZoneCleared) - 1];

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;
        bool hasSynergy = weapon is not null && ClassWeaponSynergy.Applies(player.Class, weapon.WeaponFamily);

        // Ataque/Defensa = Nivel (Base) + equipo (arma con sinergia de clase si corresponde /
        // amuleto), misma fórmula que usa el combate real — ver GameData/CombatStats.cs.
        // El arma y el amuleto cuentan CON su encantamiento (/enchant): son las mismas dos cuentas del combate real (PlayerCombatProfileCalculator.WeaponDamage/AmuletDefense, dentro de Resolve).
        // El banquete activo (si lo hay) suma a lo que se ve igual que en el combate real.
        var buff = await buffRepository.GetActiveAttackAsync(discordId);
        // Las mascotas (v0.10.0): la del Gólem suma a la defensa igual que en el combate real (PlayerCombatProfileCalculator.Resolve). Un perfil nunca se cae por esto: si la
        // lectura falla se muestra sin mascotas.
        IReadOnlyList<OwnedPet> pets = [];
        if (petRepository is not null)
        {
            try
            {
                pets = await petRepository.GetOwnedAsync(discordId);
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        }

        // Todo lo permanente junto (v0.12.0: Fuego Nuevo y bendiciones además de las mascotas): el ataque y la defensa del perfil salen de la MISMA cuenta que usa el combate contra
        // monstruos (PlayerCombatProfileCalculator.Resolve), así el número que se ve es el que pelea. Sin servicio (pruebas) cuenta solo las mascotas, como antes.
        PlayerBonuses bonuses = bonusService is null ? PetRules.Total(pets) : await bonusService.GetAsync(discordId, player.FuegoNuevo);
        var petBonuses = bonuses.Pets;
        var combat = PlayerCombatProfileCalculator.Resolve(player, weapon, amulet, buff?.AttackPercent ?? 0, bonuses);
        int attack = combat.Damage;
        int defense = combat.Defense;

        var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == player.Class);
        var ability = ClassAbilities.For(player.Class);
        int requiredXp = LevelingCalculator.RequiredXpForLevel(player.Level);

        // El bloque «Estado», una línea cada uno: la zona, el ataque, la defensa, el oro, el banco... (el dueño, 2026-10-20: «ATQ: el número y el arma, pero no el nombre, solo el emoji; abajo lo mismo con DEF; abajo el oro y luego el banco»):
        //   ATQ 123 <arma> ⚡ ...      DEF 45 <amuleto> ...      Oro 1.234 <moneda>      Banco 99 <banco>
        // Después del número va el emoji PROPIO de la pieza (el nombre lo dice /inventory y la herrería; sin emoji cargado se escribe el nombre para no dejar la línea muda). Las marcas de lo que suma
        // aparecen solo cuando existen: ⚡ sinergia de clase, 🍖 banquete (con los minutos que le quedan), 🔥 Fuego Nuevo / bendición, 🐾 mascotas, ✨ encantamiento (el porcentaje de la pieza).
        string PieceMark(Item? piece, string none) =>
            piece is null ? none : (string.IsNullOrWhiteSpace(piece.Emoji) ? piece.Name : piece.Emoji);
        string EnchantMark(int tier) => Enchantments.IsValidTier(tier) ? $" ✨+{Enchantments.BonusPercent(tier)} %" : string.Empty;

        string attackLine = $"**ATQ** {attack} {PieceMark(weapon, "_sin arma_")}" + (hasSynergy ? " ⚡" : string.Empty)
            + (buff is null ? string.Empty : $" 🍖+{buff.AttackPercent} % ({Math.Max(1, (int)Math.Ceiling(buff.Remaining.TotalMinutes))} min)")
            + (bonuses.AttackMultiplier > 1.0 ? $" 🔥{FuegoNuevoRules.PercentText(bonuses.AttackMultiplier)}" : string.Empty)
            + EnchantMark(weapon is null ? 0 : player.WeaponEnchant);
        string defenseLine = $"**DEF** {defense} {PieceMark(amulet, "_sin amuleto_")}" + (petBonuses.DefensePercent > 0 ? $" 🐾{PetRules.PercentText(petBonuses.DefensePercent)}" : string.Empty)
            + (bonuses.BlessingLevel(BlessingCatalog.DefenseKey) > 0 ? $" 🔥{FuegoNuevoRules.PercentText(1 + (BlessingCatalog.DefensePerLevel * bonuses.BlessingLevel(BlessingCatalog.DefenseKey)))}" : string.Empty)
            + EnchantMark(amulet is null ? 0 : player.AmuletEnchant);

        // El oro, el banco (a salvo de la penalidad por morir), el Polvo (solo cuando hay) y la racha del /daily (solo cuando hay) debajo, cada uno en su línea y con su ícono propio al final.
        var numberLines = new List<string>
        {
            attackLine,
            defenseLine,
            $"**Oro** {GameHistory.Number(player.Gold)} {UiEmojis.Gold}",
        };
        if (player.HasBank)
        {
            numberLines.Add($"**Banco** {GameHistory.Number(player.BankGold)} {UiEmojis.Bank}");
        }

        if (player.Dust > 0)
        {
            numberLines.Add($"**Polvo** {GameHistory.Number(player.Dust)} {UiEmojis.Dust}");
        }

        if (player.DailyStreak > 0)
        {
            numberLines.Add($"**Racha** día {player.DailyStreak} {UiEmojis.Streak}");
        }

        // La zona va en el bloque de abajo, SIN el nombre (el dueño, 2026-10-20: «no poner el nombre, Zona 1 (máx. 3) y el ícono»): «Zona 1 (máx. 3) 🌾», con el ícono de la zona al final como las demás líneas.
        string zoneRow = zone is null
            ? "**Zona** _desconocida_"
            : $"**Zona** {zone.ZoneId}" + (maxZone is null ? string.Empty : $" (máx. {maxZone.ZoneId})") + (string.IsNullOrWhiteSpace(zone.Emoji) ? string.Empty : $" {zone.Emoji}");

        // Parado en El Fogón Eterno (zona 0, GameData/FogonRules.cs): la zona de siempre sigue siendo la última normal, pero el jugador está en la puerta.
        if (player.InGate)
        {
            zoneRow = $"**Zona** 0 🔥 (tu zona: {(zone is null ? "?" : zone.ZoneId.ToString())})";
        }

        numberLines.Insert(0, zoneRow);

        // Debajo de la primera línea de la descripción, solo cuando corresponde: que venció al Asador y el título cosmético de la bendición Brasa de Color (que también cambia el color del perfil con su nivel).
        var descriptionLines = new List<string>();
        if (player.GateCleared)
        {
            descriptionLines.Add("🔥 _Venció al Asador Eterno._");
        }

        int colorLevel = bonuses.BlessingLevel(BlessingCatalog.ColorKey);
        if (colorLevel > 0)
        {
            descriptionLines.Add($"🌈 _{BlessingCatalog.ColorTitle(colorLevel)}_");
        }

        // Debajo de la clase (el título) van el Fuego Nuevo y el nivel, en ese orden (el dueño, 2026-10-20: «clase, FN, LVL»): el título no se garantiza para emojis propios, así que viven en la
        // descripción. Sin Fuego Nuevo solo el nivel. Sin los emojis subidos queda «🔥 FN 3 · Nivel 41» (GameData/UiEmojis.cs).
        string levelLine = (player.FuegoNuevo > 0 ? UiEmojis.FuegoNuevoTag(player.FuegoNuevo) + " · " : string.Empty) + UiEmojis.LevelTag(player.Level);
        descriptionLines.Insert(0, levelLine);

        var embed = new EmbedBuilder()
            .WithAuthor(username, avatarUrl)
            .WithTitle($"{classDef?.Emoji ?? "🔥"} {player.Class}")
            .WithDescription(string.Join("\n", descriptionLines))
            .WithColor(colorLevel > 0 ? new Color((uint)BlessingCatalog.ColorValue(colorLevel)) : Color.Orange) // Color cálido acorde al asado
            .WithThumbnailUrl(avatarUrl)
            // Los nombres de campo van en texto plano («XP», «Vida») y el ícono al final de la línea de números: un emoji propio no se garantiza en el nombre de un campo.
            .AddField("XP", $"{ProgressBar.Render(player.Xp, requiredXp, kind: UiEmojis.BarXp)}\n{player.Xp} / {requiredXp} {UiEmojis.Xp}", false)
            .AddField("Vida", $"{ProgressBar.Render(player.CurrentHp, player.MaxHp, kind: UiEmojis.BarHp)}\n{player.CurrentHp} / {player.MaxHp} HP ❤️", false)
            .AddField("Estado", string.Join("\n", numberLines), false);

        // Las mascotas, los porcentajes del Fuego Nuevo, las bendiciones y los oficios YA NO van en el perfil (el dueño, 2026-10-07: «dejalas aparte»): cada uno tiene su comando propio,
        // /pet (aa pet), /fuegonuevo (aa fn), /blessings (aa bendiciones) y /professions (aa oficios). El perfil queda con lo básico y el número que pelea: el ataque y la defensa siguen
        // mostrando sus extras (🔥 / 🐾) en la misma línea, y el título y el color de la bendición Brasa de Color son cosméticos de acá arriba.

        return embed
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
    // El inventario va en DOS COLUMNAS (pedido del dueño, v0.9.3: "madera, minerales, drops, consumibles; los consumibles en una columna sola debajo de minerales"):
    //     🪵 Madera                    ⛏️ Mineral
    //     🩸 Drops de monstruo         🍖 Consumibles (con las 📦 Cajas debajo)
    // Discord acomoda los campos en línea de a TRES por fila y cada uno ocupa un tercio del ancho, así que para que queden solo dos por fila cada fila lleva un
    // tercer campo en línea VACÍO (nombre y texto de ancho cero) que la cierra: al estar al costado no suma altura (lo que dejaba huecos enormes, v0.9.1, eran los
    // campos vacíos APILADOS, en su propia fila). Las dos columnas siguen siendo angostas (~19 caracteres): un nombre largo como "Cuero Curtido de Pradera"
    // se parte en dos renglones dentro de su columna. Eso ya se probó antes (v0.8.3) y por eso se probaron las alternativas: las "fichas" con espacios que no
    // se cortan (U+00A0) no aguantaron (Discord corta entre el ícono, que es una imagen, y el texto, y el ícono de un ítem quedaba al final del renglón anterior,
    // pegado al ítem equivocado, v0.9.0) y los drops uno por renglón a ancho completo (v0.9.0-v0.9.2) sí andaban. Si los nombres partidos molestan, la salida es
    // volver a esos (git: v0.9.2). NO usar fichas.
    // Cada ítem es "ícono **Nombre**: cantidad" (nombre en negrita, cantidad con separador de miles), uno por renglón. Un campo de embed admite 1024 caracteres y el
    // código de un emoji ocupa ~45, así que una columna larga (los ~15 drops de las 5 zonas) se reparte en varios campos: el primero con título y los demás con
    // título invisible (nunca "(1/2)"), cada uno en su fila. Solo aparece lo que tiene algo; si solo hay una de las dos columnas, va a ancho completo. El type "Material"
    // ES, por diseño de todo el juego, exactamente lo que sueltan los monstruos (nunca madera ni piedra, ver Database/seed_class_gear_and_monster_drops.sql). La
    // rareza se ve con el emoji del ítem (o un círculo de color si todavía no tiene). Las armas y los amuletos no están acá: se forjan directo a equipamiento
    // (ver /profile). Los íconos no se pueden agrandar: dentro de un texto Discord los dibuja siempre de 22 px.
    public static async Task<Embed> BuildInventoryEmbedAsync(IInventoryRepository inventoryRepository, ulong discordId, string username)
    {
        var entries = await inventoryRepository.GetByDiscordIdAsync(discordId);

        var embed = new EmbedBuilder()
            .WithTitle($"🎒 Inventario de {username}")
            .WithColor(Color.Orange)
            .WithCurrentTimestamp();

        var wood = Lines(entries.Where(e => e.Type == "Madera"));
        var minerals = Lines(entries.Where(e => e.Type == "Mineral"));
        // La comida de las mascotas va con los consumibles y los huevos con las cajas (v0.10.0): los dos son cosas que se tienen en la mochila para usar después.
        var food = Lines(entries.Where(e => e.Type is "Consumable" or "PetFood"));
        var drops = Lines(entries.Where(e => e.Type == "Material"));
        var boxes = Lines(entries.Where(e => e.Type is "Caja" or "Huevo"));
        string boxTitle = entries.Any(e => e.Type == "Huevo" && e.Quantity > 0) ? "Cajas y huevos" : "Cajas";

        if (wood.Count + minerals.Count + food.Count + drops.Count + boxes.Count == 0)
        {
            embed.WithDescription("Todavía no tenés ningún material. ¡Probá /hunt, /chop o /mine!");
            return embed.Build();
        }

        // Columna izquierda: Madera y, debajo, los drops. Columna derecha: Mineral y, debajo, los consumibles. Las Cajas van con la comida (en el mismo campo) si entran juntas.
        var left = new List<(string Name, string Value)>();
        var right = new List<(string Name, string Value)>();
        AddFields(left, "🪵 Madera", wood);
        AddFields(right, "⛏️ Mineral", minerals);
        AddFields(left, "🩸 Drops de monstruo", drops);

        string stacked = food.Count > 0 && boxes.Count > 0 ? string.Join('\n', food) + $"\n\n📦 **{boxTitle}**\n" + string.Join('\n', boxes) : string.Empty;
        if (stacked.Length > 0 && stacked.Length <= FieldLimit)
        {
            right.Add(("🍖 Consumibles", stacked));
        }
        else
        {
            AddFields(right, "🍖 Consumibles", food);
            AddFields(right, $"📦 {boxTitle}", boxes);
        }

        if (left.Count > 0 && right.Count > 0)
        {
            // Fila por fila: el campo de la izquierda, el de la derecha y un tercero vacío que cierra la fila (si una columna es más larga, la otra se rellena con vacíos).
            for (int row = 0; row < Math.Max(left.Count, right.Count); row++)
            {
                var (leftName, leftValue) = row < left.Count ? left[row] : (Blank, Blank);
                var (rightName, rightValue) = row < right.Count ? right[row] : (Blank, Blank);
                embed.AddField(leftName, leftValue, true).AddField(rightName, rightValue, true).AddField(Blank, Blank, true);
            }
        }
        else
        {
            // Una sola columna con algo: a ancho completo (angosta no tiene sentido).
            foreach (var (name, value) in left.Concat(right))
            {
                embed.AddField(name, value, false);
            }
        }

        if (boxes.Count > 0)
        {
            embed.WithFooter(boxTitle == "Cajas" ? "📦 Abrí tus cajas con /open" : "📦 Abrí tus cajas y huevos con /open");
        }

        return embed.Build();
    }

    // Espacio de ancho cero (U+200B): Discord no deja campos con el texto vacío y así se arma una columna sin título.
    private static readonly string Blank = ((char)0x200B).ToString();

    // El límite de un campo de embed es 1024 caracteres; un poco de margen.
    private const int FieldLimit = 1000;

    // Los renglones de una lista, repartidos en los campos que hagan falta (de a lo sumo FieldLimit caracteres): el primero lleva el título y los demás un título
    // invisible. No agrega nada si no hay renglones.
    private static void AddFields(List<(string Name, string Value)> column, string title, IReadOnlyList<string> lines)
    {
        var fields = PackLines(lines);
        for (int i = 0; i < fields.Count; i++)
        {
            column.Add((i == 0 ? title : Blank, fields[i]));
        }
    }

    // "<emoji> **Madera de Roble**: 3": de lo más común a lo más raro y por nombre dentro de la misma rareza.
    private static List<string> Lines(IEnumerable<InventoryEntry> entries) =>
        Ordered(entries)
            .Select(e => $"{Icon(e)} **{e.ItemName}**: {GameHistory.Number(e.Quantity)}")
            .ToList();

    private static IEnumerable<InventoryEntry> Ordered(IEnumerable<InventoryEntry> entries) =>
        entries.OrderBy(e => RarityCatalog.RankOf(e.Rarity)).ThenBy(e => e.ItemName, StringComparer.Ordinal);

    private static string Icon(InventoryEntry e) => string.IsNullOrWhiteSpace(e.Emoji) ? RarityDot(e.Rarity) : e.Emoji;

    // Junta los renglones (uno por ítem) en campos de a lo sumo FieldLimit caracteres, cortando siempre entre renglón y renglón.
    internal static List<string> PackLines(IReadOnlyList<string> lines)
    {
        var fields = new List<string>();
        var current = new StringBuilder();

        foreach (string line in lines)
        {
            if (current.Length > 0 && current.Length + 1 + line.Length > FieldLimit)
            {
                fields.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
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
