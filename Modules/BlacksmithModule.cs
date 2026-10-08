using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// AttachmentPath: el archivo de la imagen del herrero que hay que adjuntar al mensaje (null si no hay imagen o es una URL).
public sealed record BlacksmithScene(Embed Embed, MessageComponent? Components, string? AttachmentPath = null);

// /forge (aa forge / aa herrero): hablás con el herrero en una escena en vez de tipear comandos. Aparece con su imagen
// (si hay una configurada, ver NpcImages) y te pregunta qué necesitás; elegís de la primera lista desplegable (✅ = ya tenés todo, ❌ = te falta
// algo, diciendo qué) y te contesta: forjado, o "andá a farmear". Su respuesta sale como un mensaje APARTE, justo debajo (si reemplazaba el
// texto de la escena se perdía), y la lista se vuelve a armar después de cada pedido, así podés seguir pidiendo. Por debajo es EXACTAMENTE la
// misma forja de "aa forge make" (ForgeModule.ExecuteMakeAsync): mismas validaciones, mismo cobro atómico y mismos eventos para misiones y logros.
//
// La segunda lista es para VER las recetas por zona: elegís una zona y la escena muestra las recetas de esa zona para tu clase (con lo que
// tenés de cada material ✅/❌), las de zonas que todavía no alcanzás con un 🔒. Es solo para mirar: la primera lista sigue siendo la de tu zona.
public class BlacksmithModule(
    IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository,
    IInventoryRepository inventoryRepository, ICraftingRepository craftingRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string MenuPrefix = "blacksmith_pick";
    private const string ZonePrefix = "blacksmith_zone";

    [SlashCommand("forge", "Pasá por la herrería: elegí de la lista qué querés que te forje, o mirá las recetas de cada zona.")]
    public async Task HandleBlacksmithAsync()
    {
        await DeferAsync();

        try
        {
            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id);
            if (scene.AttachmentPath is { } file)
            {
                await FollowupWithFileAsync(file, embed: scene.Embed, components: scene.Components);
            }
            else
            {
                await FollowupAsync(embed: scene.Embed, components: scene.Components);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! El herrero no está en la fragua ahora, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // El id de cada desplegable lleva a quién pertenece la escena: otro jugador no puede pedirle cosas al herrero con la tuya.
    [ComponentInteraction($"{MenuPrefix}:*")]
    public async Task HandlePickAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: hablá con el herrero con **/forge**.", ephemeral: true);
                return;
            }

            var result = await ForgeModule.ExecuteMakeAsync(
                userRepository, recipeRepository, craftingRepository, gameEvents, Context.User.Id, selected.FirstOrDefault() ?? string.Empty);

            // La escena se refresca (tus materiales y tu oro al día) y lo que contestó el herrero va en un mensaje aparte.
            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });

            await FollowupAsync(embed: BuildAnswerEmbed(result));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude pasarle el pedido al herrero, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Elegiste una zona para ver sus recetas: la escena cambia a esa zona (y deja marcada la que elegiste).
    [ComponentInteraction($"{ZonePrefix}:*")]
    public async Task HandleZoneAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: hablá con el herrero con **/forge**.", ephemeral: true);
                return;
            }

            if (!int.TryParse(selected.FirstOrDefault(), out int zoneId))
            {
                await FollowupAsync("No entendí qué zona querías ver, probá de nuevo.", ephemeral: true);
                return;
            }

            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id, zoneId);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir esa página de recetas, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La respuesta del herrero a lo que pediste, como mensaje propio: el embed del resultado (forjado, o por qué no pudo), o el rechazo simple.
    public static Embed BuildAnswerEmbed(ForgeModule.ForgeMakeResult result) =>
        result.Embed
        ?? new EmbedBuilder().WithColor(Color.Orange)
            .WithDescription(string.IsNullOrWhiteSpace(result.PlainMessage) ? "⚒️" : result.PlainMessage)
            .Build();

    // La escena: el saludo del herrero (o, si viewZoneId trae una zona, las recetas de esa zona) y las listas. Pública y sin Context para que
    // "aa herrero" muestre exactamente lo mismo.
    public static async Task<BlacksmithScene> BuildSceneAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository,
        IInventoryRepository inventoryRepository, ulong discordId, int? viewZoneId = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new BlacksmithScene(
                new EmbedBuilder().WithTitle("⚒️ La Herrería").WithColor(Color.Orange)
                    .WithDescription("Primero tenés que empezar tu aventura con **/start**.").Build(),
                null);
        }

        var recipes = await recipeRepository.GetAllAsync();
        var zones = await zoneRepository.GetAllAsync();
        var inventory = await inventoryRepository.GetByDiscordIdAsync(discordId);
        var owned = inventory.ToDictionary(entry => entry.ItemName, entry => entry.Quantity);
        var choices = ForgeChoices.For(recipes, zones, player, owned, string.Empty);

        // Las zonas con recetas, de la más fácil a la más difícil: lo que se puede mirar.
        var zonesWithRecipes = ZoneRanking.OrderByDifficulty(zones)
            .Where(z => recipes.Any(r => r.ZoneId == z.ZoneId))
            .ToList();

        var image = NpcImages.Blacksmith;
        EmbedBuilder embed;

        if (viewZoneId is int zoneId)
        {
            // La página de recetas de la zona elegida (con lo que tenés de cada material). La frase de cómo forjar es la de la escena.
            embed = ForgeModule.RenderRecipesEmbed(
                    recipes, zones, player.Class, zoneId, owned, player.Gold, player.Level,
                    forgeHint: choices.Count > 0 ? "Forjá desde la primera lista (✅ lo que ya podés hacer)." : "Todavía no tenés nada para forjar en tu zona.",
                    highestZoneCleared: player.HighestZoneCleared)
                .ToEmbedBuilder()
                .WithColor(Color.Orange);
        }
        else
        {
            string talk = choices.Count == 0
                ? NpcDialogue.Blacksmith(BlacksmithLine.NoRecipes)
                : $"{NpcDialogue.Blacksmith(BlacksmithLine.Greeting, fuegoNuevo: player.FuegoNuevo)}\n\n_✅ lo que ya podés forjar · ❌ lo que todavía te falta (te digo qué)_";

            if (zonesWithRecipes.Count > 0)
            {
                talk += "\n_📜 En la segunda lista mirás las recetas de cada zona._";
            }

            embed = new EmbedBuilder().WithTitle("⚒️ La Herrería").WithColor(Color.Orange).WithDescription(talk);

            if (choices.Count > 0)
            {
                embed.WithFooter("Lo que te conteste el herrero sale abajo.");
            }
        }

        // La foto del herrero va de miniatura en toda la charla (el mensaje conserva el adjunto cuando se edita).
        if (image is not null)
        {
            embed.WithThumbnailUrl(image.Reference);
        }

        var components = new ComponentBuilder();
        int row = 0;

        if (choices.Count > 0)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{MenuPrefix}:{discordId}")
                .WithPlaceholder("🔨 ¿Qué querés que te forje?")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (var choice in choices)
            {
                menu.AddOption(choice.Name, choice.Value.ToString()!);
            }

            components.WithSelectMenu(menu, row++);
        }

        if (zonesWithRecipes.Count > 0)
        {
            components.WithSelectMenu(ZoneMenu(discordId, player, zonesWithRecipes, viewZoneId), row);
        }

        var built = embed.Build();
        return new BlacksmithScene(built, row > 0 || zonesWithRecipes.Count > 0 ? components.Build() : null, NpcImages.AttachmentPathFor(built));
    }

    // Una opción por zona: "🌲 Zona 2 · Bosque de Cenizas" y, abajo, el nivel que pide (y si es la tuya). Sin emojis personalizados en las
    // opciones (si el bot no puede usar uno, Discord rechaza el mensaje ENTERO): los de las zonas son emojis comunes de texto.
    private static SelectMenuBuilder ZoneMenu(ulong discordId, User player, IReadOnlyList<Zone> zones, int? viewZoneId)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{ZonePrefix}:{discordId}")
            .WithPlaceholder("📜 Ver las recetas de una zona")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var zone in zones)
        {
            string label = $"{zone.Emoji ?? "🗺️"} Zona {zone.ZoneId} · {zone.Name}";
            string description = $"Nivel {zone.MinLevel}+" + (zone.ZoneId == player.CurrentZoneId ? " · tu zona" : string.Empty);
            menu.AddOption(
                label.Length <= 100 ? label : label[..99] + "…",
                zone.ZoneId.ToString(),
                description,
                isDefault: viewZoneId == zone.ZoneId);
        }

        return menu;
    }
}
