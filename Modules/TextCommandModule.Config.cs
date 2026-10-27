using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): la configuración. Misma lógica que /config (ConfigModule): acá solo se lee lo que escribió el jugador.
public partial class TextCommandModule
{
    // "aa config" abre el panel (con sus botones); "aa config resumen off" / "aa set summary on" y "aa config recetas completo" / "aa set recipes classic" cambian la opción directo.
    [Command("config")]
    [Alias("conf", "set", "settings", "ajustes", "configuracion")]
    [Summary("Configurá cómo ves el bot: \"aa config\" abre el panel, \"aa config resumen off\" apaga la tablita del resumen del combate (\"on\" la vuelve a prender) y \"aa config recetas completo\" elige el recetario de la herrería (\"clasico\" vuelve al de siempre).")]
    public async Task ConfigAsync(string option = "", string value = "")
    {
        try
        {
            var (note, screen) = await ConfigModule.ExecuteTypedAsync(settingsService, Context.User.Id, option, value);
            await ReplyAsync(note, embed: screen.Embed, components: screen.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir la configuración, intentá de nuevo en un momento."));
        }
    }
}
