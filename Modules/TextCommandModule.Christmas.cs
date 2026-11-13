using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa navidad" — misma pantalla que /navidad (ChristmasModule): tus Regalos y el calendario de adviento con el botón del día.
    [Command("navidad")]
    [Alias("xmas", "christmas")]
    [Summary("Navidad en el Fogón: tus Regalos y el calendario de adviento.")]
    public async Task ChristmasAsync()
    {
        try
        {
            var view = await ChristmasModule.BuildAsync(christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir la Navidad, intentá de nuevo en un momento."));
        }
    }

    // "aa adviento" — abre la caja de hoy del calendario sin pasar por el botón.
    [Command("adviento")]
    [Alias("calendario", "advent")]
    [Summary("Abrí la caja de hoy del calendario de adviento (del 1 al 24 de diciembre).")]
    public async Task AdventAsync()
    {
        try
        {
            var result = await ChristmasModule.ClaimAsync(userRepository, zoneRepository, christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow);
            await ReplyAsync(result.Error, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir la caja de hoy, intentá de nuevo en un momento."));
        }
    }
}
