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
    [SlashCommand("dismantle", "Desmantelá materiales para conseguir Polvo (de 1 a 100 por vez).")]
    public async Task HandleDismantleAsync(
        [Summary("item", "Elegí de la lista el material que querés desmantelar.")] [Autocomplete(typeof(DismantleItemAutocompleteHandler))] string itemName,
        [Summary("cantidad", "Cuántos desmantelás, de 1 a 100 (por defecto 1).")] [MinValue(1)] [MaxValue(Dismantling.MaxPerCommand)] int quantity = 1)
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
            await FollowupAsync("¡Upa! No pude desmantelar, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("enchant", "Encantá tu arma o tu amuleto con oro y Polvo (sin elegir, ves cómo estás).")]
    public async Task HandleEnchantAsync(
        [Summary("pieza", "Qué encantar. Sin elegir ves lo tuyo; con Info, los tiers, sus chances y costos.")]
        [Choice("Arma", "weapon"), Choice("Amuleto", "amulet"), Choice("Info: tiers, chances y costos", "info")] string? slot = null,
        [Summary("modo", "Normal, o Avanzado (2 tiradas, te quedás la mejor, 1,5× el costo; pide el oficio Encantador al 100).")]
        [Choice("Normal", "normal"), Choice("Avanzado", "avanzado")] string modo = "normal")
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteEnchantAsync(
                userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, slot, bonusService: bonusService, advanced: GatheringModule.ParseAdvanced(modo) ?? false);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude encantar, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que GiveModule.GiveResult).
    public sealed record DustResult(string? PlainMessage, Embed? Embed);

    private const string NoAccount = "Todavía no tenés cuenta: empezá con **/start**.";
    public static async Task<DustResult> ExecuteDismantleAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string itemName, int quantity)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new DustResult(NoAccount, null);
        }

        if (quantity < 1 || quantity > Dismantling.MaxPerCommand)
        {
            return new DustResult($"Podés desmantelar de 1 a {Dismantling.MaxPerCommand} por vez.", null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        int dustGained = item is null ? 0 : Dismantling.DustFor(item.Rarity, quantity);
        if (item is null || !Dismantling.CanDismantle(item.Type) || dustGained <= 0)
        {
            return new DustResult($"**{itemName.Trim()}** no se puede desmantelar: solo se desmantelan materiales (madera, minerales y drops de monstruos).", null);
        }

        var outcome = await dustRepository.DismantleAsync(discordId, item.ItemId, quantity, dustGained);
        if (outcome is null)
        {
            return new DustResult($"No tenés **{quantity}** de **{ItemDisplay.Format(item.Emoji, item.Name)}** para desmantelar: mirá cuántos tenés en **/inventory**.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.Dismantle, amount: quantity, detail: item.Name);

        var embed = new EmbedBuilder()
            .WithTitle("🔨 ¡Desmantelado!")
            .WithColor(Color.Teal)
            .WithItemThumbnail(item.Emoji)
            .WithDescription($"Rompiste **{quantity}×** {ItemDisplay.Format(item.Emoji, item.Name)}.\n\n✨ Juntaste **+{GameHistory.Number(dustGained)}** de Polvo.")
            .AddField("✨ Tu Polvo", $"**{GameHistory.Number(outcome.DustAfter)}**", false)
            .AddField("🎒 Te quedan", $"**{GameHistory.Number(outcome.QuantityLeft)}**", false)
            .WithFooter("El Polvo sirve para encantar tu arma y tu amuleto: /enchant");
        return new DustResult(null, embed.Build());
    }

    // slot: "weapon" / "amulet" (o "arma" / "amuleto" desde "aa enchant"); null = solo mirar.
    public static async Task<DustResult> ExecuteEnchantAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string? slot, Random? rng = null, IPlayerBonusService? bonusService = null, bool advanced = false)
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
            return new DustResult("Elegí qué encantar: **arma** o **amuleto**.", null);
        }

        bool isWeapon = slotKey == "weapon";
        var gear = isWeapon ? weapon : amulet;
        if (gear is null)
        {
            return new DustResult($"No tenés {(isWeapon ? "un arma" : "un amuleto")} puesto para encantar: forjá uno con **/forge**.", null);
        }

        if (advanced && !ProfessionRules.AdvancedUnlocked(progressBefore.Level))
        {
            return new DustResult(
                $"El **{enchanter.AdvancedName.ToLowerInvariant()}** se desbloquea con el oficio **{enchanter.Name}** al nivel **{ProfessionRules.MaxLevel}** y el tuyo está en el **{progressBefore.Level}**. Mirá tu avance con **/professions**.", null);
        }

        // El Polvo de cada intento baja con el nivel del oficio; el avanzado cuesta 1,5 veces (oro y Polvo).
        var cost = ProfessionRules.EnchantCost(Enchantments.Cost(Enchantments.GearRank(gear.Rarity)), progressBefore.Level, advanced);
        if (player.Gold < cost.Gold)
        {
            return new DustResult($"No te alcanza el oro: cada intento{(advanced ? " avanzado" : string.Empty)} sobre **{gear.Name}** cuesta **{GameHistory.Number(cost.Gold)}** de oro y **{cost.Dust}** de Polvo.", null);
        }

        if (player.Dust < cost.Dust)
        {
            return new DustResult($"Te falta Polvo: cada intento{(advanced ? " avanzado" : string.Empty)} sobre **{gear.Name}** cuesta **{cost.Dust}** (tenés **{player.Dust}**). Juntalo desmantelando materiales con **/dismantle**.", null);
        }

        // Avanzado: dos tiradas y se queda la mejor (igual el tier final nunca baja el que ya tenía la pieza, eso lo decide TryEnchantAsync).
        int[] rolls = Enumerable.Range(0, advanced ? ProfessionRules.AdvancedEnchantRolls : 1).Select(_ => Enchantments.Roll(rng)).ToArray();
        int rolled = rolls.Max();
        var outcome = await dustRepository.TryEnchantAsync(discordId, slotKey, rolled, cost.Gold, cost.Dust);
        if (outcome.Status != EnchantStatus.Ok)
        {
            return new DustResult(outcome.Status switch
            {
                EnchantStatus.NoGear => $"No tenés {(isWeapon ? "un arma" : "un amuleto")} puesto para encantar.",
                EnchantStatus.NotEnoughGold => "No te alcanza el oro para este intento.",
                EnchantStatus.NotEnoughDust => "Te falta Polvo para este intento.",
                _ => NoAccount,
            }, null);
        }

        // Cada tirada cuenta como un intento (el contador de «enchant» es la XP del oficio y el del logro Encantador).
        await gameEvents.RecordAsync(discordId, GameEventKinds.Enchant, amount: rolls.Length, detail: $"{slotKey}:{rolled}");

        var progressAfter = ProfessionRules.ProgressFor(progressBefore.TotalXp + ((long)rolls.Length * enchanter.XpPerAction));
        string footer = GatheringModule.ProfessionFooter(enchanter, progressBefore, progressAfter);
        return new DustResult(null, BuildResultEmbed(player, gear, slotKey, outcome, cost, footer, advanced ? rolls : null));
    }

    // "info", "opciones", "ayuda" o "chances" (con o sin acento): lo que pide quien quiere ver cómo funciona el encantamiento.
    public static bool IsInfoRequest(string? text) => text?.Trim().ToLowerInvariant() is "info" or "opciones" or "ayuda" or "chances" or "tiers";

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
        string rolledText = $"{Enchantments.Label(slotKey, rolled)} (+{Enchantments.BonusPercent(rolled)} %)";
        string rollsText = allRolls is { Length: > 1 }
            ? $"Tiraste {allRolls.Length} veces ({string.Join(" y ", allRolls.Select(r => Enchantments.TierName(r)))}) y te quedás con la mejor: **{rolledText}**."
            : $"Sobre {ItemDisplay.Format(gear.Emoji, gear.Name)} salió **{rolledText}**.";

        string verdict = now > previous
            ? $"🔥 **¡Mejoró!** {Enchantments.Label(slotKey, previous)} ➜ **{Enchantments.Label(slotKey, now)}**"
            : rolled == previous
                ? $"Era justo el que ya tenías: seguís con **{Enchantments.Label(slotKey, previous)}**."
                : $"Ya tenías **{Enchantments.Label(slotKey, previous)}**, que es mejor: te quedás con ese.";

        // Lo que rinde la pieza (con la sinergia de clase si es un arma) antes y después.
        int baseStat = isWeapon ? ClassWeaponSynergy.ApplyBonus(gear.StatValue, playerBefore.Class, gear.WeaponFamily) : gear.StatValue;
        int before = Enchantments.Apply(baseStat, previous), after = Enchantments.Apply(baseStat, now);

        return new EmbedBuilder()
            .WithTitle($"✨ Encantamiento: {(isWeapon ? "arma" : "amuleto")}")
            .WithColor(now > previous ? Color.Gold : Color.DarkGrey)
            .WithItemThumbnail(gear.Emoji)
            .WithDescription($"{(allRolls is { Length: > 1 } ? $"Sobre {ItemDisplay.Format(gear.Emoji, gear.Name)}: " : string.Empty)}{rollsText}\n\n{verdict}")
            .AddField(isWeapon ? "⚔️ ATQ del arma" : "🛡️ DEF del amuleto", before == after ? $"**{after}**" : $"{before} ➜ **{after}**", false)
            .AddField("💸 Costó", $"{GameHistory.Number(cost.Gold)} oro · {cost.Dust} Polvo", false)
            .AddField("🎒 Te quedan", $"{GameHistory.Number(outcome.User!.Gold)} oro · {GameHistory.Number(outcome.User.Dust)} Polvo", false)
            .WithFooter(string.IsNullOrWhiteSpace(professionFooter) ? "Cada intento se paga igual: el tier sale al azar y nunca baja." : professionFooter)
            .Build();
    }

    // Público y puro: lo que se ve con "/enchant" sin elegir pieza.
    public static Embed BuildInfoEmbed(User player, Item? weapon, Item? amulet, PlayerBonuses? bonuses = null)
    {
        int enchanterLevel = bonuses?.ProfessionLevel(ProfessionCatalog.EnchanterKey) ?? 0;
        string Line(string slotKey, Item? gear, int tier)
        {
            if (gear is null)
            {
                return $"_Sin {(slotKey == "weapon" ? "arma" : "amuleto")} puesto_";
            }

            // El Polvo ya con el descuento del oficio Encantador.
            var cost = ProfessionRules.EnchantCost(Enchantments.Cost(Enchantments.GearRank(gear.Rarity)), enchanterLevel, advanced: false);
            string current = tier > 0 ? $"{Enchantments.Label(slotKey, tier)} (+{Enchantments.BonusPercent(tier)} %)" : "Sin encantar";
            return $"{ItemDisplay.Format(gear.Emoji, gear.Name)}\n**{current}**\nPróximo intento: {GameHistory.Number(cost.Gold)} oro + {cost.Dust} Polvo";
        }

        // Los tiers, en dos renglones cortos (en uno solo se apretaban y se partían a mitad de nombre).
        string Tier(int t) => $"{Enchantments.TierName(t)} **+{Enchantments.BonusPercent(t)} %**";
        string tiers = $"{string.Join("  ·  ", Enumerable.Range(1, 3).Select(Tier))}\n{string.Join("  ·  ", Enumerable.Range(4, Enchantments.MaxTier - 3).Select(Tier))}";
        var embed = new EmbedBuilder()
            .WithTitle("✨ Encantamientos")
            .WithColor(Color.Purple)
            .WithDescription(
                "Mejorá tu arma o tu amuleto con **Polvo** y oro: **/enchant arma** o **/enchant amuleto**.\n\n" +
                "El Polvo lo juntás desmantelando materiales con **/dismantle**.\n\n" +
                "Cada intento sortea un tier al azar y **nunca baja** el que ya tenés: te quedás con el mejor.")
            .AddField("⚔️ Arma", Line("weapon", weapon, player.WeaponEnchant), false)
            .AddField("🛡️ Amuleto", Line("amulet", amulet, player.AmuletEnchant), false)
            .AddField("✨ Tu Polvo", $"**{GameHistory.Number(player.Dust)}**", false);

        // El oficio Encantador (solo si el llamador lo pasó): su nivel y lo que ya te ahorra.
        if (bonuses is not null)
        {
            var enchanter = ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey);
            var progress = ProfessionRules.ProgressFor(bonuses.ProfessionXpOf(enchanter.Key));
            embed.AddField(
                $"{enchanter.Emoji} {enchanter.Name} · nivel {progress.Level}",
                progress.Level == 0 ? "Cada intento suma XP y baja el Polvo que cuesta el siguiente: **/professions**." : $"{ProfessionRules.DescribeEffect(enchanter, progress.Level)}.",
                false);
        }

        return embed
            .AddField("🎲 Tiers posibles", tiers, false)
            .WithFooter("Para ver el detalle de cada tier (chance y bonus) y lo que cuesta cada intento: /enchant info")
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
            return attempts <= 1.05 ? string.Empty : $" · ~{attempts.ToString(attempts < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} intentos para llegar";
        }

        string tiers = string.Join('\n', Enumerable.Range(1, Enchantments.MaxTier).Select(t =>
            $"**{Enchantments.TierName(t)}** — bonus **+{Enchantments.BonusPercent(t)} %** — sale el **{Enchantments.ChancePercent(t).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} %**{Attempts(t)}"));

        string costs = string.Join('\n', Enumerable.Range(1, 5).Select(rank =>
        {
            var cost = Enchantments.Cost(rank);
            return $"Pieza de **Zona {rank}**: {GameHistory.Number(cost.Gold)} oro + {cost.Dust} Polvo";
        }));

        string dust = string.Join(" · ", new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Select(r => $"{r} {GameHistory.Number(Dismantling.DustPerUnit(r))}"));

        return new EmbedBuilder()
            .WithTitle("✨ Encantamientos: opciones")
            .WithColor(Color.Purple)
            .WithDescription(
                "Mejorá tu **arma** (Filo) o tu **amuleto** (Guarda) gastando **Polvo** y oro: **/enchant arma** o **/enchant amuleto**.\n\n" +
                "Cada intento sortea un tier **al azar**. El tier **nunca baja**: si sale uno peor que el que tenés, te quedás con el que tenés (pero el intento se paga igual).\n\n" +
                "El bonus es un % del stat **de la pieza** (siempre suma al menos +1) y cuenta igual en el combate, en el perfil y en los duelos. Si vendés o cambiás la pieza, **pierde su encantamiento**.")
            .AddField("🎲 Tiers: bonus y chance por intento", tiers, false)
            .AddField("💸 Lo que cuesta cada intento", costs + "\n_La zona de la pieza sale de su rareza (Común = Zona 1 … Mítico = Zona 5)._", false)
            .AddField("✨ De dónde sale el Polvo", $"Desmantelando materiales con **/dismantle** (de 1 a {Dismantling.MaxPerCommand} por vez). Polvo por unidad:\n{dust}", false)
            .AddField(
                $"{ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey).Emoji} El oficio Encantador",
                $"Cada intento sube tu oficio: por nivel, {ProfessionRules.DescribePerLevel(ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey))}. Al nivel {ProfessionRules.MaxLevel}: " +
                $"{ProfessionRules.DescribeAdvanced(ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey))}. Tu avance: **/professions**.",
                false)
            .WithFooter("Mirá cómo estás vos con /enchant (sin elegir pieza).")
            .Build();
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
