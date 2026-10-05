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
        [Summary("pieza", "Qué querés encantar. Si no elegís, mirás tus encantamientos y lo que cuesta cada intento.")]
        [Choice("Arma", "weapon"), Choice("Amuleto", "amulet")] string? slot = null)
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
            .WithDescription($"Rompiste **{quantity}×** {ItemDisplay.Format(item.Emoji, item.Name)} y juntaste **+{GameHistory.Number(dustGained)}** ✨ de Polvo.")
            .AddField("✨ Tu Polvo", GameHistory.Number(outcome.DustAfter), true)
            .AddField("🎒 Te quedan", $"{GameHistory.Number(outcome.QuantityLeft)}×", true)
            .WithFooter("El Polvo sirve para encantar tu arma y tu amuleto: /enchant");
        return new DustResult(null, embed.Build());
    }

    // slot: "weapon" / "amulet" (o "arma" / "amuleto" desde "aa enchant"); null = solo mirar.
    public static async Task<DustResult> ExecuteEnchantAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string? slot, Random? rng = null)
    {
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
            .AddField(isWeapon ? "⚔️ ATQ del arma" : "🛡️ DEF del amuleto", before == after ? $"**{after}**" : $"{before} ➜ **{after}**", true)
            .AddField("💸 Costó", $"{GameHistory.Number(cost.Gold)} oro · {cost.Dust} Polvo", true)
            .AddField("Te quedan", $"💰 {GameHistory.Number(outcome.User!.Gold)} · ✨ {GameHistory.Number(outcome.User.Dust)}", true)
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

        string tiers = string.Join(" · ", Enumerable.Range(1, Enchantments.MaxTier).Select(t => $"{Enchantments.TierName(t)} +{Enchantments.BonusPercent(t)} %"));
        return new EmbedBuilder()
            .WithTitle("✨ Encantamientos")
            .WithColor(Color.Purple)
            .WithDescription(
                "Con **Polvo** (lo juntás desmantelando materiales con **/dismantle**) y oro, probás mejorar tu arma o tu amuleto con **/enchant arma** o **/enchant amuleto**. " +
                $"El tier sale al azar y nunca baja:\n{tiers}")
            .AddField("⚔️ Arma", Line("weapon", weapon, player.WeaponEnchant), true)
            .AddField("🛡️ Amuleto", Line("amulet", amulet, player.AmuletEnchant), true)
            .AddField("✨ Tu Polvo", GameHistory.Number(player.Dust), true)
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
