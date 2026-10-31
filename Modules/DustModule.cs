using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

// /dismantle y /enchant: el Polvo. Desmantelar rompe un MATERIAL (madera, mineral o drop de monstruo) y da Polvo (GameData/Dismantling.cs); encantar gasta
// Polvo + oro en un intento de mejorar el arma o el amuleto (GameData/Enchantments.cs). Las reglas viven en los métodos estáticos de abajo (sin Context) para que
// "aa dismantle" y "aa enchant" hagan exactamente lo mismo; el dinero y los ítems se mueven en IDustRepository con guardas atómicas.
public class DustModule(
    IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents, IPlayerBonusService bonusService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("dismantle", "Desmantelá materiales para conseguir Polvo (la cantidad que quieras, o todo).")]
    public async Task HandleDismantleAsync(
        [Summary("item", "Elegí de la lista el material que querés desmantelar.")] [Autocomplete(typeof(DismantleItemAutocompleteHandler))] string itemName,
        [Summary("cantidad", "Cuántos desmantelás: un número o \"todo\" (por defecto 1).")] string quantity = "1")
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteDismantleAsync(userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, itemName, quantity);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude desmantelar, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [SlashCommand("enchant", "Encantá tu arma o tu amuleto con oro y Polvo (sin elegir, ves cómo estás).")]
    public async Task HandleEnchantAsync(
        [Summary("pieza", "Qué encantar. Sin elegir ves lo tuyo; con Info, los tiers, sus chances y costos.")]
        [Choice("Arma", "weapon"), Choice("Amuleto", "amulet"), Choice("Info: tiers, chances y costos", "info")] string? slot = null,
        [Summary("modo", "Normal, o Avanzado (2 tiradas, te quedás la mejor, 1,5× el costo; pide el oficio Encantador al 100).")]
        [Choice("Normal", "normal"), Choice("Avanzado", "avanzado")] string modo = "normal",
        [Summary("efecto", "Arma: Filo, Crítico o Sangre. Amuleto: Guarda, Vida o Esquive. Sin elegir, el que ya tiene.")]
        [Choice("Filo (arma)", "filo"), Choice("Crítico (arma)", "critico"), Choice("Sangre (arma)", "sangre"), Choice("Guarda (amuleto)", "guarda"), Choice("Vida (amuleto)", "vida"), Choice("Esquive (amuleto)", "esquive")] string? effect = null)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteEnchantAsync(
                userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, slot, bonusService: bonusService, advanced: GatheringModule.ParseAdvanced(modo) ?? false, effect: effect);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, components: result.Components, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude encantar, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // La confirmación de CAMBIAR DE EFECTO (v0.22.0): reemplaza el encantamiento que ya tiene la pieza, así que se pregunta antes de cobrar. Los botones llevan el dueño, la pieza, el efecto
    // y si es avanzado en su id (hasta 100 caracteres): enchant_switch:{owner}:{weapon|amulet}:{efecto}:{0|1}. Solo el dueño los puede apretar.
    [ComponentInteraction("enchant_switch:*:*:*:*")]
    public async Task HandleSwitchAsync(string ownerId, string slot, string kind, string advanced)
    {
        if (!ulong.TryParse(ownerId, out ulong owner) || owner != Context.User.Id)
        {
            await RespondAsync(Loc.T("Esa confirmación no es tuya."), ephemeral: true);
            return;
        }

        await DeferAsync();

        try
        {
            var result = await ExecuteEnchantAsync(
                userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, slot, bonusService: bonusService, advanced: advanced == "1", effect: kind, confirmedSwitch: true);
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = result.PlainMessage ?? string.Empty;
                m.Embed = result.Embed;
                m.Components = result.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude encantar, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction("enchant_cancel:*")]
    public async Task HandleSwitchCancelAsync(string ownerId)
    {
        if (!ulong.TryParse(ownerId, out ulong owner) || owner != Context.User.Id)
        {
            await RespondAsync(Loc.T("Esa confirmación no es tuya."), ephemeral: true);
            return;
        }

        await DeferAsync();
        await ModifyOriginalResponseAsync(m =>
        {
            m.Content = Loc.T("Listo: no cambiaste nada y no se gastó nada.");
            m.Embed = null;
            m.Components = new ComponentBuilder().Build();
        });
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que GiveModule.GiveResult).
    // Components (v0.22.0): los botones de la confirmación de cambiar de efecto.
    public sealed record DustResult(string? PlainMessage, Embed? Embed, MessageComponent? Components = null);

    private static string NoAccount => Loc.T("Todavía no tenés cuenta: empezá con **/start**.");

    // La cantidad como la escribe el jugador (v0.16.2): un número ("30", "1.000") o "todo" (también all/max: TODAS las unidades que tiene de ese ítem). Sin tope por comando:
    // el único límite es lo que hay en la mochila, y la resta sigue siendo guardada (si en el medio cambió, se rechaza y no se toca nada).
    public static async Task<DustResult> ExecuteDismantleAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string itemName, string quantityText)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return new DustResult(Loc.T("¿Qué querés desmantelar? Por ejemplo `aa desmantelar 30 Piedra` o `aa desmantelar todo Piedra`."), null);
        }

        if (AmountParser.IsAllWord(quantityText))
        {
            if (await userRepository.GetByDiscordIdAsync(discordId) is null)
            {
                return new DustResult(NoAccount, null);
            }

            var wanted = await itemRepository.GetByNameAsync(itemName);
            int held = wanted is null ? 0 : await dustRepository.GetQuantityAsync(discordId, wanted.ItemId);
            if (wanted is not null && Dismantling.CanDismantle(wanted.Type) && held < 1)
            {
                return new DustResult(Loc.F($"No tenés **{ItemDisplay.Format(wanted.Emoji, wanted.Name)}** para desmantelar: mirá qué tenés en **/inventory**."), null);
            }

            // Un ítem que no existe o que no se desmantela (held = 0) cae al camino de siempre, que lo explica.
            return await ExecuteDismantleAsync(userRepository, itemRepository, dustRepository, gameEvents, discordId, itemName, Math.Max(held, 1));
        }

        var parsed = AmountParser.Parse(quantityText, int.MaxValue);
        if (!parsed.Ok)
        {
            bool numeric = !string.IsNullOrWhiteSpace(quantityText) && quantityText.Trim().All(c => char.IsDigit(c) || c is '.' or ',' or ' ' or '_');
            return new DustResult(
                numeric ? parsed.Error! : Loc.F($"No entendí la cantidad **{quantityText.Trim()}**: escribí un número (por ejemplo 30) o **todo**."), null);
        }

        return await ExecuteDismantleAsync(userRepository, itemRepository, dustRepository, gameEvents, discordId, itemName, parsed.Amount);
    }

    public static async Task<DustResult> ExecuteDismantleAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string itemName, int quantity)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new DustResult(NoAccount, null);
        }

        if (quantity < 1)
        {
            return new DustResult(Loc.T("La cantidad tiene que ser al menos 1."), null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        int dustPerUnit = item is null ? 0 : Dismantling.DustPerUnit(item.Rarity, item.DustValue);
        if (item is null || !Dismantling.CanDismantle(item.Type) || dustPerUnit <= 0)
        {
            return new DustResult(Loc.F($"**{itemName.Trim()}** no se puede desmantelar: solo se desmantelan materiales (madera, minerales y drops de monstruos)."), null);
        }

        // Sin tope por comando, la cuenta se hace en long: una cantidad absurda no puede desbordar el Polvo ni salir negativa.
        long dustLong = (long)dustPerUnit * quantity;
        if (dustLong > int.MaxValue)
        {
            return new DustResult(Loc.T("Esa cantidad es demasiado grande."), null);
        }

        int dustGained = (int)dustLong;

        var outcome = await dustRepository.DismantleAsync(discordId, item.ItemId, quantity, dustGained);
        if (outcome is null)
        {
            return new DustResult(Loc.F($"No tenés **{quantity}** de **{ItemDisplay.Format(item.Emoji, item.Name)}** para desmantelar: mirá cuántos tenés en **/inventory**."), null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.Dismantle, amount: quantity, detail: item.Name);

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🔨 ¡Desmantelado!"))
            .WithColor(Color.Teal)
            .WithItemThumbnail(item.Emoji)
            .WithDescription(Loc.F($"Rompiste **{quantity}×** {ItemDisplay.Format(item.Emoji, item.Name)}.\n\n✨ Juntaste **+{GameHistory.Number(dustGained)}** de Polvo."))
            .AddField(Loc.T("✨ Tu Polvo"), $"**{GameHistory.Number(outcome.DustAfter)}**", false)
            .AddField(Loc.T("🎒 Te quedan"), $"**{GameHistory.Number(outcome.QuantityLeft)}**", false)
            .WithFooter(Loc.T("El Polvo sirve para encantar tu arma y tu amuleto: /enchant"));
        return new DustResult(null, embed.Build());
    }

    // slot: "weapon" / "amulet" (o "arma" / "amuleto" desde "aa enchant"); null = solo mirar.
    public static async Task<DustResult> ExecuteEnchantAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string? slot, Random? rng = null, IPlayerBonusService? bonusService = null, bool advanced = false, string? effect = null, bool confirmedSwitch = false)
    {
        // "info" (también "opciones"/"ayuda"): cómo funciona, los tiers con su chance y su bonus, y lo que cuesta cada intento. Es lo mismo para todos: no toca la cuenta.
        if (IsInfoRequest(slot))
        {
            return new DustResult(null, BuildOptionsEmbed());
        }

        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new DustResult(NoAccount, null);
        }

        // El oficio Encantador (v0.13.0): su nivel baja el Polvo de cada intento y, al máximo, habilita el encantamiento avanzado. Sin servicio (llamadores viejos) no hay oficio.
        var bonuses = bonusService is null ? null : await bonusService.GetAsync(discordId, player.FuegoNuevo);
        var enchanter = ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey);
        var progressBefore = ProfessionRules.ProgressFor(bonuses?.ProfessionXpOf(enchanter.Key) ?? 0);

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;

        string? slotKey = NormalizeSlot(slot);
        if (string.IsNullOrWhiteSpace(slot))
        {
            return new DustResult(null, BuildInfoEmbed(player, weapon, amulet, bonuses));
        }

        if (slotKey is null)
        {
            return new DustResult(Loc.T("Elegí qué encantar: **arma** o **amuleto**."), null);
        }

        bool isWeapon = slotKey == "weapon";
        var gear = isWeapon ? weapon : amulet;
        if (gear is null)
        {
            return new DustResult(isWeapon ? Loc.T("No tenés un arma puesta para encantar: forjá una con **/forge**.") : Loc.T("No tenés un amuleto puesto para encantar: forjá uno con **/forge**."), null);
        }

        // El efecto (v0.22.0): el jugador lo ELIGE (el tier lo sortea la suerte); sin elegir queda el que ya tiene la pieza. Uno que no es de esa pieza se rechaza explicando cuáles hay.
        string currentKind = isWeapon ? player.WeaponEnchantKind : player.AmuletEnchantKind;
        int currentTier = isWeapon ? player.WeaponEnchant : player.AmuletEnchant;
        string? chosen = null;
        if (!string.IsNullOrWhiteSpace(effect))
        {
            chosen = Enchantments.ParseKind(slotKey, effect);
            if (chosen is null)
            {
                return new DustResult((isWeapon ? Loc.F($"**{effect.Trim()}** no es un efecto del arma. Elegí entre: {EffectNames(slotKey)}.") : Loc.F($"**{effect.Trim()}** no es un efecto del amuleto. Elegí entre: {EffectNames(slotKey)}.")), null);
            }
        }

        string kindKey = chosen ?? currentKind;

        if (advanced && !ProfessionRules.AdvancedUnlocked(progressBefore.Level))
        {
            return new DustResult(
                Loc.F($"El **{enchanter.DisplayAdvancedName.ToLowerInvariant()}** se desbloquea con el oficio **{enchanter.DisplayName}** al nivel **{ProfessionRules.MaxLevel}** y el tuyo está en el **{progressBefore.Level}**. Mirá tu avance con **/professions**."), null);
        }

        // El Polvo de cada intento baja con el nivel del oficio; el avanzado cuesta 1,5 veces (oro y Polvo).
        var cost = ProfessionRules.EnchantCost(Enchantments.Cost(Enchantments.GearRank(gear.Rarity)), progressBefore.Level, advanced);
        if (player.Gold < cost.Gold)
        {
            return new DustResult((advanced ? Loc.F($"No te alcanza el oro: cada intento avanzado sobre **{gear.Name}** cuesta **{GameHistory.Number(cost.Gold)}** de oro y **{cost.Dust}** de Polvo.") : Loc.F($"No te alcanza el oro: cada intento sobre **{gear.Name}** cuesta **{GameHistory.Number(cost.Gold)}** de oro y **{cost.Dust}** de Polvo.")), null);
        }

        if (player.Dust < cost.Dust)
        {
            return new DustResult((advanced ? Loc.F($"Te falta Polvo: cada intento avanzado sobre **{gear.Name}** cuesta **{cost.Dust}** (tenés **{player.Dust}**). Juntalo desmantelando materiales con **/dismantle**.") : Loc.F($"Te falta Polvo: cada intento sobre **{gear.Name}** cuesta **{cost.Dust}** (tenés **{player.Dust}**). Juntalo desmantelando materiales con **/dismantle**.")), null);
        }

        // Cambiar de efecto REEMPLAZA el encantamiento que ya tiene la pieza (con el tier que salga): se pregunta antes de cobrar nada.
        if (kindKey != currentKind && currentTier > 0 && !confirmedSwitch)
        {
            return new DustResult(null, BuildSwitchConfirmEmbed(gear, slotKey, currentKind, currentTier, kindKey, cost), BuildSwitchButtons(discordId, slotKey, kindKey, advanced));
        }

        // Avanzado: dos tiradas y se queda la mejor (igual el tier final nunca baja el que ya tenía la pieza, eso lo decide TryEnchantAsync).
        // Cada tirada cuenta con la ayuda del oficio Encantador (v0.22.1): con esa chance el tier sorteado sube uno.
        double tierUpgrade = ProfessionRules.TierUpgradeChance(progressBefore.Level);
        var rolled2 = Enumerable.Range(0, advanced ? ProfessionRules.AdvancedEnchantRolls : 1).Select(_ => Enchantments.RollWithBonus(rng, tierUpgrade)).ToArray();
        int[] rolls = rolled2.Select(x => x.Tier).ToArray();
        int rolled = rolls.Max();
        bool upgradedByProfession = rolled2.Any(x => x.Upgraded && x.Tier == rolled);
        var outcome = await dustRepository.TryEnchantAsync(discordId, slotKey, rolled, cost.Gold, cost.Dust, kindKey, allowSwitch: confirmedSwitch);
        if (outcome.Status != EnchantStatus.Ok)
        {
            return new DustResult(outcome.Status switch
            {
                EnchantStatus.NoGear => isWeapon ? Loc.T("No tenés un arma puesta para encantar.") : Loc.T("No tenés un amuleto puesto para encantar."),
                EnchantStatus.NotEnoughGold => Loc.T("No te alcanza el oro para este intento."),
                EnchantStatus.NotEnoughDust => Loc.T("Te falta Polvo para este intento."),
                EnchantStatus.NeedsConfirm => Loc.T("Cambiar de efecto reemplaza el que tiene la pieza: volvé a pedirlo y confirmá."),
                _ => NoAccount,
            }, null);
        }

        // Cada tirada cuenta como un intento (el contador de «enchant» es la XP del oficio y el del logro Encantador).
        await gameEvents.RecordAsync(discordId, GameEventKinds.Enchant, amount: rolls.Length, detail: $"{slotKey}:{rolled}:{kindKey}");

        var progressAfter = ProfessionRules.ProgressFor(progressBefore.TotalXp + ((long)rolls.Length * enchanter.XpPerAction));
        string footer = GatheringModule.ProfessionFooter(enchanter, progressBefore, progressAfter) + (upgradedByProfession ? Loc.T(" · ⬆️ ¡tu oficio mejoró el tier!") : string.Empty);
        return new DustResult(null, BuildResultEmbed(player, gear, slotKey, outcome, cost, footer, advanced ? rolls : null));
    }

    // «Filo, Crítico o Sangre» / «Guarda, Vida o Esquive»: los efectos de esa pieza, para los mensajes.
    public static string EffectNames(string slotKey)
    {
        var names = Enchantments.KindsFor(slotKey).Select(k => $"**{k.Name}**").ToList();
        return names.Count <= 1 ? string.Concat(names) : Loc.F($"{string.Join(", ", names.Take(names.Count - 1))} o {names[^1]}");
    }

    // Público y puro: la pregunta de «¿seguro?» cuando se pide OTRO efecto sobre una pieza que ya tiene tier. No se cobró nada.
    public static Embed BuildSwitchConfirmEmbed(Item gear, string slotKey, string currentKind, int currentTier, string newKind, (int Gold, int Dust) cost)
    {
        string now = $"{Enchantments.KindLabel(currentKind, currentTier)} ({Enchantments.ValueText(currentKind, currentTier)})";
        string target = Enchantments.FindKind(newKind)?.Name ?? newKind;
        return new EmbedBuilder()
            .WithTitle(Loc.T("🔄 ¿Cambiar de efecto?"))
            .WithColor(Color.Orange)
            .WithItemThumbnail(gear.Emoji)
            .WithDescription(
                Loc.F($"{ItemDisplay.Format(gear.Emoji, gear.Name)} tiene **{now}**.\n\n") +
                Loc.F($"Si seguís, ese encantamiento se **reemplaza** por **{target}** con el tier que salga (puede ser más bajo) y el intento cuesta **{GameHistory.Number(cost.Gold)}** oro y **{cost.Dust}** de Polvo."))
            .WithFooter(Loc.T("Si solo querías subir el tier que ya tenés, volvé a pedirlo sin elegir efecto."))
            .Build();
    }

    // Los botones de esa pregunta; el id lleva el dueño (solo él puede apretarlos), la pieza, el efecto y si era avanzado.
    public static MessageComponent BuildSwitchButtons(ulong owner, string slotKey, string newKind, bool advanced) =>
        new ComponentBuilder()
            .WithButton(Loc.T("Sí, cambiar"), $"enchant_switch:{owner}:{slotKey}:{newKind}:{(advanced ? 1 : 0)}", ButtonStyle.Danger)
            .WithButton(Loc.T("No, dejarlo como está"), $"enchant_cancel:{owner}", ButtonStyle.Secondary)
            .Build();

    // "info", "opciones"/"options", "ayuda"/"help", "chances" o "tiers": lo que pide quien quiere ver cómo funciona el encantamiento.
    public static bool IsInfoRequest(string? text) => text?.Trim().ToLowerInvariant() is "info" or "opciones" or "options" or "ayuda" or "help" or "chances" or "tiers";

    private static string? NormalizeSlot(string? slot) => slot?.Trim().ToLowerInvariant() switch
    {
        "weapon" or "arma" => "weapon",
        "amulet" or "amuleto" => "amulet",
        _ => null,
    };

    // Público y puro: se prueba sin Discord. Lo que rinde la pieza antes y después, con el encantamiento aplicado igual que en el combate.
    // professionFooter: el pie del oficio Encantador (nivel y XP); sin él queda el de siempre. allRolls: las tiradas del encantamiento avanzado (se muestran las dos y se queda la mejor).
    public static Embed BuildResultEmbed(
        User playerBefore, Item gear, string slotKey, EnchantOutcome outcome, (int Gold, int Dust) cost, string? professionFooter = null, int[]? allRolls = null)
    {
        bool isWeapon = slotKey == "weapon";
        int previous = outcome.PreviousTier, rolled = outcome.RolledTier, now = outcome.NewTier;
        // El efecto de antes y el que quedó (v0.22.0): sin dato (llamadores viejos) es el de siempre de esa pieza.
        string prevKind = outcome.PreviousKind ?? Enchantments.DefaultKind(slotKey);
        string newKind = outcome.NewKind ?? prevKind;
        bool switched = prevKind != newKind;
        string rolledText = $"{Enchantments.KindLabel(newKind, rolled)} ({Enchantments.ValueText(newKind, rolled)})";
        string rollsText = allRolls is { Length: > 1 }
            ? Loc.F($"Tiraste {allRolls.Length} veces ({string.Join(" y ", allRolls.Select(r => Enchantments.TierName(r)))}) y te quedás con la mejor: **{rolledText}**.")
            : Loc.F($"Sobre {ItemDisplay.Format(gear.Emoji, gear.Name)} salió **{rolledText}**.");

        string verdict = switched && previous > 0
            ? Loc.F($"🔄 **¡Cambiaste de efecto!** {Enchantments.KindLabel(prevKind, previous)} ➜ **{Enchantments.KindLabel(newKind, now)}**")
            : now > previous
                ? Loc.F($"🔥 **¡Mejoró!** {Enchantments.KindLabel(newKind, previous)} ➜ **{Enchantments.KindLabel(newKind, now)}**")
                : rolled == previous
                    ? Loc.F($"Era justo el que ya tenías: seguís con **{Enchantments.KindLabel(newKind, previous)}**.")
                    : Loc.F($"Ya tenías **{Enchantments.KindLabel(newKind, previous)}**, que es mejor: te quedás con ese.");

        // Lo que rinde la pieza antes y después (el arma de clase ya trae el ×1,5 de la vieja sinergia en su stat, v0.27.0).
        int baseStat = gear.StatValue;
        int before = Enchantments.Apply(baseStat, prevKind, Enchantments.StatTier(prevKind, previous)), after = Enchantments.Apply(baseStat, newKind, Enchantments.StatTier(newKind, now));

        var embed = new EmbedBuilder()
            .WithTitle(isWeapon ? Loc.T("✨ Encantamiento: arma") : Loc.T("✨ Encantamiento: amuleto"))
            .WithColor(now > previous ? Color.Gold : Color.DarkGrey)
            .WithItemThumbnail(gear.Emoji)
            .WithDescription((allRolls is { Length: > 1 } ? Loc.F($"Sobre {ItemDisplay.Format(gear.Emoji, gear.Name)}: ") : string.Empty) + $"{rollsText}\n\n{verdict}")
            .AddField(isWeapon ? Loc.T("⚔️ ATQ del arma") : Loc.T("🛡️ DEF del amuleto"), before == after ? $"**{after}**" : $"{before} ➜ **{after}**", false)
            .AddField(Loc.T("💸 Costó"), Loc.F($"{GameHistory.Number(cost.Gold)} oro · {cost.Dust} Polvo"), false)
            .AddField(Loc.T("🎒 Te quedan"), Loc.F($"{GameHistory.Number(outcome.User!.Gold)} oro · {GameHistory.Number(outcome.User.Dust)} Polvo"), false)
            .WithFooter(string.IsNullOrWhiteSpace(professionFooter) ? Loc.T("Cada intento se paga igual: el tier sale al azar y nunca baja.") : professionFooter);

        // Un efecto de combate no cambia el stat de la pieza: se dice qué hace y que solo vale contra monstruos.
        if (!Enchantments.IsStatKind(newKind))
        {
            embed.AddField(Loc.T("🎯 Efecto"), Loc.F($"**{Enchantments.ValueText(newKind, now)}** · {Loc.Tr(Enchantments.FindKind(newKind)?.Summary ?? string.Empty)}. Solo cuenta contra monstruos."), false);
        }

        return embed.Build();
    }

    // Público y puro: lo que se ve con "/enchant" sin elegir pieza.
    public static Embed BuildInfoEmbed(User player, Item? weapon, Item? amulet, PlayerBonuses? bonuses = null)
    {
        int enchanterLevel = bonuses?.ProfessionLevel(ProfessionCatalog.EnchanterKey) ?? 0;
        string Line(string slotKey, Item? gear, int tier, string kind)
        {
            if (gear is null)
            {
                return slotKey == "weapon" ? Loc.T("_Sin arma puesta_") : Loc.T("_Sin amuleto puesto_");
            }

            // El Polvo ya con el descuento del oficio Encantador.
            var cost = ProfessionRules.EnchantCost(Enchantments.Cost(Enchantments.GearRank(gear.Rarity)), enchanterLevel, advanced: false);
            string current = tier > 0 ? $"{Enchantments.KindLabel(kind, tier)} ({Enchantments.ValueText(kind, tier)})" : Loc.T("Sin encantar");
            return Loc.F($"{ItemDisplay.Format(gear.Emoji, gear.Name)}\n**{current}**\nPróximo intento: {GameHistory.Number(cost.Gold)} oro + {cost.Dust} Polvo");
        }

        // Los tiers, en dos renglones cortos (en uno solo se apretaban y se partían a mitad de nombre).
        // Solo el nombre: cuánto da cada tier depende del EFECTO (el Filo y la Guarda ya no suman lo mismo desde la v0.25.1), y eso lo dice «/enchant info».
        string Tier(int t) => $"{Enchantments.TierName(t)} **({ShortPercent(Enchantments.ChancePercent(t))})**";
        string tiers = $"{string.Join("  ·  ", Enumerable.Range(1, 3).Select(Tier))}\n{string.Join("  ·  ", Enumerable.Range(4, Enchantments.MaxTier - 3).Select(Tier))}";
        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("✨ Encantamientos"))
            .WithColor(Color.Purple)
            .WithDescription(
                Loc.T("Mejorá tu arma o tu amuleto con **Polvo** y oro: **/enchant arma** o **/enchant amuleto**.\n\n") +
                Loc.T("El Polvo lo juntás desmantelando materiales con **/dismantle**.\n\n") +
                Loc.T("Cada intento sortea un tier al azar y **nunca baja** el que ya tenés: te quedás con el mejor.\n\n") +
                Loc.T("Elegís el **efecto** con **/enchant arma efecto:Crítico** (mirá cuáles hay en **/enchant info**)."))
            .AddField(Loc.T("⚔️ Arma"), Line("weapon", weapon, player.WeaponEnchant, player.WeaponEnchantKind), false)
            .AddField(Loc.T("🛡️ Amuleto"), Line("amulet", amulet, player.AmuletEnchant, player.AmuletEnchantKind), false)
            .AddField(Loc.T("✨ Tu Polvo"), $"**{GameHistory.Number(player.Dust)}**", false);

        // El oficio Encantador (solo si el llamador lo pasó): su nivel y lo que ya te ahorra.
        if (bonuses is not null)
        {
            var enchanter = ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey);
            var progress = ProfessionRules.ProgressFor(bonuses.ProfessionXpOf(enchanter.Key));
            embed.AddField(
                Loc.F($"{enchanter.Emoji} {enchanter.DisplayName} · nivel {progress.Level}"),
                progress.Level == 0 ? Loc.T("Cada intento suma XP y baja el Polvo que cuesta el siguiente: **/professions**.") : $"{ProfessionRules.DescribeEffect(enchanter, progress.Level)}.",
                false);
        }

        return embed
            .AddField(Loc.T("🎲 Tiers posibles"), tiers, false)
            .WithFooter(Loc.T("Para ver el detalle de cada tier (chance y bonus) y lo que cuesta cada intento: /enchant info"))
            .Build();
    }

    // Público y puro: "/enchant info", "aa enchant info" y "aa info enchant". Todo lo que hay que saber antes de gastar: cómo funciona, los tiers con su CHANCE y su BONUS,
    // cuántos intentos llevan en promedio, lo que cuesta cada intento según la zona de la pieza y de dónde sale el Polvo. Todos los números salen de GameData/Enchantments.cs
    // y GameData/Dismantling.cs (nada escrito a mano acá), así que esta pantalla no se puede desactualizar si se retocan.
    public static Embed BuildOptionsEmbed()
    {
        string Attempts(int tier)
        {
            double attempts = Enchantments.AttemptsToReach(tier);
            return attempts <= 1.05 ? string.Empty : Loc.F($" · ~{Loc.Decimal(attempts, attempts < 10 ? 1 : 0)} intentos para llegar");
        }

        string tiers = string.Join('\n', Enumerable.Range(1, Enchantments.MaxTier).Select(t =>
            Loc.F($"**{Enchantments.TierName(t)}** — sale el **{ShortPercent(Enchantments.ChancePercent(t))}**{Attempts(t)}")));

        // Los efectos de cada pieza con sus cinco valores (del tier I al V), leídos de la tabla: no se puede desactualizar.
        string EffectBlock(string slotKey, string title) =>
            $"**{title}**\n" + string.Join('\n', Enchantments.KindsFor(slotKey).Select(k => $"• **{k.Name}**: {Enchantments.DescribeValues(k)}"));
        string effects =
            EffectBlock("weapon", "Arma") + "\n" + EffectBlock("amulet", "Amuleto") + "\n" +
            Loc.T("Elegís el efecto y el **tier se sortea**. **Cambiar de efecto reemplaza el que tenés** (se pregunta antes). ") +
            Loc.T("Filo y Guarda cuentan en todos lados; **Crítico, Sangre, Vida y Esquive solo contra monstruos**.");

        string costs = string.Join('\n', Enumerable.Range(1, 5).Select(rank =>
        {
            var cost = Enchantments.Cost(rank);
            return Loc.F($"Pieza de **Zona {rank}**: {GameHistory.Number(cost.Gold)} oro + {cost.Dust} Polvo");
        }));

        string dust = string.Join(" · ", new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Select(r => $"{RarityCatalog.DisplayName(r)} {GameHistory.Number(Dismantling.DustPerUnit(r))}"))
            + Loc.F($"\nDrops de monstruos (no tienen rareza): **{Dismantling.HuntDropDust}** cada uno.");

        return new EmbedBuilder()
            .WithTitle(Loc.T("✨ Encantamientos: opciones"))
            .WithColor(Color.Purple)
            .WithDescription(
                Loc.T("Mejorá tu **arma** o tu **amuleto** gastando **Polvo** y oro: **/enchant arma** o **/enchant amuleto**.\n\n") +
                Loc.T("Cada intento sortea un tier **al azar**. El tier **nunca baja**: si sale uno peor que el que tenés, te quedás con el que tenés (pero el intento se paga igual).\n\n") +
                Loc.T("Si vendés o cambiás la pieza, **pierde su encantamiento**."))
            .AddField(Loc.T("🎯 Los efectos (elegís uno por pieza)"), effects, false)
            .AddField(Loc.T("🎲 Tiers: bonus y chance por intento"), tiers, false)
            .AddField(Loc.T("💸 Lo que cuesta cada intento"), costs + Loc.T("\n_La zona de la pieza sale de su rareza (Común = Zona 1 … Mítico = Zona 5)._"), false)
            .AddField(Loc.T("✨ De dónde sale el Polvo"), Loc.F($"Desmantelando materiales con **/dismantle** (la cantidad que quieras, o `todo`). Polvo por unidad:\n{dust}"), false)
            .AddField(
                Loc.F($"{ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey).Emoji} El oficio Encantador"),
                Loc.F($"Cada intento sube tu oficio: por nivel, {ProfessionRules.DescribePerLevel(ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey))}. Al nivel {ProfessionRules.MaxLevel}: ") +
                Loc.F($"{ProfessionRules.DescribeAdvanced(ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey))}. Tu avance: **/professions**."),
                false)
            .WithFooter(Loc.T("Mirá cómo estás vos con /enchant (sin elegir pieza)."))
            .Build();
    }

    // «4,2 %» en español, «4.2%» en inglés, sin el decimal cuando es redondo («1 %»).
    private static string ShortPercent(double value)
    {
        string text = value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return Loc.Current == Loc.English ? text + "%" : text.Replace('.', ',') + " %";
    }
}

// Lista de /dismantle: los materiales que el jugador TIENE y se pueden desmantelar, con cuántos y cuánto Polvo da cada uno.
public sealed class DismantleItemAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var inventory = await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(userId);
        return ItemChoices.ForDismantle(inventory, typed);
    }
}
