using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// El Herrero: separado de la Tienda (/shop) por diseño de juego. La Tienda vende consumibles
// con oro; la Herrería forja equipamiento con oro + materiales/MonsterDrops del inventario.
[Group("forge", "La herrería: forjá armas y amuletos con oro y materiales.")]
public class ForgeModule(IUserRepository userRepository, IItemRepository itemRepository, ICraftingRepository craftingRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /forge recipes
    [SlashCommand("recipes", "Mostrá las recetas de forja disponibles.")]
    public async Task HandleRecipesAsync()
    {
        await DeferAsync();

        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("⚒️ Recetas del Herrero")
                .WithColor(Color.DarkGrey);

            if (CraftingCatalog.Recipes.Count == 0)
            {
                embed.WithDescription("Todavía no hay recetas cargadas.");
            }
            else
            {
                var lines = CraftingCatalog.Recipes.Values.Select(recipe =>
                {
                    string ingredients = string.Join(" + ", recipe.Ingredients.Select(i => $"{i.Quantity}x {i.ItemName}"));
                    return $"**{recipe.ResultItemName}**: {recipe.GoldCost} Oro + {ingredients}";
                });

                embed.WithDescription(string.Join('\n', lines));
            }

            await FollowupAsync(embed: embed.Build());
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
            if (!CraftingCatalog.Recipes.TryGetValue(itemName, out var recipe))
            {
                string available = string.Join(", ", CraftingCatalog.Recipes.Keys);
                await FollowupAsync($"El herrero no conoce esa receta. Las disponibles son: {available}.", ephemeral: true);
                return;
            }

            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            await userRepository.GetOrCreateUserAsync(Context.User.Id);

            var resultItem = await itemRepository.GetByNameAsync(recipe.ResultItemName);
            if (resultItem is null)
            {
                await FollowupAsync($"La receta produce **{recipe.ResultItemName}**, que todavía no existe en el catálogo de ítems.", ephemeral: true);
                return;
            }

            // Resolvemos cada ingrediente (nombre -> item_id) antes de tocar la base: si alguno
            // no existe en el catálogo, avisamos sin haber empezado ninguna transacción.
            var resolvedIngredients = new List<(int ItemId, string ItemName, int Quantity)>();
            foreach (var ingredient in recipe.Ingredients)
            {
                var ingredientItem = await itemRepository.GetByNameAsync(ingredient.ItemName);
                if (ingredientItem is null)
                {
                    await FollowupAsync($"La receta necesita **{ingredient.ItemName}**, que todavía no existe en el catálogo de ítems.", ephemeral: true);
                    return;
                }

                resolvedIngredients.Add((ingredientItem.ItemId, ingredientItem.Name, ingredient.Quantity));
            }

            // CraftAsync valida oro + cada ingrediente dentro de una única transacción SQL
            // (SELECT ... FOR UPDATE) y hace rollback completo si falta algo.
            var outcome = await craftingRepository.CraftAsync(Context.User.Id, recipe.GoldCost, resolvedIngredients, resultItem.ItemId);

            if (!outcome.Success)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("⚒️ El herrero no pudo forjarlo")
                    .WithDescription($"No pudiste forjar **{recipe.ResultItemName}**: {outcome.FailureReason}")
                    .WithColor(Color.Red)
                    .Build(), ephemeral: true);
                return;
            }

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("⚒️ ¡Forjado con éxito!")
                .WithDescription($"¡El Herrero ha forjado **{recipe.ResultItemName}** con éxito!\nOro restante: **{outcome.Player!.Gold}**.")
                .WithColor(Color.Green)
                .Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló en la herrería, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
