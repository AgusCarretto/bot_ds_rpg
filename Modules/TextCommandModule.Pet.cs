using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): las mascotas.
public partial class TextCommandModule
{
    // "aa pet" (alias "aa mascota", "aa mascotas") — ver las mascotas; "aa pet feed" alimenta a todas las que puedan comer y "aa pet feed Ñandusito" a una. Misma lógica que /pet.
    [Command("pet")]
    [Alias("mascota", "mascotas")]
    [Summary("Tus mascotas: \"aa pet\" para verlas, \"aa pet feed\" para alimentar a las que puedan comer o \"aa pet feed Ñandusito\" para una sola.")]
    public async Task PetAsync(string accion = "", [Remainder] string mascota = "")
    {
        try
        {
            string action = accion.Trim().ToLowerInvariant();
            var result = action switch
            {
                "" or "view" or "ver" => await PetModule.ExecuteViewAsync(userRepository, petRepository, inventoryRepository, Context.User.Id),
                "feed" or "alimentar" or "comer" => await PetModule.ExecuteFeedAsync(userRepository, petRepository, gameEvents, Context.User.Id, mascota),
                _ => new PetModule.PetResult("No entendí: probá **aa pet** o **aa pet feed** (o **aa pet feed Ñandusito** para una sola).", null),
            };

            await ReplyAsync(result.PlainMessage, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude con tus mascotas ahora mismo, intentá de nuevo en un momento.");
        }
    }
}
