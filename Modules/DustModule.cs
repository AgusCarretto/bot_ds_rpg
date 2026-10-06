using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

// /dismantle y /enchant: el Polvo. Desmantelar rompe un MATERIAL (madera, mineral, drop de monstruo o trofeo) y da Polvo (GameData/Dismantling.cs); encantar gasta
// Polvo + oro en un intento de mejorar el arma o el amuleto (GameData/Enchantments.cs). Las reglas viven en los métodos estáticos de abajo (sin Context) para que
// "aa dismantle" y "aa enchant" hagan exactamente lo mismo; el dinero y los ítems se mueven en IDustRepository con guardas atómicas.
public class DustModule(IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("dismantle", "Desmantelá materiales para conseguir Polvo (de 1 a 5 por vez).")]
    public async Task HandleDismantleAsync(
        [Summary("item", "Elegí de la lista el material que querés desmantelar.")] [Autocomplete(typeof(DismantleItemAutocompleteHandler))] string itemName,
        [Summary("cantidad", "Cuántos desmantelás, de 1 a 5 (por defecto 1).")] [MinValue(1)] [MaxValue(Dismantling.MaxPerCommand)] int quantity = 1)
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
        [Choice("Arma", "weapon"), Choice("Amuleto", "amulet"), Choice("Info: tiers, chances y costos", "info")] string? slot = null)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteEnchantAsync(userRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, slot);
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
        ulong discordId, string? slot, Random? rng = null)
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

        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;

        string? slotKey = NormalizeSlot(slot);
        if (string.IsNullOrWhiteSpace(slot))
        {
            return new DustResult(null, BuildInfoEmbed(player, weapon, amulet));
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

        var cost = Enchantments.Cost(Enchantments.GearRank(gear.Rarity));
        if (player.Gold < cost.Gold)
        {
            return new DustResult($"No te alcanza el oro: cada intento sobre **{gear.Name}** cuesta **{GameHistory.Number(cost.Gold)}** de oro y **{cost.Dust}** de Polvo.", null);
        }

        if (player.Dust < cost.Dust)
        {
            return new DustResult($"Te falta Polvo: cada intento sobre **{gear.Name}** cuesta **{cost.Dust}** (tenés **{player.Dust}**). Juntalo desmantelando materiales con **/dismantle**.", null);
        }

        int rolled = Enchantments.Roll(rng);
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

        await gameEvents.RecordAsync(discordId, GameEventKinds.Enchant, detail: $"{slotKey}:{rolled}");
        return new DustResult(null, BuildResultEmbed(player, gear, slotKey, outcome, cost));
    }

    // "info", "opciones", "ayuda" o "chances" (con o sin acento): lo que pide quien quiere ver cómo funciona el encantamiento.
    public static bool IsInfoRequest(string? text) => text?.Trim().ToLowerInvariant() is "info" or "opciones" or "ayuda" or "chances" or "tiers";

    // "aa info enchant" / "aa info encantar" / "aa info encantamientos": el tema de /info que lleva a la pantalla de encantamientos.
    public static bool IsEnchantTopic(string? text) =>
        text?.Trim().ToLowerInvariant() is "enchant" or "enchants" or "encantar" or "encanto" or "encantos" or "encantamiento" or "encantamientos";

    private static string? NormalizeSlot(string? slot) => slot?.Trim().ToLowerInvariant() switch
    {
        "weapon" or "arma" => "weapon",
        "amulet" or "amuleto" => "amulet",
        _ => null,
    };

    // Público y puro: se prueba sin Discord. Lo que rinde la pieza antes y después, con el encantamiento aplicado igual que en el combate.
    public static Embed BuildResultEmbed(User playerBefore, Item gear, string slotKey, EnchantOutcome outcome, (int Gold, int Dust) cost)
    {
        bool isWeapon = slotKey == "weapon";
        int previous = outcome.PreviousTier, rolled = outcome.RolledTier, now = outcome.NewTier;
        string rolledText = $"{Enchantments.Label(slotKey, rolled)} (+{Enchantments.BonusPercent(rolled)} %)";

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
            .WithDescription($"Sobre {ItemDisplay.Format(gear.Emoji, gear.Name)} salió **{rolledText}**.\n\n{verdict}")
            .AddField(isWeapon ? "⚔️ ATQ del arma" : "🛡️ DEF del amuleto", before == after ? $"**{after}**" : $"{before} ➜ **{after}**", false)
            .AddField("💸 Costó", $"{GameHistory.Number(cost.Gold)} oro · {cost.Dust} Polvo", false)
            .AddField("🎒 Te quedan", $"{GameHistory.Number(outcome.User!.Gold)} oro · {GameHistory.Number(outcome.User.Dust)} Polvo", false)
            .WithFooter("Cada intento se paga igual: el tier sale al azar y nunca baja.")
            .Build();
    }

    // Público y puro: lo que se ve con "/enchant" sin elegir pieza.
    public static Embed BuildInfoEmbed(User player, Item? weapon, Item? amulet)
    {
        string Line(string slotKey, Item? gear, int tier)
        {
            if (gear is null)
            {
                return $"_Sin {(slotKey == "weapon" ? "arma" : "amuleto")} puesto_";
            }

            var cost = Enchantments.Cost(Enchantments.GearRank(gear.Rarity));
            string current = tier > 0 ? $"{Enchantments.Label(slotKey, tier)} (+{Enchantments.BonusPercent(tier)} %)" : "Sin encantar";
            return $"{ItemDisplay.Format(gear.Emoji, gear.Name)}\n**{current}**\nPróximo intento: {GameHistory.Number(cost.Gold)} oro + {cost.Dust} Polvo";
        }

        // Los tiers, en dos renglones cortos (en uno solo se apretaban y se partían a mitad de nombre).
        string Tier(int t) => $"{Enchantments.TierName(t)} **+{Enchantments.BonusPercent(t)} %**";
        string tiers = $"{string.Join("  ·  ", Enumerable.Range(1, 3).Select(Tier))}\n{string.Join("  ·  ", Enumerable.Range(4, Enchantments.MaxTier - 3).Select(Tier))}";
        return new EmbedBuilder()
            .WithTitle("✨ Encantamientos")
            .WithColor(Color.Purple)
            .WithDescription(
                "Mejorá tu arma o tu amuleto con **Polvo** y oro: **/enchant arma** o **/enchant amuleto**.\n\n" +
                "El Polvo lo juntás desmantelando materiales con **/dismantle**.\n\n" +
                "Cada intento sortea un tier al azar y **nunca baja** el que ya tenés: te quedás con el mejor.")
            .AddField("⚔️ Arma", Line("weapon", weapon, player.WeaponEnchant), false)
            .AddField("🛡️ Amuleto", Line("amulet", amulet, player.AmuletEnchant), false)
            .AddField("✨ Tu Polvo", $"**{GameHistory.Number(player.Dust)}**", false)
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
