using Discord.Commands;

// Cuarta parte de TextCommandModule (ver el comentario en TextCommandModule.cs): comandos del
// Sistema de Zonas. Sin [Group]/constructor propio — misma clase de C#, las dependencias
// inyectadas en TextCommandModule.cs quedan accesibles acá también.
public partial class TextCommandModule
{
    // "aa zona <id>" — misma lógica que ZoneModule.HandleZonaAsync.
    [Command("zona")]
    [Summary("Viajá a otra zona del mundo: \"aa zona <id>\" (ver \"aa zonas\").")]
    public async Task ZonaAsync(int zoneId)
    {
        try
        {
            var (message, embed) = await ZoneModule.ExecuteTravelAsync(userRepository, zoneRepository, Context.User.Id, zoneId);
            await ReplyAsync(message, embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude procesar el viaje ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa zonas" — misma lógica que ZoneModule.HandleZonasAsync.
    [Command("zonas")]
    [Summary("Mostrá todas las zonas del mundo y sus niveles requeridos.")]
    public async Task ZonasAsync()
    {
        try
        {
            var embed = await ZoneModule.BuildZoneListEmbedAsync(userRepository, zoneRepository, Context.User.Id);
            await ReplyAsync(embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("No pude consultar las zonas ahora mismo, intentá de nuevo en un momento.");
        }
    }
}
