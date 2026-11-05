using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /stats (aa stats) — la ficha de lo que tenés AHORA contra monstruos (v0.49.0, el dueño: «un comando nuevo aa stats que te muestre las cosas actuales con encantamientos actuales y bufos actuales, pero nada de cargar el
// perfil»). El perfil muestra los números ya con todo adentro; acá se ve CUÁNTO aporta cada cosa: el nivel, el arma y el amuleto con su encantamiento, lo cocinado, el banquete, el dragón, las mascotas y las bendiciones. Solo mira
// (está en la lista de FightLock) y no crea cuentas. Todo se arma con las mismas cuentas del combate (PlayerCombatProfileCalculator.Resolve), así que el total que se ve es el que pelea.
public sealed record StatsReply(string? PlainMessage, Embed? Embed);

public class StatsModule(IUserRepository users, IItemRepository items, IBuffRepository buffs, IPlayerBonusService bonusService, ICookingRepository cooking, IDragonRepository dragons) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("stats", "Tu ataque, defensa y vida, y cuánto aporta cada cosa (equipo, platos, dragón...).")]
    public async Task HandleAsync()
    {
        await DeferAsync();

        try
        {
            var reply = await BuildStatsAsync(users, items, buffs, bonusService, cooking, dragons, Context.User.Id);
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
        IUserRepository users, IItemRepository items, IBuffRepository buffs, IPlayerBonusService bonusService, ICookingRepository cooking, IDragonRepository dragons, ulong discordId)
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

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("📊 Tus stats"))
            .WithColor(Color.Orange)
            .WithDescription(Loc.T("Lo que cuenta **contra monstruos**. Los números del perfil ya incluyen todo esto; acá ves cuánto aporta cada cosa. En duelos y en la Arena cuentan solo el nivel, el equipo y la clase."));

        // ---- Ataque
        var attackParts = new List<string>
        {
            Loc.F($"nivel {CombatStats.BaseAttack(player.Level)}"),
            weapon is null ? Loc.T("sin arma") : $"{weapon.Name} {PlayerCombatProfileCalculator.WeaponDamage(player, weapon)}" + EnchantTail(player.WeaponEnchantKind, player.WeaponEnchant),
        };
        if (bonuses.CookedAttack > 0) attackParts.Add(Loc.F($"platos +{bonuses.CookedAttack}"));
        var attackMults = new List<string>();
        if (bonuses.Dragon is { AttackPercent: > 0 } dragonAtk) attackMults.Add(Loc.F($"dragón {FuegoNuevoRules.PercentText(1 + (dragonAtk.AttackPercent / 100.0))}"));
        double blessingAtk = 1 + (BlessingCatalog.AttackPerLevel * bonuses.BlessingLevel(BlessingCatalog.AttackKey));
        if (blessingAtk > 1.0) attackMults.Add(Loc.F($"bendición {FuegoNuevoRules.PercentText(blessingAtk)}"));
        if (buff is not null) attackMults.Add(Loc.F($"banquete +{buff.AttackPercent} % ({Math.Max(1, (int)Math.Ceiling(buff.Remaining.TotalMinutes))} min)"));
        embed.AddField(Loc.F($"⚔️ Ataque: {combat.Damage}"), Breakdown(attackParts, attackMults), false);

        // ---- Defensa
        var defenseParts = new List<string>
        {
            Loc.F($"nivel {CombatStats.BaseDefense(player.Level)}"),
            amulet is null ? Loc.T("sin amuleto") : $"{amulet.Name} {PlayerCombatProfileCalculator.AmuletDefense(player, amulet)}" + EnchantTail(player.AmuletEnchantKind, player.AmuletEnchant),
        };
        if (bonuses.CookedDefense > 0) defenseParts.Add(Loc.F($"platos +{bonuses.CookedDefense}"));
        var defenseMults = new List<string>();
        if (bonuses.Pets.DefensePercent > 0) defenseMults.Add(Loc.F($"mascota {FuegoNuevoRules.PercentText(1 + (bonuses.Pets.DefensePercent / 100.0))}"));
        double blessingDef = 1 + (BlessingCatalog.DefensePerLevel * bonuses.BlessingLevel(BlessingCatalog.DefenseKey));
        if (blessingDef > 1.0) defenseMults.Add(Loc.F($"bendición {FuegoNuevoRules.PercentText(blessingDef)}"));
        if (bonuses.Dragon is { DefensePercent: > 0 } dragonDef) defenseMults.Add(Loc.F($"dragón {FuegoNuevoRules.PercentText(1 + (dragonDef.DefensePercent / 100.0))}"));
        embed.AddField(Loc.F($"🛡️ Defensa: {combat.Defense}"), Breakdown(defenseParts, defenseMults), false);

        // ---- Vida (lo que se VE: la vida base por el factor de la clase y los encantamientos, más lo cocinado exacto)
        var classBase = ClassPassives.For(player.Class);
        var withEnchants = Enchantments.ApplyEffects(classBase, player);
        var hpParts = new List<string> { Loc.F($"base {player.MaxHp}") };
        double classFactor = classBase.MaxHpMultiplier * PveTuning.HpFactor(player.Class);
        if (Math.Abs(classFactor - 1.0) > 0.0001) hpParts.Add(Loc.F($"clase {player.Class} ×{Loc.Decimal(classFactor, 2)}"));
        double enchantHp = withEnchants.MaxHpMultiplier / classBase.MaxHpMultiplier;
        if (enchantHp > 1.0) hpParts.Add(Loc.F($"encantamiento {FuegoNuevoRules.PercentText(enchantHp)}"));
        if (bonuses.MaxHpMultiplier > 1.0) hpParts.Add(Loc.F($"bendición y dragón {FuegoNuevoRules.PercentText(bonuses.MaxHpMultiplier)}"));
        if (bonuses.CookedMaxHp > 0) hpParts.Add(Loc.F($"platos +{bonuses.CookedMaxHp}"));
        embed.AddField(Loc.F($"❤️ Vida máxima: {combat.CombatMaxHp}"), Breakdown(hpParts, []), false);

        // ---- Crítico y esquive
        double critTotal = (CombatMath.CritChance + combat.Passives.CritChanceBonus) * 100;
        double dodgeTotal = (CombatMath.DodgeChance + combat.Passives.DodgeChanceBonus) * 100;
        embed.AddField(Loc.T("🎯 Crítico y esquive"),
            Loc.F($"Crítico **{Pct(critTotal)}** (base {Pct(CombatMath.CritChance * 100)}{Points(Loc.Mark("clase"), classBase.CritChanceBonus * 100)}{Points(Loc.Mark("encantamiento"), (withEnchants.CritChanceBonus - classBase.CritChanceBonus) * 100)}{Points(Loc.Mark("platos"), bonuses.CookedCritPoints)})") + "\n" +
            Loc.F($"Esquive **{Pct(dodgeTotal)}** (base {Pct(CombatMath.DodgeChance * 100)}{Points(Loc.Mark("clase"), classBase.DodgeChanceBonus * 100)}{Points(Loc.Mark("encantamiento"), (withEnchants.DodgeChanceBonus - classBase.DodgeChanceBonus) * 100)}{Points(Loc.Mark("platos"), bonuses.CookedDodgePoints)})"),
            false);

        // ---- Encantamientos
        embed.AddField(Loc.T("✨ Encantamientos"),
            Loc.F($"Arma: {EnchantLine(player.WeaponEnchantKind, weapon is null ? 0 : player.WeaponEnchant)}") + "\n" + Loc.F($"Amuleto: {EnchantLine(player.AmuletEnchantKind, amulet is null ? 0 : player.AmuletEnchant)}"), false);

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

    // «nivel 39 · Espada 48 (✨…) · platos +2», y debajo «Además multiplica: dragón +7 % · …» solo si hay algo.
    private static string Breakdown(IReadOnlyList<string> parts, IReadOnlyList<string> multipliers) =>
        string.Join(" + ", parts) + (multipliers.Count == 0 ? string.Empty : "\n" + Loc.F($"Además multiplica: {string.Join(" · ", multipliers)}"));

    private static string EnchantTail(string? kind, int tier) => Enchantments.IsValidTier(tier) ? $" (✨{Enchantments.ShortText(kind, tier)})" : string.Empty;

    private static string EnchantLine(string? kind, int tier) =>
        Enchantments.IsValidTier(tier) ? $"{Enchantments.KindLabel(kind, tier)} · {Enchantments.ShortText(kind, tier)}" : Loc.T("sin encantar");

    // «, clase +15», o nada si no suma: una fuente de puntos de crítico o de esquive dentro del paréntesis de la ficha.
    private static string Points(string label, double points) =>
        points <= 0.0001 ? string.Empty : $", {Loc.Tr(label)} +{Num(points)}";

    // Un número con un decimal solo si hace falta, escrito como el idioma del jugador (4,5 en español, 4.5 en inglés).
    private static string Num(double value) => Loc.Decimal(value, Math.Abs(value - Math.Round(value)) < 0.05 ? 0 : 1);

    private static string Pct(double value) => Num(value) + (Loc.Current == Loc.English ? "%" : " %");
}
