using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /heal: "curate del todo". Come de la mochila lo NECESARIO para llenar la vida de una (en vez de una sola comida por vez) y lo anota todo en un
// mensaje. Qué come lo decide GameData/HealPlanner.cs: la combinación que desperdicia menos curación (y, a igual desperdicio, menos unidades), sin
// tocar los banquetes (su valor está en el buff de ataque, que se activa a propósito con /use). Si el jugador elige una comida ("comida"), come
// solo de esa, las unidades que hagan falta. Si no alcanza la comida, se la come toda y avisa cuánto falta. Todo se cobra en UNA transacción
// (IInventoryRepository.EatAndHealAsync): o se gasta todo lo planeado y se cura, o no cambia nada. Sigue bloqueado en combate por la misma razón
// de siempre: comer tranquilo no debería ser gratis en pleno combate, para eso está /use (cede el turno al monstruo, ver Modules/UseModule.cs).
public class TavernModule(IUserRepository userRepository, IInventoryRepository inventoryRepository, ICombatSessionService combatSessions, IBuffRepository buffRepository, IRaidSessionService raidSessions, IPlayerBonusService bonusService)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /heal [comida]
    [SlashCommand("heal", "Curate toda la vida de una: come lo necesario de tu mochila (o elegí qué comida usar).")]
    public async Task HandleHealAsync(
        [Summary("comida", "Opcional: qué comida usar. Sin elegir, uso la combinación que menos desperdicie.")]
        [Autocomplete(typeof(HealFoodAutocompleteHandler))] string? food = null)
    {
        await DeferAsync();

        try
        {
            var embed = await ExecuteHealAsync(userRepository, inventoryRepository, combatSessions, buffRepository, Context.User.Id, food, raidSessions, bonusService);
            if (NpcImages.AttachmentPathFor(embed) is { } file)
            {
                await FollowupWithFileAsync(file, embed: embed);
            }
            else
            {
                await FollowupAsync(embed: embed);
            }
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /heal] {ex}");
            await FollowupAsync("¡Upa! No pude procesar la curación, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa heal".
    public static async Task<Embed> ExecuteHealAsync(
        IUserRepository userRepository, IInventoryRepository inventoryRepository, ICombatSessionService combatSessions,
        IBuffRepository buffRepository, ulong discordId, string? foodName = null, IRaidSessionService? raidSessions = null, IPlayerBonusService? bonusService = null) =>
        NpcImages.Decorate(await ExecuteHealCoreAsync(userRepository, inventoryRepository, combatSessions, buffRepository, discordId, foodName, raidSessions, bonusService), NpcImages.Innkeeper);

    // Sin la foto del tabernero: lo que reutiliza la taberna (la opción "Curarme del todo") para sacarle el texto.
    // La vida que dice el texto es la que se ve en todas partes (con el factor de clase, GameData/HpDisplay.cs); lo que come y cuánto cura sigue contándose en vida real.
    public static async Task<Embed> ExecuteHealCoreAsync(
        IUserRepository userRepository, IInventoryRepository inventoryRepository, ICombatSessionService combatSessions,
        IBuffRepository buffRepository, ulong discordId, string? foodName = null, IRaidSessionService? raidSessions = null, IPlayerBonusService? bonusService = null)
    {
        // También en pleno jefe en equipo (ya peleando): ahí se come con el botón del combate, no pasando por la posada.
        if (combatSessions.Peek(discordId) is not null || raidSessions?.IsInActiveFight(discordId) == true)
        {
            return new EmbedBuilder()
                .WithTitle("⚔️ No podés comer tranquilo en pleno combate")
                .WithDescription(NpcDialogue.Innkeeper(InnkeeperLine.InCombat) + "\n\nMientras estás peleando no podés parar a comer así nomás. Usá **/use <ítem>** para curarte con un consumible de tu inventario (te va a costar el turno).")
                .WithColor(Color.DarkGrey)
                .Build();
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);
        double hpMultiplier = await bonusService.HpMultiplierAsync(player);

        // La vida se cuenta en las unidades que se ven (GameData/HpDisplay.cs): una comida que cura 15 cura 15 de esa vida, como dentro de una pelea. A la base se le pasa la vida real equivalente.
        int shownMissing = HpDisplay.Scale(player.MaxHp, hpMultiplier) - HpDisplay.Scale(player.CurrentHp, hpMultiplier);

        if (player.CurrentHp >= player.MaxHp || shownMissing <= 0)
        {
            return new EmbedBuilder()
                .WithTitle("❤️ Ya estás al máximo")
                .WithDescription($"{NpcDialogue.Innkeeper(InnkeeperLine.FullHp)}\n\nTenés {HpDisplay.Scale(player.CurrentHp, hpMultiplier)}/{HpDisplay.Scale(player.MaxHp, hpMultiplier)} HP, no necesitás curarte todavía.")
                .WithColor(Color.Green)
                .Build();
        }

        // Los banquetes (comida con buff de ataque) NO se gastan acá: su valor está en el buff, que se quiere activar a propósito (con /use) y no
        // desperdiciarlo curando una herida.
        var buffItems = await buffRepository.GetItemBuffsAsync();
        var owned = (await inventoryRepository.GetOwnedByTypeAsync(discordId, "Consumable"))
            .Where(o => !buffItems.ContainsKey(o.Item.ItemId) && o.Quantity > 0)
            .ToList();

        // Si eligió una comida puntual, se usa solo esa.
        if (!string.IsNullOrWhiteSpace(foodName))
        {
            var named = owned.FirstOrDefault(o => AutocompleteText.SameName(o.Item.Name, foodName));
            if (named is null)
            {
                bool isBanquet = (await inventoryRepository.GetOwnedByTypeAsync(discordId, "Consumable"))
                    .Any(o => buffItems.ContainsKey(o.Item.ItemId) && AutocompleteText.SameName(o.Item.Name, foodName));

                return new EmbedBuilder()
                    .WithTitle(isBanquet ? "🍷 Eso es un banquete" : "🍽️ No tenés eso")
                    .WithDescription(isBanquet
                        ? "Los banquetes no se usan para curarse del todo: dan un buff de ataque, así que se comen a propósito con **/use**."
                        : $"No tenés **{foodName}** en la mochila (o no se puede usar para curarse). Mirá la lista que te sale al escribir **/heal** o dejalo vacío para que elija el tabernero.")
                    .WithColor(Color.Red)
                    .Build();
            }

            owned = [named];
        }

        if (owned.Count == 0)
        {
            return new EmbedBuilder()
                .WithTitle("🍽️ No tenés nada para comer")
                .WithDescription(NpcDialogue.Innkeeper(InnkeeperLine.NothingToEat) + "\n\nPara curarte primero tenés que comprar comida. Pasá por **/taberna** (o **/shop view**) para ver qué hay.")
                .WithColor(Color.Red)
                .Build();
        }

        var plan = HealPlanner.Plan(
            shownMissing, owned.Select(o => new FoodStock(o.Item.ItemId, o.Item.Name, o.Item.Emoji, o.Item.StatValue, o.Quantity)).ToList());

        if (plan.Foods.Count == 0)
        {
            return new EmbedBuilder()
                .WithTitle("🍽️ Esa comida no cura")
                .WithDescription("No hay nada en tu mochila que sirva para curarte ahora.")
                .WithColor(Color.Red)
                .Build();
        }

        // Si el plan llena la vida, se llena del todo (el redondeo de pasar de una unidad a otra no puede dejar uno de menos); si no, la curación que se ve pasada a vida real.
        int realHeal = plan.Covers ? player.MaxHp - player.CurrentHp : HpDisplay.ToReal(plan.TotalHeal, hpMultiplier);
        var healed = await inventoryRepository.EatAndHealAsync(discordId, plan.Foods.Select(f => (f.Food.ItemId, f.Quantity)).ToList(), realHeal);
        if (healed is null)
        {
            // Perdió la carrera contra otra acción que gastó la misma comida casi al mismo tiempo (ej. /use o /shop sell disparados en simultáneo):
            // no se gastó nada. Rarísimo, pero posible.
            return new EmbedBuilder()
                .WithTitle("😅 Justo se gastó")
                .WithDescription("Justo se te fue algo de la comida por otra acción, probá de nuevo en un toque.")
                .WithColor(Color.DarkGrey)
                .Build();
        }

        string eaten = string.Join("\n", plan.Foods.Select(f => $"• {f.Quantity}× {ItemDisplay.Format(f.Food.Emoji, f.Food.Name)}"));
        // Todo lo que se dice de la vida va ya con el factor de clase: lo que recuperó es la diferencia de las dos cifras que se ven, así los números del mensaje cierran entre sí.
        int shownNow = HpDisplay.Scale(healed.CurrentHp, hpMultiplier), shownMax = HpDisplay.Scale(healed.MaxHp, hpMultiplier);
        int recovered = shownNow - HpDisplay.Scale(player.CurrentHp, hpMultiplier);

        if (!plan.Covers)
        {
            return new EmbedBuilder()
                .WithTitle("🍖 Te comiste todo lo que tenías")
                .WithDescription(
                    $"{NpcDialogue.Innkeeper(InnkeeperLine.Healed)}\n\nComiste:\n{eaten}\n\n" +
                    $"❤️ Recuperaste **{recovered}** HP: ahora tenés **{shownNow}/{shownMax}**, pero todavía te faltan **{shownMax - shownNow}**. " +
                    "Comprá más comida en **/taberna** y volvé a curarte.")
                .WithColor(Color.Orange)
                .Build();
        }

        return new EmbedBuilder()
            .WithTitle("🍖 ¡Buen provecho!")
            .WithDescription(
                $"{NpcDialogue.Innkeeper(InnkeeperLine.Healed)}\n\nComiste:\n{eaten}\n\n" +
                $"❤️ Recuperaste **{recovered}** HP: ahora tenés **{shownNow}/{shownMax}**.")
            .WithColor(Color.Green)
            .Build();
    }
}
