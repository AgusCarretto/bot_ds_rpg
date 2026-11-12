using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /stats (aa stats) — la ficha de lo que tenés AHORA contra monstruos (v0.49.0, el dueño: «un comando nuevo aa stats que te muestre las cosas actuales con encantamientos actuales y bufos actuales, pero nada de cargar el
// perfil»; v0.49.2: «el total con letra más grande, en negrita, el base y después los agregados, pero que sean parte del total, desglosados»). Para ATAQUE, DEFENSA y VIDA: un título grande con el TOTAL (el mismo que pelea:
// sale de PlayerCombatProfileCalculator.Resolve), la BASE (el nivel) y cada AGREGADO con lo que sumó en puntos, de modo que base + agregados = total EXACTO (lo que multiplica, como el banquete, el dragón o las bendiciones,
// se reparte en puntos y el último absorbe el redondeo). Después, en la MISMA pantalla, crítico y esquive y las chances de drop. Lo demás (lo que corre de la cocina, el dragón, las mascotas y el Fuego Nuevo con las bendiciones) va en una
// SEGUNDA pantalla, «Más stats» (v0.50.3, el dueño: «hacer otra pestaña para todo ese resto, desde la cocina hasta abajo»): un botón cambia de una a la otra en el mismo mensaje. Solo mira (está en la lista de FightLock) y no crea cuentas.
// Components (v0.50.3): el botón «Ver más stats» / «Volver a las stats» cuando hay una segunda pantalla.
// De OTRO jugador (v0.53.0, el dueño: «que se pueda ver stats de otra persona»): /stats jugador:@x, «aa stats @x» (mención, ID o nombre exacto) o respondiendo su mensaje con «aa stats». Son SUS números (los mismos que usa su pelea),
// con su nombre en el título; no crea la cuenta de nadie y los botones de pasar de pantalla llevan quién mira y de quién son («stats_view:{quien mira}:{pantalla}:{de quién}»).
public sealed record StatsReply(string? PlainMessage, Embed? Embed, MessageComponent? Components = null);

public class StatsModule(IUserRepository users, IItemRepository items, IBuffRepository buffs, IPlayerBonusService bonusService, ICookingRepository cooking, IDragonRepository dragons, IZoneBoxService zoneBoxes) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("stats", "Tu ataque, defensa y vida, y cuánto aporta cada cosa (equipo, platos, dragón...).")]
    public async Task HandleAsync(
        [Summary("jugador", "Opcional: de qué jugador del server querés ver las stats.")] Discord.WebSocket.SocketGuildUser? targetUser = null)
    {
        await DeferAsync();

        try
        {
            // Mirarse a uno mismo («/stats jugador:@yo») es el /stats de siempre.
            var reply = targetUser is not null && targetUser.Id != Context.User.Id
                ? await BuildStatsAsync(users, items, buffs, bonusService, cooking, dragons, targetUser.Id, zoneBoxes, 0, GameModule.GetDisplayName(targetUser), Context.User.Id)
                : await BuildStatsAsync(users, items, buffs, bonusService, cooking, dragons, Context.User.Id, zoneBoxes);
            await FollowupAsync(reply.PlainMessage, embed: reply.Embed, components: reply.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude armar tus stats ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // El botón de cambiar de pantalla: «stats_page:{dueño}:{0|1}» (el valor es la pantalla a la que PASA). Solo lo toca el dueño de la ficha.
    public const string PageButton = "stats_page";

    [ComponentInteraction($"{PageButton}:*:*")]
    public async Task HandlePageAsync(string ownerRaw, string pageRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa ficha es de otra persona: abrí la tuya con **/stats**."), ephemeral: true);
                return;
            }

            var reply = await BuildStatsAsync(users, items, buffs, bonusService, cooking, dragons, ownerId, zoneBoxes, pageRaw == "1" ? 1 : 0);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = reply.PlainMessage;
                p.Embed = reply.Embed;
                p.Components = reply.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude armar tus stats ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // El botón de cambiar de pantalla en las stats de OTRO jugador: «stats_view:{quien mira}:{pantalla}:{de quién}». Es otro prefijo (no un tercer comodín de stats_page) para que Discord.Net nunca confunda los dos ids.
    // Solo lo toca quien pidió la ficha.
    public const string OtherPageButton = "stats_view";

    [ComponentInteraction($"{OtherPageButton}:*:*:*")]
    public async Task HandleOtherPageAsync(string viewerRaw, string pageRaw, string targetRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(viewerRaw, out ulong viewerId) || viewerId != Context.User.Id || !ulong.TryParse(targetRaw, out ulong targetId))
            {
                await FollowupAsync(Loc.T("Esa ficha es de otra persona: abrí la tuya con **/stats**."), ephemeral: true);
                return;
            }

            // El nombre del otro: el del server si está a mano; si no, el de Discord; si no, «ese jugador».
            string name = Context.Guild?.GetUser(targetId) is { } member ? GameModule.GetDisplayName(member) : Context.Client.GetUser(targetId)?.Username ?? Loc.T("ese jugador");
            var reply = await BuildStatsAsync(users, items, buffs, bonusService, cooking, dragons, targetId, zoneBoxes, pageRaw == "1" ? 1 : 0, name, viewerId);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = reply.PlainMessage;
                p.Embed = reply.Embed;
                p.Components = reply.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude armar esas stats ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // «aa stats» después del nombre del comando: la palabra de «más» (mas, más, more, extra, 2) y/o el jugador (una mención, un ID o su nombre exacto, con comillas o sin ellas) en cualquier orden. Devuelve la pantalla
    // (0 la de siempre, 1 «Más stats») y a quién mirar (null = a uno mismo). Pura: la prueba el arnés.
    public static (int Page, string? Player) ParseTextArgs(string? first, string? rest)
    {
        var words = $"{first} {rest}".Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        int page = 0;
        if (words.Count > 0 && IsMoreWord(words[0]))
        {
            page = 1;
            words.RemoveAt(0);
        }
        else if (words.Count > 1 && IsMoreWord(words[^1]))
        {
            page = 1;
            words.RemoveAt(words.Count - 1);
        }

        string player = string.Join(' ', words).Trim().Trim('"', '“', '”', '„', '«', '»').Trim();
        return (page, player.Length == 0 ? null : player);
    }

    public static bool IsMoreWord(string? word) => (word ?? string.Empty).Trim().ToLowerInvariant() is "mas" or "más" or "more" or "extra" or "2";

    // Estático y sin Discord: lo comparte «aa stats». No crea cuentas (GetByDiscordIdAsync). page = 0 la ficha de siempre, 1 «Más stats» (si no hay nada que mostrar ahí, siempre sale la primera).
    public static async Task<StatsReply> BuildStatsAsync(
        IUserRepository users, IItemRepository items, IBuffRepository buffs, IPlayerBonusService bonusService, ICookingRepository cooking, IDragonRepository dragons, ulong discordId, IZoneBoxService? zoneBoxes = null, int page = 0, string? targetName = null, ulong? viewerId = null)
    {
        // targetName y viewerId (v0.53.0): con un nombre, son las stats de OTRO jugador (viewerId = quien mira, para los botones); sin él, las de uno mismo como siempre.
        var player = await users.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new StatsReply(targetName is null ? Loc.T("Primero creá tu personaje con **/start**.") : Loc.F($"**{targetName}** todavía no tiene personaje."), null);
        }

        var weapon = player.WeaponId is int weaponId ? await items.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await items.GetByIdAsync(amuletId) : null;
        var buff = await buffs.GetActiveAttackAsync(discordId);
        var bonuses = await bonusService.GetAsync(discordId, player.FuegoNuevo);
        var combat = PlayerCombatProfileCalculator.Resolve(player, weapon, amulet, buff?.AttackPercent ?? 0, bonuses);

        IReadOnlyDictionary<string, TimeSpan> activeDishes = new Dictionary<string, TimeSpan>();
        try
        {
            activeDishes = await cooking.GetActiveBuffsAsync(discordId);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }

        // ---- ATAQUE: nivel + arma + platos (lo que se tiene puesto), después el banquete y lo que multiplican el dragón y las bendiciones.
        int weaponAtk = weapon is null ? 0 : PlayerCombatProfileCalculator.WeaponDamage(player, weapon);
        int cookedAtk = bonuses.CookedAttack;
        int attackGear = CombatStats.TotalAttack(player.Level, weaponAtk + cookedAtk);
        int attackBase = attackGear - weaponAtk - cookedAtk;
        int attackWithBuff = AttackBuff.Apply(attackGear, buff?.AttackPercent ?? 0);
        double blessingAtkPct = (BlessingAttackFactor(bonuses) - 1) * 100;
        double dragonAtkPct = bonuses.Dragon?.AttackPercent ?? 0;
        var attackShares = Share(combat.Damage - attackWithBuff, dragonAtkPct, blessingAtkPct);
        var attackAdds = new List<string>();
        if (weapon is not null) attackAdds.Add(Loc.F($"➕ Arma **+{weaponAtk}** · {weapon.Name}{EnchantTail(player.WeaponEnchantKind, player.WeaponEnchant)}"));
        if (cookedAtk > 0) attackAdds.Add(Loc.F($"➕ Platos **+{cookedAtk}**"));
        if (buff is not null && attackWithBuff - attackGear > 0) attackAdds.Add(Loc.F($"➕ Banquete **+{attackWithBuff - attackGear}** · +{buff.AttackPercent} % ({Math.Max(1, (int)Math.Ceiling(buff.Remaining.TotalMinutes))} min)"));
        if (attackShares[0] > 0) attackAdds.Add(Loc.F($"➕ Dragón **+{attackShares[0]}**"));
        if (attackShares[1] > 0) attackAdds.Add(Loc.F($"➕ Bendición **+{attackShares[1]}**"));
        string attackBlock = Block(Loc.F($"# ⚔️ Ataque **{combat.Damage}**"),
            weapon is null ? Loc.F($"Base **{attackBase}** · sin arma") : $"Base **{attackBase}**", attackAdds);

        // ---- DEFENSA: nivel + amuleto + platos, después lo que multiplican la mascota, las bendiciones y el dragón.
        int amuletDef = amulet is null ? 0 : PlayerCombatProfileCalculator.AmuletDefense(player, amulet);
        int cookedDef = bonuses.CookedDefense;
        int defenseGear = CombatStats.TotalDefense(player.Level, amuletDef + cookedDef);
        int defenseBase = defenseGear - amuletDef - cookedDef;
        double petDefPct = bonuses.Pets.DefensePercent;
        double blessingDefPct = ((1 + (BlessingCatalog.DefensePerLevel * bonuses.BlessingLevel(BlessingCatalog.DefenseKey))) - 1) * 100;
        double dragonDefPct = bonuses.Dragon?.DefensePercent ?? 0;
        var defenseShares = Share(combat.Defense - defenseGear, petDefPct, dragonDefPct, blessingDefPct);
        var defenseAdds = new List<string>();
        if (amulet is not null) defenseAdds.Add(Loc.F($"➕ Amuleto **+{amuletDef}** · {amulet.Name}{EnchantTail(player.AmuletEnchantKind, player.AmuletEnchant)}"));
        if (cookedDef > 0) defenseAdds.Add(Loc.F($"➕ Platos **+{cookedDef}**"));
        if (defenseShares[0] > 0) defenseAdds.Add(Loc.F($"➕ Mascota **+{defenseShares[0]}**"));
        if (defenseShares[1] > 0) defenseAdds.Add(Loc.F($"➕ Dragón **+{defenseShares[1]}**"));
        if (defenseShares[2] > 0) defenseAdds.Add(Loc.F($"➕ Bendición **+{defenseShares[2]}**"));
        string defenseBlock = Block(Loc.F($"# 🛡️ Defensa **{combat.Defense}**"),
            amulet is null ? Loc.F($"Base **{defenseBase}** · sin amuleto") : $"Base **{defenseBase}**", defenseAdds);

        // ---- VIDA MÁXIMA (la que se ve): la vida del nivel por el factor de la clase, el encantamiento Vida, las bendiciones y el dragón, y los platos EXACTOS al final (CombatPassives los suma así).
        var classBase = ClassPassives.For(player.Class);
        var withEnchants = Enchantments.ApplyEffects(classBase, player);
        double classFactor = classBase.MaxHpMultiplier * PveTuning.HpFactor(player.Class);
        double enchantFactor = withEnchants.MaxHpMultiplier / classBase.MaxHpMultiplier;
        double blessingDragonFactor = Math.Max(1.0, bonuses.MaxHpMultiplier);
        int hpBase = player.MaxHp;
        int hpClass = HpDisplay.Scale(hpBase, classFactor);
        int hpEnchant = HpDisplay.Scale(hpBase, classFactor * enchantFactor);
        int hpBlessingDragon = HpDisplay.Scale(hpBase, classFactor * enchantFactor * blessingDragonFactor);
        double blessingHpPct = BlessingCatalog.HpPerLevel * bonuses.BlessingLevel(BlessingCatalog.HpKey) * 100;
        double dragonHpPct = bonuses.Dragon?.HpPercent ?? 0;
        var hpShares = Share(hpBlessingDragon - hpEnchant, dragonHpPct, blessingHpPct);
        var hpAdds = new List<string>();
        if (hpClass - hpBase != 0) hpAdds.Add(Loc.F($"➕ Clase **{Signed(hpClass - hpBase)}** · {player.Class} ×{Loc.Decimal(classFactor, 2)}"));
        if (hpEnchant - hpClass > 0) hpAdds.Add(Loc.F($"➕ Encantamiento **+{hpEnchant - hpClass}**"));
        if (hpShares[0] > 0) hpAdds.Add(Loc.F($"➕ Dragón **+{hpShares[0]}**"));
        if (hpShares[1] > 0) hpAdds.Add(Loc.F($"➕ Bendición **+{hpShares[1]}**"));
        if (bonuses.CookedMaxHp > 0) hpAdds.Add(Loc.F($"➕ Platos **+{bonuses.CookedMaxHp}**"));
        string hpBlock = Block(Loc.F($"# ❤️ Vida máxima **{combat.CombatMaxHp}**"), $"Base **{hpBase}**", hpAdds);

        // ---- DROP: la chance base de /hunt (6 %) y de la caja de /travel con TODO lo extra. Las fuentes se multiplican entre sí, así que cada ➕ cuenta sobre lo ya acumulado más arriba (el plato va último: su % es del
        // acumulado, no de la base). Si el desglose no se puede armar, la ficha sale igual sin estos bloques.
        var dropBlockList = new List<string>();
        try
        {
            var (huntTotal, huntAdds) = DropSteps(CombatRewardCalculator.HuntDropChancePercent, bonuses.HuntDropFactors);
            dropBlockList.Add(Block(Loc.F($"# 🏹 Drop en /hunt **{Pct(huntTotal)}**"), $"Base **{Pct(CombatRewardCalculator.HuntDropChancePercent)}**", huntAdds));

            if (zoneBoxes is not null)
            {
                var (travelBase, _) = await TravelBox.ChanceAsync(zoneBoxes, player.CurrentZoneId);
                if (travelBase > 0)
                {
                    var (travelTotal, travelAdds) = DropSteps(travelBase, bonuses.TravelDropFactors);
                    dropBlockList.Add(Block(Loc.F($"# 🗺️ Caja en /travel **{Pct(travelTotal)}**"), $"Base **{Pct(travelBase)}**", travelAdds));
                }
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }

        // ---- CRÍTICO y ESQUIVE: del combate, junto al ataque, la defensa y la vida, en el mismo formato (el total grande, la base y cada agregado en puntos). Los porcentajes de DROP van aparte, en su propia sección.
        var classCrit = classBase.CritChanceBonus * 100;
        double critTotal = (CombatMath.CritChance + combat.Passives.CritChanceBonus) * 100;
        double dodgeTotal = (CombatMath.DodgeChance + combat.Passives.DodgeChanceBonus) * 100;
        var critAdds = PointSteps(CombatMath.CritChance * 100, (classCrit, x => Loc.F($"➕ Clase **+{x}**")), ((withEnchants.CritChanceBonus - classBase.CritChanceBonus) * 100, x => Loc.F($"➕ Encantamiento **+{x}**")), (bonuses.CookedCritPoints, x => Loc.F($"➕ Platos **+{x}**")));
        var dodgeAdds = PointSteps(CombatMath.DodgeChance * 100, (classBase.DodgeChanceBonus * 100, x => Loc.F($"➕ Clase **+{x}**")), ((withEnchants.DodgeChanceBonus - classBase.DodgeChanceBonus) * 100, x => Loc.F($"➕ Encantamiento **+{x}**")), (bonuses.CookedDodgePoints, x => Loc.F($"➕ Platos **+{x}**")));
        string critBlock = Block(Loc.F($"# 🎯 Crítico **{Pct(critTotal)}**"), $"Base **{Pct(CombatMath.CritChance * 100)}**", critAdds);
        string dodgeBlock = Block(Loc.F($"# 💨 Esquive **{Pct(dodgeTotal)}**"), $"Base **{Pct(CombatMath.DodgeChance * 100)}**", dodgeAdds);
        string dropSection = dropBlockList.Count == 0 ? string.Empty : "\n\n" + Loc.T("## 🎲 Chances de drop") + "\n" + string.Join("\n\n", dropBlockList);

        var embed = new EmbedBuilder()
            .WithTitle(targetName is null ? Loc.T("📊 Tus stats") : Loc.F($"📊 Stats de {targetName}"))
            .WithColor(Color.Orange)
            .WithDescription(attackBlock + "\n\n" + defenseBlock + "\n\n" + hpBlock + "\n\n" + critBlock + "\n\n" + dodgeBlock + dropSection)
            .WithFooter(Loc.T("Todo cuenta contra monstruos (en duelos y en la Arena, solo el nivel, el equipo y la clase). El perfil ya muestra estos totales. En el drop, cada ➕ cuenta sobre lo que ya llevás acumulado más arriba."));

        // ---- La segunda pantalla, «Más stats»: lo que se cocinó, el dragón, las mascotas y el Fuego Nuevo con las bendiciones (cada campo solo si hay algo que contar).
        var more = new EmbedBuilder()
            .WithTitle(targetName is null ? Loc.T("📊 Más stats") : Loc.F($"📊 Más stats de {targetName}"))
            .WithColor(Color.Orange)
            .WithFooter(Loc.T("Todo esto ya está sumado en los totales de la primera pantalla."));

        // ---- Cocina: lo apilado de esta vuelta y los platos de tiempo que corren
        var cookLines = new List<string>();
        foreach (var dish in CookingCatalog.All.Where(d => d.Kind == DishKind.Permanent))
        {
            if (bonuses.Cooked is not null && bonuses.Cooked.TryGetValue(dish.Key, out int count) && count > 0)
            {
                cookLines.Add($"{dish.Icon} {dish.Name} ×{count} → {CookingCatalog.EffectLine(dish, count)}");
            }
        }

        foreach (var dish in CookingCatalog.All.Where(d => d.Kind == DishKind.Timed))
        {
            if (activeDishes.TryGetValue(dish.Key, out var left))
            {
                cookLines.Add($"{dish.Icon} {dish.Name}: {CookingCatalog.EffectLine(dish)} · {TimeFormat.Remaining(left)}");
            }
        }

        if (cookLines.Count > 0)
        {
            more.AddField(Loc.T("🍳 Cocina (hasta el próximo Fuego Nuevo)"), string.Join("\n", cookLines), false);
        }

        // ---- Dragón
        if (bonuses.Dragon is { IsNone: false } dragonBonus)
        {
            var parts = new List<string>();
            if (dragonBonus.AttackPercent > 0) parts.Add(Loc.F($"ATQ {FuegoNuevoRules.PercentText(1 + (dragonBonus.AttackPercent / 100.0))}"));
            if (dragonBonus.DefensePercent > 0) parts.Add(Loc.F($"DEF {FuegoNuevoRules.PercentText(1 + (dragonBonus.DefensePercent / 100.0))}"));
            if (dragonBonus.HpPercent > 0) parts.Add(Loc.F($"vida {FuegoNuevoRules.PercentText(1 + (dragonBonus.HpPercent / 100.0))}"));
            if (dragonBonus.GoldPercent > 0) parts.Add(Loc.F($"oro {FuegoNuevoRules.PercentText(1 + (dragonBonus.GoldPercent / 100.0))}"));
            if (dragonBonus.XpPercent > 0) parts.Add(Loc.F($"EXP {FuegoNuevoRules.PercentText(1 + (dragonBonus.XpPercent / 100.0))}"));
            if (dragonBonus.DropPercent > 0) parts.Add(Loc.F($"drop {FuegoNuevoRules.PercentText(1 + (dragonBonus.DropPercent / 100.0))}"));
            if (dragonBonus.GatherPercent > 0) parts.Add(Loc.F($"recolección {FuegoNuevoRules.PercentText(1 + (dragonBonus.GatherPercent / 100.0))}"));
            if (dragonBonus.LastStand) parts.Add(Loc.T("aliento final"));

            string title = Loc.T("🐲 Dragón");
            try
            {
                if (await dragons.GetAsync(discordId) is { } owned)
                {
                    title = $"{DragonRules.KindEmoji(owned.Kind)} {owned.Name ?? DragonRules.TierName(owned.Tier)} ({DragonRules.TierNumeral(owned.Tier)}) lvl {owned.Level}";
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }

            more.AddField(title, string.Join(" · ", parts), false);
        }

        // ---- Mascotas
        if (!bonuses.Pets.IsNone)
        {
            more.AddField(Loc.T("🐾 Mascotas"), PetModule.BonusSummary(bonuses.Pets), false);
        }

        // ---- Fuego Nuevo y bendiciones
        var blessingNames = BlessingCatalog.All
            .Where(b => bonuses.BlessingLevel(b.Key) > 0)
            .Select(b => $"{b.Emoji} {b.Name} {BlessingCatalog.RomanLevel(bonuses.BlessingLevel(b.Key))}")
            .ToList();
        if (player.FuegoNuevo > 0 || blessingNames.Count > 0)
        {
            string fnLine = player.FuegoNuevo > 0
                ? Loc.F($"Fuego Nuevo {player.FuegoNuevo}: EXP {FuegoNuevoRules.PercentText(FuegoNuevoRules.XpMultiplier(player.FuegoNuevo))} · drop de cacería {FuegoNuevoRules.PercentText(FuegoNuevoRules.HuntDropMultiplier(player.FuegoNuevo))} · drop de viaje {FuegoNuevoRules.PercentText(FuegoNuevoRules.TravelDropMultiplier(player.FuegoNuevo))} · recolección {FuegoNuevoRules.PercentText(FuegoNuevoRules.GatherMultiplier(player.FuegoNuevo))}")
                : string.Empty;
            more.AddField(Loc.T("🔥 Fuego Nuevo y bendiciones"), string.Join("\n", new[] { fnLine, string.Join(" · ", blessingNames) }.Where(t => t.Length > 0)), false);
        }

        // Un botón para pasar de una pantalla a la otra (solo si hay segunda pantalla). Siempre lleva el dueño en el id.
        bool hasMore = more.Fields.Count > 0;
        // Las propias: «stats_page:{dueño}:{pantalla}». Las de otro: «stats_view:{quien mira}:{pantalla}:{de quién}».
        string ButtonId(int toPage) => targetName is null ? $"{PageButton}:{discordId}:{toPage}" : $"{OtherPageButton}:{viewerId ?? discordId}:{toPage}:{discordId}";
        if (page == 1 && hasMore)
        {
            var back = new ComponentBuilder().WithButton(Loc.T("Volver a las stats"), ButtonId(0), ButtonStyle.Secondary, new Emoji("↩"));
            return new StatsReply(null, more.Build(), back.Build());
        }

        var next = hasMore ? new ComponentBuilder().WithButton(Loc.T("Ver más stats"), ButtonId(1), ButtonStyle.Primary, new Emoji("➕")).Build() : null;
        return new StatsReply(null, embed.Build(), next);
    }

    // La chance de drop con cada fuente aplicada UNA TRAS OTRA sobre lo acumulado (Fuego Nuevo, mascota, bendición, dragón y, al final, los platos). Cada paso se redondea en décimas sobre el acumulado, así que la
    // suma de los agregados da EXACTO el total que se muestra (el último paso lleva el redondeo). Con el tope en 100 % también: los factores nunca bajan de 1, el tope se alcanza una vez y ahí quedan quietos.
    private static (double Total, List<string> Adds) DropSteps(double basePercent, DropFactors factors)
    {
        var steps = new (double Factor, Func<string, string> Line)[]
        {
            (factors.FuegoNuevo, x => Loc.F($"➕ Fuego Nuevo **+{x}**")),
            (factors.Pet, x => Loc.F($"➕ Mascota **+{x}**")),
            (factors.Blessing, x => Loc.F($"➕ Bendición **+{x}**")),
            (factors.Dragon, x => Loc.F($"➕ Dragón **+{x}**")),
            (factors.Cook, x => Loc.F($"➕ Platos **+{x}**")),
        };

        var adds = new List<string>();
        double running = basePercent;
        long previous = (long)Math.Round(basePercent * 10, MidpointRounding.AwayFromZero);
        foreach (var (factor, line) in steps)
        {
            if (factor <= 1.0)
            {
                continue;
            }

            running = Math.Min(100.0, running * factor);
            long tenths = (long)Math.Round(running * 10, MidpointRounding.AwayFromZero);
            if (tenths > previous)
            {
                adds.Add(line(Pct((tenths - previous) / 10.0)));
            }

            previous = tenths;
        }

        return (previous / 10.0, adds);
    }

    // Un bloque: el título grande con el total, la línea de la base y cada agregado en su renglón.
    private static string Block(string heading, string baseLine, IReadOnlyList<string> adds) =>
        heading + "\n" + baseLine + (adds.Count == 0 ? string.Empty : "\n" + string.Join("\n", adds));

    // Reparte `total` puntos entre varias fuentes en proporción a su porcentaje, sin perder ni inventar ninguno: cada una recibe la parte entera de lo suyo y los que sobran van a las de mayor resto.
    // Así base + agregados da EXACTO el total que pelea (lo que multiplican el dragón, la mascota o las bendiciones sale de un solo redondeo en el combate).
    private static int[] Share(int total, params double[] weights)
    {
        var shares = new int[weights.Length];
        double sum = weights.Sum();
        if (total <= 0 || sum <= 0)
        {
            return shares;
        }

        var exact = weights.Select(w => total * w / sum).ToArray();
        for (int i = 0; i < shares.Length; i++)
        {
            shares[i] = (int)Math.Floor(exact[i]);
        }

        int rest = total - shares.Sum();
        foreach (int i in Enumerable.Range(0, shares.Length).OrderByDescending(i => exact[i] - shares[i]).Take(rest))
        {
            shares[i]++;
        }

        return shares;
    }

    private static double BlessingAttackFactor(PlayerBonuses bonuses) => 1 + (BlessingCatalog.AttackPerLevel * bonuses.BlessingLevel(BlessingCatalog.AttackKey));

    // «+12» o «-3» (la clase puede restar vida: el Mago ×0,9 en algún ajuste).
    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string EnchantTail(string? kind, int tier) => Enchantments.IsValidTier(tier) ? $" (✨{Enchantments.ShortText(kind, tier)})" : string.Empty;

    // Los agregados en PUNTOS de una chance (crítico, esquive): cada fuente suma sobre la base y se redondea en décimas sobre lo acumulado, así que la base más los ➕ da EXACTO el total que se muestra.
    // Lo que no suma (cero o negativo) no sale.
    private static List<string> PointSteps(double basePercent, params (double Points, Func<string, string> Line)[] steps)
    {
        var adds = new List<string>();
        double running = basePercent;
        long previous = (long)Math.Round(basePercent * 10, MidpointRounding.AwayFromZero);
        foreach (var (points, line) in steps)
        {
            if (points <= 0.0001)
            {
                continue;
            }

            running += points;
            long tenths = (long)Math.Round(running * 10, MidpointRounding.AwayFromZero);
            if (tenths > previous)
            {
                adds.Add(line(Pct((tenths - previous) / 10.0)));
            }

            previous = tenths;
        }

        return adds;
    }

    // Un número con un decimal solo si hace falta, escrito como el idioma del jugador (4,5 en español, 4.5 en inglés).
    private static string Num(double value) => Loc.Decimal(value, Math.Abs(value - Math.Round(value)) < 0.05 ? 0 : 1);

    private static string Pct(double value) => Num(value) + (Loc.Current == Loc.English ? "%" : " %");
}
