using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Lo que sale de invitar a criar: Error (un rechazo, solo para quien invitó) o el mensaje con la invitación (Content pinguea al invitado) y sus botones.
public sealed record BreedInvite(string? Error, string? Content = null, Embed? Embed = null, MessageComponent? Components = null);

// El DRAGÓN (v0.30.0; GameData/DragonRules.cs, Repositories/DragonRepository.cs, tabla player_dragons): /dragon muestra el tuyo, /dragon-breed @amigo lo cría con el de un amigo del MISMO tier (si sale bien suben los
// dos) y se compra en la taberna (DragonModule.ExecuteBuyAsync, que la lista de cajas de la taberna ofrece mientras no tengas uno). Los botones de la invitación viven en este mismo módulo (sin [Group], para que Discord.Net
// no les pegue el nombre del grupo): Aceptar solo lo toca el invitado; Rechazar, cualquiera de los dos. Toda la lógica es estática y sin Context: el slash, los botones y el comando de texto llaman a lo mismo.
public class DragonModule(IUserRepository userRepository, IDragonRepository dragonRepository, IDragonBreedOfferService offerService)
    : InteractionModuleBase<SocketInteractionContext>
{
    // El valor de la opción «Dragón» en la lista de la taberna. No choca con ningún nombre de ítem.
    public const string ShopKey = "__dragon__";
    private const string AcceptPrefix = "dragon_accept";
    private const string DeclinePrefix = "dragon_decline";

    [SlashCommand("dragon", "Mirá tu dragón: su tipo, su tier, lo que te da y cómo criarlo con un amigo.")]
    public async Task HandleDragonAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: await BuildDragonEmbedAsync(dragonRepository, Context.User.Id));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude mirar tu dragón, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("dragon-breed", "Criá tu dragón con el de un amigo que tenga tu mismo tier: si sale bien, suben los dos.")]
    public async Task HandleBreedAsync([Summary("amigo", "Con quién lo querés criar")] IUser amigo)
    {
        await DeferAsync();

        try
        {
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

    private static string Bonus(string kind, int tier) => $"**+{DragonRules.Percent(DragonRules.BonusPercent(kind, tier))} %** {DragonRules.KindEffect(kind)}";

    // La pantalla del dragón. Sin dragón, cómo conseguirlo; con él, tipo, tier, lo que da ahora y en el próximo tier, la escalera completa de su tipo y cómo va la cría.
    public static async Task<Embed> BuildDragonEmbedAsync(IDragonRepository dragonRepository, ulong discordId)
    {
        var dragon = await dragonRepository.GetAsync(discordId);
        var embed = new EmbedBuilder().WithTitle("🐲 Tu dragón").WithColor(Color.DarkOrange);

        if (dragon is null)
        {
            return embed.WithDescription(
                $"Todavía no tenés dragón. En la **/taberna** podés comprar uno por **{DragonRules.ShopPrice:N0}** de oro (una sola vez): el tipo sale al azar —🗡️ Fuerza, 🛡️ Defensa, 💰 Oro o, con suerte, ✨ Especial— y empieza de **Cría**. " +
                "Después lo criás con el de un amigo que tenga **tu mismo tier** (**/dragon-breed**): si sale bien, suben los dos. Cuanto más alto el tier, más te da.").Build();
        }

        string kind = dragon.Kind;
        bool max = dragon.Tier >= DragonRules.MaxTier;
        embed.WithDescription(
            $"{DragonRules.KindEmoji(kind)} **{DragonRules.TierName(dragon.Tier)}** · Tier **{dragon.Tier}/{DragonRules.MaxTier}** · tipo **{DragonRules.KindName(kind)}**\n" +
            $"Te da {Bonus(kind, dragon.Tier)}" + (max ? "\n🏆 Está en el tier máximo." : $" · en el próximo tier: **+{DragonRules.Percent(DragonRules.BonusPercent(kind, dragon.Tier + 1))} %**") +
            "\n_Solo cuenta contra los monstruos (no en duelos ni en la Arena) y dura toda la vida de tu cuenta: el Fuego Nuevo no lo toca._");

        var ladder = Enumerable.Range(1, DragonRules.MaxTier).Select(t =>
            $"{(t == dragon.Tier ? "▶️" : "▫️")} **{DragonRules.TierNumeral(t)}** {DragonRules.TierName(t)} · +{DragonRules.Percent(DragonRules.BonusPercent(kind, t))} %");
        embed.AddField($"📈 Los tiers del tipo {DragonRules.KindName(kind)}", string.Join('\n', ladder), false);

        if (!max)
        {
            int cap = DragonRules.PityCap(dragon.Tier);
            string wait = dragon.CanBreed ? "✅ Puede criar **ya**" : $"⏳ Descansa: puede criar en **{TimeFormat.Remaining(dragon.Wait)}**";
            string pity = cap == 0
                ? "En este tier la cría es **segura**."
                : $"Fallos seguidos: **{dragon.FailStreak}/{cap}**. Con {cap} fallos seguidos, el siguiente intento sale **seguro**.";
            embed.AddField(
                "🥚 Criar",
                $"{wait}\nChance de subir de tier: **{DragonRules.Percent(Math.Round(DragonRules.BreedChance(dragon.Tier) * 100, 2))} %** por intento · en promedio, unos **{DragonRules.ExpectedAttempts(dragon.Tier):0.#}** intentos.\n{pity}\n" +
                $"Se cría con un amigo que tenga tu mismo tier: **/dragon-breed @amigo** (cada intento los hace descansar {TimeFormat.Remaining(DragonRules.BreedCooldown)}).",
                false);
        }

        return embed.Build();
    }

    // Comprar el dragón (lo que hace la opción de la taberna). Devuelve lo que contesta el tabernero.
    public static async Task<string> ExecuteBuyAsync(IDragonRepository dragonRepository, ulong discordId, Random? rng = null)
    {
        string kind = DragonRules.RollKind(rng);
        var result = await dragonRepository.BuyAsync(discordId, DragonRules.ShopPrice, kind);
        return result.Status switch
        {
            DragonBuyStatus.Bought =>
                $"{NpcDialogue.Shopkeeper(ShopkeeperLine.BuySuccess)}\n\n🐲 Compraste un dragón: sale del huevo un **{DragonRules.KindEmoji(kind)} {DragonRules.KindName(kind)}** (tier 1, **{DragonRules.TierName(1)}**) y ya te da {Bonus(kind, 1)}. Miralo con **/dragon** y criálo con un amigo con **/dragon-breed**.",
            DragonBuyStatus.AlreadyHave => "Pero si ya tenés un dragón. Cada uno tiene el suyo: miralo con **/dragon**.",
            DragonBuyStatus.NotEnoughGold => $"{NpcDialogue.Shopkeeper(ShopkeeperLine.NoGold)}\nUn dragón cuesta **{DragonRules.ShopPrice:N0}** de oro.",
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
        var embed = new EmbedBuilder()
            .WithTitle("🐲 Invitación a criar")
            .WithColor(Color.DarkOrange)
            .WithDescription(
                $"<@{fromId}> quiere criar su dragón con el de <@{toId}>: los dos son **Tier {mine.Tier} ({DragonRules.TierName(mine.Tier)})**.\n" +
                $"Chance de que los dos suban al tier {mine.Tier + 1}: **{DragonRules.Percent(Math.Round(DragonRules.BreedChance(mine.Tier) * 100, 2))} %**" +
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
                return embed.WithColor(Color.Gold).WithDescription(
                    $"✨ **¡Salió bien!** Los dos dragones suben al **Tier {tier}: {DragonRules.TierName(tier)}**.\n" +
                    $"<@{offer.FromId}>: {DragonRules.KindEmoji(result.A!.Kind)} {DragonRules.KindName(result.A.Kind)} · {Bonus(result.A.Kind, tier)}\n" +
                    $"<@{offer.ToId}>: {DragonRules.KindEmoji(result.B!.Kind)} {DragonRules.KindName(result.B.Kind)} · {Bonus(result.B.Kind, tier)}\n" +
                    (tier >= DragonRules.MaxTier ? "\n🏆 ¡Llegaron al tier máximo!" : $"\nDescansan {TimeFormat.Remaining(DragonRules.BreedCooldown)} antes de volver a criar.")).Build();
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
