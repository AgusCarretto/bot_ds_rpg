using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /stats (aa stats) — la ficha de lo que tenés AHORA contra monstruos (v0.49.0, el dueño: «un comando nuevo aa stats que te muestre las cosas actuales con encantamientos actuales y bufos actuales, pero nada de cargar el
// perfil»; v0.49.2: «el total con letra más grande, en negrita, el base y después los agregados, pero que sean parte del total, desglosados»). Para ATAQUE, DEFENSA y VIDA: un título grande con el TOTAL (el mismo que pelea:
// sale de PlayerCombatProfileCalculator.Resolve), la BASE (el nivel) y cada AGREGADO con lo que sumó en puntos, de modo que base + agregados = total EXACTO (lo que multiplica, como el banquete, el dragón o las bendiciones,
// se reparte en puntos y el último absorbe el redondeo). Abajo, en campos chicos: crítico y esquive, lo que corre de la cocina, el dragón, las mascotas y el Fuego Nuevo. Solo mira (está en la lista de FightLock) y no crea cuentas.
public sealed record StatsReply(string? PlainMessage, Embed? Embed);

public class StatsModule(IUserRepository users, IItemRepository items, IBuffRepository buffs, IPlayerBonusService bonusService, ICookingRepository cooking, IDragonRepository dragons, IZoneBoxService zoneBoxes) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("stats", "Tu ataque, defensa y vida, y cuánto aporta cada cosa (equipo, platos, dragón...).")]
    public async Task HandleAsync()
    {
        await DeferAsync();

        try
        {
            var reply = await BuildStatsAsync(users, items, buffs, bonusService, cooking, dragons, Context.User.Id, zoneBoxes);
            await FollowupAsync(reply.PlainMessage, embed: reply.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude armar tus stats ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Estático y sin Discord: lo comparte «aa stats». No crea cuentas (GetByDiscordIdAsync).
    public static async Task<StatsReply> BuildStatsAsync(
        IUserRepository users, IItemRepository items, IBuffRepository buffs, IPlayerBonusService bonusService, ICookingRepository cooking, IDragonRepository dragons, ulong discordId, IZoneBoxService? zoneBoxes = null)
    {
        var player = await users.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new StatsReply(Loc.T("Primero creá tu personaje con **/start**."), null);
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
        string dropBlocks = string.Empty;
        try
        {
            var (huntTotal, huntAdds) = DropSteps(CombatRewardCalculator.HuntDropChancePercent, bonuses.HuntDropFactors);
            dropBlocks += "\n\n" + Block(Loc.F($"# 🏹 Drop en /hunt **{Pct(huntTotal)}**"), $"Base **{Pct(CombatRewardCalculator.HuntDropChancePercent)}**", huntAdds);

            if (zoneBoxes is not null)
            {
                var (travelBase, _) = await TravelBox.ChanceAsync(zoneBoxes, player.CurrentZoneId);
                if (travelBase > 0)
                {
                    var (travelTotal, travelAdds) = DropSteps(travelBase, bonuses.TravelDropFactors);
                    dropBlocks += "\n\n" + Block(Loc.F($"# 🗺️ Caja en /travel **{Pct(travelTotal)}**"), $"Base **{Pct(travelBase)}**", travelAdds);
                }
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("📊 Tus stats"))
            .WithColor(Color.Orange)
            .WithDescription(attackBlock + "\n\n" + defenseBlock + "\n\n" + hpBlock + dropBlocks)
            .WithFooter(Loc.T("Todo cuenta contra monstruos (en duelos y en la Arena, solo el nivel, el equipo y la clase). El perfil ya muestra estos totales. En el drop, cada ➕ cuenta sobre lo que ya llevás acumulado más arriba."));

        // ---- Crítico y esquive
        var classCrit = classBase.CritChanceBonus * 100;
        double critTotal = (CombatMath.CritChance + combat.Passives.CritChanceBonus) * 100;
        double dodgeTotal = (CombatMath.DodgeChance + combat.Passives.DodgeChanceBonus) * 100;
        embed.AddField(Loc.T("🎯 Crítico y esquive"),
            Loc.F($"Crítico **{Pct(critTotal)}** (base {Pct(CombatMath.CritChance * 100)}{Points(Loc.Mark("clase"), classCrit)}{Points(Loc.Mark("encantamiento"), (withEnchants.CritChanceBonus - classBase.CritChanceBonus) * 100)}{Points(Loc.Mark("platos"), bonuses.CookedCritPoints)})") + "\n" +
            Loc.F($"Esquive **{Pct(dodgeTotal)}** (base {Pct(CombatMath.DodgeChance * 100)}{Points(Loc.Mark("clase"), classBase.DodgeChanceBonus * 100)}{Points(Loc.Mark("encantamiento"), (withEnchants.DodgeChanceBonus - classBase.DodgeChanceBonus) * 100)}{Points(Loc.Mark("platos"), bonuses.CookedDodgePoints)})"),
            false);

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
            embed.AddField(Loc.T("🍳 Cocina (hasta el próximo Fuego Nuevo)"), string.Join("\n", cookLines), false);
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

            embed.AddField(title, string.Join(" · ", parts), false);
        }

        // ---- Mascotas
        if (!bonuses.Pets.IsNone)
        {
            embed.AddField(Loc.T("🐾 Mascotas"), PetModule.BonusSummary(bonuses.Pets), false);
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
            embed.AddField(Loc.T("🔥 Fuego Nuevo y bendiciones"), string.Join("\n", new[] { fnLine, string.Join(" · ", blessingNames) }.Where(t => t.Length > 0)), false);
        }

        return new StatsReply(null, embed.Build());
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

    // «, clase +15», o nada si no suma: una fuente de puntos de crítico o de esquive dentro del paréntesis.
    private static string Points(string label, double points) =>
        points <= 0.0001 ? string.Empty : $", {Loc.Tr(label)} +{Num(points)}";

    // Un número con un decimal solo si hace falta, escrito como el idioma del jugador (4,5 en español, 4.5 en inglés).
    private static string Num(double value) => Loc.Decimal(value, Math.Abs(value - Math.Round(value)) < 0.05 ? 0 : 1);

    private static string Pct(double value) => Num(value) + (Loc.Current == Loc.English ? "%" : " %");
}
