using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): la ficha de stats (v0.49.0). La lógica está en StatsModule (la comparte /stats).
public partial class TextCommandModule
{
    // "aa stats" — tu ataque, defensa y vida con cuánto aporta cada cosa (equipo, encantamientos, platos, dragón, mascotas, bendiciones). «aa equipamiento» ya es el atajo del Carpintero.
    [Command("stats")]
    [Alias("estadisticas", "ficha")]
    [Summary("Tu ataque, defensa y vida con cuánto aporta cada cosa (equipo, platos, dragón...).")]
    public async Task StatsAsync(string page = "")
    {
        try
        {
            var reply = await StatsModule.BuildStatsAsync(userRepository, itemRepository, buffRepository, bonusService, cookingRepository, dragonRepository, Context.User.Id, zoneBoxService, IsMoreWord(page) ? 1 : 0);
            await ReplyAsync(reply.PlainMessage, embed: reply.Embed, components: reply.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude armar tus stats ahora mismo, intentá de nuevo en un momento."));
        }
    }

    // «aa stats mas» (también «más», «more», «extra», «2») abre directo la segunda pantalla.
    private static bool IsMoreWord(string? word) => (word ?? string.Empty).Trim().ToLowerInvariant() is "mas" or "más" or "more" or "extra" or "2";
}
