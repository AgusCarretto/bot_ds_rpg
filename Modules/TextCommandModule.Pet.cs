using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): las mascotas.
public partial class TextCommandModule
{
    // "aa pet" (alias "aa mascota", "aa mascotas") — ver las mascotas; "aa pet feed" alimenta a todas las que puedan comer y "aa pet feed Ñandusito" a una. Misma lógica que /pet.
    [Command("pet")]
    [Alias("mascota", "mascotas", "pets")]
    [Summary("Tus mascotas: \"aa pet\" para verlas, \"aa pet feed\" para alimentar a las que puedan comer, \"aa pet feed Ñandusito\" para una sola o \"aa pet expedition Ñandusito 8\" para mandar una de expedición.")]
    public async Task PetAsync(string accion = "", [Remainder] string mascota = "")
    {
        try
        {
            string action = accion.Trim().ToLowerInvariant();
            var result = action switch
            {
                "" or "view" or "ver" => await PetModule.ExecuteViewAsync(userRepository, petRepository, inventoryRepository, Context.User.Id, bonusService: bonusService),
                "feed" or "alimentar" or "comer" => await PetModule.ExecuteFeedAsync(userRepository, petRepository, gameEvents, Context.User.Id, mascota, bonusService),
                "expedition" or "expedicion" or "expedición" or "exp" or "aventura" => await ExpeditionAsync(mascota),
                "evolve" or "evolucionar" or "evolucion" or "evolución" or "evo" => await PetModule.ExecuteEvolveAsync(userRepository, petRepository, gameEvents, Context.User.Id, mascota),
                _ => new PetModule.PetResult("No entendí: probá **aa pet**, **aa pet feed** (o **aa pet feed Ñandusito** para una sola), **aa pet expedition** o **aa pet evolve**.", null),
            };

            await ReplyAsync(result.PlainMessage, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude con tus mascotas ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // «aa pet expedition [mascota] [horas]» (v0.39.0): "aa pet expedition" abre el menú o recoge el premio si la mascota ya volvió; "aa pet expedition Ñandusito 8" la manda por 8 horas.
    private Task<PetModule.PetResult> ExpeditionAsync(string request)
    {
        var (petName, hours) = PetModule.ParseExpeditionRequest(request);
        return PetModule.ExecuteExpeditionAsync(userRepository, petRepository, petExpeditionRepository, inventoryRepository, gameEvents, Context.User.Id, petName, hours);
    }
}
