using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// El Herrero: separado de la Tienda (/shop) por diseño de juego. La Tienda vende consumibles
// con oro; la Herrería forja equipamiento con oro + materiales/MonsterDrops del inventario.
// Las recetas viven en la base (tablas "recipes"/"recipe_ingredients", ver IRecipeRepository) en
// vez de en código: como sus item_id son Foreign Keys reales, nunca puede haber una receta
// apuntando a un ingrediente o resultado que no exista.
[Group("forge", "La herrería: forjá armas y amuletos con oro y materiales.")]
public class ForgeModule(
    IUserRepository userRepository, IRecipeRepository recipeRepository, ICraftingRepository craftingRepository, IZoneRepository zoneRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /forge recipes
    [SlashCommand("recipes", "Mostrá las recetas de forja de tu zona actual, con lo que suma cada ítem (+ATQ / +DEF).")]
    public async Task HandleRecipesAsync()
    {
        await DeferAsync();

        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);
            await FollowupAsync(embed: await BuildRecipesEmbed(recipeRepository, zoneRepository, player.Class, player.CurrentZoneId));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude cargar las recetas ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /forge make
    [SlashCommand("make", "Pagale al herrero para forjar un ítem de las recetas conocidas.")]
    public async Task HandleMakeAsync(
        [Summary("item", "Elegí de la lista qué forjar (✅ = ya tenés el oro y los materiales).")]
        [Autocomplete(typeof(ForgeAutocompleteHandler))] string itemName)
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
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló en la herrería, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Todo lo que sigue es estático (sin dependencia de Context) para que
    // Modules/TextCommandModule.cs comparta exactamente la misma lógica en "aa forge recipes"/"aa forge make".
    //
    // Muestra SOLO las recetas de la zona actual del jugador (o, si esa todavía no tiene ninguna, la anterior
    // más cercana que sí — ver GameData/RecipeCatalog.cs), de esas las que le corresponden: su arma de afinidad,
    // el arma general y los amuletos. Son 4 recetas (2 armas y 2 amuletos), una por bloque, así que entra sobrado en
    // los límites de Discord (un campo de embed admite 1024 caracteres y el embed entero 6000; Build() tira excepción
    // si se pasa) — por eso el molde de 7 recetas por zona y ver solo la propia. Cada bloque dice cuánto suma el
    // resultado ("+15 ATQ" un arma, "+20 DEF" un amuleto; con ⭐ y el valor real si es de la familia de la clase del
    // jugador), el oro y los ingredientes uno por línea.
    public static async Task<Embed> BuildRecipesEmbed(
        IRecipeRepository recipeRepository, IZoneRepository zoneRepository, string playerClass, int currentZoneId)
    {
        var recipes = await recipeRepository.GetAllAsync();
        var zones = await zoneRepository.GetAllAsync();

        var embed = new EmbedBuilder()
            .WithTitle("⚒️ Recetas del Herrero")
            .WithColor(Color.DarkGrey);

        if (recipes.Count == 0)
        {
            embed.WithDescription("Todavía no hay recetas cargadas.");
            return embed.Build();
        }

        var view = RecipeCatalog.ViewFor(recipes, zones, playerClass, currentZoneId);
        if (view.Zone is null)
        {
            embed.WithDescription("Todavía no hay recetas para tu zona.");
            return embed.Build();
        }

        string zoneName = $"{view.Zone.Emoji ?? "🗺️"} **Zona {view.Zone.ZoneId}: {view.Zone.Name}**";
        string fallbackNote = view.IsFallback
            ? "\n_Tu zona actual todavía no tiene recetas propias: te muestro las de la última zona que sí._"
            : string.Empty;

        if (view.Recipes.Count == 0)
        {
            embed.WithDescription($"{zoneName}{fallbackNote}\nNo hay recetas para vos en esta zona todavía.");
            return embed.Build();
        }

        embed.WithDescription($"{zoneName}{fallbackNote}\nForjá con `/forge make` (la lista te marca ✅ lo que ya podés hacer).")
            .WithFooter("🎯 arma de tu clase  ·  ⚔️ arma general  ·  📿 amuleto");

        AddRecipes(embed, view.Recipes, RecipeGroup.ClassWeapon, "🎯", playerClass);
        AddRecipes(embed, view.Recipes, RecipeGroup.GeneralWeapon, "⚔️", playerClass);
        AddRecipes(embed, view.Recipes, RecipeGroup.Amulet, "📿", playerClass);
        AddRecipes(embed, view.Recipes, RecipeGroup.Other, "📦", playerClass);

        return embed.Build();
    }

    // UNA receta por bloque (un field no en línea), en vez de una línea larga por receta: el título lleva el ítem, y
    // abajo van lo que suma, el oro y cada ingrediente en su propia línea. Con las cantidades de la recolección (Hierro
    // x10...) la línea única era una pared de texto.
    private static void AddRecipes(
        EmbedBuilder embed, IEnumerable<RecipeDetails> recipes, RecipeGroup group, string groupEmoji, string playerClass)
    {
        var inGroup = recipes
            .Where(r => RecipeCatalog.GroupOf(r) == group)
            .OrderBy(r => r.ResultItem.StatValue)
            .ThenBy(r => r.ResultItem.Name, StringComparer.Ordinal);

        foreach (var recipe in inGroup)
        {
            string stat = ItemStatLabel.FormatFor(recipe.ResultItem, playerClass) ?? recipe.ResultItem.Type;
            string ingredients = string.Join('\n', recipe.Ingredients.Select(i => $"• {i.Quantity}× {ItemDisplay.Format(i.Emoji, i.ItemName)}"));
            string value = $"**{stat}** · 💰 {recipe.GoldCost} oro\n{ingredients}";

            // Defensa: un campo de más de 1024 caracteres haría reventar todo el mensaje.
            embed.AddField(
                $"{groupEmoji} {ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}",
                value.Length <= 1024 ? value : value[..1023] + "…",
                false);
        }
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
            // No se listan todas las recetas: con 8 por zona y 5 zonas (40), el nombre con el emoji de cada una pasaría
            // el límite de 2000 caracteres de un mensaje de Discord y el comando fallaría justo cuando el jugador
            // se equivoca de nombre. Se lo manda a las listas, que ya muestran solo lo de su zona.
            return new ForgeMakeResult(
                "El herrero no conoce esa receta. Usá `/forge recipes` para ver las de tu zona, o elegí de la lista al escribir `/forge make`.", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (recipe.ResultItem.ClassRequirement is not null
            && !string.Equals(recipe.ResultItem.ClassRequirement, player.Class, StringComparison.OrdinalIgnoreCase))
        {
            return new ForgeMakeResult(
                $"**{ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}** es exclusivo de la clase **{recipe.ResultItem.ClassRequirement}** — vos sos **{player.Class}**.", null);
        }

        // CraftAsync valida oro + cada ingrediente dentro de una única transacción SQL
        // (SELECT ... FOR UPDATE) y hace rollback completo si falta algo.
        var resolvedIngredients = recipe.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList();
        var outcome = await craftingRepository.CraftAsync(discordId, recipe.GoldCost, resolvedIngredients, recipe.ResultItem.ItemId);

        if (!outcome.Success)
        {
            return new ForgeMakeResult(null, new EmbedBuilder()
                .WithTitle("⚒️ El herrero no pudo forjarlo")
                .WithDescription($"No pudiste forjar **{ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}**: {outcome.FailureReason}")
                .WithColor(Color.Red)
                .Build());
        }

        return new ForgeMakeResult(null, new EmbedBuilder()
            .WithTitle("⚒️ ¡Forjado con éxito!")
            .WithDescription($"¡El Herrero ha forjado **{ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}** con éxito!\nOro restante: **{outcome.Player!.Gold}**.")
            .WithColor(Color.Green)
            .Build());
    }
}
