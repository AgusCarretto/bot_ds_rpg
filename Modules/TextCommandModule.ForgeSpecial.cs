using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el forge especial (el equipo del Fogón Eterno).
public partial class TextCommandModule
{
    // "aa forgespecial" — misma lógica que ForgeSpecialModule.HandleForgeSpecialAsync. Una sola palabra, así no se mezcla con "aa forge make|recipes".
    [Command("forgespecial")]
    [Alias("forge-special", "forjaespecial", "fogonforge")]
    [Summary("El equipo del Fogón Eterno (Zona 0) a la vista desde el principio: \"aa forgespecial\".")]
    public async Task ForgeSpecialAsync()
    {
        try
        {
            await ReplyAsync(embed: await ForgeSpecialModule.BuildEmbedAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude mostrar el forge especial ahora mismo, intentá de nuevo en un momento.");
        }
    }
}
