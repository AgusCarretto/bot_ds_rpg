using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// /chop y /mine. Desde la v0.13.0 cada uso sube el oficio de su comando (Leñador / Minero, GameData/ProfessionRules.cs: la XP sale del contador de usos, así que acá solo se
// registra el evento) y, con el oficio al nivel 100, el modo "Avanzada" hace 4 sorteos juntos con su propio cooldown. La lógica vive en ExecuteGatherAsync (sin Context) y la
// comparten el comando de barra y "aa chop"/"aa mine".
public class GatheringModule(
    ICooldownRepository cooldownRepository,
    IGatheringRepository gatheringRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IPlayerBonusService bonusService,
    IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("chop", "Talá madera cercana (cooldown de 5 minutos).")]
    public Task HandleChopAsync(
        [Summary("modo", "Normal, o Avanzada (4 sorteos juntos; pide el oficio Leñador al nivel 100).")]
        [Choice("Normal", "normal"), Choice("Avanzada", "avanzada")] string modo = "normal") =>
        RunGatheringAsync(CooldownCatalog.Chop, itemType: "Madera", modo);

    [SlashCommand("mine", "Miná materiales cercanos (cooldown de 5 minutos).")]
    public Task HandleMineAsync(
        [Summary("modo", "Normal, o Avanzada (4 sorteos juntos; pide el oficio Minero al nivel 100).")]
        [Choice("Normal", "normal"), Choice("Avanzada", "avanzada")] string modo = "normal") =>
        RunGatheringAsync(CooldownCatalog.Mine, itemType: "Mineral", modo);

    private async Task RunGatheringAsync(CooldownDefinition definition, string itemType, string modo)
    {
        // La consulta de cooldown + la transacción pueden superar los 3s que da Discord
        // antes de que la interacción expire.
        await DeferAsync();

        try
        {
            var result = await ExecuteGatherAsync(
                cooldownRepository, gatheringRepository, userRepository, itemRepository, bonusService, gameEvents, Context.User.Id, definition, itemType, ParseAdvanced(modo) ?? false);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Ephemeral);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync(Loc.T("¡Upa! Algo falló procesando la recolección, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Exactamente lo que se responde: texto o pantalla, y si es solo para quien lo pidió (los rechazos).
    public sealed record GatherResult(string? PlainMessage, Embed? Embed, bool Ephemeral);

    // "avanzada" / "avanzado" / "adv" / "advanced" → true; vacío / "normal" → false; cualquier otra cosa → null (el texto no se entendió).
    public static bool? ParseAdvanced(string? mode)
    {
        string text = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return text switch
        {
            "" or "normal" => false,
            "avanzada" or "avanzado" or "adv" or "advanced" => true,
            _ => null,
        };
    }

    // La recolección entera, para /chop, /mine, "aa chop" y "aa mine": valida, sortea (rareza con la mejora del oficio, cantidad con Fuego Nuevo × bendición × oficio), aplica de
    // forma atómica (el cooldown y los materiales en la misma transacción), registra los eventos DESPUÉS de que quedó guardado y arma la pantalla con el pie del oficio.
    // rng solo para probarlo con un sorteo fijo.
    public static async Task<GatherResult> ExecuteGatherAsync(
        ICooldownRepository cooldownRepository, IGatheringRepository gatheringRepository, IUserRepository userRepository, IItemRepository itemRepository,
        IPlayerBonusService bonusService, IGameEvents gameEvents, ulong discordId, CooldownDefinition definition, string itemType, bool advanced = false, Random? rng = null)
    {
        bool isChop = definition.CommandName == CooldownCatalog.Chop.CommandName;
        var profession = ProfessionCatalog.Get(isChop ? ProfessionCatalog.WoodcutterKey : ProfessionCatalog.MinerKey);
        var active = advanced ? CooldownCatalog.AdvancedOf(definition)! : definition;

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var bonuses = await bonusService.GetAsync(discordId, player.FuegoNuevo);
        var before = ProfessionRules.ProgressFor(bonuses.ProfessionXpOf(profession.Key));

        if (advanced && !ProfessionRules.AdvancedUnlocked(before.Level))
        {
            return new GatherResult(
                Loc.F($"La **{profession.DisplayAdvancedName.ToLowerInvariant()}** se desbloquea con el oficio **{profession.DisplayName}** al nivel **{ProfessionRules.MaxLevel}** y el tuyo está en el **{before.Level}**. Mirá tu avance con **/professions**."),
                null, true);
        }

        var remaining = await cooldownRepository.GetRemainingAsync(discordId, active.CommandName, active.Duration);
        if (remaining is not null)
        {
            return new GatherResult(null, BuildCooldownEmbed(active, remaining.Value), true);
        }

        int rolls = advanced ? ProfessionRules.AdvancedGatherRolls : 1;
        double multiplier = isChop ? bonuses.ChopMultiplier : bonuses.MineMultiplier;
        double toolFactor = isChop ? bonuses.ChopToolFactor : bonuses.MineToolFactor;   // el hacha / el pico (v0.29.0): solo para Común, Raro y Épico
        double upgradeChance = isChop ? bonuses.ChopRarityUpgrade : bonuses.MineRarityUpgrade;

        var drops = new List<(Item Item, int Quantity)>();
        bool upgraded = false;
        for (int i = 0; i < rolls; i++)
        {
            string rarity = RarityCatalog.RollGatheringRarity();
            string finalRarity = ProfessionRules.UpgradeRarity(rarity, upgradeChance, rng);
            var item = await itemRepository.GetRandomByTypeAndRarityAsync(itemType, finalRarity);

            // Si el catálogo no tiene nada de la rareza mejorada, se queda la que salió (la mejora es un extra y nunca deja sin recolectar).
            if (item is null && finalRarity != rarity)
            {
                finalRarity = rarity;
                item = await itemRepository.GetRandomByTypeAndRarityAsync(itemType, rarity);
            }

            if (item is null)
            {
                if (drops.Count == 0)
                {
                    // El catálogo todavía no tiene ítems cargados para esa combinación tipo+rareza.
                    return new GatherResult(Loc.F($"Todavía no hay materiales de tipo **{itemType}** y rareza **{RarityCatalog.DisplayName(rarity)}** cargados en el catálogo."), null, true);
                }

                continue;
            }

            upgraded |= finalRarity != rarity;

            // Cuántas unidades salen depende de la rareza: lo común a montones, lo mejor de a una (GatheringYield), por el multiplicador del jugador.
            drops.Add((item, GatheringYield.RollScaled(item.Rarity, multiplier * ToolRules.QuantityFactor(item.Rarity, toolFactor), rng)));
        }

        bool applied = await gatheringRepository.ApplyGatheringBatchAsync(
            discordId, active.CommandName, active.Duration, drops.Select(d => (d.Item.ItemId, d.Quantity)).ToList());

        if (!applied)
        {
            // Perdió la carrera contra otra ejecución concurrente del mismo comando (ej. doble click).
            return new GatherResult(Loc.T("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque."), null, true);
        }

        await GatheringEvents.RecordAsync(gameEvents, discordId, definition, drops, rolls);

        // El oficio: el evento de arriba ya sumó a su contador la XP de la rareza de cada tirada; acá solo se calcula cómo queda para el pie de la pantalla.
        long gained = GatheringEvents.XpOf(drops);
        var after = ProfessionRules.ProgressFor(before.TotalXp + gained);
        string footer = ProfessionFooter(profession, before, after, gained) + (upgraded ? Loc.T(" · ⬆️ ¡rareza mejorada!") : string.Empty);

        var embed = advanced
            ? BuildAdvancedResultEmbed(active, drops, footer)
            : BuildResultEmbed(definition, drops[0].Item, drops[0].Quantity, footer);
        return new GatherResult(null, embed, false);
    }

    // El pie de la pantalla de un comando que sube un oficio: dónde está («🪓 Leñador nivel 12 · 340/412 XP»), si subió de nivel o si llegó al máximo. Público para que /enchant use el mismo.
    public static string ProfessionFooter(ProfessionDefinition profession, ProfessionProgress before, ProfessionProgress after, long? gained = null)
    {
        if (after.IsMax && !before.IsMax)
        {
            return Loc.F($"🏆 ¡{profession.DisplayName} al nivel {ProfessionRules.MaxLevel}! Desbloqueaste la {profession.DisplayAdvancedName.ToLowerInvariant()}");
        }

        if (after.IsMax)
        {
            return Loc.F($"{profession.Emoji} {profession.DisplayName} nivel {ProfessionRules.MaxLevel} (máximo)");
        }

        string progress = $"{after.IntoLevel}/{after.ToNext} XP" + (gained is > 0 ? $" (+{gained})" : string.Empty);   // igual en los dos idiomas
        return after.Level > before.Level
            ? Loc.F($"🎉 ¡{profession.DisplayName} subió al nivel {after.Level}! · {progress}")
            : Loc.F($"{profession.Emoji} {profession.DisplayName} nivel {after.Level} · {progress}");
    }

    // Públicos para que Modules/TextCommandModule.cs arme los mismos embeds en "aa chop"/"aa mine".
    public static Embed BuildCooldownEmbed(CooldownDefinition definition, TimeSpan remaining)
    {
        return new EmbedBuilder()
            .WithTitle(Loc.F($"⏳ {definition.Emoji} {Loc.Tr(definition.DisplayName)}: todavía no podés"))
            .WithDescription(Loc.F($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a intentarlo."))
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildResultEmbed(CooldownDefinition definition, Item item, int quantity, string? footer = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"{definition.Emoji} ¡{Loc.Tr(definition.DisplayName)} exitoso!"))
            .WithColor(RarityColor(item.Rarity))
            .WithDescription(Loc.F($"Conseguiste **{quantity}× {ItemDisplay.Format(item.Emoji, item.Name)}**"))
            .AddField(Loc.T("Rareza"), RarityCatalog.DisplayName(item.Rarity), true)
            .WithItemThumbnail(item.Emoji);

        if (!string.IsNullOrWhiteSpace(footer))
        {
            embed.WithFooter(footer);
        }

        return embed.Build();
    }

    // La recolección avanzada: los materiales de los 4 sorteos juntos (los repetidos se suman), uno por renglón, lo más raro primero; el color y la miniatura son los del mejor.
    public static Embed BuildAdvancedResultEmbed(CooldownDefinition definition, IReadOnlyList<(Item Item, int Quantity)> drops, string? footer = null)
    {
        var grouped = drops
            .GroupBy(d => d.Item.ItemId)
            .Select(g => (Item: g.First().Item, Quantity: g.Sum(d => d.Quantity)))
            .OrderByDescending(d => RarityCatalog.RankOf(d.Item.Rarity))
            .ThenBy(d => d.Item.Name, StringComparer.Ordinal)
            .ToList();

        var best = grouped[0].Item;
        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"{definition.Emoji} ¡{Loc.Tr(definition.DisplayName)} exitosa!"))
            .WithColor(RarityColor(best.Rarity))
            .WithDescription(string.Join('\n', grouped.Select(g => $"**{g.Quantity}× {ItemDisplay.Format(g.Item.Emoji, g.Item.Name)}** · {RarityCatalog.DisplayName(g.Item.Rarity)}")))
            .WithItemThumbnail(best.Emoji);

        if (!string.IsNullOrWhiteSpace(footer))
        {
            embed.WithFooter(footer);
        }

        return embed.Build();
    }

    public static Color RarityColor(string rarity) => rarity switch
    {
        "Común" => Color.LightGrey,
        "Raro" => Color.Blue,
        "Épico" => Color.Purple,
        "Legendario" => Color.Gold,
        "Mítico" => Color.Red,
        _ => Color.Default,
    };
}
