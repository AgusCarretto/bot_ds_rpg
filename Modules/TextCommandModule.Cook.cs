using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): la cocina (v0.49.0). La lógica está en CookModule (la comparte /cook).
public partial class TextCommandModule
{
    // "aa cook" / "aa cook recipes" muestran las recetas; "aa cook <plato> [veces]" cocina (y se come) uno o varios.
    [Command("cook")]
    [Alias("cocinar", "cocina")]
    [Summary("Cociná un plato con tus pescados y materiales: \"aa cook <plato> [veces]\"; \"aa cook recipes\" muestra las recetas.")]
    public async Task CookAsync([Remainder] string args = "")
    {
        try
        {
            var request = CookingCatalog.ParseRequest(args);
            if (request.Error is not null)
            {
                await ReplyAsync(request.Error);
                return;
            }

            if (request.ShowRecipes)
            {
                await ReplyAsync(embed: await CookModule.BuildRecipesEmbedAsync(inventoryRepository, cookingRepository, Context.User.Id));
                return;
            }

            var reply = await CookModule.ExecuteCookAsync(cookingRepository, gameEvents, Context.User.Id, request.Dish!, request.Times);
            await ReplyAsync(reply.PlainMessage, embed: reply.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude cocinar ahora mismo, intentá de nuevo en un momento."));
        }
    }
}
