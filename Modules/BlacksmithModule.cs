using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// AttachmentPath: el archivo de la imagen del herrero que hay que adjuntar al mensaje (null si no hay imagen o es una URL).
public sealed record BlacksmithScene(Embed Embed, MessageComponent? Components, string? AttachmentPath = null);

// /forge (aa forge / aa herrero): hablás con el herrero en una escena en vez de tipear comandos. Aparece con su imagen
// (si hay una configurada, ver NpcImages) y te pregunta qué necesitás; elegís de la lista desplegable (✅ = ya tenés todo, ❌ = te falta
// algo, diciendo qué) y te contesta: forjado, o "andá a farmear". La lista se vuelve a armar después de cada pedido, así podés seguir
// pidiendo. Por debajo es EXACTAMENTE la misma forja de /forge make (ForgeModule.ExecuteMakeAsync): mismas validaciones, mismo
// cobro atómico y mismos eventos para misiones y logros. /forge sigue andando, para quien prefiera el comando directo.
public class BlacksmithModule(
    IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository,
    IInventoryRepository inventoryRepository, ICraftingRepository craftingRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string MenuPrefix = "blacksmith_pick";

    [SlashCommand("forge", "Pasá por la herrería: elegí de la lista qué querés que te forje.")]
    public async Task HandleBlacksmithAsync()
    {
        await DeferAsync();

        try
        {
            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id, null);
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

    // El id del desplegable lleva a quién pertenece la escena: otro jugador no puede pedirle cosas al herrero con la tuya.
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
            var scene = await BuildSceneAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id, result);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude pasarle el pedido al herrero, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La escena: el saludo (o, si "after" trae lo que pasó con el último pedido, la respuesta del herrero) y la lista de recetas de la zona.
    // Pública y sin Context para que "aa herrero" muestre exactamente lo mismo.
    public static async Task<BlacksmithScene> BuildSceneAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository,
        IInventoryRepository inventoryRepository, ulong discordId, ForgeModule.ForgeMakeResult? after)
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

        if (after?.Embed is { } result)
        {
            // La respuesta del herrero a lo último que le pediste (ya trae su frase: forjado, o "andá a farmear").
            embed = result.ToEmbedBuilder();
        }
        else
        {
            string talk = after?.PlainMessage
                ?? (choices.Count == 0
                    ? NpcDialogue.Blacksmith(BlacksmithLine.NoRecipes)
                    : $"{NpcDialogue.Blacksmith(BlacksmithLine.Greeting)}\n\n_✅ lo que ya podés forjar · ❌ lo que todavía te falta (te digo qué)_");
            embed = new EmbedBuilder().WithTitle("⚒️ La Herrería").WithColor(Color.Orange).WithDescription(talk);
        }

        // La foto del herrero va de miniatura en toda la charla (el mensaje conserva el adjunto cuando se edita).
        if (image is not null)
        {
            embed.WithThumbnailUrl(image.Reference);
        }

        if (choices.Count == 0)
        {
            var plain = embed.Build();
            return new BlacksmithScene(plain, null, NpcImages.AttachmentPathFor(plain));
        }

        embed.WithFooter("Elegí otra cosa de la lista cuando quieras.");

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{MenuPrefix}:{discordId}")
            .WithPlaceholder("🔨 ¿Qué querés que te forje?")
            .WithMinValues(1)
            .WithMaxValues(1);
        foreach (var choice in choices)
        {
            menu.AddOption(choice.Name, choice.Value.ToString()!);
        }

        var built = embed.Build();
        return new BlacksmithScene(built, new ComponentBuilder().WithSelectMenu(menu).Build(), NpcImages.AttachmentPathFor(built));
    }
}
