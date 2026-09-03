using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// El Herrero: separado de la Tienda (/shop) por diseño de juego. La Tienda vende consumibles
// con oro; la Herrería forja equipamiento con oro + materiales/MonsterDrops del inventario.
// Las recetas viven en la base (tablas "recipes"/"recipe_ingredients", ver IRecipeRepository) en
// vez de en código: como sus item_id son Foreign Keys reales, nunca puede haber una receta
// apuntando a un ingrediente o resultado que no exista.
[Group("forge", "La herrería: forjá armas y amuletos con oro y materiales.")]
public class ForgeModule(IUserRepository userRepository, IRecipeRepository recipeRepository, ICraftingRepository craftingRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /forge recipes
    [SlashCommand("recipes", "Mostrá las recetas de forja disponibles (las de tu clase primero).")]
    public async Task HandleRecipesAsync()
    {
        await DeferAsync();

        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);
            await FollowupAsync(embed: await BuildRecipesEmbed(recipeRepository, player.Class));
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude cargar las recetas ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /forge make
    [SlashCommand("make", "Pagale al herrero para forjar un ítem de las recetas conocidas.")]
    public async Task HandleMakeAsync([Summary("item", "Nombre de lo que querés forjar (ver /forge recipes).")] string itemName)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteMakeAsync(userRepository, recipeRepository, craftingRepository, Context.User.Id, itemName);

            if (result.PlainMessage is not null)
            {
                await FollowupAsync(result.PlainMessage, ephemeral: true);
            }
            else
            {
                await FollowupAsync(embed: result.Embed);
            }
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló en la herrería, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Todo lo que sigue es estático (sin dependencia de Context) para que
    // Modules/TextCommandModule.cs comparta exactamente la misma lógica en "aa forge recipes"/"aa forge make".
    //
    // La clase requerida de cada receta se lee del class_requirement de su ítem resultado: las
    // recetas de la clase del jugador se muestran primero, las genéricas (class_requirement NULL)
    // después, y las de OTRAS clases se ocultan del todo — no le sirve a un Arquero ver la receta
    // exclusiva del Hechicero que nunca va a poder forjar.
    public static async Task<Embed> BuildRecipesEmbed(IRecipeRepository recipeRepository, string playerClass)
    {
        var recipes = await recipeRepository.GetAllAsync();

        var embed = new EmbedBuilder()
            .WithTitle("⚒️ Recetas del Herrero")
            .WithColor(Color.DarkGrey);

        if (recipes.Count == 0)
        {
            embed.WithDescription("Todavía no hay recetas cargadas.");
            return embed.Build();
        }

        var classLines = new List<string>();
        var generalLines = new List<string>();

        foreach (var recipe in recipes)
        {
            string ingredients = string.Join(" + ", recipe.Ingredients.Select(i => $"{i.Quantity}x {i.ItemName}"));
            string line = $"**{recipe.ResultItem.Name}**: {recipe.GoldCost} Oro + {ingredients}";

            if (recipe.ResultItem.ClassRequirement is null)
            {
                generalLines.Add(line);
            }
            else if (string.Equals(recipe.ResultItem.ClassRequirement, playerClass, StringComparison.OrdinalIgnoreCase))
            {
                classLines.Add(line);
            }
        }

        if (classLines.Count > 0)
        {
            embed.AddField($"🎯 Recetas de {playerClass}", string.Join('\n', classLines), false);
        }

        if (generalLines.Count > 0)
        {
            embed.AddField("📦 Recetas generales", string.Join('\n', generalLines), false);
        }

        if (classLines.Count == 0 && generalLines.Count == 0)
        {
            embed.WithDescription("Todavía no hay recetas disponibles para vos.");
        }

        return embed.Build();
    }

    // Exactamente uno de los dos campos viene con valor: PlainMessage para los rechazos simples
    // (receta desconocida, clase equivocada), Embed para el resultado real de intentar forjar.
    public sealed record ForgeMakeResult(string? PlainMessage, Embed? Embed);

    public static async Task<ForgeMakeResult> ExecuteMakeAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, ICraftingRepository craftingRepository, ulong discordId, string itemName)
    {
        var recipe = await recipeRepository.GetByResultItemNameAsync(itemName);
        if (recipe is null)
        {
            var allRecipes = await recipeRepository.GetAllAsync();
            string available = string.Join(", ", allRecipes.Select(r => r.ResultItem.Name));
            return new ForgeMakeResult($"El herrero no conoce esa receta. Las disponibles son: {available}.", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (recipe.ResultItem.ClassRequirement is not null
            && !string.Equals(recipe.ResultItem.ClassRequirement, player.Class, StringComparison.OrdinalIgnoreCase))
        {
            return new ForgeMakeResult(
                $"**{recipe.ResultItem.Name}** es exclusivo de la clase **{recipe.ResultItem.ClassRequirement}** — vos sos **{player.Class}**.", null);
        }

        // CraftAsync valida oro + cada ingrediente dentro de una única transacción SQL
        // (SELECT ... FOR UPDATE) y hace rollback completo si falta algo.
        var resolvedIngredients = recipe.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList();
        var outcome = await craftingRepository.CraftAsync(discordId, recipe.GoldCost, resolvedIngredients, recipe.ResultItem.ItemId);

        if (!outcome.Success)
        {
            return new ForgeMakeResult(null, new EmbedBuilder()
                .WithTitle("⚒️ El herrero no pudo forjarlo")
                .WithDescription($"No pudiste forjar **{recipe.ResultItem.Name}**: {outcome.FailureReason}")
                .WithColor(Color.Red)
                .Build());
        }

        return new ForgeMakeResult(null, new EmbedBuilder()
            .WithTitle("⚒️ ¡Forjado con éxito!")
            .WithDescription($"¡El Herrero ha forjado **{recipe.ResultItem.Name}** con éxito!\nOro restante: **{outcome.Player!.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }
}
