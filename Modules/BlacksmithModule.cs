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
// El botón de abajo es para VER las recetas: abre el recetario completo (todas las zonas, una línea por receta, con lo de tu clase y con un 🔒 en las zonas que todavía no alcanzás) y desde ahí volvés al saludo.
// Es solo para mirar: la lista desplegable sigue siendo la de tu zona.
public class BlacksmithModule(
    IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository,
    IInventoryRepository inventoryRepository, ICraftingRepository craftingRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string MenuPrefix = "blacksmith_pick";
    private const string BookPrefix = "blacksmith_book";
    private const string HomePrefix = "blacksmith_home";

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

    // Tocaste «Ver todas las recetas»: la escena muestra el recetario completo (todas las zonas, una línea por receta) en vez del saludo. Es solo para mirar.
    [ComponentInteraction($"{BookPrefix}:*")]
    public async Task HandleBookAsync(string ownerRaw) => await ShowSceneAsync(ownerRaw, showBook: true);

    // Tocaste «Volver a la herrería»: vuelve el saludo del herrero.
    [ComponentInteraction($"{HomePrefix}:*")]
    public async Task HandleHomeAsync(string ownerRaw) => await ShowSceneAsync(ownerRaw, showBook: false);

    private async Task ShowSceneAsync(string ownerRaw, bool showBook)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: hablá con el herrero con **/forge**.", ephemeral: true);
                return;
            }

            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id, showBook);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir el recetario, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La respuesta del herrero a lo que pediste, como mensaje propio: el embed del resultado (forjado, o por qué no pudo), o el rechazo simple.
    public static Embed BuildAnswerEmbed(ForgeModule.ForgeMakeResult result) =>
        result.Embed
        ?? new EmbedBuilder().WithColor(Color.Orange)
            .WithDescription(string.IsNullOrWhiteSpace(result.PlainMessage) ? "⚒️" : result.PlainMessage)
            .Build();

    // La escena: el saludo del herrero (o, con showBook, el recetario completo) y los controles. Pública y sin Context para que "aa herrero" muestre exactamente lo mismo.
    public static async Task<BlacksmithScene> BuildSceneAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository,
        IInventoryRepository inventoryRepository, ulong discordId, bool showBook = false)
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

        var image = NpcImages.Blacksmith;
        EmbedBuilder embed;

        if (showBook)
        {
            // El recetario completo (todas las zonas para tu clase, con 🔒 en las que todavía no alcanzás). La frase de cómo forjar es la de la escena.
            embed = ForgeModule.RenderRecipesEmbed(
                    recipes, zones, player.Class, null, player.Level,
                    forgeHint: choices.Count > 0 ? "Forjá desde la lista de arriba (✅ lo que ya podés hacer)." : "Todavía no tenés nada para forjar en tu zona.",
                    highestZoneCleared: player.HighestZoneCleared)
                .ToEmbedBuilder()
                .WithColor(Color.Orange);
        }
        else
        {
            string talk = choices.Count == 0
                ? NpcDialogue.Blacksmith(BlacksmithLine.NoRecipes)
                : $"{NpcDialogue.Blacksmith(BlacksmithLine.Greeting, fuegoNuevo: player.FuegoNuevo)}\n\n_✅ lo que ya podés forjar · ❌ lo que todavía te falta (te digo qué)_";

            talk += "\n_📜 Con el botón de abajo ves todas las recetas._";

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
                // El valor es el nombre del ítem que se forja: su emoji va en la opción (el ✅ / ❌ del estado se queda en el texto).
                menu.AddItemOption(choice.Value.ToString()!, choice.Name, choice.Value.ToString()!);
            }

            components.WithSelectMenu(menu, row++);
        }

        // Un solo botón: abre el recetario completo, y desde el recetario vuelve al saludo.
        components.WithButton(
            showBook ? "↩ Volver a la herrería" : "📜 Ver todas las recetas",
            showBook ? $"{HomePrefix}:{discordId}" : $"{BookPrefix}:{discordId}",
            ButtonStyle.Secondary, row: row);

        var built = embed.Build();
        return new BlacksmithScene(built, components.Build(), NpcImages.AttachmentPathFor(built));
    }
}
