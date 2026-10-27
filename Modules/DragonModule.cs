using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Lo que sale de invitar a criar: Error (un rechazo, solo para quien invitó) o el mensaje con la invitación (Content pinguea al invitado) y sus botones.
public sealed record BreedInvite(string? Error, string? Content = null, Embed? Embed = null, MessageComponent? Components = null);

// La pantalla del dragón: el embed y, si todavía se puede entrenar, el botón de entrenar un nivel.
public sealed record DragonView(Embed Embed, MessageComponent? Components);

// El DRAGÓN (v0.30.0; GameData/DragonRules.cs, Repositories/DragonRepository.cs, tabla player_dragons): /dragon muestra el tuyo, /dragon-breed @amigo lo cría con el de un amigo del MISMO tier (si sale bien suben los
// dos de TIER y cada tier desbloquea una mejora nueva), /dragon-train lo ENTRENA con oro (sube el NIVEL, cada uno más caro, hasta tier × 10) y se compra en la taberna (DragonModule.ExecuteBuyAsync, que la lista de cajas de la
// taberna ofrece mientras no tengas uno). Los botones viven en este mismo módulo (sin [Group], para que Discord.Net no les pegue el nombre del grupo): Aceptar solo lo toca el invitado; Rechazar, cualquiera de los dos;
// Entrenar, el dueño de la pantalla. Toda la lógica es estática y sin Context: el slash, los botones y el comando de texto llaman a lo mismo.
public class DragonModule(IUserRepository userRepository, IDragonRepository dragonRepository, IDragonBreedOfferService offerService)
    : InteractionModuleBase<SocketInteractionContext>
{
    // El valor de la opción «Dragón» en la lista de la taberna. No choca con ningún nombre de ítem.
    public const string ShopKey = "__dragon__";
    private const string AcceptPrefix = "dragon_accept";
    private const string DeclinePrefix = "dragon_decline";
    private const string TrainPrefix = "dragon_train";

    [SlashCommand("dragon", "Mirá tu dragón: su tipo, su tier, sus mejoras, y cómo entrenarlo y criarlo con un amigo.")]
    public async Task HandleDragonAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildDragonViewAsync(dragonRepository, Context.User.Id);
            await FollowupAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude mirar tu dragón, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("dragon-train", "Entrená a tu dragón con oro: cada nivel suma al bono de su tipo y sale más caro que el anterior.")]
    public async Task HandleTrainAsync([Summary("veces", "Cuántos niveles (se entrena hasta donde alcance el oro)")] int veces = 1)
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(await ExecuteTrainAsync(dragonRepository, Context.User.Id, veces));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude entrenar a tu dragón, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // El botón «Entrenar» de la pantalla del dragón: entrena UN nivel, refresca la pantalla en su lugar y dice cómo salió (solo lo ves vos).
    [ComponentInteraction($"{TrainPrefix}:*")]
    public async Task HandleTrainButtonAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Ese dragón es de otra persona: mirá el tuyo con **/dragon**.", ephemeral: true);
                return;
            }

            string message = await ExecuteTrainAsync(dragonRepository, ownerId, 1);
            var view = await BuildDragonViewAsync(dragonRepository, ownerId);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = view.Embed;
                p.Components = view.Components ?? new ComponentBuilder().Build();
            });
            await FollowupAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude entrenar a tu dragón, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("dragon-name", "Ponele nombre a tu dragón (o cambiáselo): cuesta oro cada vez.")]
    public async Task HandleNameAsync([Summary("nombre", "El nombre nuevo (2 a 24 letras, números o espacios)")] string nombre)
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(await ExecuteRenameAsync(dragonRepository, Context.User.Id, nombre));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude cambiarle el nombre a tu dragón, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("dragon-breed", "Criá tu dragón con un amigo del mismo tier (suben los dos) o solo, con la mitad de chance.")]
    public async Task HandleBreedAsync([Summary("amigo", "Con quién (si no ponés a nadie, lo criás solo, con la mitad de chance)")] IUser? amigo = null)
    {
        await DeferAsync();

        try
        {
            // Sin amigo: se cría SOLO (cuenta como intento, con la mitad de chance).
            if (amigo is null)
            {
                await FollowupAsync(embed: await ExecuteSoloBreedAsync(dragonRepository, Context.User.Id));
                return;
            }

            var invite = await PrepareBreedAsync(dragonRepository, offerService, Context.User.Id, amigo.Id, amigo.IsBot);
            if (invite.Error is not null)
            {
                await FollowupAsync(invite.Error, ephemeral: true);
                return;
            }

            await FollowupAsync(invite.Content, embed: invite.Embed, components: invite.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar la invitación, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{AcceptPrefix}:*")]
    public async Task HandleAcceptAsync(string offerRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(offerRaw, out Guid id) || offerService.Peek(id) is not { } peek)
            {
                await FollowupAsync("Esa invitación ya venció o se resolvió. Quien quiera criar tiene que mandar otra con **/dragon-breed**.", ephemeral: true);
                return;
            }

            if (Context.User.Id != peek.ToId)
            {
                await FollowupAsync($"Esa invitación es para <@{peek.ToId}>: solo esa persona puede aceptarla.", ephemeral: true);
                return;
            }

            // TryTake es atómico: de varios clicks a la vez, solo uno cría.
            if (offerService.TryTake(id) is not { } offer)
            {
                await FollowupAsync("Esa invitación ya se resolvió.", ephemeral: true);
                return;
            }

            var embed = await ResolveBreedAsync(dragonRepository, offer);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = $"<@{offer.FromId}> <@{offer.ToId}>";
                p.Embed = embed;
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude criar los dragones, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{DeclinePrefix}:*")]
    public async Task HandleDeclineAsync(string offerRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(offerRaw, out Guid id) || offerService.Peek(id) is not { } peek)
            {
                await FollowupAsync("Esa invitación ya venció o se resolvió.", ephemeral: true);
                return;
            }

            if (Context.User.Id != peek.ToId && Context.User.Id != peek.FromId)
            {
                await FollowupAsync("Esa invitación no es tuya.", ephemeral: true);
                return;
            }

            offerService.TryTake(id);
            string who = Context.User.Id == peek.FromId ? $"<@{peek.FromId}> canceló la invitación" : $"<@{peek.ToId}> rechazó la invitación";
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = $"❌ {who}: los dragones no se criaron.";
                p.Embed = null;
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude cancelar la invitación, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---------------- La lógica (estática, sin Context) ----------------

    // «+12,5 % de ATQ contra monstruos» (el bono de su tipo, ya con tier y nivel).
    private static string TypeBonus(string kind, int tier, int level) => $"**+{DragonRules.Percent(DragonRules.TypePercent(kind, tier, level))} %** {DragonRules.KindEffect(kind)}";

    // La pantalla del dragón. Sin dragón, cómo conseguirlo; con él: tipo, tier, nivel, lo que da (base + entrenamiento), las mejoras de cada tier (las que ya desbloqueó y las que faltan), el entrenamiento y la cría.
    public static async Task<DragonView> BuildDragonViewAsync(IDragonRepository dragonRepository, ulong discordId)
    {
        var dragon = await dragonRepository.GetAsync(discordId);
        var embed = new EmbedBuilder().WithColor(Color.DarkOrange);

        if (dragon is null)
        {
            // El dragón general (ui_dragon) de miniatura si está subido; si no, el 🐲 de siempre en el título.
            string? generic = ItemDisplay.ImageUrl(UiEmojis.Dragon, 256);
            embed.WithTitle(generic is null ? "🐲 Tu dragón" : "Tu dragón");
            if (generic is not null)
            {
                embed.WithThumbnailUrl(generic);
            }

            return new DragonView(embed.WithDescription(
                $"Todavía no tenés dragón. En la **/taberna** podés comprar uno por **{GameHistory.Number(DragonRules.ShopPrice)}** de oro (una sola vez): el tipo sale al azar —🗡️ Fuerza, 🛡️ Defensa, 💰 Oro o, con suerte, ✨ Especial— y empieza de **Cría**. " +
                "Después lo **entrenás** con oro (**/dragon-train**) y lo **criás** con el de un amigo que tenga **tu mismo tier** (**/dragon-breed**): cada tier le desbloquea una mejora nueva.").Build(), null);
        }

        string kind = dragon.Kind;

        // El dragón de SU tipo, grande, de miniatura (los títulos no garantizan dibujar emojis propios); sin el emoji subido, el 🐲 de siempre en el título.
        string? thumb = ItemDisplay.ImageUrl(DragonRules.KindEmoji(kind), 256);
        embed.WithTitle(thumb is null ? "🐲 Tu dragón" : "Tu dragón");
        if (thumb is not null)
        {
            embed.WithThumbnailUrl(thumb);
        }

        string shownName = dragon.Name is { } named ? $"**{named}** ({DragonRules.TierName(dragon.Tier)})" : $"**{DragonRules.TierName(dragon.Tier)}**";
        int cap = DragonRules.LevelCap(dragon.Tier);
        bool maxTier = dragon.Tier >= DragonRules.MaxTier;
        string detail = maxTier
            ? $"base {DragonRules.Percent(DragonRules.BonusPercent(kind, dragon.Tier))} + entrenamiento {DragonRules.Percent(DragonRules.LevelBonusPercent(kind, dragon.Level))}, por la Llama eterna ×{DragonRules.Percent(DragonRules.EternalMultiplier)}"
            : $"del tier {DragonRules.Percent(DragonRules.BonusPercent(kind, dragon.Tier))} + del entrenamiento {DragonRules.Percent(DragonRules.LevelBonusPercent(kind, dragon.Level))}";
        embed.WithDescription(
            $"{DragonRules.KindEmoji(kind)} {shownName} · Tier **{dragon.Tier}/{DragonRules.MaxTier}** · Nivel **{dragon.Level}/{cap}** · tipo **{DragonRules.KindName(kind)}**\n" +
            $"Te da {TypeBonus(kind, dragon.Tier, dragon.Level)} ({detail}).\n" +
            (maxTier ? "🏆 Está en el tier máximo.\n" : string.Empty) +
            (dragon.Name is null ? $"_Ponele nombre con **/dragon-name** ({GameHistory.Number(DragonRules.RenamePrice)} de oro)._\n" : string.Empty) +
            "_Solo cuenta contra los monstruos (no en duelos ni en la Arena) y dura toda la vida de tu cuenta: el Fuego Nuevo no lo toca._");

        // Una mejora nueva por tier: ✅ las que ya desbloqueó, 🔒 las que le faltan.
        var perkLines = DragonRules.Perks.Select(p => $"{(p.Tier <= dragon.Tier ? "✅" : "🔒")} **{DragonRules.TierNumeral(p.Tier)}** {p.Name}: {p.Text}");
        string perks = string.Join('\n', perkLines);
        embed.AddField("Una mejora nueva en cada tier", perks.Length <= 1024 ? perks : perks[..1023] + "…", false);

        if (dragon.Level >= cap)
        {
            embed.AddField("Entrenar", $"Llegó al nivel máximo de su tier (**{cap}**)." + (maxTier ? " ¡Es lo más alto que se puede!" : " Para seguir entrenándolo, subilo de tier criándolo (**/dragon-breed**): cada tier suma 10 niveles más."), false);
        }
        else
        {
            long toCap = DragonRules.TrainCostBetween(dragon.Level, cap);
            embed.AddField(
                "Entrenar",
                $"Próximo nivel: **{GameHistory.Number(DragonRules.TrainCost(dragon.Level))}** de oro (cada nivel sale más caro). Hasta el tope de este tier ({cap}): **{GameHistory.Number(toCap)}** de oro. " +
                $"Cada nivel suma **+{DragonRules.Percent(DragonRules.MaxPercent(kind) * DragonRules.LevelShare)} %** {DragonRules.KindEffect(kind)}. Con el botón de abajo o con **/dragon-train**.",
                false);
        }

        if (!maxTier)
        {
            int pityCap = DragonRules.PityCap(dragon.Tier);
            string pity = pityCap == 0
                ? "En este tier la cría con un amigo es **segura**."
                : $"Fallos seguidos: **{dragon.FailStreak}/{pityCap}**. Con {pityCap} fallos seguidos, el siguiente intento sale **seguro**.";
            embed.AddField(
                "Criar",
                $"Chance de subir de tier: **{DragonRules.Percent(Math.Round(DragonRules.BreedChance(dragon.Tier) * 100, 2))} %** por intento · en promedio, unos **{DragonRules.ExpectedAttempts(dragon.Tier):0.#}** intentos.\n{pity}\n" +
                $"Se cría con un amigo que tenga tu mismo tier: **/dragon-breed @amigo** (cada intento los hace descansar {TimeFormat.Remaining(DragonRules.BreedCooldown)}; cuánto falta para el próximo lo ves en **/cd**).\n" +
                $"También podés criarlo **solo**, sin amigo (**/dragon-breed** a secas): cuenta como intento, pero con la **mitad** de chance ({DragonRules.Percent(Math.Round(DragonRules.SoloBreedChance(dragon.Tier) * 100, 2))} %).",
                false);
        }

        MessageComponent? components = dragon.Level >= cap
            ? null
            : new ComponentBuilder().WithButton($"Entrenar ({GameHistory.Number(DragonRules.TrainCost(dragon.Level))} oro)", $"{TrainPrefix}:{discordId}", ButtonStyle.Primary, new Emoji("🏋️")).Build();

        return new DragonView(embed.Build(), components);
    }

    // Solo el embed (lo que usan el comando de texto y las pruebas).
    public static async Task<Embed> BuildDragonEmbedAsync(IDragonRepository dragonRepository, ulong discordId) => (await BuildDragonViewAsync(dragonRepository, discordId)).Embed;

    // Entrenar al dragón (el slash, el botón y «aa dragon train»): hasta `times` niveles, hasta donde alcance el oro y el tope del tier. Devuelve lo que se le cuenta al jugador.
    public static async Task<string> ExecuteTrainAsync(IDragonRepository dragonRepository, ulong discordId, int times)
    {
        var result = await dragonRepository.TrainAsync(discordId, times);
        switch (result.Status)
        {
            case DragonTrainStatus.Trained:
            {
                var dragon = result.Dragon!;
                string levels = result.LevelsGained == 1 ? "1 nivel" : $"{result.LevelsGained} niveles";
                return $"🏋️ Entrenaste a tu dragón **{levels}** por **{GameHistory.Number(result.GoldSpent)}** de oro (te quedan {GameHistory.Number(result.GoldLeft)}): ahora está en el **nivel {dragon.Level}/{DragonRules.LevelCap(dragon.Tier)}** y te da {TypeBonus(dragon.Kind, dragon.Tier, dragon.Level)}. " +
                       (result.NextCost > 0
                           ? $"El próximo nivel cuesta **{GameHistory.Number(result.NextCost)}**."
                           : "Llegó al nivel máximo de su tier: para seguir, subilo de tier criándolo con **/dragon-breed**.");
            }

            case DragonTrainStatus.AtCap:
                return $"Tu dragón ya está en el nivel máximo de su tier (**{DragonRules.LevelCap(result.Dragon!.Tier)}**). Para seguir entrenándolo, subilo de tier criándolo con **/dragon-breed**.";

            case DragonTrainStatus.NotEnoughGold:
                return $"Te falta oro: el próximo nivel cuesta **{GameHistory.Number(result.NextCost)}** y tenés **{GameHistory.Number(result.GoldLeft)}**.";

            default:
                return "No tenés dragón: comprá uno en la **/taberna**.";
        }
    }

    // Ponerle nombre al dragón (o cambiárselo): cuesta DragonRules.RenamePrice cada vez. Devuelve lo que se le cuenta al jugador.
    public static async Task<string> ExecuteRenameAsync(IDragonRepository dragonRepository, ulong discordId, string? rawName)
    {
        string? name = DragonRules.CleanName(rawName);
        if (name is null)
        {
            return $"Ese nombre no sirve: tiene que tener entre {DragonRules.NameMinLength} y {DragonRules.NameMaxLength} caracteres, con letras, números, espacios y . - ' ! (sin menciones ni símbolos).";
        }

        var result = await dragonRepository.RenameAsync(discordId, name, DragonRules.RenamePrice);
        return result.Status switch
        {
            DragonRenameStatus.Renamed => $"✏️ Listo: tu dragón ahora se llama **{name}**. Pagaste **{GameHistory.Number(DragonRules.RenamePrice)}** de oro (te quedan {GameHistory.Number(result.GoldLeft)}); cambiárselo de nuevo cuesta lo mismo.",
            DragonRenameStatus.SameName => "Tu dragón ya se llama así: no te cobro nada.",
            DragonRenameStatus.NotEnoughGold => $"Te falta oro: ponerle nombre a tu dragón cuesta **{GameHistory.Number(DragonRules.RenamePrice)}** y tenés **{GameHistory.Number(result.GoldLeft)}**.",
            _ => "No tenés dragón: comprá uno en la **/taberna**.",
        };
    }

    // El nombre «normalizado» para comparar lo que se escribe en /shop buy: sin tildes, sin mayúsculas y sin espacios de más.
    private static string NormalizeName(string? text)
    {
        string decomposed = (text ?? string.Empty).Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    // ¿Lo escrito en «/shop buy» o «aa shop buy» nombra al dragón? («dragon», «dragón», «dragon de cria»).
    public static bool IsDragonName(string? text) => NormalizeName(text) is "dragon" or "dragon de cria";

    // ¿Lo que se va escribiendo en la lista de /shop buy se parece al dragón (o todavía no se escribió nada)?
    public static bool MatchesDragon(string? typed)
    {
        string normalized = NormalizeName(typed);
        return normalized.Length == 0 || "dragon de cria".Contains(normalized, StringComparison.Ordinal);
    }

    // Criar el dragón SOLO (v0.31.0): sin pareja, con la mitad de la chance del tier; cuenta como intento (descanso de 12 h y fallos seguidos, la piedad sigue igual). Devuelve lo que se le cuenta al jugador.
    public static async Task<Embed> ExecuteSoloBreedAsync(IDragonRepository dragonRepository, ulong discordId, Random? rng = null)
    {
        var result = await dragonRepository.BreedSoloAsync(discordId, rng);
        var embed = new EmbedBuilder().WithTitle("🐲 Criar solo").WithColor(Color.Orange);

        switch (result.Status)
        {
            case DragonBreedStatus.Bred when result.Success:
            {
                int tier = result.Tier + 1;
                var perk = DragonRules.Perks[tier - 1];
                var dragon = result.A!;
                return embed.WithColor(Color.Gold).WithDescription(
                    $"✨ **¡Lo lograste solo!** Tu dragón sube al **Tier {tier}: {DragonRules.TierName(tier)}** y desbloquea una mejora nueva: **{perk.Name}** — {perk.Text}\n" +
                    $"{DragonRules.KindEmoji(dragon.Kind)} {DragonRules.KindName(dragon.Kind)} · {TypeBonus(dragon.Kind, tier, dragon.Level)}\n" +
                    (tier >= DragonRules.MaxTier ? "\n🏆 ¡Llegó al tier máximo!" : $"\nAhora puede entrenar hasta el nivel {DragonRules.LevelCap(tier)} y descansa {TimeFormat.Remaining(DragonRules.BreedCooldown)} antes de volver a criar.")).Build();
            }

            case DragonBreedStatus.Bred:
            {
                int cap = DragonRules.SoloPityCap(result.Tier);
                return embed.WithDescription(
                    $"💨 No se dio esta vez: tu dragón se queda en el **Tier {result.Tier}**. Criándolo solo la chance es la **mitad** ({DragonRules.Percent(Math.Round(DragonRules.SoloBreedChance(result.Tier) * 100, 2))} %); con un amigo del mismo tier es del {DragonRules.Percent(Math.Round(DragonRules.BreedChance(result.Tier) * 100, 2))} %.\n" +
                    $"Fallos seguidos: **{result.A!.FailStreak}/{cap}**; con {cap} fallos seguidos, el siguiente intento sale **seguro**.\n" +
                    $"Descansa {TimeFormat.Remaining(DragonRules.BreedCooldown)} antes de volver a intentar.").Build();
            }

            case DragonBreedStatus.MaxTier:
                return embed.WithColor(Color.Red).WithDescription("Tu dragón ya está en el tier máximo: no hay nada más que criar.").Build();

            case DragonBreedStatus.OnCooldown:
                return embed.WithColor(Color.Red).WithDescription($"Tu dragón está descansando: puede criar en **{TimeFormat.Remaining(result.Wait)}**.").Build();

            default:
                return embed.WithColor(Color.Red).WithDescription("No tenés dragón: comprá uno en la **/taberna** (o con **/shop buy dragon**).").Build();
        }
    }

    // Comprar el dragón (lo que hace la opción de la taberna). Devuelve lo que contesta el tabernero.
    public static async Task<string> ExecuteBuyAsync(IDragonRepository dragonRepository, ulong discordId, Random? rng = null)
    {
        string kind = DragonRules.RollKind(rng);
        var result = await dragonRepository.BuyAsync(discordId, DragonRules.ShopPrice, kind);
        return result.Status switch
        {
            DragonBuyStatus.Bought =>
                $"{NpcDialogue.Shopkeeper(ShopkeeperLine.BuySuccess)}\n\n{UiEmojis.Dragon} Compraste un dragón: sale del huevo un **{DragonRules.KindEmoji(kind)} {DragonRules.KindName(kind)}** (tier 1, **{DragonRules.TierName(1)}**) y ya te da {TypeBonus(kind, 1, 0)}. Miralo con **/dragon**, entrenalo con **/dragon-train** y criálo con un amigo con **/dragon-breed**.",
            DragonBuyStatus.AlreadyHave => "Pero si ya tenés un dragón. Cada uno tiene el suyo: miralo con **/dragon**.",
            DragonBuyStatus.NotEnoughGold => $"{NpcDialogue.Shopkeeper(ShopkeeperLine.NoGold)}\nUn dragón cuesta **{GameHistory.Number(DragonRules.ShopPrice)}** de oro.",
            _ => "Primero tenés que empezar tu aventura con **/start**.",
        };
    }

    // Invitar a criar: valida TODO antes de armar la invitación (los dos con dragón, del mismo tier, ninguno en el máximo ni descansando) y, si está bien, devuelve el mensaje con sus botones.
    public static async Task<BreedInvite> PrepareBreedAsync(IDragonRepository dragonRepository, IDragonBreedOfferService offerService, ulong fromId, ulong toId, bool toIsBot)
    {
        if (toIsBot || toId == fromId)
        {
            return new BreedInvite(toIsBot ? "Los bots no tienen dragón: invitá a otra persona." : "Para criar hace falta el dragón de OTRA persona.");
        }

        var mine = await dragonRepository.GetAsync(fromId);
        if (mine is null)
        {
            return new BreedInvite("No tenés dragón: comprá uno en la **/taberna**.");
        }

        if (mine.Tier >= DragonRules.MaxTier)
        {
            return new BreedInvite("Tu dragón ya está en el tier máximo: no hay nada más que criar.");
        }

        if (!mine.CanBreed)
        {
            return new BreedInvite($"Tu dragón está descansando: puede criar en **{TimeFormat.Remaining(mine.Wait)}**.");
        }

        var theirs = await dragonRepository.GetAsync(toId);
        if (theirs is null)
        {
            return new BreedInvite($"<@{toId}> no tiene dragón.");
        }

        if (theirs.Tier != mine.Tier)
        {
            return new BreedInvite($"Tienen que tener el **mismo tier**: el tuyo es el **{mine.Tier}** ({DragonRules.TierName(mine.Tier)}) y el de <@{toId}> es el **{theirs.Tier}** ({DragonRules.TierName(theirs.Tier)}).");
        }

        if (!theirs.CanBreed)
        {
            return new BreedInvite($"El dragón de <@{toId}> está descansando: puede criar en **{TimeFormat.Remaining(theirs.Wait)}**.");
        }

        var offer = offerService.Create(fromId, toId);
        var perk = DragonRules.Perks[mine.Tier]; // la mejora que desbloquea el tier siguiente
        var embed = new EmbedBuilder()
            .WithTitle("🐲 Invitación a criar")
            .WithColor(Color.DarkOrange)
            .WithDescription(
                $"<@{fromId}> quiere criar su dragón con el de <@{toId}>: los dos son **Tier {mine.Tier} ({DragonRules.TierName(mine.Tier)})**.\n" +
                $"Chance de que los dos suban al tier {mine.Tier + 1} (**{perk.Name}**: {perk.Text}): **{DragonRules.Percent(Math.Round(DragonRules.BreedChance(mine.Tier) * 100, 2))} %**" +
                (Math.Max(mine.FailStreak, theirs.FailStreak) >= DragonRules.PityCap(mine.Tier) ? " (¡y esta vez sale **segura**!)" : string.Empty) +
                $". Pase lo que pase, los dos descansan {TimeFormat.Remaining(DragonRules.BreedCooldown)}.\n\n<@{toId}>, ¿aceptás? Tenés 2 minutos.")
            .Build();
        var buttons = new ComponentBuilder()
            .WithButton("Aceptar", $"{AcceptPrefix}:{offer.Id}", ButtonStyle.Success, new Emoji("🥚"))
            .WithButton("Rechazar", $"{DeclinePrefix}:{offer.Id}", ButtonStyle.Danger, new Emoji("❌"))
            .Build();

        return new BreedInvite(null, $"<@{toId}>", embed, buttons);
    }

    // Cuando el invitado acepta: criar de verdad (el repositorio vuelve a validar todo adentro de la transacción: pasó tiempo) y contar cómo salió.
    public static async Task<Embed> ResolveBreedAsync(IDragonRepository dragonRepository, DragonBreedOffer offer, Random? rng = null)
    {
        var result = await dragonRepository.BreedAsync(offer.FromId, offer.ToId, rng);
        var embed = new EmbedBuilder().WithTitle("🐲 Criar dragones").WithColor(Color.Orange);

        switch (result.Status)
        {
            case DragonBreedStatus.Bred when result.Success:
            {
                int tier = result.Tier + 1;
                var perk = DragonRules.Perks[tier - 1];
                return embed.WithColor(Color.Gold).WithDescription(
                    $"✨ **¡Salió bien!** Los dos dragones suben al **Tier {tier}: {DragonRules.TierName(tier)}** y desbloquean una mejora nueva: **{perk.Name}** — {perk.Text}\n" +
                    $"<@{offer.FromId}>: {DragonRules.KindEmoji(result.A!.Kind)} {DragonRules.KindName(result.A.Kind)} · {TypeBonus(result.A.Kind, tier, result.A.Level)}\n" +
                    $"<@{offer.ToId}>: {DragonRules.KindEmoji(result.B!.Kind)} {DragonRules.KindName(result.B.Kind)} · {TypeBonus(result.B.Kind, tier, result.B.Level)}\n" +
                    (tier >= DragonRules.MaxTier ? "\n🏆 ¡Llegaron al tier máximo!" : $"\nAhora pueden entrenar hasta el nivel {DragonRules.LevelCap(tier)} y descansan {TimeFormat.Remaining(DragonRules.BreedCooldown)} antes de volver a criar.")).Build();
            }

            case DragonBreedStatus.Bred:
            {
                int cap = DragonRules.PityCap(result.Tier);
                int streak = Math.Max(result.A!.FailStreak, result.B!.FailStreak);
                return embed.WithDescription(
                    $"💨 No se dio esta vez: los dragones se quedan en el **Tier {result.Tier}**.\n" +
                    $"Fallos seguidos: **{streak}/{cap}**; con {cap} fallos seguidos, el siguiente intento sale **seguro**.\n" +
                    $"Descansan {TimeFormat.Remaining(DragonRules.BreedCooldown)} antes de volver a intentar.").Build();
            }

            case DragonBreedStatus.TierMismatch:
                return embed.WithColor(Color.Red).WithDescription("Ya no tienen el mismo tier (algo cambió mientras esperaban): no se crió nada.").Build();

            case DragonBreedStatus.MaxTier:
                return embed.WithColor(Color.Red).WithDescription("Ya están en el tier máximo: no hay nada más que criar.").Build();

            case DragonBreedStatus.OnCooldown:
                return embed.WithColor(Color.Red).WithDescription($"El dragón de <@{result.BlockedId}> está descansando: puede criar en **{TimeFormat.Remaining(result.Wait)}**. No se crió nada.").Build();

            default:
                return embed.WithColor(Color.Red).WithDescription($"<@{result.BlockedId}> no tiene dragón: no se crió nada.").Build();
        }
    }
}
