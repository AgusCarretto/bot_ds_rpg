using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

public sealed record ToolsScene(Embed Embed, MessageComponent? Components);

// /tools (aa tools | herramientas | equipamiento): el Carpintero arma tu HACHA (para /chop) y tu PICO (para /mine) (v0.29.0, el dueño: «un carpintero o constructor, que sea el de las herramientas, que solo con materiales se
// haga la herramienta… zona 3 y zona 5 por ahora; de base está la zona 1, que es lo actual ×1… y que no esté en el perfil sino en aa equipamiento o aa herramientas»). Sin herramienta se recolecta ×1; la de la Zona 3 da ×2
// y la de la Zona 5 ×3 de lo Común, Raro y Épico (lo Legendario y lo Mítico siguen dando 1, GameData/ToolRules.cs). Se arman solo con materiales de /chop y /mine (sin oro), se ponen solas y la nueva reemplaza a la vieja.
// Los datos viven en la base (Database/add_tools.sql: ítems 'Hacha' y 'Pico' con su receta y la zona que la desbloquea; lo que tiene cada jugador, en player_tools). La escena es como la de la herrería: el saludo, lo que llevás,
// lo que podés armar con ✅/❌ de cada material, y una lista para armar; la respuesta del carpintero sale como un mensaje aparte. Es el lugar pensado para sumar más equipamiento (granja, cocina…) más adelante.
public class ToolsModule(
    IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository, IInventoryRepository inventoryRepository,
    ICraftingRepository craftingRepository, IToolRepository toolRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string MenuPrefix = "tools_pick";

    [SlashCommand("tools", "Pasá por el Carpintero: mirá tu hacha y tu pico y armá uno mejor para recolectar más.")]
    public async Task HandleToolsAsync()
    {
        await DeferAsync();

        try
        {
            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, toolRepository, Context.User.Id);
            await FollowupAsync(embed: scene.Embed, components: scene.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! El carpintero no está en el taller ahora, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // El id de la lista lleva a quién pertenece la escena: otro jugador no puede pedirle cosas al carpintero con la tuya.
    [ComponentInteraction($"{MenuPrefix}:*")]
    public async Task HandlePickAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: hablá con el carpintero con **/tools**.", ephemeral: true);
                return;
            }

            var result = await ExecuteCraftAsync(
                userRepository, recipeRepository, zoneRepository, craftingRepository, toolRepository, gameEvents, Context.User.Id, selected.FirstOrDefault() ?? string.Empty);

            // La escena se refresca (tus materiales y tus herramientas al día) y lo que contestó el carpintero va en un mensaje aparte.
            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, toolRepository, Context.User.Id);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });

            await FollowupAsync(embed: BlacksmithModule.BuildAnswerEmbed(result));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude pasarle el pedido al carpintero, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La zona (1-indexada por dificultad) que desbloquea una receta de herramienta; sin zona cargada, la primera.
    private static int UnlockRank(RecipeDetails recipe, IReadOnlyList<Zone> orderedZones) =>
        recipe.ZoneId is int zoneId ? Math.Max(1, ZoneRanking.RankOf(orderedZones, zoneId)) : 1;

    // La escena: el saludo, lo que llevás de cada herramienta, las mejoras que hay (con lo que tenés de cada material) y la lista para armarlas. Pública y sin Context para que "aa herramientas" muestre exactamente lo mismo.
    public static async Task<ToolsScene> BuildSceneAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository, IInventoryRepository inventoryRepository,
        IToolRepository toolRepository, ulong discordId, Random? rng = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new ToolsScene(
                new EmbedBuilder().WithTitle("🪚 El Carpintero").WithColor(Color.DarkOrange)
                    .WithDescription("Primero tenés que empezar tu aventura con **/start**.").Build(),
                null);
        }

        var toolRecipes = (await recipeRepository.GetAllAsync()).Where(r => ToolRules.IsToolType(r.ResultItem.Type)).ToList();
        var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        int unlocked = ZoneRanking.MaxUnlockedRank(orderedZones, player.Level, player.HighestZoneCleared);
        var worn = await toolRepository.GetAsync(discordId);
        var owned = (await inventoryRepository.GetByDiscordIdAsync(discordId)).ToDictionary(e => e.ItemName, e => e.Quantity);

        var embed = new EmbedBuilder()
            .WithTitle("🪚 El Carpintero")
            .WithColor(Color.DarkOrange)
            .WithDescription(
                $"{NpcDialogue.Carpenter(CarpenterLine.Greeting, rng)}\n\n" +
                "_Las herramientas se arman solo con materiales de **/chop** y **/mine** (sin oro) y se ponen solas. Multiplican lo **Común, Raro y Épico** que recolectás; lo Legendario y lo Mítico siguen saliendo de a uno. " +
                "Con cada Fuego Nuevo se arman de nuevo._");

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{MenuPrefix}:{discordId}")
            .WithPlaceholder("🔨 ¿Qué querés que te arme?")
            .WithMinValues(1)
            .WithMaxValues(1);
        int options = 0;

        foreach (string kind in new[] { ToolRules.AxeKind, ToolRules.PickaxeKind })
        {
            var current = worn.FirstOrDefault(t => t.Kind == kind);
            int currentMultiplier = current?.Multiplier ?? 1;
            var upgrades = toolRecipes
                .Where(r => ToolRules.KindOfItemType(r.ResultItem.Type) == kind && r.ResultItem.StatValue > currentMultiplier)
                .OrderBy(r => r.ResultItem.StatValue)
                .ToList();

            string label = $"{ToolRules.KindEmoji(kind)} {ToolRules.KindLabel(kind)} ({ToolRules.KindCommand(kind)})";
            string wornText = current is null
                ? $"Sin {ToolRules.KindLabel(kind).ToLowerInvariant()} · **×1** (lo de siempre)"
                : $"{ItemDisplay.Format(current.Emoji, current.Name)} · **×{current.Multiplier}** de cantidad";
            embed.AddField(label, upgrades.Count == 0 && current is not null ? $"{wornText}\n✨ Es lo mejor que arma el Carpintero por ahora." : wornText, false);

            foreach (var recipe in upgrades)
            {
                int rank = UnlockRank(recipe, orderedZones);
                bool isLocked = rank > unlocked;
                bool canMake = !isLocked && recipe.Ingredients.All(i => owned.GetValueOrDefault(i.ItemName) >= i.Quantity);
                string ingredients = string.Join('\n', recipe.Ingredients.Select(i =>
                    $"{(owned.GetValueOrDefault(i.ItemName) >= i.Quantity ? "✅" : "❌")} {ItemDisplay.Format(i.Emoji, i.ItemName)} — {owned.GetValueOrDefault(i.ItemName)}/{i.Quantity}"));
                string lockNote = isLocked ? $"\n🔒 _Se desbloquea al llegar a la Zona {rank}: {orderedZones[Math.Min(rank, orderedZones.Count) - 1].Name}._" : string.Empty;
                string value = $"**×{recipe.ResultItem.StatValue}** de cantidad en {ToolRules.KindCommand(kind)} · _{ToolRules.KindLabel(kind).ToLowerInvariant()}_{lockNote}\n\n{ingredients}";
                embed.AddField(ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name), value.Length <= 1024 ? value : value[..1023] + "…", false);

                if (!isLocked)
                {
                    menu.AddItemOption(recipe.ResultItem.Name, $"{(canMake ? "✅" : "❌")} {recipe.ResultItem.Name}", recipe.ResultItem.Name, $"×{recipe.ResultItem.StatValue} en {ToolRules.KindCommand(kind)}");
                    options++;
                }
            }
        }

        if (options > 0)
        {
            embed.WithFooter("Lo que te conteste el carpintero sale abajo.");
        }

        return new ToolsScene(embed.Build(), options > 0 ? new ComponentBuilder().WithSelectMenu(menu).Build() : null);
    }

    // Armar una herramienta: la misma transacción del crafteo (ICraftingRepository.CraftAsync: valida cada material, los descuenta y deja la herramienta puesta, o no toca nada). Antes de gastar nada se chequea que la
    // receta sea de una herramienta, que la zona esté desbloqueada y que no tengas una igual o mejor; CraftAsync vuelve a validar lo último adentro de la transacción. Estático y sin Context para probarlo sin Discord.
    public static async Task<ForgeModule.ForgeMakeResult> ExecuteCraftAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository, ICraftingRepository craftingRepository,
        IToolRepository toolRepository, IGameEvents gameEvents, ulong discordId, string itemName, Random? rng = null)
    {
        var recipe = await recipeRepository.GetByResultItemNameAsync(itemName);
        if (recipe is null || !ToolRules.IsToolType(recipe.ResultItem.Type))
        {
            return new ForgeModule.ForgeMakeResult($"{NpcDialogue.Carpenter(CarpenterLine.UnknownTool, rng)}\n(Mirá lo que arma con `/tools`.)", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        string kind = ToolRules.KindOfItemType(recipe.ResultItem.Type)!;
        string itemText = ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name);

        var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        int rank = UnlockRank(recipe, orderedZones);
        if (rank > ZoneRanking.MaxUnlockedRank(orderedZones, player.Level, player.HighestZoneCleared))
        {
            return new ForgeModule.ForgeMakeResult(
                $"{NpcDialogue.Carpenter(CarpenterLine.Locked, rng)}\n🔒 **{itemText}** se desbloquea al llegar a la **Zona {rank}**.", null);
        }

        var current = (await toolRepository.GetAsync(discordId)).FirstOrDefault(t => t.Kind == kind);
        if (current is not null && current.Multiplier >= recipe.ResultItem.StatValue)
        {
            return new ForgeModule.ForgeMakeResult($"{NpcDialogue.Carpenter(CarpenterLine.AlreadyHave, rng)}\nTu {ToolRules.KindLabel(kind).ToLowerInvariant()} actual: {ItemDisplay.Format(current.Emoji, current.Name)} (×{current.Multiplier}).", null);
        }

        var ingredients = recipe.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList();
        var outcome = await craftingRepository.CraftAsync(discordId, recipe.GoldCost, ingredients, recipe.ResultItem.ItemId);

        if (!outcome.Success)
        {
            if (outcome.SlotOccupied)
            {
                // Se armó otra mejor entre el chequeo de arriba y la transacción (dos pedidos a la vez): mismo aviso.
                return new ForgeModule.ForgeMakeResult($"{NpcDialogue.Carpenter(CarpenterLine.AlreadyHave, rng)}", null);
            }

            return new ForgeModule.ForgeMakeResult(null, new EmbedBuilder()
                .WithTitle("🪚 El carpintero no pudo armarla")
                .WithDescription($"{NpcDialogue.Carpenter(CarpenterLine.NotEnough, rng)}\n\nNo pudiste armar **{itemText}**: {outcome.FailureReason}")
                .WithColor(Color.Red)
                .Build());
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.Craft, player.CurrentZoneId, detail: recipe.ResultItem.Name);

        return new ForgeModule.ForgeMakeResult(null, new EmbedBuilder()
            .WithTitle("🪚 ¡Herramienta lista!")
            .WithDescription(
                $"{NpcDialogue.Carpenter(CarpenterLine.Success, rng)}\n\n**{itemText}** ya quedó **puesta** como tu {ToolRules.KindLabel(kind).ToLowerInvariant()}: " +
                $"{ToolRules.KindCommand(kind)} te da **×{recipe.ResultItem.StatValue}** de lo Común, Raro y Épico.")
            .WithColor(Color.Green)
            .Build());
    }
}
